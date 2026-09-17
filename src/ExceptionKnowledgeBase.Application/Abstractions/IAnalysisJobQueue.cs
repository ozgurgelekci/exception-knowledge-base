using ExceptionKnowledgeBase.Contracts.Exceptions;

namespace ExceptionKnowledgeBase.Application.Abstractions;

// Phase 2 (§64): decouple POST /analyze/async from LLM latency.
public interface IAnalysisJobQueue
{
    ValueTask EnqueueAsync(AnalysisJob job, CancellationToken cancellationToken);
    IAsyncEnumerable<AnalysisJob> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed record AnalysisJob(string AnalysisId, string TenantId, ReportExceptionRequest Request);
