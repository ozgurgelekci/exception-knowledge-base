using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Knowledge;

// Section 29: human-verified solution artifact, embedded separately.
public sealed class KnowledgeEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";
    public string Title { get; set; } = string.Empty;
    public List<string> ExceptionTypes { get; set; } = new();
    public List<string> Symptoms { get; set; } = new();
    public string RootCause { get; set; } = string.Empty;
    public List<string> Solution { get; set; } = new();
    public List<string> Verification { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string Status { get; set; } = "draft";     // draft | verified | archived

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? VerifiedBy { get; set; }

    public int Version { get; set; } = 1;

    public EmbeddingState EmbeddingState { get; set; } = EmbeddingState.Pending;
    public string? EmbeddingModel { get; set; }
    public string? EmbeddingModelVersion { get; set; }
    public DateTime? EmbeddedAt { get; set; }
}

public enum EmbeddingState
{
    Pending = 0,
    Processing = 1,
    Indexed = 2,
    Failed = 3
}
