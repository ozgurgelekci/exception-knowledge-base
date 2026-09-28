using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Domain.Common;
using ExceptionKnowledgeBase.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Analysis;

public sealed class DefinitionClustererTests
{
    [Fact]
    public async Task Assigns_new_cluster_id_when_no_neighbours_match()
    {
        var (sut, defs) = Build(neighbours: Array.Empty<VectorSearchHit>(), existing: new());
        var def = NewDefinition(id: "def-1");

        var clusterId = await sut.AssignAsync(def, new float[] { 0.1f, 0.2f }, CancellationToken.None);

        Assert.Equal("def-1", clusterId);
        Assert.Equal("def-1", def.ClusterId);
        Assert.Empty(def.LinkedDefinitionIds);
        Assert.Empty(defs.Upserts);
    }

    [Fact]
    public async Task Adopts_smallest_existing_cluster_and_folds_neighbours()
    {
        var neighbourA = NewDefinition(id: "def-a", clusterId: "cluster-z");
        var neighbourB = NewDefinition(id: "def-b", clusterId: "cluster-a");
        var (sut, defs) = Build(
            neighbours: new[]
            {
                Hit(neighbourA.Id, similarity: 0.95),
                Hit(neighbourB.Id, similarity: 0.92)
            },
            existing: new() { [neighbourA.Id] = neighbourA, [neighbourB.Id] = neighbourB });

        var def = NewDefinition(id: "def-new");
        var clusterId = await sut.AssignAsync(def, new float[] { 0.1f }, CancellationToken.None);

        Assert.Equal("cluster-a", clusterId);
        Assert.Equal("cluster-a", def.ClusterId);
        Assert.Contains("def-a", def.LinkedDefinitionIds);
        Assert.Contains("def-b", def.LinkedDefinitionIds);

        // Sibling with divergent cluster gets rewritten to the winning cluster id.
        Assert.Equal("cluster-a", neighbourA.ClusterId);
        Assert.Contains(defs.Upserts, u => u.Id == "def-a");
    }

    [Fact]
    public async Task Skips_neighbours_below_similarity_threshold()
    {
        var neighbour = NewDefinition(id: "cousin", clusterId: "cluster-x");
        var (sut, _) = Build(
            neighbours: new[] { Hit(neighbour.Id, similarity: 0.5) },
            existing: new() { [neighbour.Id] = neighbour });

        var def = NewDefinition(id: "def-new");
        var clusterId = await sut.AssignAsync(def, new float[] { 0f }, CancellationToken.None);

        Assert.Equal("def-new", clusterId);
    }

    [Fact]
    public async Task Returns_existing_cluster_when_disabled()
    {
        var opts = new AnalysisOptions { ClusteringEnabled = false };
        var sut = new DefinitionClusterer(
            new StubSearch(Array.Empty<VectorSearchHit>()),
            new StubDefs(new()),
            Options.Create(opts),
            NullLogger<DefinitionClusterer>.Instance);

        var def = NewDefinition(id: "def-x", clusterId: "already");
        var clusterId = await sut.AssignAsync(def, new float[] { 0f }, CancellationToken.None);

        Assert.Equal("already", clusterId);
    }

    private static (DefinitionClusterer sut, StubDefs defs) Build(
        IReadOnlyList<VectorSearchHit> neighbours,
        Dictionary<string, ExceptionDefinition> existing)
    {
        var opts = new AnalysisOptions
        {
            ClusteringEnabled = true,
            ClusterSimilarityThreshold = 0.9,
            ClusterCandidatePoolSize = 10,
            ClusterMaxLinkedIds = 50
        };
        var defs = new StubDefs(existing);
        var sut = new DefinitionClusterer(
            new StubSearch(neighbours),
            defs,
            Options.Create(opts),
            NullLogger<DefinitionClusterer>.Instance);
        return (sut, defs);
    }

    private static ExceptionDefinition NewDefinition(string id, string? clusterId = null) => new()
    {
        Id = id,
        TenantId = "acme",
        Fingerprint = "fp-" + id,
        ExceptionType = "System.TimeoutException",
        ClusterId = clusterId
    };

    private static VectorSearchHit Hit(string id, double similarity) => new(
        EntityId: id,
        EntityType: EntityTypes.Exception,
        TenantId: "acme",
        Content: "",
        Distance: 1 - similarity,
        Similarity: similarity);

    private sealed class StubSearch : IVectorSearchService
    {
        private readonly IReadOnlyList<VectorSearchHit> _hits;
        public StubSearch(IReadOnlyList<VectorSearchHit> hits) => _hits = hits;
        public Task UpsertAsync(EmbeddingUpsert upsert, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken cancellationToken)
            => Task.FromResult(_hits);
    }

    private sealed class StubDefs : IExceptionDefinitionRepository
    {
        private readonly Dictionary<string, ExceptionDefinition> _byId;
        public List<ExceptionDefinition> Upserts { get; } = new();
        public StubDefs(Dictionary<string, ExceptionDefinition> byId) => _byId = byId;

        public Task<ExceptionDefinition?> GetByFingerprintAsync(string tenantId, string fingerprint, CancellationToken cancellationToken)
            => Task.FromResult<ExceptionDefinition?>(null);
        public Task<ExceptionDefinition?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken)
            => Task.FromResult<ExceptionDefinition?>(_byId.GetValueOrDefault(id));
        public Task UpsertAsync(ExceptionDefinition definition, CancellationToken cancellationToken)
        {
            Upserts.Add(definition);
            _byId[definition.Id] = definition;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ExceptionDefinition>> GetByIdsAsync(string tenantId, IEnumerable<string> ids, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ExceptionDefinition>>(
                ids.Select(id => _byId.GetValueOrDefault(id)).Where(x => x is not null).ToList()!);
        public Task<IReadOnlyList<ExceptionDefinition>> GetPendingEmbeddingsAsync(int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ExceptionDefinition>>(Array.Empty<ExceptionDefinition>());
        public Task MarkEmbeddedAsync(string id, string model, string modelVersion, int dimensions, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task IncrementOccurrenceAsync(string id, DateTime occurredAt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
