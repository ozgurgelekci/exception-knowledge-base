using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Domain.Common;
using ExceptionKnowledgeBase.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Application.Analysis;

// Phase 3 (§74): automatic clustering beyond fingerprint. Two definitions with
// distinct fingerprints (different service, wording, etc.) may still describe
// the same underlying problem — we detect that via cosine similarity between
// their embeddings and share a ClusterId so dashboards + reviewers can collapse
// them into a single incident thread.
public interface IDefinitionClusterer
{
    Task<string?> AssignAsync(
        ExceptionDefinition definition,
        float[] embedding,
        CancellationToken cancellationToken);
}

public sealed class DefinitionClusterer : IDefinitionClusterer
{
    private readonly IVectorSearchService _vectorSearch;
    private readonly IExceptionDefinitionRepository _definitions;
    private readonly AnalysisOptions _options;
    private readonly ILogger<DefinitionClusterer> _logger;

    public DefinitionClusterer(
        IVectorSearchService vectorSearch,
        IExceptionDefinitionRepository definitions,
        IOptions<AnalysisOptions> options,
        ILogger<DefinitionClusterer> logger)
    {
        _vectorSearch = vectorSearch;
        _definitions = definitions;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> AssignAsync(
        ExceptionDefinition definition,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        if (!_options.ClusteringEnabled) return definition.ClusterId;

        var hits = await _vectorSearch.SearchAsync(new VectorSearchQuery(
            Vector: embedding,
            TenantId: definition.TenantId,
            EntityType: EntityTypes.Exception,
            MetadataFilters: null,
            Limit: _options.ClusterCandidatePoolSize), cancellationToken);

        var neighbours = hits
            .Where(h => !string.Equals(h.EntityId, definition.Id, StringComparison.Ordinal))
            .Where(h => h.Similarity >= _options.ClusterSimilarityThreshold)
            .ToList();

        if (neighbours.Count == 0)
        {
            // First member of a new cluster — reuse the existing ClusterId
            // when we've already been assigned one (e.g. re-embedding).
            definition.ClusterId ??= definition.Id;
            return definition.ClusterId;
        }

        var neighbourDefs = await _definitions.GetByIdsAsync(
            definition.TenantId,
            neighbours.Select(n => n.EntityId),
            cancellationToken);

        // Pick the lexicographically smallest existing ClusterId so merges are
        // deterministic and idempotent across concurrent writers.
        var clusterId = neighbourDefs
            .Select(d => d.ClusterId ?? d.Id)
            .Concat(new[] { definition.ClusterId ?? definition.Id })
            .OrderBy(id => id, StringComparer.Ordinal)
            .First();

        var linked = definition.LinkedDefinitionIds ?? new List<string>();
        foreach (var n in neighbourDefs)
            if (!linked.Contains(n.Id, StringComparer.Ordinal))
                linked.Add(n.Id);

        // Cap the linked list so a runaway cluster can't bloat the document.
        if (linked.Count > _options.ClusterMaxLinkedIds)
            linked = linked
                .OrderBy(id => id, StringComparer.Ordinal)
                .Take(_options.ClusterMaxLinkedIds)
                .ToList();

        definition.ClusterId = clusterId;
        definition.LinkedDefinitionIds = linked;

        // Fold sibling definitions into the same cluster so future queries see
        // a consistent view. Best-effort; a follow-up rebalance job could reconcile.
        foreach (var n in neighbourDefs)
        {
            if (string.Equals(n.ClusterId, clusterId, StringComparison.Ordinal)) continue;
            n.ClusterId = clusterId;
            try
            {
                await _definitions.UpsertAsync(n, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Cluster fold failed for neighbour {Id}; skipping", n.Id);
            }
        }

        return clusterId;
    }
}
