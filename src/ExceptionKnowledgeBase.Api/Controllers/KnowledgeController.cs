using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Contracts.Knowledge;
using ExceptionKnowledgeBase.Domain.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExceptionKnowledgeBase.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
[EnableRateLimiting(RateLimitPolicies.Knowledge)]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _service;
    public KnowledgeController(IKnowledgeService service) => _service = service;

    // Section 39
    [HttpPost]
    public async Task<ActionResult<KnowledgeEntryResponse>> Create(
        [FromBody] CreateKnowledgeEntryRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.CreateAsync(tenantId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = entry.Id }, ToDto(entry));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<KnowledgeEntryResponse>> Get(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.GetAsync(tenantId, id, ct);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<KnowledgeEntryResponse>> Update(
        string id,
        [FromBody] CreateKnowledgeEntryRequest request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.UpdateAsync(tenantId, id, request, ct);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var ok = await _service.DeleteAsync(tenantId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    // Phase 2 (§74): human approval lifecycle.
    [HttpPost("{id}/verify")]
    public async Task<ActionResult<KnowledgeEntryResponse>> Verify(
        string id,
        [FromBody] VerifyKnowledgeRequest? request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.VerifyAsync(tenantId, id, request?.VerifiedBy, ct);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    [HttpPost("{id}/archive")]
    public async Task<ActionResult<KnowledgeEntryResponse>> Archive(
        string id,
        [FromBody] ArchiveKnowledgeRequest? request,
        CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.ArchiveAsync(tenantId, id, request?.ArchivedBy, ct);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    [HttpPost("{id}/reset")]
    public async Task<ActionResult<KnowledgeEntryResponse>> Reset(string id, CancellationToken ct)
    {
        var tenantId = HttpContext.ResolveTenantId();
        var entry = await _service.ResetToDraftAsync(tenantId, id, ct);
        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    private static KnowledgeEntryResponse ToDto(KnowledgeEntry entry) => new()
    {
        Id = entry.Id,
        Title = entry.Title,
        ExceptionTypes = entry.ExceptionTypes,
        Symptoms = entry.Symptoms,
        RootCause = entry.RootCause,
        Solution = entry.Solution,
        Verification = entry.Verification,
        Tags = entry.Tags,
        Status = entry.Status,
        Version = entry.Version,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt,
        VerifiedBy = entry.VerifiedBy,
        VerifiedAt = entry.VerifiedAt,
        ArchivedAt = entry.ArchivedAt
    };
}
