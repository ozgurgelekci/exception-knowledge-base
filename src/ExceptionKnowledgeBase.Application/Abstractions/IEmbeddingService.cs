namespace ExceptionKnowledgeBase.Application.Abstractions;

public interface IEmbeddingService
{
    Task<EmbeddingResult> EmbedAsync(string input, CancellationToken cancellationToken);
}

public sealed record EmbeddingResult(
    float[] Vector,
    string Model,
    string ModelVersion,
    int Dimensions);
