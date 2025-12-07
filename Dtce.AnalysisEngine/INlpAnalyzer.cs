namespace Dtce.AnalysisEngine;

public interface INlpAnalyzer
{
    Task<NlpAnalysisResult> AnalyzeAsync(string text, CancellationToken cancellationToken = default);
}

public class NlpAnalysisResult
{
    public string Formality { get; set; } = string.Empty; // formal, informal, neutral
    public double FormalityConfidence { get; set; }
    public string Tone { get; set; } = string.Empty; // persuasive, objective, serious, optimistic
    public double ToneConfidence { get; set; }
    public List<double> StyleVector { get; set; } = new(); // 768-dimensional embedding
}

