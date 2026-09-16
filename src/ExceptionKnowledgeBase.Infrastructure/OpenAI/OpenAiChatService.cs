using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Infrastructure.OpenAI;

public sealed class OpenAiChatService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly OpenAiOptions _options;

    public OpenAiChatService(HttpClient http, IOptions<OpenAiOptions> options)
    {
        _http = http;
        _options = options.Value;

        _http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public async Task<ChatCompletionResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Model = _options.ChatModel,
            Temperature = _options.ChatTemperature,
            MaxTokens = _options.ChatMaxTokens,
            ResponseFormat = new ResponseFormat("json_object"),
            Messages = new List<ChatMessage>
            {
                new("system", systemPrompt),
                new("user", userPrompt)
            }
        };

        using var response = await _http.PostAsJsonAsync("chat/completions", request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("OpenAI chat response was empty");

        var content = body.Choices.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var usage = body.Usage ?? new Usage();
        return new ChatCompletionResult(
            content,
            body.Model ?? _options.ChatModel,
            usage.PromptTokens,
            usage.CompletionTokens,
            usage.TotalTokens);
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("response_format")] public ResponseFormat ResponseFormat { get; set; } = new("text");
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
    }

    private sealed record ResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatResponse
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("choices")] public List<Choice> Choices { get; set; } = new();
        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }
}
