namespace ExceptionKnowledgeBase.Application.Abstractions;

public interface IChatCompletionService
{
    Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}

public sealed record ChatCompletionResult(
    string Content,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);
