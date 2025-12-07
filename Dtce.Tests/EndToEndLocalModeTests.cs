using System.Text.Json;
using Dtce.AnalysisEngine.Services;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Dtce.Tests;

public class EndToEndLocalModeTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndLocalModeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LocalMode_EndToEnd_GeneratesTemplateAndContext()
    {
        var storage = new TestObjectStorage();
        var docStorageKey = "documents/sample/resume.docx";
        storage.SeedFileFromPath(
            docStorageKey,
            TestResourcePaths.GetSampleDocument("ResLatest-EngMgr-Fin.docx"),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var docxHandler = new DocxHandler(storage, NullLogger<DocxHandler>.Instance);
        var parseResult = await docxHandler.ParseAsync(new JobRequest
        {
            JobId = "local-mode-job",
            DocumentType = DocumentType.Docx,
            FilePath = docStorageKey
        });

        var nlpAnalyzer = new NlpAnalyzer(NullLogger<NlpAnalyzer>.Instance);
        var cvAnalyzer = new ComputerVisionAnalyzer(storage, NullLogger<ComputerVisionAnalyzer>.Instance);

        var combinedText = string.Join(" ", parseResult.ContentSections.Select(s => s.SampleText));
        var nlpResult = await nlpAnalyzer.AnalyzeAsync(combinedText);
        var logos = await cvAnalyzer.DetectLogosAsync(parseResult);

        parseResult.TemplateJson.LogoMap = logos;

        var contextJson = new ContextJson
        {
            LinguisticStyle = new LinguisticStyleAttributes
            {
                OverallFormality = nlpResult.Formality,
                FormalityConfidenceScore = nlpResult.FormalityConfidence,
                DominantTone = nlpResult.Tone,
                ToneConfidenceScore = nlpResult.ToneConfidence,
                WritingStyleVector = nlpResult.StyleVector
            },
            ContentBlocks = parseResult.ContentSections.Select(s => new ContentBlock
            {
                PlaceholderId = s.PlaceholderId,
                SectionSampleText = s.SampleText,
                WordCount = s.WordCount
            }).ToList()
        };

        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        var templateJsonString = JsonSerializer.Serialize(parseResult.TemplateJson, serializerOptions);
        var contextJsonString = JsonSerializer.Serialize(contextJson, serializerOptions);

        _output.WriteLine("=== Template JSON ===");
        _output.WriteLine(templateJsonString);
        _output.WriteLine("=== Context JSON ===");
        _output.WriteLine(contextJsonString);

        parseResult.TemplateJson.SectionHierarchy.Sections.Should().NotBeEmpty();
        contextJson.ContentBlocks.Should().NotBeEmpty();
    }
}


