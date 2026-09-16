using ExceptionKnowledgeBase.Contracts.Exceptions;

namespace ExceptionKnowledgeBase.Application.Services;

public interface IExceptionAnalysisService
{
    Task<AnalyzeExceptionResponse> AnalyzeAsync(
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken cancellationToken);

    Task<SearchExceptionsResponse> SearchAsync(
        string tenantId,
        SearchExceptionsRequest request,
        CancellationToken cancellationToken);
}
