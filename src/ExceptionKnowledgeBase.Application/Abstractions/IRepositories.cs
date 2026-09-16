using ExceptionKnowledgeBase.Domain.Analyses;
using ExceptionKnowledgeBase.Domain.Exceptions;
using ExceptionKnowledgeBase.Domain.Knowledge;

namespace ExceptionKnowledgeBase.Application.Abstractions;

public interface IExceptionDefinitionRepository
{
    Task<ExceptionDefinition?> GetByFingerprintAsync(string tenantId, string fingerprint, CancellationToken cancellationToken);
    Task<ExceptionDefinition?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task UpsertAsync(ExceptionDefinition definition, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExceptionDefinition>> GetByIdsAsync(string tenantId, IEnumerable<string> ids, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExceptionDefinition>> GetPendingEmbeddingsAsync(int limit, CancellationToken cancellationToken);
    Task MarkEmbeddedAsync(string id, string model, string modelVersion, int dimensions, CancellationToken cancellationToken);
    Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken);
    Task IncrementOccurrenceAsync(string id, DateTime occurredAt, CancellationToken cancellationToken);
}

public interface IExceptionOccurrenceRepository
{
    Task InsertAsync(ExceptionOccurrence occurrence, CancellationToken cancellationToken);
}

public interface IKnowledgeEntryRepository
{
    Task<KnowledgeEntry?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeEntry>> GetByIdsAsync(string tenantId, IEnumerable<string> ids, CancellationToken cancellationToken);
    Task UpsertAsync(KnowledgeEntry entry, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeEntry>> GetPendingEmbeddingsAsync(int limit, CancellationToken cancellationToken);
    Task MarkEmbeddedAsync(string id, string model, string modelVersion, CancellationToken cancellationToken);
}

public interface ISolutionRepository
{
    Task<Solution?> GetTopSolutionForKnowledgeAsync(string tenantId, string knowledgeEntryId, CancellationToken cancellationToken);
    Task<Solution?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task RecordOutcomeAsync(string tenantId, string solutionId, bool success, CancellationToken cancellationToken);
}

public interface IAiAnalysisRepository
{
    Task InsertAsync(AiAnalysis analysis, CancellationToken cancellationToken);
    Task<AiAnalysis?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken);
}

public interface IFeedbackRepository
{
    Task InsertAsync(Feedback feedback, CancellationToken cancellationToken);
}
