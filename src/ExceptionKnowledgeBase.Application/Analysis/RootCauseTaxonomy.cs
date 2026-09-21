namespace ExceptionKnowledgeBase.Application.Analysis;

// Phase 4 (§75): coarse root-cause bucket derived from the exception type name.
// The LLM still owns the natural-language root cause; this label just powers
// downstream analytics (dashboards, trend groupings, runbook routing).
public interface IRootCauseClassifier
{
    string Classify(string? exceptionType, string? normalizedMessage);
}

public sealed class RootCauseClassifier : IRootCauseClassifier
{
    private static readonly (string Category, string[] Needles)[] Rules =
    {
        ("timeout",    new[] { "timeout", "timedout", "timed out", "canceled due to timeout", "deadline" }),
        ("auth",       new[] { "unauthorized", "forbidden", "authentication", "authorization", "token", "denied" }),
        ("validation", new[] { "validation", "argument", "invalidoperation", "format", "parse", "badrequest" }),
        ("database",   new[] { "sqlexception", "mongo", "postgres", "npgsql", "dbupdate", "deadlock", "constraint" }),
        ("network",    new[] { "socket", "httprequestexception", "dns", "connectionreset", "connectionrefused", "unreachable" }),
        ("io",         new[] { "ioexception", "filenotfound", "directorynotfound", "diskfull", "pathtoolong" }),
        ("config",     new[] { "config", "keynotfound", "notconfigured", "missingsetting", "settings" }),
        ("concurrency",new[] { "concurrency", "optimisticlock", "semaphore", "deadlock", "stalestate" }),
        ("null",       new[] { "nullreference" }),
    };

    public string Classify(string? exceptionType, string? normalizedMessage)
    {
        var t = (exceptionType ?? string.Empty).ToLowerInvariant();
        var m = (normalizedMessage ?? string.Empty).ToLowerInvariant();
        foreach (var (category, needles) in Rules)
            foreach (var n in needles)
                if (t.Contains(n) || m.Contains(n)) return category;
        return "unknown";
    }
}
