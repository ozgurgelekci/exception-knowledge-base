using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Contracts.Feedback;
using ExceptionKnowledgeBase.Domain.Analyses;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Application.Services;

public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _feedback;
    private readonly ISolutionRepository _solutions;
    private readonly AnalysisOptions _analysis;

    public FeedbackService(
        IFeedbackRepository feedback,
        ISolutionRepository solutions,
        IOptions<AnalysisOptions> analysis)
    {
        _feedback = feedback;
        _solutions = solutions;
        _analysis = analysis.Value;
    }

    public async Task SubmitAsync(string tenantId, string analysisId, SubmitFeedbackRequest request, CancellationToken ct)
    {
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;
        var result = ParseResult(request.Result);

        var feedback = new Feedback
        {
            TenantId = tenantId,
            AnalysisId = analysisId,
            SolutionId = request.SolutionId,
            Result = result,
            Helpful = request.Helpful,
            Comment = request.Comment,
            SubmittedBy = request.SubmittedBy
        };

        await _feedback.InsertAsync(feedback, ct);

        if (!string.IsNullOrWhiteSpace(request.SolutionId) && result != FeedbackResult.Unspecified)
        {
            var success = result == FeedbackResult.Resolved;
            await _solutions.RecordOutcomeAsync(tenantId, request.SolutionId!, success, ct);
        }
    }

    private static FeedbackResult ParseResult(string value) => value?.ToLowerInvariant() switch
    {
        "resolved" => FeedbackResult.Resolved,
        "partially_resolved" or "partial" => FeedbackResult.PartiallyResolved,
        "not_resolved" or "unresolved" => FeedbackResult.NotResolved,
        _ => FeedbackResult.Unspecified
    };
}
