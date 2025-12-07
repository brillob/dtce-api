using Dtce.Common;
using Dtce.Common.Models;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtce.Tests;

public class DocxStructureDetectionTests
{
    [Fact]
    public async Task ParseAsync_DetectsDocumentStructure()
    {
        var storage = new TestObjectStorage();
        var docKey = "documents/sample/resume.docx";
        storage.SeedFileFromPath(
            docKey,
            TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.docx"),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var handler = new DocxHandler(storage, NullLogger<DocxHandler>.Instance);
        var result = await handler.ParseAsync(new JobRequest
        {
            JobId = "structure-test",
            DocumentType = DocumentType.Docx,
            FilePath = docKey
        });

        result.TemplateJson.SectionHierarchy.Sections.Should().NotBeEmpty("document should have sections");
        result.ContentSections.Should().NotBeEmpty("document should have content sections");

        var titles = FlattenSections(result.TemplateJson.SectionHierarchy.Sections)
            .Select(s => s.SectionTitle)
            .ToList();

        // Verify generic section detection (should detect common headings)
        titles.Should().Contain(title =>
            title.Contains("Summary", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Professional", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Core", StringComparison.OrdinalIgnoreCase),
            "should detect common document sections");

        // Verify hierarchical structure (sections with subsections)
        var sectionsWithSubsections = result.TemplateJson.SectionHierarchy.Sections
            .Where(s => s.SubSections != null && s.SubSections.Count > 0)
            .ToList();
        
        sectionsWithSubsections.Should().NotBeEmpty("document should have hierarchical sections");

        // Verify that most sections have corresponding content (some headings may not have content)
        var sectionsWithContent = FlattenSections(result.TemplateJson.SectionHierarchy.Sections)
            .Where(s => result.ContentSections.Any(c => string.Equals(c.PlaceholderId, s.PlaceholderId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        
        sectionsWithContent.Should().NotBeEmpty("most sections should have corresponding content");
        
        // Verify at least some sections have content
        var allSections = FlattenSections(result.TemplateJson.SectionHierarchy.Sections).ToList();
        var contentRatio = allSections.Count > 0 ? (double)sectionsWithContent.Count / allSections.Count : 0.0;
        contentRatio.Should().BeGreaterThan(0.5, "at least 50% of sections should have content");
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
}

