namespace Dtce.Common;

public enum JobStatus
{
    Pending,
    Processing,
    LayoutDetectionInProgress,
    ParsingInProgress,
    AnalysisInProgress,
    Complete,
    Failed
}

public enum DocumentType
{
    Docx,
    Pdf,
    GoogleDoc
}

public class JobRequest
{
    public string JobId { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; }
    public string? DocumentUrl { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class JobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SubmitJobRequest
{
    public byte[]? DocumentData { get; set; }
    public string? DocumentUrl { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}

public class SubmitJobResponse
{
    public string JobId { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
}
