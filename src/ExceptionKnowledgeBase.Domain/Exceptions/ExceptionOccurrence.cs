using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Exceptions;

// Section 45: an individual event pointing at a Definition.
public sealed class ExceptionOccurrence
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";
    public string DefinitionId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;

    public string OriginalMessage { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? InnerException { get; set; }
    public string? ErrorCode { get; set; }

    public string? Application { get; set; }
    public string? Service { get; set; }
    public string? Module { get; set; }
    public string? Version { get; set; }
    public string? Environment { get; set; }

    public string? Host { get; set; }
    public string? Container { get; set; }
    public string? Pod { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Context { get; set; } = new();
}
