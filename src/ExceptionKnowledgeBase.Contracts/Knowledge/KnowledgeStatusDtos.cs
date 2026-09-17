namespace ExceptionKnowledgeBase.Contracts.Knowledge;

// Phase 2: knowledge lifecycle transitions (draft → verified → archived).
public sealed class VerifyKnowledgeRequest
{
    public string? VerifiedBy { get; set; }
}

public sealed class ArchiveKnowledgeRequest
{
    public string? ArchivedBy { get; set; }
    public string? Reason { get; set; }
}
