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
}
