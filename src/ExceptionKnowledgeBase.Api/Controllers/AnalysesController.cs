using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using ExceptionKnowledgeBase.Contracts.Feedback;
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
        return Ok(AnalysisResponseMapper.ToResponse(analysis));
    }
}
