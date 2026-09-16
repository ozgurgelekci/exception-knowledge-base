using System.Diagnostics;
using System.Text.Json;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Application.Normalization;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Application.Security;
using ExceptionKnowledgeBase.Contracts.Exceptions;
using ExceptionKnowledgeBase.Domain.Analyses;
using ExceptionKnowledgeBase.Domain.Common;
using ExceptionKnowledgeBase.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExceptionKnowledgeBase.Application.Services;

// Section 62: single orchestration point for analyze/search.
public sealed class ExceptionAnalysisService : IExceptionAnalysisService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IExceptionNormalizer _normalizer;
    private readonly IFingerprintGenerator _fingerprints;
    private readonly ISecretRedactor _redactor;
    private readonly IEmbeddingInputBuilder _embeddingInput;
    private readonly IEmbeddingService _embedding;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IExceptionDefinitionRepository _definitions;
    private readonly IExceptionOccurrenceRepository _occurrences;
    private readonly IKnowledgeEntryRepository _knowledge;
    private readonly ISolutionRepository _solutions;
    private readonly IAiAnalysisRepository _analyses;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IChatCompletionService _chat;
    private readonly IConfidenceCalculator _confidence;
    private readonly OpenAiOptions _openAi;
    private readonly AnalysisOptions _analysis;
    private readonly ILogger<ExceptionAnalysisService> _logger;

    public ExceptionAnalysisService(
        IExceptionNormalizer normalizer,
        IFingerprintGenerator fingerprints,
        ISecretRedactor redactor,
        IEmbeddingInputBuilder embeddingInput,
        IEmbeddingService embedding,
        IVectorSearchService vectorSearch,
        IExceptionDefinitionRepository definitions,
        IExceptionOccurrenceRepository occurrences,
        IKnowledgeEntryRepository knowledge,
        ISolutionRepository solutions,
        IAiAnalysisRepository analyses,
        IPromptBuilder promptBuilder,
        IChatCompletionService chat,
        IConfidenceCalculator confidence,
        IOptions<OpenAiOptions> openAi,
        IOptions<AnalysisOptions> analysis,
        ILogger<ExceptionAnalysisService> logger)
    {
        _normalizer = normalizer;
        _fingerprints = fingerprints;
        _redactor = redactor;
        _embeddingInput = embeddingInput;
        _embedding = embedding;
        _vectorSearch = vectorSearch;
        _definitions = definitions;
        _occurrences = occurrences;
        _knowledge = knowledge;
        _solutions = solutions;
        _analyses = analyses;
        _promptBuilder = promptBuilder;
        _chat = chat;
        _confidence = confidence;
        _openAi = openAi.Value;
        _analysis = analysis.Value;
        _logger = logger;
    }

    public async Task<AnalyzeExceptionResponse> AnalyzeAsync(
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;

        var redactedMessage = _redactor.Redact(request.Message);
        var redactedStack = _redactor.Redact(request.StackTrace ?? string.Empty);
        var normalized = _normalizer.Normalize(redactedMessage);
        var fingerprint = _fingerprints.Generate(request.ExceptionType, normalized);

        var definition = await _definitions.GetByFingerprintAsync(tenantId, fingerprint, ct)
                         ?? BuildDefinition(tenantId, request, redactedMessage, normalized, fingerprint);

        MergeIntoDefinition(definition, request);
        definition.LastSeenAt = DateTime.UtcNow;
        definition.OccurrenceCount += 1;
        await _definitions.UpsertAsync(definition, ct);

        var occurrence = BuildOccurrence(tenantId, request, redactedMessage, redactedStack, definition);
        await _occurrences.InsertAsync(occurrence, ct);

        var embedSw = Stopwatch.StartNew();
        var embeddingContent = _embeddingInput.ForException(definition);
        definition.EmbeddingContent = embeddingContent;
        var embedding = await _embedding.EmbedAsync(embeddingContent, ct);
        embedSw.Stop();

        await _vectorSearch.UpsertAsync(new EmbeddingUpsert(
            EntityId: definition.Id,
            EntityType: EntityTypes.Exception,
            TenantId: tenantId,
            Content: embeddingContent,
            Vector: embedding.Vector,
            Model: embedding.Model,
            ModelVersion: embedding.ModelVersion,
            Dimensions: embedding.Dimensions,
            Metadata: BuildMetadata(definition)), ct);

        definition.EmbeddingState = EmbeddingState.Indexed;
        definition.EmbeddingModel = embedding.Model;
        definition.EmbeddingModelVersion = embedding.ModelVersion;
        definition.EmbeddingDimensions = embedding.Dimensions;
        definition.EmbeddedAt = DateTime.UtcNow;
        await _definitions.UpsertAsync(definition, ct);

        var searchSw = Stopwatch.StartNew();
        var hits = await _vectorSearch.SearchAsync(new VectorSearchQuery(
            Vector: embedding.Vector,
            TenantId: tenantId,
            EntityType: EntityTypes.Knowledge,
            MetadataFilters: null,
            Limit: _analysis.VectorCandidatePoolSize), ct);
        searchSw.Stop();

        var filtered = hits
            .Where(h => h.Similarity >= _analysis.MinSimilarity)
            .OrderByDescending(h => h.Similarity)
            .Take(_analysis.TopK)
            .ToList();

        var candidates = await BuildCandidatesAsync(tenantId, filtered, ct);

        var prompt = _promptBuilder.Build(definition, redactedMessage, candidates, _openAi.PromptVersion);
        var llmSw = Stopwatch.StartNew();
        var completion = await _chat.CompleteAsync(prompt.SystemPrompt, prompt.UserPrompt, ct);
        llmSw.Stop();

        var parsed = TryParseLlmResponse(completion.Content);
        var similarities = candidates.Select(c => c.Similarity).ToList();
        var topSuccess = candidates.Select(c => c.SolutionSuccessRate).FirstOrDefault(r => r.HasValue);
        var appConfidence = _confidence.Compute(similarities, parsed?.RootCauseConfidence ?? 0d, topSuccess);

        totalSw.Stop();

        var analysis = new AiAnalysis
        {
            TenantId = tenantId,
            OccurrenceId = occurrence.Id,
            DefinitionId = definition.Id,
            Fingerprint = fingerprint,
            Status = parsed is null ? "failed" : "ready",
            FailureReason = parsed is null ? "llm-parse-error" : null,
            Summary = parsed?.Summary ?? string.Empty,
            RootCause = parsed?.RootCause ?? string.Empty,
            RootCauseConfidence = parsed?.RootCauseConfidence ?? 0,
            ApplicationConfidence = appConfidence,
            RecommendedChecks = parsed?.RecommendedChecks ?? new(),
            RecommendedSolutions = parsed?.RecommendedSolutions ?? new(),
            KnownStatement = parsed?.Known,
            LikelyStatement = parsed?.Likely,
            UnknownStatement = parsed?.Unknown,
            Evidence = candidates.Select(c => new Evidence
            {
                EntityType = EntityTypes.Knowledge,
                EntityId = c.Entry.Id,
                Title = c.Entry.Title,
                Similarity = c.Similarity
            }).ToList(),
            Sources = candidates.Select(c => c.Entry.Id).ToList(),
            EmbeddingModel = embedding.Model,
            LlmModel = completion.Model,
            PromptVersion = _openAi.PromptVersion,
            KnowledgeVersion = _analysis.KnowledgeVersion,
            PromptTokens = completion.PromptTokens,
            CompletionTokens = completion.CompletionTokens,
            TotalTokens = completion.TotalTokens,
            EmbeddingLatencyMs = embedSw.ElapsedMilliseconds,
            VectorSearchLatencyMs = searchSw.ElapsedMilliseconds,
            LlmLatencyMs = llmSw.ElapsedMilliseconds,
            TotalLatencyMs = totalSw.ElapsedMilliseconds
        };

        await _analyses.InsertAsync(analysis, ct);

        return new AnalyzeExceptionResponse
        {
            AnalysisId = analysis.Id,
            Status = analysis.Status,
            Summary = analysis.Summary,
            RootCause = new RootCauseDto
            {
                Text = analysis.RootCause,
                Confidence = analysis.RootCauseConfidence
            },
            Confidence = analysis.ApplicationConfidence,
            Evidence = analysis.Evidence.Select(e => new EvidenceDto
            {
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Title = e.Title,
                Similarity = e.Similarity
            }).ToList(),
            Sources = analysis.Sources,
            RecommendedChecks = analysis.RecommendedChecks,
            RecommendedSolutions = analysis.RecommendedSolutions,
            Known = analysis.KnownStatement,
            Likely = analysis.LikelyStatement,
            Unknown = analysis.UnknownStatement,
            Usage = new UsageDto
            {
                EmbeddingModel = analysis.EmbeddingModel,
                LlmModel = analysis.LlmModel,
                PromptTokens = analysis.PromptTokens,
                CompletionTokens = analysis.CompletionTokens,
                TotalTokens = analysis.TotalTokens,
                EmbeddingLatencyMs = analysis.EmbeddingLatencyMs,
                VectorSearchLatencyMs = analysis.VectorSearchLatencyMs,
                LlmLatencyMs = analysis.LlmLatencyMs,
                TotalLatencyMs = analysis.TotalLatencyMs
            }
        };
    }

    public async Task<SearchExceptionsResponse> SearchAsync(
        string tenantId,
        SearchExceptionsRequest request,
        CancellationToken ct)
    {
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;
        var normalized = _normalizer.Normalize(_redactor.Redact(request.Query));
        var embedding = await _embedding.EmbedAsync(normalized, ct);

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(request.Service)) metadata["service"] = request.Service!;
        if (!string.IsNullOrWhiteSpace(request.Environment)) metadata["environment"] = request.Environment!;
        if (!string.IsNullOrWhiteSpace(request.Database)) metadata["database"] = request.Database!;

        var hits = await _vectorSearch.SearchAsync(new VectorSearchQuery(
            Vector: embedding.Vector,
            TenantId: tenantId,
            EntityType: null,
            MetadataFilters: metadata.Count == 0 ? null : metadata,
            Limit: Math.Max(1, request.TopK)), ct);

        var min = request.MinSimilarity ?? _analysis.MinSimilarity;
        return new SearchExceptionsResponse
        {
            Results = hits
                .Where(h => h.Similarity >= min)
                .Select(h => new SearchResultDto
                {
                    EntityType = h.EntityType,
                    EntityId = h.EntityId,
                    Snippet = Snippet(h.Content),
                    Similarity = Math.Round(h.Similarity, 4)
                })
                .ToList()
        };
    }

    private async Task<IReadOnlyList<KnowledgeCandidate>> BuildCandidatesAsync(
        string tenantId,
        IReadOnlyList<VectorSearchHit> hits,
        CancellationToken ct)
    {
        if (hits.Count == 0) return Array.Empty<KnowledgeCandidate>();

        var ids = hits.Select(h => h.EntityId).ToList();
        var entries = await _knowledge.GetByIdsAsync(tenantId, ids, ct);
        var byId = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

        var candidates = new List<KnowledgeCandidate>();
        foreach (var hit in hits)
        {
            if (!byId.TryGetValue(hit.EntityId, out var entry)) continue;
            var top = await _solutions.GetTopSolutionForKnowledgeAsync(tenantId, entry.Id, ct);
            candidates.Add(new KnowledgeCandidate(entry, hit.Similarity, top?.SuccessRate));
        }
        return candidates;
    }

    private static ExceptionDefinition BuildDefinition(
        string tenantId,
        ReportExceptionRequest request,
        string redactedMessage,
        string normalized,
        string fingerprint) => new()
        {
            TenantId = tenantId,
            Fingerprint = fingerprint,
            ExceptionType = request.ExceptionType,
            OriginalMessage = redactedMessage,
            NormalizedMessage = normalized,
            Database = request.Database,
            Module = request.Module,
            Category = request.Category,
            Severity = request.Severity,
            Tags = request.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

    private static void MergeIntoDefinition(ExceptionDefinition definition, ReportExceptionRequest request)
    {
        definition.Database ??= request.Database;
        definition.Module ??= request.Module;
        definition.Category ??= request.Category;
        definition.Severity ??= request.Severity;

        if (request.Tags.Count > 0)
            definition.Tags = definition.Tags
                .Concat(request.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static ExceptionOccurrence BuildOccurrence(
        string tenantId,
        ReportExceptionRequest request,
        string redactedMessage,
        string redactedStack,
        ExceptionDefinition definition) => new()
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            Fingerprint = definition.Fingerprint,
            OriginalMessage = redactedMessage,
            StackTrace = string.IsNullOrEmpty(redactedStack) ? null : redactedStack,
            InnerException = request.InnerException,
            ErrorCode = request.ErrorCode,
            Application = request.Application,
            Service = request.Service,
            Module = request.Module,
            Version = request.Version,
            Environment = request.Environment,
            Host = request.Host,
            Container = request.Container,
            Pod = request.Pod,
            Endpoint = request.Endpoint,
            HttpMethod = request.HttpMethod,
            StatusCode = request.StatusCode,
            Context = request.Context ?? new()
        };

    private static Dictionary<string, string> BuildMetadata(ExceptionDefinition d)
    {
        var meta = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(d.Database)) meta["database"] = d.Database;
        if (!string.IsNullOrWhiteSpace(d.Module)) meta["module"] = d.Module;
        if (!string.IsNullOrWhiteSpace(d.Category)) meta["category"] = d.Category;
        if (d.Tags.Count > 0) meta["tags"] = string.Join(",", d.Tags);
        return meta;
    }

    private LlmResponse? TryParseLlmResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            var json = ExtractJson(content);
            return JsonSerializer.Deserialize<LlmResponse>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON");
            return null;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text;
    }

    private static string Snippet(string content)
    {
        var trimmed = content.Replace('\n', ' ').Replace('\r', ' ');
        return trimmed.Length <= 240 ? trimmed : trimmed[..240] + "...";
    }

    private sealed class LlmResponse
    {
        public string Summary { get; set; } = string.Empty;
        public string RootCause { get; set; } = string.Empty;
        public double RootCauseConfidence { get; set; }
        public string? Known { get; set; }
        public string? Likely { get; set; }
        public string? Unknown { get; set; }
        public List<string> RecommendedChecks { get; set; } = new();
        public List<string> RecommendedSolutions { get; set; } = new();
        public List<string> SourceKnowledgeIds { get; set; } = new();
    }
}
