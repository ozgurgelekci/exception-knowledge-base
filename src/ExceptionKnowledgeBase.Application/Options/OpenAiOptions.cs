namespace ExceptionKnowledgeBase.Application.Options;

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public string EmbeddingModelVersion { get; set; } = "1";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public int ChatMaxTokens { get; set; } = 1024;
    public double ChatTemperature { get; set; } = 0.2;
    public int PromptVersion { get; set; } = 1;
}

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "exception_kb";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=exception_kb;Username=ekb;Password=ekb";
}

public sealed class RedisOptions
{
    public string? ConnectionString { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);
    public int EmbeddingTtlSeconds { get; set; } = 60 * 60 * 24 * 7;
    public int AnalysisTtlSeconds { get; set; } = 60 * 60;
}

public sealed class AnalysisOptions
{
    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.75;
    public int KnowledgeVersion { get; set; } = 1;
    public string DefaultTenantId { get; set; } = "default";
    public int VectorCandidatePoolSize { get; set; } = 20;

    // Phase 2 (§16): hybrid search re-ranking.
    public bool HybridSearchEnabled { get; set; } = true;
    public double HybridVectorWeight { get; set; } = 0.7;
    public double HybridKeywordWeight { get; set; } = 0.3;
    public int HybridRrfK { get; set; } = 60;

    // Phase 2 (§36): cache AI analysis + search responses.
    public bool AnalysisCacheEnabled { get; set; } = true;
    public int SearchCacheTtlSeconds { get; set; } = 300;
    public bool RequireVerifiedKnowledge { get; set; } = false;

    // Phase 3 (§74): re-rank knowledge candidates by historical solution success rate.
    // Final rank score = similarity + SuccessRateBoost * successRate (0..1).
    // Only kicks in after MinSimilarity filter; keeps semantic gating intact.
    public bool SuccessRateRerankEnabled { get; set; } = true;
    public double SuccessRateBoost { get; set; } = 0.15;

    // Phase 3 (§74): merge tenant knowledge with an org-wide "global" tenant.
    public bool GlobalKnowledgeEnabled { get; set; } = false;
    public string GlobalTenantId { get; set; } = "global";

    // Phase 4 (§75): fingerprint trend endpoint horizon.
    public int TrendDefaultDays { get; set; } = 14;
    public int TrendMaxDays { get; set; } = 90;
}

// Phase 2 (§65): per-tenant rate limiting.
public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public int AnalyzePermitPerMinute { get; set; } = 30;
    public int SearchPermitPerMinute { get; set; } = 120;
    public int KnowledgePermitPerMinute { get; set; } = 60;
    public int QueueLimit { get; set; } = 4;
}
