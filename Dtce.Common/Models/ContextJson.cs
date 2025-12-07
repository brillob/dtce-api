namespace Dtce.Common.Models;

public class ContextJson
{
    public LinguisticStyleAttributes LinguisticStyle { get; set; } = new();
    public List<ContentBlock> ContentBlocks { get; set; } = new();
    public AdministrativeMetadata? AdministrativeMetadata { get; set; }
}

public class LinguisticStyleAttributes
{
    public string OverallFormality { get; set; } = string.Empty; // formal, informal, neutral
    public double FormalityConfidenceScore { get; set; } // 0.0 to 1.0
    public string DominantTone { get; set; } = string.Empty; // persuasive, objective, serious, optimistic
    public double ToneConfidenceScore { get; set; } // 0.0 to 1.0
    public List<double> WritingStyleVector { get; set; } = new(); // 768-dimensional embedding
}

public class ContentBlock
{
    public string PlaceholderId { get; set; } = string.Empty;
    public string SectionSampleText { get; set; } = string.Empty;
    public int WordCount { get; set; }
}

public class AdministrativeMetadata
{
    public DateTime? CreationDate { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? RetentionSchedule { get; set; }
}

