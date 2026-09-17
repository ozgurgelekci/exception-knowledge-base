using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using ExceptionKnowledgeBase.Domain.Analyses;

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

    Task<string> EnqueueAsync(
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken cancellationToken);

    Task RunPendingAsync(AnalysisJob job, CancellationToken cancellationToken);

    Task<AiAnalysis?> GetAnalysisAsync(
        string tenantId,
        string id,
        CancellationToken cancellationToken);
}
