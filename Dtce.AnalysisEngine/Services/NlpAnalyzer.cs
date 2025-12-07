using System.Text.RegularExpressions;
using VaderSharp2;

namespace Dtce.AnalysisEngine.Services;

public class NlpAnalyzer : INlpAnalyzer
{
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{M}']+\b", RegexOptions.Compiled);
    private static readonly Regex ContractionRegex = new(@"(?i)\b(\w+)'(re|ve|ll|d|m|s|t)\b", RegexOptions.Compiled);
    private static readonly string[] InformalMarkers =
    {
        "gonna", "wanna", "kinda", "sorta", "lol", "btw", "fyi", "hey", "yo", "what's up", "dude"
    };
    private static readonly SentimentIntensityAnalyzer SentimentAnalyzer = new();

    private readonly ILogger<NlpAnalyzer> _logger;

    public NlpAnalyzer(ILogger<NlpAnalyzer> logger)
    {
        _logger = logger;
    }

    public Task<NlpAnalysisResult> AnalyzeAsync(string text, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing NLP analysis on text ({Length} characters)", text.Length);
        text ??= string.Empty;

        var tokens = WordRegex.Matches(text)
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .ToArray();

        var wordCount = Math.Max(1, tokens.Length);
        var contractionCount = ContractionRegex.Matches(text).Count;
        var informalCount = tokens.Count(t =>
            InformalMarkers.Any(marker => string.Equals(marker, t, StringComparison.OrdinalIgnoreCase)));

        var uppercaseWords = tokens.Count(t => t.Length > 1 && t.All(char.IsUpper));

        var contractionRatio = (double)contractionCount / wordCount;
        var informalRatio = (double)informalCount / wordCount;
        var uppercaseRatio = (double)uppercaseWords / wordCount;

        var formalityScore = 1.0
                             - contractionRatio * 0.8
                             - Math.Min(0.8, informalRatio * 2.0)
                             - Math.Min(0.3, uppercaseRatio * 0.3);
        formalityScore = Math.Clamp(formalityScore, 0, 1);

        var formality = formalityScore >= 0.55 ? "formal" : "informal";
        var formalityConfidence = Math.Clamp(Math.Abs(formalityScore - 0.5) * 2, 0.1, 1.0);

        var sentiment = SentimentAnalyzer.PolarityScores(text);
        var tone = sentiment.Compound switch
        {
            > 0.25 => "positive",
            < -0.25 => "negative",
            _ => "neutral"
        };
        var toneConfidence = Math.Clamp(Math.Abs(sentiment.Compound), 0.05, 1.0);

        var styleVector = GenerateStyleVector(text);

        var result = new NlpAnalysisResult
        {
            Formality = formality,
            FormalityConfidence = Math.Round(formalityConfidence, 3),
            Tone = tone,
            ToneConfidence = Math.Round(toneConfidence, 3),
            StyleVector = styleVector.Select(v => (double)v).ToList()
        };

        _logger.LogInformation("NLP analysis completed: Formality={Formality}, Tone={Tone}", result.Formality, result.Tone);
        return Task.FromResult(result);
    }

    private static float[] GenerateStyleVector(string text)
    {
        const int vectorSize = 128;
        var vector = new float[vectorSize];

        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        var tokens = WordRegex.Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

        if (tokens.Length == 0)
        {
            return vector;
        }

        foreach (var token in tokens)
        {
            var hash = (uint)HashCode.Combine(token);
            var index = (int)(hash % vectorSize);
            vector[index] += 1f;

            var charSum = token.Sum(c => c);
            var charIndex = (int)((charSum + token.Length) % vectorSize);
            vector[charIndex] += 0.5f;
        }

        var magnitude = Math.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] / magnitude);
            }
        }

        return vector;
    }
}

