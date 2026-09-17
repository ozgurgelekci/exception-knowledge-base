using ExceptionKnowledgeBase.Contracts.Exceptions;

namespace ExceptionKnowledgeBase.Application.Abstractions;

// Phase 2 (§36): cache full analysis/search responses using composite keys that
// include tenant, fingerprint/query, prompt/model/knowledge versions so any
// invalidating change naturally rolls the key over.
public interface IAnalysisResponseCache
{
    Task<AnalyzeExceptionResponse?> GetAsync(AnalysisCacheKey key, CancellationToken cancellationToken);
    Task SetAsync(AnalysisCacheKey key, AnalyzeExceptionResponse response, CancellationToken cancellationToken);
}

public interface ISearchResponseCache
{
    Task<SearchExceptionsResponse?> GetAsync(SearchCacheKey key, CancellationToken cancellationToken);
    Task SetAsync(SearchCacheKey key, SearchExceptionsResponse response, CancellationToken cancellationToken);
}

public sealed record AnalysisCacheKey(
    string TenantId,
    string Fingerprint,
    string EmbeddingModel,
    string EmbeddingModelVersion,
    string ChatModel,
    int PromptVersion,
    int KnowledgeVersion);

public sealed record SearchCacheKey(
    string TenantId,
    string QueryHash,
    string EmbeddingModel,
    string EmbeddingModelVersion,
    int TopK,
    double MinSimilarity);
