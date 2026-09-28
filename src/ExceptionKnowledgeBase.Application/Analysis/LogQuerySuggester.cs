using ExceptionKnowledgeBase.Domain.Exceptions;

namespace ExceptionKnowledgeBase.Application.Analysis;

// Phase 4 (§75): given a definition + occurrence context, emit ready-to-paste
// log queries for the common observability backends. Deterministic — we build
// them from taxonomy category, exception type and service metadata rather than
// asking the LLM, so they never hallucinate a label that doesn't exist.
public interface ILogQuerySuggester
{
    IReadOnlyList<SuggestedLogQuery> Suggest(ExceptionDefinition definition, LogQueryContext context);
}

public sealed record LogQueryContext(
    string? Service,
    string? Environment,
    string? Endpoint,
    string? ErrorCode);

public sealed class SuggestedLogQuery
{
    public string Backend { get; set; } = string.Empty;   // loki | kibana | splunk | grafana
    public string Label { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
}

public sealed class LogQuerySuggester : ILogQuerySuggester
{
    public IReadOnlyList<SuggestedLogQuery> Suggest(ExceptionDefinition definition, LogQueryContext context)
    {
        if (definition is null) return Array.Empty<SuggestedLogQuery>();

        var service = Sanitize(context.Service);
        var env = Sanitize(context.Environment);
        var endpoint = Sanitize(context.Endpoint);
        var errorCode = Sanitize(context.ErrorCode);
        var type = Sanitize(definition.ExceptionType);
        var category = Sanitize(definition.RootCauseCategory);
        var fingerprint = Sanitize(definition.Fingerprint);

        var suggestions = new List<SuggestedLogQuery>();

        // Loki: fingerprint is the strongest signal — every occurrence of *this*
        // exception. Include service/env label filters when present.
        suggestions.Add(new SuggestedLogQuery
        {
            Backend = "loki",
            Label = "All occurrences of this exception (by fingerprint)",
            Query = BuildLokiQuery(service, env, extraFilter: $"| json | fingerprint = \"{fingerprint}\"")
        });

        if (!string.IsNullOrEmpty(type))
        {
            suggestions.Add(new SuggestedLogQuery
            {
                Backend = "loki",
                Label = $"Recent stack traces containing {type}",
                Query = BuildLokiQuery(service, env, extraFilter: $"|= \"{type}\"")
            });
        }

        // Kibana / Elasticsearch KQL — categorical drill-down.
        suggestions.Add(new SuggestedLogQuery
        {
            Backend = "kibana",
            Label = "Errors for this fingerprint (last 24h)",
            Query = BuildKql(new[]
            {
                ("log.level", "error"),
                ("exception.fingerprint", fingerprint),
                ("service.name", service),
                ("service.environment", env)
            })
        });

        if (!string.IsNullOrEmpty(errorCode))
        {
            suggestions.Add(new SuggestedLogQuery
            {
                Backend = "kibana",
                Label = $"Correlated errors with code {errorCode}",
                Query = BuildKql(new[]
                {
                    ("error.code", errorCode),
                    ("service.name", service),
                    ("service.environment", env)
                })
            });
        }

        // Splunk SPL — searches the raw index by symbol names.
        var splTerms = new List<string> { "index=app", "log_level=ERROR" };
        if (!string.IsNullOrEmpty(service)) splTerms.Add($"service={service}");
        if (!string.IsNullOrEmpty(env)) splTerms.Add($"env={env}");
        if (!string.IsNullOrEmpty(type)) splTerms.Add($"\"{type}\"");
        suggestions.Add(new SuggestedLogQuery
        {
            Backend = "splunk",
            Label = "Splunk error search for this exception",
            Query = string.Join(' ', splTerms)
        });

        // Category-aware suggestion (§75): different runtime signals matter per bucket.
        var categorySuggestion = SuggestByCategory(category, service, env, endpoint);
        if (categorySuggestion is not null) suggestions.Add(categorySuggestion);

        return suggestions;
    }

    private static SuggestedLogQuery? SuggestByCategory(string category, string service, string env, string endpoint)
        => category switch
        {
            "timeout" or "network" => new SuggestedLogQuery
            {
                Backend = "grafana",
                Label = "Upstream latency + error rate",
                Query = $"rate(http_client_request_duration_seconds_count{{{JoinLabels(("service", service), ("env", env), ("status", "5.."))}}}[5m])"
            },
            "database" => new SuggestedLogQuery
            {
                Backend = "grafana",
                Label = "DB call latency (p95) and error rate",
                Query = $"histogram_quantile(0.95, sum by (le) (rate(db_client_operation_duration_seconds_bucket{{{JoinLabels(("service", service), ("env", env))}}}[5m])))"
            },
            "auth" => new SuggestedLogQuery
            {
                Backend = "kibana",
                Label = "401/403 responses in the same window",
                Query = BuildKql(new[]
                {
                    ("http.response.status_code", "401 or 403"),
                    ("service.name", service),
                    ("service.environment", env),
                    ("url.path", endpoint)
                })
            },
            "validation" => new SuggestedLogQuery
            {
                Backend = "kibana",
                Label = "4xx spikes on this endpoint",
                Query = BuildKql(new[]
                {
                    ("http.response.status_code", "[400 TO 499]"),
                    ("url.path", endpoint),
                    ("service.name", service)
                })
            },
            _ => null
        };

    private static string BuildLokiQuery(string service, string env, string extraFilter)
    {
        var labels = new List<string>();
        if (!string.IsNullOrEmpty(service)) labels.Add($"app=\"{service}\"");
        if (!string.IsNullOrEmpty(env)) labels.Add($"env=\"{env}\"");
        if (labels.Count == 0) labels.Add("app=~\".+\"");
        return $"{{{string.Join(",", labels)}}} {extraFilter}".Trim();
    }

    private static string BuildKql(IEnumerable<(string Field, string Value)> pairs)
    {
        var parts = new List<string>();
        foreach (var (field, value) in pairs)
        {
            if (string.IsNullOrEmpty(value)) continue;
            parts.Add(value.Contains(' ') || value.Contains('[')
                ? $"{field}: {value}"
                : $"{field}: \"{value}\"");
        }
        return parts.Count == 0 ? "*" : string.Join(" AND ", parts);
    }

    private static string JoinLabels(params (string Key, string Value)[] pairs)
    {
        var parts = new List<string>();
        foreach (var (k, v) in pairs)
        {
            if (string.IsNullOrEmpty(v)) continue;
            parts.Add(v.Contains("..") ? $"{k}=~\"{v}\"" : $"{k}=\"{v}\"");
        }
        return string.Join(",", parts);
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Keep quotes/braces out of the query so we don't have to escape everywhere.
        var trimmed = value.Trim();
        var buf = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (ch is '"' or '{' or '}' or '\\' or '\n' or '\r') continue;
            buf.Append(ch);
        }
        return buf.ToString();
    }
}
