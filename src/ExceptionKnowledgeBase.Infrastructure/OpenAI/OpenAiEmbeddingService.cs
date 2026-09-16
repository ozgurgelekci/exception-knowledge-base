using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Infrastructure.OpenAI;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;
    private readonly IEmbeddingCache? _cache;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        HttpClient http,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiEmbeddingService> logger,
        IEmbeddingCache? cache = null)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _cache = cache;

        _http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<EmbeddingResult> EmbedAsync(string input, CancellationToken ct)
    {
        var key = BuildCacheKey(input);
        if (_cache is not null)
        {
            var cached = await _cache.GetAsync(key, ct);
            if (cached is not null)
                return new EmbeddingResult(cached, _options.EmbeddingModel, _options.EmbeddingModelVersion, cached.Length);
        }

        var payload = new EmbeddingRequest(_options.EmbeddingModel, input);
        using var response = await _http.PostAsJsonAsync("embeddings", payload, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("OpenAI embedding response was empty");

        var vector = body.Data.FirstOrDefault()?.Embedding
                     ?? throw new InvalidOperationException("OpenAI embedding response had no vectors");

        if (_cache is not null)
            await _cache.SetAsync(key, vector, ct);

        return new EmbeddingResult(vector, body.Model ?? _options.EmbeddingModel, _options.EmbeddingModelVersion, vector.Length);
    }

    private string BuildCacheKey(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"embedding:{hex}:{_options.EmbeddingModel}:{_options.EmbeddingModelVersion}";
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("data")] public List<EmbeddingDatum> Data { get; set; } = new();
    }

    private sealed class EmbeddingDatum
    {
        [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
