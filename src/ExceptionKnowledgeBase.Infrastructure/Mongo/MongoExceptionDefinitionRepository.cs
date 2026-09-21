using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Domain.Exceptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ExceptionKnowledgeBase.Infrastructure.Mongo;

public sealed class MongoExceptionDefinitionRepository : IExceptionDefinitionRepository
{
    private readonly MongoContext _ctx;
    public MongoExceptionDefinitionRepository(MongoContext ctx) => _ctx = ctx;

    public Task<ExceptionDefinition?> GetByFingerprintAsync(string tenantId, string fingerprint, CancellationToken ct)
        => _ctx.ExceptionDefinitions
            .Find(x => x.TenantId == tenantId && x.Fingerprint == fingerprint)
            .FirstOrDefaultAsync(ct)!;

    public Task<ExceptionDefinition?> GetByIdAsync(string tenantId, string id, CancellationToken ct)
        => _ctx.ExceptionDefinitions
            .Find(x => x.TenantId == tenantId && x.Id == id)
            .FirstOrDefaultAsync(ct)!;

    public Task UpsertAsync(ExceptionDefinition definition, CancellationToken ct)
        => _ctx.ExceptionDefinitions.ReplaceOneAsync(
            x => x.Id == definition.Id,
            definition,
            new ReplaceOptions { IsUpsert = true },
            ct);

    public async Task<IReadOnlyList<ExceptionDefinition>> GetByIdsAsync(string tenantId, IEnumerable<string> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<ExceptionDefinition>();
        var cursor = await _ctx.ExceptionDefinitions
            .Find(x => x.TenantId == tenantId && idList.Contains(x.Id))
            .ToListAsync(ct);
        return cursor;
    }

    public async Task<IReadOnlyList<ExceptionDefinition>> GetPendingEmbeddingsAsync(int limit, CancellationToken ct)
    {
        var cursor = await _ctx.ExceptionDefinitions
            .Find(x => x.EmbeddingState == EmbeddingState.Pending || x.EmbeddingState == EmbeddingState.Failed)
            .Limit(limit)
            .ToListAsync(ct);
        return cursor;
    }

    public Task MarkEmbeddedAsync(string id, string model, string modelVersion, int dimensions, CancellationToken ct)
    {
        var update = Builders<ExceptionDefinition>.Update
            .Set(x => x.EmbeddingState, EmbeddingState.Indexed)
            .Set(x => x.EmbeddingModel, model)
            .Set(x => x.EmbeddingModelVersion, modelVersion)
            .Set(x => x.EmbeddingDimensions, dimensions)
            .Set(x => x.EmbeddedAt, DateTime.UtcNow)
            .Set(x => x.EmbeddingError, null);
        return _ctx.ExceptionDefinitions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }

    public Task MarkFailedAsync(string id, string error, CancellationToken ct)
    {
        var update = Builders<ExceptionDefinition>.Update
            .Set(x => x.EmbeddingState, EmbeddingState.Failed)
            .Set(x => x.EmbeddingError, error)
            .Inc(x => x.EmbeddingAttempts, 1);
        return _ctx.ExceptionDefinitions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }

    public Task IncrementOccurrenceAsync(string id, DateTime occurredAt, CancellationToken ct)
    {
        var update = Builders<ExceptionDefinition>.Update
            .Inc(x => x.OccurrenceCount, 1L)
            .Set(x => x.LastSeenAt, occurredAt);
        return _ctx.ExceptionDefinitions.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }
}

public sealed class MongoExceptionOccurrenceRepository : IExceptionOccurrenceRepository
{
    private readonly MongoContext _ctx;
    public MongoExceptionOccurrenceRepository(MongoContext ctx) => _ctx = ctx;

    public Task InsertAsync(ExceptionOccurrence occurrence, CancellationToken ct)
        => _ctx.ExceptionOccurrences.InsertOneAsync(occurrence, cancellationToken: ct);

    // Phase 4 (§75): daily grouping via aggregation pipeline. Fingerprint index makes
    // this cheap even without a dedicated summary collection.
    public async Task<IReadOnlyList<FingerprintDailyCount>> GetTrendsAsync(string tenantId, DateTime sinceUtc, CancellationToken ct)
    {
        // MongoContext applies CamelCaseElementNameConvention → field names are camelCase.
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "tenantId", tenantId },
                { "occurredAt", new BsonDocument("$gte", sinceUtc) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "fingerprint", "$fingerprint" },
                        { "day", new BsonDocument("$dateTrunc", new BsonDocument
                            {
                                { "date", "$occurredAt" },
                                { "unit", "day" }
                            })
                        }
                    }
                },
                { "count", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument
            {
                { "_id.day", 1 },
                { "count", -1 }
            })
        };

        var cursor = await _ctx.ExceptionOccurrences.AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct);
        var docs = await cursor.ToListAsync(ct);
        var results = new List<FingerprintDailyCount>(docs.Count);
        foreach (var doc in docs)
        {
            var key = doc["_id"].AsBsonDocument;
            results.Add(new FingerprintDailyCount(
                Fingerprint: key["fingerprint"].AsString,
                Day: key["day"].ToUniversalTime(),
                Count: doc["count"].ToInt64()));
        }
        return results;
    }
}
