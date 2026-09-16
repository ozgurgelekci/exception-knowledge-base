using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Contracts.Knowledge;
using ExceptionKnowledgeBase.Domain.Common;
using ExceptionKnowledgeBase.Domain.Knowledge;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Application.Services;

public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IKnowledgeEntryRepository _repo;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IEmbeddingInputBuilder _inputBuilder;
    private readonly AnalysisOptions _analysis;

    public KnowledgeService(
        IKnowledgeEntryRepository repo,
        IEmbeddingService embedding,
        IVectorSearchService vectorSearch,
        IEmbeddingInputBuilder inputBuilder,
        IOptions<AnalysisOptions> analysis)
    {
        _repo = repo;
        _embedding = embedding;
        _vectorSearch = vectorSearch;
        _inputBuilder = inputBuilder;
        _analysis = analysis.Value;
    }

    public async Task<KnowledgeEntry> CreateAsync(string tenantId, CreateKnowledgeEntryRequest request, CancellationToken ct)
    {
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;
        var entry = new KnowledgeEntry
        {
            TenantId = tenantId,
            Title = request.Title,
            ExceptionTypes = request.ExceptionTypes,
            Symptoms = request.Symptoms,
            RootCause = request.RootCause,
            Solution = request.Solution,
            Verification = request.Verification,
            Tags = request.Tags,
            Status = request.Status,
            CreatedBy = request.CreatedBy
        };

        await _repo.UpsertAsync(entry, ct);
        await IndexAsync(entry, ct);
        return entry;
    }

    public Task<KnowledgeEntry?> GetAsync(string tenantId, string id, CancellationToken ct)
        => _repo.GetByIdAsync(NormalizeTenant(tenantId), id, ct);

    public async Task<KnowledgeEntry?> UpdateAsync(string tenantId, string id, CreateKnowledgeEntryRequest request, CancellationToken ct)
    {
        tenantId = NormalizeTenant(tenantId);
        var existing = await _repo.GetByIdAsync(tenantId, id, ct);
        if (existing is null) return null;

        existing.Title = request.Title;
        existing.ExceptionTypes = request.ExceptionTypes;
        existing.Symptoms = request.Symptoms;
        existing.RootCause = request.RootCause;
        existing.Solution = request.Solution;
        existing.Verification = request.Verification;
        existing.Tags = request.Tags;
        existing.Status = request.Status;
        existing.Version += 1;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.EmbeddingState = Domain.Knowledge.EmbeddingState.Pending;

        await _repo.UpsertAsync(existing, ct);
        await IndexAsync(existing, ct);
        return existing;
    }

    public Task<bool> DeleteAsync(string tenantId, string id, CancellationToken ct)
        => _repo.DeleteAsync(NormalizeTenant(tenantId), id, ct);

    private async Task IndexAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        var content = _inputBuilder.ForKnowledge(entry);
        var embedding = await _embedding.EmbedAsync(content, ct);
        await _vectorSearch.UpsertAsync(new EmbeddingUpsert(
            EntityId: entry.Id,
            EntityType: EntityTypes.Knowledge,
            TenantId: entry.TenantId,
            Content: content,
            Vector: embedding.Vector,
            Model: embedding.Model,
            ModelVersion: embedding.ModelVersion,
            Dimensions: embedding.Dimensions,
            Metadata: BuildMetadata(entry)), ct);

        await _repo.MarkEmbeddedAsync(entry.Id, embedding.Model, embedding.ModelVersion, ct);
    }

    private static Dictionary<string, string> BuildMetadata(KnowledgeEntry entry)
    {
        var meta = new Dictionary<string, string>
        {
            ["status"] = entry.Status,
            ["version"] = entry.Version.ToString()
        };
        if (entry.Tags.Count > 0) meta["tags"] = string.Join(",", entry.Tags);
        return meta;
    }

    private string NormalizeTenant(string tenantId)
        => string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;
}
