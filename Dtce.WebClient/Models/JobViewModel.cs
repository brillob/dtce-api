namespace Dtce.WebClient.Models;

public class JobViewModel
{
    public string? JobId { get; set; }
    public string Status { get; set; } = "Idle";
    public string StatusMessage { get; set; } = string.Empty;
    public string? TemplateJsonUrl { get; set; }
    public string? ContextJsonUrl { get; set; }
    public IReadOnlyList<Dtce.Identity.Models.ApiKeyRecord> ApiKeys { get; set; } = Array.Empty<Dtce.Identity.Models.ApiKeyRecord>();
    public string? SelectedApiKey { get; set; }
}

