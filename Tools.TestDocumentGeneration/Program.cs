using System.Text.Json;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.DocumentRendering;
using Dtce.Infrastructure.Local;
using Dtce.ParsingEngine.Handlers;
using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tools.TestDocumentGeneration;

class Program
{
    static async Task Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        try
        {
            // Find workspace root (look for SampleDocs directory)
            var currentDir = Directory.GetCurrentDirectory();
            var workspaceRoot = currentDir;
            while (!Directory.Exists(Path.Combine(workspaceRoot, "SampleDocs")) && workspaceRoot != Path.GetPathRoot(workspaceRoot))
            {
                workspaceRoot = Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot;
            }
            
            var sampleDocPath = Path.Combine(workspaceRoot, "SampleDocs", "ResLatest-EngMgr-Fin.docx");
            var outputDir = Path.Combine(workspaceRoot, "SampleDocs", "Test3");

            if (!File.Exists(sampleDocPath))
            {
                Console.WriteLine($"Error: Sample document not found at {sampleDocPath}");
                return;
            }

            Directory.CreateDirectory(outputDir);

            Console.WriteLine($"Processing document: {sampleDocPath}");
            Console.WriteLine($"Output directory: {outputDir}");

            // Setup local file storage
            var storageOptions = Options.Create(new FileSystemStorageOptions 
            { 
                RootPath = Path.Combine(outputDir, "storage") 
            });
            var storage = new FileSystemObjectStorage(
                loggerFactory.CreateLogger<FileSystemObjectStorage>(),
                storageOptions);

            // Step 1: Extract template and context JSONs
            Console.WriteLine("\nStep 1: Extracting template and context JSONs...");
            var jobId = $"test-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var storagePath = $"documents/{jobId}/ResLatest-EngMgr-Fin.docx";

            // Upload source document
            await using (var sourceStream = File.OpenRead(sampleDocPath))
            {
                await storage.UploadFileAsync(
                    storagePath,
                    sourceStream,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    CancellationToken.None);
            }

            // Parse document
            var handler = new DocxHandler(storage, loggerFactory.CreateLogger<DocxHandler>());
            var parseResult = await handler.ParseAsync(new JobRequest
            {
                JobId = jobId,
                DocumentType = DocumentType.Docx,
                FilePath = storagePath
            });

            // Build context JSON
            var contextJson = new ContextJson
            {
                LinguisticStyle = new LinguisticStyleAttributes
                {
                    OverallFormality = "professional",
                    FormalityConfidenceScore = 0.85,
                    DominantTone = "confident",
                    ToneConfidenceScore = 0.80,
                    WritingStyleVector = new List<double>()
                },
                ContentBlocks = parseResult.ContentSections
                    .Select(section => new ContentBlock
                    {
                        PlaceholderId = section.PlaceholderId,
                        SectionSampleText = section.SampleText,
                        WordCount = section.WordCount
                    })
                    .ToList()
            };

            // Save template and context JSONs
            var templateJsonPath = Path.Combine(outputDir, "template.json");
            var contextJsonPath = Path.Combine(outputDir, "context.json");

            await WriteJsonAsync(templateJsonPath, parseResult.TemplateJson);
            await WriteJsonAsync(contextJsonPath, contextJson);

            Console.WriteLine($"✓ Template JSON saved: {templateJsonPath}");
            Console.WriteLine($"✓ Context JSON saved: {contextJsonPath}");

            // Step 2: Generate template document (placeholder DOCX) using DocxTemplateRenderer
            Console.WriteLine("\nStep 2: Generating template document...");
            var templateDocPath = Path.Combine(outputDir, "template.docx");
            var docxRenderer = new Dtce.DocumentRendering.DocxTemplateRenderer(storage, loggerFactory.CreateLogger<Dtce.DocumentRendering.DocxTemplateRenderer>());
            
            await docxRenderer.CreateTemplateDocumentAsync(parseResult, templateDocPath);
            Console.WriteLine($"✓ Template document saved: {templateDocPath}");

            // Step 3: Generate new document with different data using DocxTemplateRenderer
            Console.WriteLine("\nStep 3: Generating new document with different data...");
            var newDocumentPath = Path.Combine(outputDir, "generated-resume.docx");

            // Create new context with different data
            var newContextJson = CreateNewContextData(parseResult.TemplateJson, contextJson);
            
            // Convert context to DocumentRenderRequest format
            var renderRequest = new Dtce.DocumentRendering.DocumentRenderRequest();
            foreach (var block in newContextJson.ContentBlocks)
            {
                renderRequest.SectionContent[block.PlaceholderId] = block.SectionSampleText;
            }

            await docxRenderer.RenderDocumentAsync(
                templateDocPath,
                parseResult.TemplateJson,
                renderRequest,
                newDocumentPath);

            Console.WriteLine($"✓ New document saved: {newDocumentPath}");

            Console.WriteLine("\n✓ All files generated successfully!");
            Console.WriteLine($"\nFiles in {outputDir}:");
            Console.WriteLine("  - template.json (Template structure)");
            Console.WriteLine("  - context.json (Original sample data)");
            Console.WriteLine("  - template.docx (Template document with placeholders)");
            Console.WriteLine("  - generated-resume.docx (New document with different data)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private static ContextJson CreateNewContextData(TemplateJson template, ContextJson originalContext)
    {
        var newContext = new ContextJson
        {
            LinguisticStyle = originalContext.LinguisticStyle,
            ContentBlocks = new List<ContentBlock>()
        };

        // Generate new content for each section, maintaining structure
        foreach (var section in FlattenSections(template.SectionHierarchy.Sections))
        {
            var originalBlock = originalContext.ContentBlocks
                .FirstOrDefault(b => string.Equals(b.PlaceholderId, section.PlaceholderId, StringComparison.OrdinalIgnoreCase));

            var newContent = GenerateNewContent(section.SectionTitle, originalBlock?.SectionSampleText ?? "", section);
            
            newContext.ContentBlocks.Add(new ContentBlock
            {
                PlaceholderId = section.PlaceholderId,
                SectionSampleText = newContent,
                WordCount = CountWords(newContent)
            });
        }

        return newContext;
    }

    private static string GenerateNewContent(string sectionTitle, string originalContent, Section? section = null)
    {
        // Get original word count to match detail level
        var originalWordCount = CountWords(originalContent);
        
        // Generate new content based on section title, maintaining similar structure
        var titleLower = sectionTitle.ToLowerInvariant();
        
        if (titleLower.Contains("professional summary") || titleLower.Contains("summary") || titleLower.Contains("executive summary"))
        {
            return "Michael Rodriguez – Senior Engineering Manager\n" +
                   "Accomplished technology leader with 15+ years of experience driving innovation in enterprise software development. " +
                   "Expert in building scalable distributed systems, leading cross-functional teams, and delivering high-impact products. " +
                   "Proven ability to transform engineering organizations and deliver solutions that serve millions of users worldwide.";
        }
        
        if (titleLower.Contains("core competencies") || titleLower.Contains("skills") || titleLower.Contains("technical skills"))
        {
            return "Cloud Platforms: AWS, Google Cloud Platform, Azure, Kubernetes, Docker, Serverless Architecture\n" +
                   "Programming Languages: Java, Python, Go, TypeScript, C++, Scala\n" +
                   "Frameworks & Tools: Spring Boot, React, Angular, Node.js, Kafka, Redis, PostgreSQL, MongoDB\n" +
                   "Leadership & Process: Agile/Scrum, DevOps, CI/CD, Team Leadership, Technical Strategy, Product Management";
        }
        
        if (titleLower.Contains("experience") || titleLower.Contains("work history") || titleLower.Contains("employment"))
        {
            // Check if this is a subsection (like a specific job)
            // Look at parent sections or check title for dates
            if (titleLower.Contains("present") || titleLower.Contains("2024") || titleLower.Contains("2023") || 
                titleLower.Contains("current") || (section?.SubSections?.Count ?? 0) > 0)
            {
                // This might be a main experience section with subsections
                if ((section?.SubSections?.Count ?? 0) > 0)
                {
                    // Return empty or minimal content - subsections will have the actual content
                    return "";
                }
                
                // This is a specific job entry
                return "CloudScale Technologies, Austin, TX (March 2021 – Present)\n\n" +
                       "Senior Engineering Manager\n" +
                       "Lead engineering organization of 30+ engineers across four product teams, delivering cloud-native solutions to enterprise clients.\n" +
                       "- Architected and launched a distributed microservices platform, handling 10M+ requests per day with 99.9% uptime\n" +
                       "- Established comprehensive DevOps practices, reducing deployment time from 4 hours to 15 minutes\n" +
                       "- Implemented data-driven engineering metrics, improving team velocity by 40% and reducing bug rate by 50%\n" +
                       "- Built and mentored a team of 12 senior engineers, with 6 promoted to staff/principal level\n" +
                       "- Collaborated with executive leadership to define technical strategy and product roadmaps";
            }
            
            // Another job entry
            if (titleLower.Contains("2021") || titleLower.Contains("2020") || titleLower.Contains("2019"))
            {
                return "DataFlow Systems, Boston, MA (June 2018 – February 2021)\n\n" +
                       "Engineering Manager\n" +
                       "Managed a team of 18 engineers building real-time data processing and analytics platforms.\n" +
                       "- Delivered 5 major product releases, increasing customer base from 500 to 2,500+ enterprises\n" +
                       "- Reduced system latency by 60% through architectural improvements and performance optimization\n" +
                       "- Implemented comprehensive automated testing, increasing test coverage from 50% to 90%\n" +
                       "- Led migration from monolithic architecture to microservices, improving scalability and maintainability\n" +
                       "- Established engineering best practices and code review processes, improving code quality metrics";
            }
            
            // Earlier job entry
            return "TechInnovate Solutions, Seattle, WA (January 2015 – May 2018)\n\n" +
                   "Senior Software Engineer / Tech Lead\n" +
                   "Led development of enterprise SaaS platform serving 1,000+ customers.\n" +
                   "- Designed and implemented core platform features, processing 5M+ transactions daily\n" +
                   "- Mentored junior engineers and established technical standards and coding guidelines\n" +
                   "- Collaborated with product and design teams to deliver user-centric features\n" +
                   "- Reduced infrastructure costs by 35% through optimization and cloud migration";
        }
        
        if (titleLower.Contains("education"))
        {
            return "Master of Science in Computer Science\n" +
                   "Massachusetts Institute of Technology (MIT), Cambridge, MA\n" +
                   "Graduated: May 2014\n" +
                   "Thesis: Distributed Systems for Real-Time Data Processing\n\n" +
                   "Bachelor of Science in Computer Engineering\n" +
                   "University of Texas at Austin, Austin, TX\n" +
                   "Graduated: May 2012\n" +
                   "Magna Cum Laude, Dean's List";
        }
        
        if (titleLower.Contains("certification") || titleLower.Contains("certifications"))
        {
            return "AWS Certified Solutions Architect – Professional\n" +
                   "Google Cloud Professional Cloud Architect\n" +
                   "Certified Kubernetes Administrator (CKA)\n" +
                   "Certified Scrum Master (CSM)\n" +
                   "PMP (Project Management Professional)";
        }

        if (titleLower.Contains("project") || titleLower.Contains("key projects"))
        {
            return "Enterprise Cloud Migration Platform\n" +
                   "Led development of platform enabling seamless migration of legacy systems to cloud infrastructure\n" +
                   "- Reduced migration time by 70% for enterprise clients\n" +
                   "- Served 200+ enterprise customers, processing 50M+ migrations\n\n" +
                   "Real-Time Analytics Engine\n" +
                   "Architected distributed system for real-time data processing and analytics\n" +
                   "- Handles 100M+ events per second with sub-100ms latency\n" +
                   "- Used by 500+ companies for business intelligence and decision-making";
        }

        if (titleLower.Contains("award") || titleLower.Contains("achievement") || titleLower.Contains("recognition"))
        {
            return "Engineering Excellence Award – CloudScale Technologies (2023)\n" +
                   "Outstanding Leadership Award – DataFlow Systems (2020)\n" +
                   "Innovation Award – TechInnovate Solutions (2017)";
        }
        
        // For other sections, try to maintain similar structure but with different content
        if (!string.IsNullOrWhiteSpace(originalContent))
        {
            // Replace key words/phrases to create variation while maintaining structure
            var modified = originalContent
                .Replace("UP Education", "CloudScale Technologies")
                .Replace("New Zealand", "United States")
                .Replace("Auckland", "Austin")
                .Replace("Engineering Manager", "Senior Engineering Manager")
                .Replace("20+ years", "15+ years")
                .Replace("$400 million", "$600 million")
                .Replace("Wellington", "Boston")
                .Replace("Christchurch", "Seattle");
            
            return modified;
        }
        
        return $"{sectionTitle}: [Content for this section]";
    }

    private static IEnumerable<Section> FlattenSections(IEnumerable<Section> sections)
    {
        foreach (var section in sections)
        {
            yield return section;
            foreach (var child in FlattenSections(section.SubSections ?? new List<Section>()))
            {
                yield return child;
            }
        }
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        return System.Text.RegularExpressions.Regex.Matches(text, @"\b\w+\b").Count;
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

