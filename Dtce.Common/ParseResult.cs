using Dtce.Common.Models;

namespace Dtce.Common;

public class ParseResult
{
    public TemplateJson TemplateJson { get; set; } = new();
    public List<ContentSection> ContentSections { get; set; } = new();
}

public class ContentSection
{
    public string PlaceholderId { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string SampleText { get; set; } = string.Empty;
    public int WordCount { get; set; }
}

