using ExceptionKnowledgeBase.Contracts.Exceptions;
using ExceptionKnowledgeBase.Domain.Analyses;

namespace ExceptionKnowledgeBase.Application.Analysis;

// Single source of truth for AiAnalysis → AnalyzeExceptionResponse. Called from
// both the synchronous analyze path and the /api/analyses/{id} poll endpoint
// so new fields (clusterId, suggestedLogQueries, …) don't have to be added twice.
public static class AnalysisResponseMapper
{
    public static AnalyzeExceptionResponse ToResponse(AiAnalysis a) => new()
    {
        AnalysisId = a.Id,
        Status = a.Status,
        Summary = a.Summary,
        RootCause = new RootCauseDto { Text = a.RootCause, Confidence = a.RootCauseConfidence },
        Confidence = a.ApplicationConfidence,
        RootCauseCategory = a.RootCauseCategory,
        ClusterId = a.ClusterId,
        SuggestedLogQueries = a.SuggestedLogQueries.Select(q => new SuggestedLogQueryDto
        {
            Backend = q.Backend,
            Label = q.Label,
            Query = q.Query
        }).ToList(),
        Evidence = a.Evidence.Select(e => new EvidenceDto
        {
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            Title = e.Title,
            Similarity = e.Similarity
        }).ToList(),
        Sources = a.Sources,
        RecommendedChecks = a.RecommendedChecks,
        RecommendedSolutions = a.RecommendedSolutions,
        Known = a.KnownStatement,
        Likely = a.LikelyStatement,
        Unknown = a.UnknownStatement,
        Usage = new UsageDto
        {
            EmbeddingModel = a.EmbeddingModel,
            LlmModel = a.LlmModel,
            PromptTokens = a.PromptTokens,
            CompletionTokens = a.CompletionTokens,
            TotalTokens = a.TotalTokens,
            EmbeddingLatencyMs = a.EmbeddingLatencyMs,
            VectorSearchLatencyMs = a.VectorSearchLatencyMs,
            LlmLatencyMs = a.LlmLatencyMs,
            TotalLatencyMs = a.TotalLatencyMs
        }
    };
}
