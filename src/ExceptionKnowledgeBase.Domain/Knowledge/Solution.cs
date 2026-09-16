using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExceptionKnowledgeBase.Domain.Knowledge;

// Section 26/31: tracks solution success rates for ranking signals.
public sealed class Solution
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string TenantId { get; set; } = "default";
    public string KnowledgeEntryId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int TimesApplied { get; set; }
    public int TimesSuccessful { get; set; }
    public int TimesFailed { get; set; }

    public double SuccessRate =>
        TimesApplied == 0 ? 0d : (double)TimesSuccessful / TimesApplied;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
