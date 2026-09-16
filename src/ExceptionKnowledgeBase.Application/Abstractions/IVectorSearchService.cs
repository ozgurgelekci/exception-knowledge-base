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
    int Limit);

public sealed record VectorSearchHit(
    string EntityId,
    string EntityType,
    string TenantId,
    string Content,
    double Distance,
    double Similarity);
