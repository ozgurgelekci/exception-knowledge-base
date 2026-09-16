using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Domain.Knowledge;
using MongoDB.Driver;

namespace ExceptionKnowledgeBase.Infrastructure.Mongo;

public sealed class MongoKnowledgeRepository : IKnowledgeEntryRepository
{
    private readonly MongoContext _ctx;
    public MongoKnowledgeRepository(MongoContext ctx) => _ctx = ctx;

    public Task<KnowledgeEntry?> GetByIdAsync(string tenantId, string id, CancellationToken ct)
        => _ctx.KnowledgeEntries
            .Find(x => x.TenantId == tenantId && x.Id == id)
            .FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyList<KnowledgeEntry>> GetByIdsAsync(string tenantId, IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        if (list.Count == 0) return Array.Empty<KnowledgeEntry>();
        var cursor = await _ctx.KnowledgeEntries
            .Find(x => x.TenantId == tenantId && list.Contains(x.Id))
            .ToListAsync(ct);
        return cursor;
    }

    public Task UpsertAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        entry.UpdatedAt = DateTime.UtcNow;
        return _ctx.KnowledgeEntries.ReplaceOneAsync(
            x => x.Id == entry.Id,
            entry,
            new ReplaceOptions { IsUpsert = true },
            ct);
    }

    public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct)
    {
        var result = await _ctx.KnowledgeEntries.DeleteOneAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return result.DeletedCount > 0;
    }

    public async Task<IReadOnlyList<KnowledgeEntry>> GetPendingEmbeddingsAsync(int limit, CancellationToken ct)
    {
        var cursor = await _ctx.KnowledgeEntries
            .Find(x => x.EmbeddingState == EmbeddingState.Pending || x.EmbeddingState == EmbeddingState.Failed)
            .Limit(limit)
            .ToListAsync(ct);
        return cursor;
    }

    public Task MarkEmbeddedAsync(string id, string model, string modelVersion, CancellationToken ct)
    {
        var update = Builders<KnowledgeEntry>.Update
            .Set(x => x.EmbeddingState, EmbeddingState.Indexed)
            .Set(x => x.EmbeddingModel, model)
            .Set(x => x.EmbeddingModelVersion, modelVersion)
            .Set(x => x.EmbeddedAt, DateTime.UtcNow);
        return _ctx.KnowledgeEntries.UpdateOneAsync(x => x.Id == id, update, cancellationToken: ct);
    }
}

public sealed class MongoSolutionRepository : ISolutionRepository
{
    private readonly MongoContext _ctx;
    public MongoSolutionRepository(MongoContext ctx) => _ctx = ctx;

    public async Task<Solution?> GetTopSolutionForKnowledgeAsync(string tenantId, string knowledgeEntryId, CancellationToken ct)
    {
        var cursor = await _ctx.Solutions
            .Find(x => x.TenantId == tenantId && x.KnowledgeEntryId == knowledgeEntryId)
            .SortByDescending(x => x.TimesSuccessful)
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        return cursor;
    }

    public Task<Solution?> GetByIdAsync(string tenantId, string id, CancellationToken ct)
        => _ctx.Solutions.Find(x => x.TenantId == tenantId && x.Id == id).FirstOrDefaultAsync(ct)!;

    public Task RecordOutcomeAsync(string tenantId, string solutionId, bool success, CancellationToken ct)
    {
        var update = success
            ? Builders<Solution>.Update
                .Inc(x => x.TimesApplied, 1)
                .Inc(x => x.TimesSuccessful, 1)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
            : Builders<Solution>.Update
                .Inc(x => x.TimesApplied, 1)
                .Inc(x => x.TimesFailed, 1)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
        return _ctx.Solutions.UpdateOneAsync(x => x.TenantId == tenantId && x.Id == solutionId, update, cancellationToken: ct);
    }
}
