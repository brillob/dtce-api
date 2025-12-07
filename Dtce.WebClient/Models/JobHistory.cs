namespace Dtce.WebClient.Models;

public class JobHistory
{
    public string JobId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? DocumentUrl { get; set; }
    public string InputType { get; set; } = "file"; // "file" or "url"
    public string Status { get; set; } = "Pending";
    public string StatusMessage { get; set; } = string.Empty;
    public string? TemplateJsonUrl { get; set; }
    public string? ContextJsonUrl { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

