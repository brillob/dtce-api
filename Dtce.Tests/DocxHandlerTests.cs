using Dtce.Common;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtce.Tests;

public class DocxHandlerTests
{
    private readonly TestObjectStorage _objectStorage = new();
    private readonly DocxHandler _handler;

    public DocxHandlerTests()
    {
        var samplePath = TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.docx");
        var storageKey = "documents/sample/resume.docx";

        _objectStorage.SeedFileFromPath(
            storageKey,
            samplePath,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        _handler = new DocxHandler(_objectStorage, NullLogger<DocxHandler>.Instance);
    }

    [Fact]
    public async Task ParseAsync_WithNullFilePath_ThrowsInvalidOperationException()
    {
        var jobRequest = new JobRequest
        {
            JobId = "test-job",
            DocumentType = DocumentType.Docx,
            FilePath = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.ParseAsync(jobRequest));
    }

    [Fact]
    public async Task ParseAsync_WithEmptyFilePath_ThrowsInvalidOperationException()
    {
        var jobRequest = new JobRequest
        {
            JobId = "test-job",
            DocumentType = DocumentType.Docx,
            FilePath = string.Empty
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.ParseAsync(jobRequest));
    }

    [Fact]
    public async Task ParseAsync_WithValidDocxFile_ProducesStructuredTemplateAndContent()
    {
        var jobRequest = new JobRequest
        {
            JobId = "resume-job",
            DocumentType = DocumentType.Docx,
            FilePath = "documents/sample/resume.docx"
        };

        var result = await _handler.ParseAsync(jobRequest);

        result.Should().NotBeNull();
        result.TemplateJson.SectionHierarchy.Sections.Should().NotBeEmpty();
        result.ContentSections.Should().NotBeEmpty();
        result.ContentSections.Sum(s => s.WordCount).Should().BeGreaterThan(0);
        result.TemplateJson.VisualTheme.FontMap.Should().NotBeEmpty();
        result.TemplateJson.VisualTheme.LayoutRules.PageWidth.Should().BeGreaterThan(0);

        foreach (var logo in result.TemplateJson.LogoMap)
        {
            logo.StorageKey.Should().NotBeNullOrWhiteSpace();
            logo.SecureUrl.Should().StartWith("https://local.test");
        }
    }
}

