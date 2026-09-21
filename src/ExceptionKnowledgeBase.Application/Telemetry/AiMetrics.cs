using System.Diagnostics.Metrics;

namespace ExceptionKnowledgeBase.Application.Telemetry;

// §78: cost + latency counters. Meter name is the OTLP export handle.
// Prometheus/OTLP collectors scrape it; nothing here is Prom-specific.
public sealed class AiMetrics : IDisposable
{
    public const string MeterName = "ExceptionKnowledgeBase.Ai";

    private readonly Meter _meter;
    private readonly Counter<long> _promptTokens;
    private readonly Counter<long> _completionTokens;
    private readonly Counter<long> _embeddingCalls;
    private readonly Counter<long> _analysesRun;
    private readonly Histogram<double> _llmLatency;
    private readonly Histogram<double> _embeddingLatency;
    private readonly Histogram<double> _totalLatency;

    public AiMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _promptTokens       = _meter.CreateCounter<long>("ai.tokens.prompt");
        _completionTokens   = _meter.CreateCounter<long>("ai.tokens.completion");
        _embeddingCalls     = _meter.CreateCounter<long>("ai.embedding.calls");
        _analysesRun        = _meter.CreateCounter<long>("ai.analyses.run");
        _llmLatency         = _meter.CreateHistogram<double>("ai.llm.latency_ms");
        _embeddingLatency   = _meter.CreateHistogram<double>("ai.embedding.latency_ms");
        _totalLatency       = _meter.CreateHistogram<double>("ai.analysis.latency_ms");
    }

    public void RecordTokens(string tenantId, string chatModel, int promptTokens, int completionTokens)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("tenant", tenantId),
            new("model", chatModel)
        };
        _promptTokens.Add(promptTokens, tags);
        _completionTokens.Add(completionTokens, tags);
    }

    public void RecordEmbedding(string tenantId, string model, long latencyMs)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("tenant", tenantId),
            new("model", model)
        };
        _embeddingCalls.Add(1, tags);
        _embeddingLatency.Record(latencyMs, tags);
    }

    public void RecordAnalysis(string tenantId, string chatModel, long llmLatencyMs, long totalLatencyMs, string status)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("tenant", tenantId),
            new("model", chatModel),
            new("status", status)
        };
        _analysesRun.Add(1, tags);
        _llmLatency.Record(llmLatencyMs, tags);
        _totalLatency.Record(totalLatencyMs, tags);
    }

    public void Dispose() => _meter.Dispose();
}
