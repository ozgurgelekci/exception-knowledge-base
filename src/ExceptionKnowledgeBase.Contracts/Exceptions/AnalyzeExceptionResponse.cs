namespace ExceptionKnowledgeBase.Contracts.Exceptions;

// Structured response for POST /api/exceptions/analyze (Section 56).
public sealed class AnalyzeExceptionResponse
{
    public string AnalysisId { get; set; } = string.Empty;
    public string Status { get; set; } = "ready";       // pending | ready | failed

    public string Summary { get; set; } = string.Empty;
    public RootCauseDto RootCause { get; set; } = new();
    public double Confidence { get; set; }

    public List<EvidenceDto> Evidence { get; set; } = new();
    public List<string> Sources { get; set; } = new();

    public List<string> RecommendedChecks { get; set; } = new();
    public List<string> RecommendedSolutions { get; set; } = new();

    public string? Known { get; set; }
    public string? Likely { get; set; }
    public string? Unknown { get; set; }

    public UsageDto Usage { get; set; } = new();
}

public sealed class RootCauseDto
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public sealed class EvidenceDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public double Similarity { get; set; }
}

public sealed class UsageDto
{
    public string EmbeddingModel { get; set; } = string.Empty;
    public string LlmModel { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public long EmbeddingLatencyMs { get; set; }
    public long VectorSearchLatencyMs { get; set; }
    public long LlmLatencyMs { get; set; }
    public long TotalLatencyMs { get; set; }
}
