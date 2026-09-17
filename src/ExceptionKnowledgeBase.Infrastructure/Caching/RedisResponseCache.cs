using System.Text.Json;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ExceptionKnowledgeBase.Infrastructure.Caching;

public sealed class RedisAnalysisResponseCache : IAnalysisResponseCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisOptions _options;

    public RedisAnalysisResponseCache(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task<AnalyzeExceptionResponse?> GetAsync(AnalysisCacheKey key, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(Build(key));
        if (!value.HasValue) return null;
        try
        {
            return JsonSerializer.Deserialize<AnalyzeExceptionResponse>(value!, Json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetAsync(AnalysisCacheKey key, AnalyzeExceptionResponse response, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(response, Json);
        await db.StringSetAsync(Build(key), payload, TimeSpan.FromSeconds(_options.AnalysisTtlSeconds));
    }

    private static string Build(AnalysisCacheKey k)
        => $"analysis:{k.TenantId}:{k.Fingerprint}:{k.EmbeddingModel}:{k.EmbeddingModelVersion}:{k.ChatModel}:pv{k.PromptVersion}:kv{k.KnowledgeVersion}";
}

public sealed class RedisSearchResponseCache : ISearchResponseCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _redis;
    private readonly AnalysisOptions _analysis;

    public RedisSearchResponseCache(IConnectionMultiplexer redis, IOptions<AnalysisOptions> analysis)
    {
        _redis = redis;
        _analysis = analysis.Value;
    }

    public async Task<SearchExceptionsResponse?> GetAsync(SearchCacheKey key, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(Build(key));
        if (!value.HasValue) return null;
        try
        {
            return JsonSerializer.Deserialize<SearchExceptionsResponse>(value!, Json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetAsync(SearchCacheKey key, SearchExceptionsResponse response, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(response, Json);
        await db.StringSetAsync(Build(key), payload, TimeSpan.FromSeconds(_analysis.SearchCacheTtlSeconds));
    }

    private static string Build(SearchCacheKey k)
        => $"search:{k.TenantId}:{k.QueryHash}:{k.EmbeddingModel}:{k.EmbeddingModelVersion}:k{k.TopK}:m{k.MinSimilarity:F2}";
}
