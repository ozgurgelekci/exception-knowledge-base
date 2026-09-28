using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Domain.Exceptions;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Analysis;

public sealed class LogQuerySuggesterTests
{
    private readonly LogQuerySuggester _sut = new();

    [Fact]
    public void Always_returns_loki_kibana_splunk_suggestions()
    {
        var suggestions = _sut.Suggest(BuildDef("timeout"), Context("orders", "prod"));
        Assert.Contains(suggestions, s => s.Backend == "loki");
        Assert.Contains(suggestions, s => s.Backend == "kibana");
        Assert.Contains(suggestions, s => s.Backend == "splunk");
    }

    [Fact]
    public void Includes_grafana_metric_for_timeout_category()
    {
        var suggestions = _sut.Suggest(BuildDef("timeout"), Context("orders", "prod"));
        Assert.Contains(suggestions, s => s.Backend == "grafana"
                                          && s.Query.Contains("http_client_request_duration_seconds_count"));
    }

    [Fact]
    public void Escapes_dangerous_characters_from_service_input()
    {
        var suggestions = _sut.Suggest(BuildDef("database"), Context("or\"ders", "pr{od}"));
        foreach (var s in suggestions)
        {
            Assert.DoesNotContain("\"or\"ders\"", s.Query);
            Assert.DoesNotContain("pr{od}", s.Query);
        }
    }

    [Fact]
    public void Returns_empty_when_definition_is_null()
    {
        var suggestions = _sut.Suggest(null!, Context(null, null));
        Assert.Empty(suggestions);
    }

    [Fact]
    public void Includes_error_code_specific_kibana_query_when_provided()
    {
        var suggestions = _sut.Suggest(
            BuildDef("validation"),
            new LogQueryContext("orders", "prod", "/api/orders", "ORD-42"));
        Assert.Contains(suggestions, s => s.Backend == "kibana" && s.Query.Contains("error.code"));
    }

    private static ExceptionDefinition BuildDef(string category) => new()
    {
        TenantId = "acme",
        Fingerprint = "abc123",
        ExceptionType = "System.TimeoutException",
        NormalizedMessage = "operation timed out",
        RootCauseCategory = category
    };

    private static LogQueryContext Context(string? service, string? env)
        => new(service, env, Endpoint: null, ErrorCode: null);
}
