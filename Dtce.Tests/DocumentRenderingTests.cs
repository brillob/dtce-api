using DocumentFormat.OpenXml.Packaging;
using Dtce.Common;
using Dtce.DocumentRendering;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace Dtce.Tests;

public class DocumentRenderingTests : IDisposable
{
    private readonly TestObjectStorage _storage = new();
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"dtce-render-{Guid.NewGuid():N}");

    public DocumentRenderingTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    [Fact]
    public async Task TemplateRenderer_CreatesTemplate_And_Renders_NewDocument()
    {
        var samplePath = TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.docx");
        var storageKey = "documents/sample/resume.docx";
        _storage.SeedFileFromPath(storageKey, samplePath, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var handler = new DocxHandler(_storage, NullLogger<DocxHandler>.Instance);
        var parseResult = await handler.ParseAsync(new JobRequest
        {
            JobId = "render-job",
            DocumentType = DocumentType.Docx,
            FilePath = storageKey
        });

        parseResult.TemplateJson.SectionHierarchy.Sections.Should().NotBeEmpty();
        
        // Verify hierarchical structure detection (sections with subsections)
        var sectionsWithSubsections = parseResult.TemplateJson.SectionHierarchy.Sections
            .Where(section => section.SubSections != null && section.SubSections.Count > 0)
            .ToList();
        sectionsWithSubsections.Should().NotBeEmpty("document should have hierarchical sections with subsections");

        var renderer = new DocxTemplateRenderer(_storage, NullLogger<DocxTemplateRenderer>.Instance);
        var templatePath = Path.Combine(_workingDirectory, "template.docx");
        await renderer.CreateTemplateDocumentAsync(parseResult, templatePath);
        File.Exists(templatePath).Should().BeTrue();

        var request = new DocumentRenderRequest();
        foreach (var content in parseResult.ContentSections)
        {
            request.SectionContent[content.PlaceholderId] = BuildReplacementContent(content.SectionTitle);
        }

        foreach (var logo in parseResult.TemplateJson.LogoMap.Take(2))
        {
            request.LogoReplacements.Add(new LogoReplacement(logo.AssetId, CreateLogoBytes(), "image/png"));
        }

        var outputPath = Path.Combine(_workingDirectory, "resume-new.docx");
        await renderer.RenderDocumentAsync(templatePath, parseResult.TemplateJson, request, outputPath);
        File.Exists(outputPath).Should().BeTrue();

        using var rendered = WordprocessingDocument.Open(outputPath, false);
        var documentText = string.Join(
            Environment.NewLine,
            rendered.MainDocumentPart!.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Select(paragraph => string.Concat(paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
                    .Select(t => t.Text))));

        foreach (var replacement in request.SectionContent.Values)
        {
            documentText.Should().Contain(replacement.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        }

        documentText.Should().NotContain("{{", "all placeholders should be replaced with actual content");
    }

    private static string BuildReplacementContent(string sectionTitle) =>
        sectionTitle switch
        {
            { } title when title.Contains("Summary", StringComparison.OrdinalIgnoreCase) =>
                "Alexis Rivera – Principal Engineering Leader\nDriving global SaaS scale-ups with a human-first approach.",
            { } title when title.Contains("Professional", StringComparison.OrdinalIgnoreCase) =>
                "Over 18 years of transforming ideation into reliable products across FinTech, HealthTech, and Education.",
            { } title when title.Contains("Engineering Manager", StringComparison.OrdinalIgnoreCase) =>
                "Led a 40-person cross-functional engineering division.\n- Introduced AI-assisted QA gating, cutting defects by 35%\n- Rolled out a competency framework adopted company wide",
            { } title when title.Contains("Head of Engineering", StringComparison.OrdinalIgnoreCase) =>
                "Oversaw global platform modernization.\n- Instituted DORA metrics with real-time dashboards\n- Mentored seven new technical leads into senior roles",
            { } title when title.Contains("Consultant", StringComparison.OrdinalIgnoreCase) =>
                "Delivered multi-cloud modernization for government clients while maintaining 99.95% SLA.",
            { } title when title.Contains("Software Development Manager", StringComparison.OrdinalIgnoreCase) =>
                "Unified legacy aviation risk platforms into a resilient rule engine powering worldwide inspections.",
            _ => $"{sectionTitle}: content intentionally replaced to demonstrate template rendering."
        };

    private static byte[] CreateLogoBytes()
    {
        using var image = new Image<Rgba32>(64, 64, new Rgba32(20, 45, 110, 255));
        for (var y = 8; y < 56; y++)
        {
            for (var x = 8; x < 56; x++)
            {
                image[x, y] = new Rgba32(240, 185, 60, 255);
            }
        }

        using var memory = new MemoryStream();
        image.Save(memory, new PngEncoder());
        return memory.ToArray();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, true);
            }
        }
        catch
        {
            // ignore cleanup errors
        }
    }
}

