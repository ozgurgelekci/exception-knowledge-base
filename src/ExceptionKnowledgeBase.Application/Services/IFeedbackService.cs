using ExceptionKnowledgeBase.Contracts.Feedback;

namespace ExceptionKnowledgeBase.Application.Services;

public interface IFeedbackService
{
    Task SubmitAsync(string tenantId, string analysisId, SubmitFeedbackRequest request, CancellationToken cancellationToken);
}
