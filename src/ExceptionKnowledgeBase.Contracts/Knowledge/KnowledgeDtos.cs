namespace ExceptionKnowledgeBase.Contracts.Knowledge;

// Section 39.
public sealed class CreateKnowledgeEntryRequest
{
    public string Title { get; set; } = string.Empty;
    public List<string> ExceptionTypes { get; set; } = new();
    public List<string> Symptoms { get; set; } = new();
    public string RootCause { get; set; } = string.Empty;
    public List<string> Solution { get; set; } = new();
    public List<string> Verification { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? CreatedBy { get; set; }
    public string Status { get; set; } = "draft";
}

public sealed class KnowledgeEntryResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> ExceptionTypes { get; set; } = new();
    public List<string> Symptoms { get; set; } = new();
    public string RootCause { get; set; } = string.Empty;
    public List<string> Solution { get; set; } = new();
    public List<string> Verification { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Status { get; set; } = "draft";
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
