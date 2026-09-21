using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExceptionKnowledgeBase.Contracts.Exceptions;

namespace ExceptionKnowledgeBase.Evaluator;

// §77: offline evaluator. Reads a golden set of expected (exceptionType, message → sourceIds),
// POSTs each to /api/exceptions/analyze, and reports precision@k on returned Sources.
// Also flags hallucinated ids — LLM citing a knowledge id we did not put in prompt context.
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task<int> Main(string[] args)
    {
        var baseUrl = GetArg(args, "--base-url") ?? Environment.GetEnvironmentVariable("EVAL_BASE_URL") ?? "http://localhost:5080";
        var goldenPath = GetArg(args, "--golden") ?? Path.Combine("eval", "golden.json");
        var tenantId = GetArg(args, "--tenant") ?? "eval";
        var kArg = GetArg(args, "--k");
        var k = int.TryParse(kArg, out var parsedK) ? parsedK : 3;

        if (!File.Exists(goldenPath))
        {
            Console.Error.WriteLine($"golden file not found: {goldenPath}");
            return 2;
        }

        var goldenJson = await File.ReadAllTextAsync(goldenPath);
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(goldenJson, JsonOpts) ?? new();
        if (cases.Count == 0)
        {
            Console.Error.WriteLine("golden file is empty");
            return 2;
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

        var results = new List<CaseResult>(cases.Count);
        foreach (var g in cases)
        {
            var req = new ReportExceptionRequest
            {
                ExceptionType = g.ExceptionType,
                Message = g.Message,
                StackTrace = g.StackTrace,
                Database = g.Database,
                Module = g.Module
            };

            try
            {
                var resp = await http.PostAsJsonAsync("/api/exceptions/analyze", req, JsonOpts);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[{g.Id}] HTTP {(int)resp.StatusCode}");
                    results.Add(new CaseResult(g.Id, 0, 0, false, false));
                    continue;
                }

                var body = await resp.Content.ReadFromJsonAsync<AnalyzeExceptionResponse>(JsonOpts);
                if (body is null)
                {
                    results.Add(new CaseResult(g.Id, 0, 0, false, false));
                    continue;
                }

                var topK = (body.Sources ?? new List<string>()).Take(k).ToList();
                var expected = new HashSet<string>(g.ExpectedSourceIds ?? new(), StringComparer.Ordinal);
                var evidenceIds = (body.Evidence ?? new()).Select(e => e.EntityId).ToHashSet(StringComparer.Ordinal);

                var hits = topK.Count(id => expected.Contains(id));
                var precision = topK.Count == 0 ? 0 : (double)hits / topK.Count;
                var recall = expected.Count == 0 ? 1 : (double)hits / expected.Count;
                var hallucinated = topK.Any(id => !evidenceIds.Contains(id));

                results.Add(new CaseResult(g.Id, precision, recall, precision > 0, hallucinated));

                Console.WriteLine($"[{g.Id}] P@{k}={precision:F2} R={recall:F2} hallucinated={hallucinated}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{g.Id}] error: {ex.Message}");
                results.Add(new CaseResult(g.Id, 0, 0, false, false));
            }
        }

        var meanP = results.Count == 0 ? 0 : results.Average(r => r.Precision);
        var meanR = results.Count == 0 ? 0 : results.Average(r => r.Recall);
        var hitRate = results.Count == 0 ? 0 : (double)results.Count(r => r.AnyHit) / results.Count;
        var hallRate = results.Count == 0 ? 0 : (double)results.Count(r => r.Hallucinated) / results.Count;

        Console.WriteLine();
        Console.WriteLine("=== Evaluation summary ===");
        Console.WriteLine($"cases          : {results.Count}");
        Console.WriteLine($"mean P@{k}       : {meanP:F3}");
        Console.WriteLine($"mean recall    : {meanR:F3}");
        Console.WriteLine($"any-hit rate   : {hitRate:P0}");
        Console.WriteLine($"halluc. rate   : {hallRate:P0}");

        // Non-zero exit code when quality is below a smell threshold so CI can gate on it.
        return meanP >= 0.5 ? 0 : 1;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private sealed class GoldenCase
    {
        public string Id { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string? Database { get; set; }
        public string? Module { get; set; }
        public List<string> ExpectedSourceIds { get; set; } = new();
    }

    private sealed record CaseResult(string Id, double Precision, double Recall, bool AnyHit, bool Hallucinated);
}
