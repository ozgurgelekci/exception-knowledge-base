namespace ExceptionKnowledgeBase.Application.Abstractions;

// Section 34: skip repeat OpenAI calls for identical inputs.
public interface IEmbeddingCache
{
    Task<float[]?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, float[] vector, CancellationToken cancellationToken);
}
