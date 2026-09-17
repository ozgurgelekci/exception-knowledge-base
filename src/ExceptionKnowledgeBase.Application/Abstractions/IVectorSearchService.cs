namespace ExceptionKnowledgeBase.Application.Abstractions;

public interface IVectorSearchService
{
    Task UpsertAsync(EmbeddingUpsert upsert, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchQuery query,
        CancellationToken cancellationToken);
}

public sealed record EmbeddingUpsert(
    string EntityId,
    string EntityType,
    string TenantId,
    string Content,
    float[] Vector,
    string Model,
    string ModelVersion,
    int Dimensions,
    IReadOnlyDictionary<string, string>? Metadata);

public sealed record VectorSearchQuery(
    float[] Vector,
    string TenantId,
    string? EntityType,
    IReadOnlyDictionary<string, string>? MetadataFilters,
    int Limit,
    // Phase 2 (§16): hybrid search — when QueryText is set and UseHybrid is true,
    // the tsvector rank is fused with the vector rank via Reciprocal Rank Fusion.
    string? QueryText = null,
    bool UseHybrid = false,
    double HybridVectorWeight = 0.7,
    double HybridKeywordWeight = 0.3,
    int RrfK = 60);

public sealed record VectorSearchHit(
    string EntityId,
    string EntityType,
    string TenantId,
    string Content,
    double Distance,
    double Similarity);
