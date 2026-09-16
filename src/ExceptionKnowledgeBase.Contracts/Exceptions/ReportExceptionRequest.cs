namespace ExceptionKnowledgeBase.Contracts.Exceptions;

// Payload for POST /api/exceptions (Section 37).
public sealed class ReportExceptionRequest
{
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? InnerException { get; set; }
    public string? ErrorCode { get; set; }

    public string? Application { get; set; }
    public string? Service { get; set; }
    public string? Module { get; set; }
    public string? Version { get; set; }
    public string? Environment { get; set; }
    public string? Database { get; set; }

    public string? Host { get; set; }
    public string? Container { get; set; }
    public string? Pod { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }

    public List<string> Tags { get; set; } = new();
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public Dictionary<string, string> Context { get; set; } = new();
}
