using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Infrastructure.Local;
using Dtce.ParsingEngine.Handlers;
using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tools.DocumentComparison;

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
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        try
        {
            // Find workspace root
            var currentDir = Directory.GetCurrentDirectory();
            var workspaceRoot = currentDir;
            while (!Directory.Exists(Path.Combine(workspaceRoot, "SampleDocs")) && workspaceRoot != Path.GetPathRoot(workspaceRoot))
            {
                workspaceRoot = Directory.GetParent(workspaceRoot)?.FullName ?? workspaceRoot;
            }

            var originalPath = Path.Combine(workspaceRoot, "SampleDocs", "ResLatest-EngMgr-Fin.docx");
            var generatedPath = Path.Combine(workspaceRoot, "SampleDocs", "Test2", "generated-resume.docx");

            if (!File.Exists(originalPath))
            {
                Console.WriteLine($"Error: Original document not found at {originalPath}");
                return;
            }

            if (!File.Exists(generatedPath))
            {
                Console.WriteLine($"Error: Generated document not found at {generatedPath}");
                return;
            }

            Console.WriteLine("Comparing documents...");
            Console.WriteLine($"Original: {originalPath}");
            Console.WriteLine($"Generated: {generatedPath}\n");

            // Extract themes from both documents
            var storageOptions = Options.Create(new FileSystemStorageOptions 
            { 
                RootPath = Path.Combine(workspaceRoot, "SampleDocs", "Test2", "storage") 
            });
            var storage = new FileSystemObjectStorage(
                loggerFactory.CreateLogger<FileSystemObjectStorage>(),
                storageOptions);

            var handler = new DocxHandler(storage, loggerFactory.CreateLogger<DocxHandler>());

            var originalJobId = $"compare-original-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var originalStoragePath = $"documents/{originalJobId}/original.docx";
            await using (var sourceStream = File.OpenRead(originalPath))
            {
                await storage.UploadFileAsync(originalStoragePath, sourceStream,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    CancellationToken.None);
            }

            var originalParseResult = await handler.ParseAsync(new JobRequest
            {
                JobId = originalJobId,
                DocumentType = Dtce.Common.DocumentType.Docx,
                FilePath = originalStoragePath
            });

            var generatedJobId = $"compare-generated-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var generatedStoragePath = $"documents/{generatedJobId}/generated.docx";
            await using (var sourceStream = File.OpenRead(generatedPath))
            {
                await storage.UploadFileAsync(generatedStoragePath, sourceStream,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    CancellationToken.None);
            }

            var generatedParseResult = await handler.ParseAsync(new JobRequest
            {
                JobId = generatedJobId,
                DocumentType = Dtce.Common.DocumentType.Docx,
                FilePath = generatedStoragePath
            });

            // Compare themes
            Console.WriteLine("=== FONT COMPARISON ===");
            CompareFonts(originalParseResult.TemplateJson.VisualTheme, generatedParseResult.TemplateJson.VisualTheme);

            Console.WriteLine("\n=== COLOR COMPARISON ===");
            CompareColors(originalParseResult.TemplateJson.VisualTheme, generatedParseResult.TemplateJson.VisualTheme);

            Console.WriteLine("\n=== LAYOUT COMPARISON ===");
            CompareLayout(originalParseResult.TemplateJson.VisualTheme.LayoutRules, generatedParseResult.TemplateJson.VisualTheme.LayoutRules);

            Console.WriteLine("\n=== STRUCTURE COMPARISON ===");
            CompareStructure(originalParseResult, generatedParseResult);

            Console.WriteLine("\n=== CONTENT DETAIL COMPARISON ===");
            CompareContentDetails(originalParseResult, generatedParseResult);

            // Save comparison report
            var reportPath = Path.Combine(workspaceRoot, "SampleDocs", "Test2", "comparison-report.json");
            var report = new
            {
                OriginalTheme = originalParseResult.TemplateJson.VisualTheme,
                GeneratedTheme = generatedParseResult.TemplateJson.VisualTheme,
                OriginalSections = originalParseResult.TemplateJson.SectionHierarchy.Sections.Count,
                GeneratedSections = generatedParseResult.TemplateJson.SectionHierarchy.Sections.Count,
                OriginalContentBlocks = originalParseResult.ContentSections.Count,
                GeneratedContentBlocks = generatedParseResult.ContentSections.Count
            };

            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"\n✓ Comparison report saved: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    private static void CompareFonts(VisualTheme original, VisualTheme generated)
    {
        var allFontKeys = new HashSet<string>(original.FontMap.Keys, StringComparer.OrdinalIgnoreCase);
        allFontKeys.UnionWith(generated.FontMap.Keys);

        foreach (var key in allFontKeys.OrderBy(k => k))
        {
            var origFont = original.FontMap.TryGetValue(key, out var o) ? o : null;
            var genFont = generated.FontMap.TryGetValue(key, out var g) ? g : null;

            if (origFont == null)
            {
                Console.WriteLine($"  {key}: MISSING in original (Generated: {genFont?.Family} {genFont?.Size}pt)");
                continue;
            }

            if (genFont == null)
            {
                Console.WriteLine($"  {key}: MISSING in generated (Original: {origFont.Family} {origFont.Size}pt)");
                continue;
            }

            var differences = new List<string>();
            if (!string.Equals(origFont.Family, genFont.Family, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"Family: {origFont.Family} → {genFont.Family}");
            }
            if (Math.Abs(origFont.Size - genFont.Size) > 0.1)
            {
                differences.Add($"Size: {origFont.Size}pt → {genFont.Size}pt");
            }
            if (!string.Equals(origFont.Weight, genFont.Weight, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"Weight: {origFont.Weight} → {genFont.Weight}");
            }
            if (!string.Equals(origFont.Color, genFont.Color, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add($"Color: {origFont.Color} → {genFont.Color}");
            }

            if (differences.Any())
            {
                Console.WriteLine($"  {key}: DIFFERENT");
                foreach (var diff in differences)
                {
                    Console.WriteLine($"    - {diff}");
                }
            }
            else
            {
                Console.WriteLine($"  {key}: ✓ Match ({genFont.Family} {genFont.Size}pt)");
            }
        }
    }

    private static void CompareColors(VisualTheme original, VisualTheme generated)
    {
        Console.WriteLine($"  Original has {original.ColorPalette.Count} colors");
        Console.WriteLine($"  Generated has {generated.ColorPalette.Count} colors");

        for (int i = 0; i < Math.Max(original.ColorPalette.Count, generated.ColorPalette.Count); i++)
        {
            var origColor = i < original.ColorPalette.Count ? original.ColorPalette[i] : null;
            var genColor = i < generated.ColorPalette.Count ? generated.ColorPalette[i] : null;

            if (origColor == null)
            {
                Console.WriteLine($"  Color {i}: MISSING in original (Generated: {genColor?.HexCode})");
            }
            else if (genColor == null)
            {
                Console.WriteLine($"  Color {i}: MISSING in generated (Original: {origColor.HexCode})");
            }
            else if (!string.Equals(origColor.HexCode, genColor.HexCode, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  Color {i} ({origColor.Name}): {origColor.HexCode} → {genColor.HexCode}");
            }
            else
            {
                Console.WriteLine($"  Color {i} ({origColor.Name}): ✓ Match ({origColor.HexCode})");
            }
        }
    }

    private static void CompareLayout(LayoutRules original, LayoutRules generated)
    {
        var differences = new List<string>();

        if (Math.Abs(original.PageWidth - generated.PageWidth) > 0.1)
        {
            differences.Add($"PageWidth: {original.PageWidth}mm → {generated.PageWidth}mm");
        }
        if (Math.Abs(original.PageHeight - generated.PageHeight) > 0.1)
        {
            differences.Add($"PageHeight: {original.PageHeight}mm → {generated.PageHeight}mm");
        }
        if (!string.Equals(original.Orientation, generated.Orientation, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add($"Orientation: {original.Orientation} → {generated.Orientation}");
        }
        if (Math.Abs(original.Margins.Top - generated.Margins.Top) > 0.1)
        {
            differences.Add($"Margin Top: {original.Margins.Top}mm → {generated.Margins.Top}mm");
        }
        if (Math.Abs(original.Margins.Bottom - generated.Margins.Bottom) > 0.1)
        {
            differences.Add($"Margin Bottom: {original.Margins.Bottom}mm → {generated.Margins.Bottom}mm");
        }
        if (Math.Abs(original.Margins.Left - generated.Margins.Left) > 0.1)
        {
            differences.Add($"Margin Left: {original.Margins.Left}mm → {generated.Margins.Left}mm");
        }
        if (Math.Abs(original.Margins.Right - generated.Margins.Right) > 0.1)
        {
            differences.Add($"Margin Right: {original.Margins.Right}mm → {generated.Margins.Right}mm");
        }

        if (differences.Any())
        {
            foreach (var diff in differences)
            {
                Console.WriteLine($"  {diff}");
            }
        }
        else
        {
            Console.WriteLine("  ✓ All layout settings match");
        }
    }

    private static void CompareStructure(ParseResult original, ParseResult generated)
    {
        Console.WriteLine($"  Original sections: {original.TemplateJson.SectionHierarchy.Sections.Count}");
        Console.WriteLine($"  Generated sections: {generated.TemplateJson.SectionHierarchy.Sections.Count}");
        Console.WriteLine($"  Original content blocks: {original.ContentSections.Count}");
        Console.WriteLine($"  Generated content blocks: {generated.ContentSections.Count}");

        var origSectionTitles = original.TemplateJson.SectionHierarchy.Sections.Select(s => s.SectionTitle).ToList();
        var genSectionTitles = generated.TemplateJson.SectionHierarchy.Sections.Select(s => s.SectionTitle).ToList();

        Console.WriteLine("\n  Section titles:");
        for (int i = 0; i < Math.Max(origSectionTitles.Count, genSectionTitles.Count); i++)
        {
            var orig = i < origSectionTitles.Count ? origSectionTitles[i] : "[MISSING]";
            var gen = i < genSectionTitles.Count ? genSectionTitles[i] : "[MISSING]";
            if (orig != gen)
            {
                Console.WriteLine($"    {i + 1}. DIFFERENT: '{orig}' vs '{gen}'");
            }
            else
            {
                Console.WriteLine($"    {i + 1}. ✓ '{orig}'");
            }
        }
    }

    private static void CompareContentDetails(ParseResult original, ParseResult generated)
    {
        var origAvgWords = original.ContentSections.Any() 
            ? original.ContentSections.Average(s => s.WordCount) 
            : 0;
        var genAvgWords = generated.ContentSections.Any() 
            ? generated.ContentSections.Average(s => s.WordCount) 
            : 0;

        Console.WriteLine($"  Original average words per section: {origAvgWords:F1}");
        Console.WriteLine($"  Generated average words per section: {genAvgWords:F1}");

        var origTotalWords = original.ContentSections.Sum(s => s.WordCount);
        var genTotalWords = generated.ContentSections.Sum(s => s.WordCount);

        Console.WriteLine($"  Original total words: {origTotalWords}");
        Console.WriteLine($"  Generated total words: {genTotalWords}");

        if (Math.Abs(origAvgWords - genAvgWords) > origAvgWords * 0.2)
        {
            Console.WriteLine($"  ⚠ WARNING: Significant difference in detail level ({Math.Abs(origAvgWords - genAvgWords):F1} words difference)");
        }
        else
        {
            Console.WriteLine("  ✓ Detail level is similar");
        }
    }
}

