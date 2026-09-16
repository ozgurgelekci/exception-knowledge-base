namespace ExceptionKnowledgeBase.Contracts.Feedback;

// Section 40.
public sealed class SubmitFeedbackRequest
{
    public string Result { get; set; } = "unspecified";  // resolved | partially_resolved | not_resolved
    public string? SolutionId { get; set; }
    public bool Helpful { get; set; }
    public string? Comment { get; set; }
    public string? SubmittedBy { get; set; }
}
