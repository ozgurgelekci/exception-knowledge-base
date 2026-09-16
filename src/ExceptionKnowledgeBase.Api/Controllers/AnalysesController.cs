using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Contracts.Feedback;
using Microsoft.AspNetCore.Mvc;

namespace ExceptionKnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/analyses")]
public sealed class AnalysesController : ControllerBase
{
    private readonly IFeedbackService _feedback;
    public AnalysesController(IFeedbackService feedback) => _feedback = feedback;

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
}
