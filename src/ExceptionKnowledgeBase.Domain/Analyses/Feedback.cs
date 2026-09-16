using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Analyses;

// Section 27/40: developer feedback on AI answers.
public sealed class Feedback
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";
    public string AnalysisId { get; set; } = string.Empty;
    public string? SolutionId { get; set; }

    public FeedbackResult Result { get; set; } = FeedbackResult.Unspecified;
    public bool Helpful { get; set; }
    public string? Comment { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public enum FeedbackResult
{
    Unspecified = 0,
    Resolved = 1,
    PartiallyResolved = 2,
    NotResolved = 3
}
