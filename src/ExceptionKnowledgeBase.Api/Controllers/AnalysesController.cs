using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using ExceptionKnowledgeBase.Contracts.Feedback;
using ExceptionKnowledgeBase.Domain.Analyses;
using Microsoft.AspNetCore.Mvc;

namespace ExceptionKnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/analyses")]
public sealed class AnalysesController : ControllerBase
{
    private readonly IFeedbackService _feedback;
    private readonly IExceptionAnalysisService _analyses;

    public AnalysesController(IFeedbackService feedback, IExceptionAnalysisService analyses)
    {
        _feedback = feedback;
        _analyses = analyses;
    }

    // Section 40
    [HttpPost("{id}/feedback")]
    public async Task<IActionResult> SubmitFeedback(
        string id,
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        await _feedback.SubmitAsync(tenantId, id, request, ct);
        return Accepted();
    }

    // Section 64: poll async analysis by id.
    [HttpGet("{id}", Name = "GetAnalysis")]
    public async Task<ActionResult<AnalyzeExceptionResponse>> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var analysis = await _analyses.GetAnalysisAsync(tenantId, id, ct);
        if (analysis is null) return NotFound();
        return Ok(ToDto(analysis));
    }

    private static AnalyzeExceptionResponse ToDto(AiAnalysis a) => new()
    {
        AnalysisId = a.Id,
        Status = a.Status,
        Summary = a.Summary,
        RootCause = new RootCauseDto { Text = a.RootCause, Confidence = a.RootCauseConfidence },
        Confidence = a.ApplicationConfidence,
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
