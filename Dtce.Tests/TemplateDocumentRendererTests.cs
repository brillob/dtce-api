using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.ParsingEngine.Handlers;
using Dtce.ParsingEngine.Rendering;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtce.Tests;

public class TemplateDocumentRendererTests
{
    [Fact]
    public async Task RenderAsync_ProducesDocumentWithNewContent()
    {
        var storage = new TestObjectStorage();
        var jobId = "render-test";
        var docKey = $"documents/{jobId}/resume.docx";
        storage.SeedFileFromPath(
            docKey,
            TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.docx"),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var parser = new DocxHandler(storage, NullLogger<DocxHandler>.Instance);
        var parseResult = await parser.ParseAsync(new JobRequest
        {
            JobId = jobId,
            DocumentType = DocumentType.Docx,
            FilePath = docKey
        });

        var newContext = BuildNewContext(parseResult.ContentSections);
        var renderer = new TemplateDocumentRenderer(storage, NullLogger<TemplateDocumentRenderer>.Instance);
        var options = new TemplateRenderOptions();

        var documentBytes = await renderer.RenderAsync(parseResult.TemplateJson, newContext, options);

        documentBytes.Should().NotBeNull();
        documentBytes.Length.Should().BeGreaterThan(10_000);

        var outputDirectory = Path.Combine(TestResourcePaths.SampleDocsRoot, "Generated");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "Resume_Jane_Doe.docx");
        await File.WriteAllBytesAsync(outputPath, documentBytes);

        using var docStream = new MemoryStream(documentBytes);
        using var wordDocument = WordprocessingDocument.Open(docStream, false);
        var bodyText = string.Join(" ", wordDocument.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
            .Select(t => t.Text));

        bodyText.Should().Contain("Jane Doe");
        bodyText.Should().Contain("Innovative technology executive");

        wordDocument.MainDocumentPart.ImageParts.Should().NotBeEmpty();
    }

    private static ContextJson BuildNewContext(IEnumerable<ContentSection> sections)
    {
        var context = new ContextJson
        {
            LinguisticStyle = new LinguisticStyleAttributes
            {
                OverallFormality = "formal",
                FormalityConfidenceScore = 0.92,
                DominantTone = "confident",
                ToneConfidenceScore = 0.88,
                WritingStyleVector = Enumerable.Repeat(0.01, 128).ToList()
            }
        };

        var sampleParagraphs = new[]
        {
            "Jane Doe – MBA, PMP | Email: jane.doe@example.com | Ph: 021 555 1212",
            "Innovative technology executive with a decade of experience building AI-assisted delivery organisations across APAC and North America.",
            "Led cross-functional programs delivering SaaS products to 200K+ customers while reducing operational overhead by 30%.",
            "Championed modern engineering practices, automated compliance reporting, and scaled remote teams across four time-zones."
        };

        var paragraphQueue = new Queue<string>(sampleParagraphs.Concat(sampleParagraphs));

        foreach (var section in sections)
        {
            var contentBuilder = new List<string>();
            if (paragraphQueue.Count == 0)
            {
                paragraphQueue = new Queue<string>(sampleParagraphs);
            }

            contentBuilder.Add(paragraphQueue.Dequeue());
            contentBuilder.Add(paragraphQueue.Dequeue());

            var sampleText = string.Join(Environment.NewLine, contentBuilder);

            context.ContentBlocks.Add(new ContentBlock
            {
                PlaceholderId = section.PlaceholderId,
                SectionSampleText = sampleText,
                WordCount = CountWords(sampleText)
            });
        }

        return context;
    }

    private static int CountWords(string text) =>
        Regex.Matches(text, @"\b\w+\b").Count;
}

