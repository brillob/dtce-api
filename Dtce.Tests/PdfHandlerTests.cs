using Dtce.Common;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtce.Tests;

public class PdfHandlerTests
{
    private readonly TestObjectStorage _objectStorage = new();
    private readonly PdfHandler _handler;
    private const string StorageKey = "documents/sample/resume.pdf";

    public PdfHandlerTests()
    {
        var samplePath = TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.pdf");
        _objectStorage.SeedFileFromPath(StorageKey, samplePath, "application/pdf");
        _handler = new PdfHandler(_objectStorage, NullLogger<PdfHandler>.Instance);
    }

    [Fact]
    public async Task ParseAsync_WithValidPdf_ProducesContentSections()
    {
        var jobRequest = new JobRequest
        {
            JobId = "pdf-job",
            DocumentType = DocumentType.Pdf,
            FilePath = StorageKey
        };

        var result = await _handler.ParseAsync(jobRequest);

        result.TemplateJson.SectionHierarchy.Sections.Should().NotBeEmpty();
        result.ContentSections.Should().NotBeEmpty();
        result.ContentSections.Sum(s => s.WordCount).Should().BeGreaterThan(0);
        result.TemplateJson.VisualTheme.LayoutRules.PageWidth.Should().BeGreaterThan(0);
        result.TemplateJson.LogoMap.Should().NotBeNull();
    }
}


