using Dtce.AnalysisEngine.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dtce.Tests;

public class NlpAnalyzerTests
{
    private readonly NlpAnalyzer _analyzer = new(NullLogger<NlpAnalyzer>.Instance);

    [Fact]
    public async Task AnalyzeAsync_FormalExecutiveSummary_ReturnsFormalNeutral()
    {
        var text = "Dear Board Members, the engineering division has achieved every strategic objective for this fiscal year.";

        var result = await _analyzer.AnalyzeAsync(text);

        result.Formality.Should().Be("formal");
        result.FormalityConfidence.Should().BeGreaterThan(0.5);
        result.Tone.Should().MatchRegex("neutral|positive");
        result.StyleVector.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeAsync_InformalPositiveLanguage_ReturnsInformalAndPositiveTone()
    {
        var text = "Hey team! We're gonna crush it this quarter and the vibe is absolutely amazing lol!";

        var result = await _analyzer.AnalyzeAsync(text);

        result.Formality.Should().Be("informal");
        result.Tone.Should().Be("positive");
        result.ToneConfidence.Should().BeGreaterThan(0.3);
        result.StyleVector.Should().HaveCountGreaterThan(0);
    }
}


