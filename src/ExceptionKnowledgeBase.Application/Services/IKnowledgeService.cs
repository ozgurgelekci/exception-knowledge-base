using ExceptionKnowledgeBase.Contracts.Knowledge;
using ExceptionKnowledgeBase.Domain.Knowledge;

namespace ExceptionKnowledgeBase.Application.Services;

public interface IKnowledgeService
{
    Task<KnowledgeEntry> CreateAsync(string tenantId, CreateKnowledgeEntryRequest request, CancellationToken cancellationToken);
    Task<KnowledgeEntry?> GetAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task<KnowledgeEntry?> UpdateAsync(string tenantId, string id, CreateKnowledgeEntryRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string tenantId, string id, CancellationToken cancellationToken);
    Task<KnowledgeEntry?> VerifyAsync(string tenantId, string id, string? verifiedBy, CancellationToken cancellationToken);
    Task<KnowledgeEntry?> ArchiveAsync(string tenantId, string id, string? archivedBy, CancellationToken cancellationToken);
    Task<KnowledgeEntry?> ResetToDraftAsync(string tenantId, string id, CancellationToken cancellationToken);
}
