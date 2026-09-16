using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Exceptions;

// Section 45–46: One embedding per (fingerprint, tenant); occurrences are references.
public sealed class ExceptionDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";

    // SHA256 of the normalized exception (Section 9).
    public string Fingerprint { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;

    // Section 10: keep both.
    public string OriginalMessage { get; set; } = string.Empty;
    public string NormalizedMessage { get; set; } = string.Empty;

    // Section 12: text that was actually embedded (audit + reproducibility).
    public string? EmbeddingContent { get; set; }

    public List<string> Tags { get; set; } = new();
    public string? Category { get; set; }
    public string? Severity { get; set; }

    public string? Database { get; set; }
    public string? Module { get; set; }

    public long OccurrenceCount { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Embedding lifecycle for the worker (Section 42).
    public EmbeddingState EmbeddingState { get; set; } = EmbeddingState.Pending;
    public string? EmbeddingModel { get; set; }
    public string? EmbeddingModelVersion { get; set; }
    public int? EmbeddingDimensions { get; set; }
    public DateTime? EmbeddedAt { get; set; }
    public int EmbeddingAttempts { get; set; }
    public string? EmbeddingError { get; set; }

    public List<string> LinkedKnowledgeEntryIds { get; set; } = new();
}

public enum EmbeddingState
{
    Pending = 0,
    Processing = 1,
    Indexed = 2,
    Failed = 3
}
