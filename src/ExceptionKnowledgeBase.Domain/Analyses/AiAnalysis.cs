using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Analyses;

// Section 52: audit record for every AI answer.
public sealed class AiAnalysis
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";
    public string? OccurrenceId { get; set; }
    public string? DefinitionId { get; set; }
    public string? Fingerprint { get; set; }

    public string Status { get; set; } = "pending";   // pending | ready | failed
    public string? FailureReason { get; set; }

    public string Summary { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public double RootCauseConfidence { get; set; }
    public double ApplicationConfidence { get; set; }

    public List<string> RecommendedChecks { get; set; } = new();
    public List<string> RecommendedSolutions { get; set; } = new();

    public List<Evidence> Evidence { get; set; } = new();
    public List<string> Sources { get; set; } = new();

    // Section 24 – known/likely/unknown split.
    public string? KnownStatement { get; set; }
    public string? LikelyStatement { get; set; }
    public string? UnknownStatement { get; set; }

    public string EmbeddingModel { get; set; } = string.Empty;
    public string LlmModel { get; set; } = string.Empty;
    public int PromptVersion { get; set; }
    public int KnowledgeVersion { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }

    public long EmbeddingLatencyMs { get; set; }
    public long VectorSearchLatencyMs { get; set; }
    public long LlmLatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Evidence
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public double Similarity { get; set; }
}
