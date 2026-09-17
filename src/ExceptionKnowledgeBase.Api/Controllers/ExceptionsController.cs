using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExceptionKnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/exceptions")]
public sealed class ExceptionsController : ControllerBase
{
    private readonly IExceptionAnalysisService _service;
    public ExceptionsController(IExceptionAnalysisService service) => _service = service;

    // Sections 37 + 56: end-to-end analyze
    [HttpPost("analyze")]
    [EnableRateLimiting(RateLimitPolicies.Analyze)]
    public async Task<ActionResult<AnalyzeExceptionResponse>> Analyze(
        [FromBody] ReportExceptionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new ProblemDetails { Title = "message is required" });

        var tenantId = HttpContext.ResolveTenantId();
        var response = await _service.AnalyzeAsync(tenantId, request, ct);
        return Ok(response);
    }

    // Section 64: async analyze — enqueue and return 202 + Location.
    [HttpPost("analyze/async")]
    [EnableRateLimiting(RateLimitPolicies.Analyze)]
    public async Task<IActionResult> AnalyzeAsyncEnqueue(
        [FromBody] ReportExceptionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new ProblemDetails { Title = "message is required" });

        var tenantId = HttpContext.ResolveTenantId();
        var analysisId = await _service.EnqueueAsync(tenantId, request, ct);
        var location = $"/api/analyses/{analysisId}";
        Response.Headers.Location = location;
        return Accepted(location, new { analysisId, status = "pending" });
    }

    // Section 38: semantic search
    [HttpPost("search")]
    [EnableRateLimiting(RateLimitPolicies.Search)]
    public async Task<ActionResult<SearchExceptionsResponse>> Search(
        [FromBody] SearchExceptionsRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new ProblemDetails { Title = "query is required" });

        var tenantId = HttpContext.ResolveTenantId();
        return Ok(await _service.SearchAsync(tenantId, request, ct));
    }
}
