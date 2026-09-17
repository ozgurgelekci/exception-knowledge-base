using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IAnalysisResponseCache? _analysisCache;
    private readonly ISearchResponseCache? _searchCache;
    private readonly IAnalysisJobQueue? _jobQueue;
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
        ILogger<ExceptionAnalysisService> logger,
        IAnalysisResponseCache? analysisCache = null,
        ISearchResponseCache? searchCache = null,
        IAnalysisJobQueue? jobQueue = null)
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
        _analysisCache = analysisCache;
        _searchCache = searchCache;
        _jobQueue = jobQueue;
        _openAi = openAi.Value;
        _analysis = analysis.Value;
        _logger = logger;
    }

    public Task<AnalyzeExceptionResponse> AnalyzeAsync(
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken ct)
        => RunAnalysisAsync(analysisId: null, tenantId, request, ct);

    public async Task<string> EnqueueAsync(
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken ct)
    {
        if (_jobQueue is null)
            throw new InvalidOperationException("Async analysis is not enabled: no IAnalysisJobQueue registered.");

        tenantId = NormalizeTenant(tenantId);
        var analysisId = Guid.NewGuid().ToString("N");
        var pending = new AiAnalysis
        {
            Id = analysisId,
            TenantId = tenantId,
            Status = "pending"
        };
        await _analyses.UpsertAsync(pending, ct);
        await _jobQueue.EnqueueAsync(new AnalysisJob(analysisId, tenantId, request), ct);
        return analysisId;
    }

    public async Task RunPendingAsync(AnalysisJob job, CancellationToken ct)
    {
        try
        {
            await RunAnalysisAsync(job.AnalysisId, job.TenantId, job.Request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Async analysis {Id} failed", job.AnalysisId);
            var pending = await _analyses.GetByIdAsync(job.TenantId, job.AnalysisId, ct) ?? new AiAnalysis
            {
                Id = job.AnalysisId,
                TenantId = job.TenantId
            };
            pending.Status = "failed";
            pending.FailureReason = ex.Message;
            await _analyses.UpsertAsync(pending, ct);
        }
    }

    public Task<AiAnalysis?> GetAnalysisAsync(string tenantId, string id, CancellationToken ct)
        => _analyses.GetByIdAsync(NormalizeTenant(tenantId), id, ct);

    private async Task<AnalyzeExceptionResponse> RunAnalysisAsync(
        string? analysisId,
        string tenantId,
        ReportExceptionRequest request,
        CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();
        tenantId = NormalizeTenant(tenantId);

        var redactedMessage = _redactor.Redact(request.Message);
        var redactedStack = _redactor.Redact(request.StackTrace ?? string.Empty);
        var normalized = _normalizer.Normalize(redactedMessage);
        var fingerprint = _fingerprints.Generate(request.ExceptionType, normalized);

        // §36: identical (tenant + fingerprint + model + prompt/knowledge version) → serve cached.
        var cacheKey = new AnalysisCacheKey(
            tenantId,
            fingerprint,
            _openAi.EmbeddingModel,
            _openAi.EmbeddingModelVersion,
            _openAi.ChatModel,
            _openAi.PromptVersion,
            _analysis.KnowledgeVersion);

        if (analysisId is null && _analysis.AnalysisCacheEnabled && _analysisCache is not null)
        {
            var cached = await _analysisCache.GetAsync(cacheKey, ct);
            if (cached is not null)
            {
                _logger.LogInformation("Analysis cache hit for tenant {Tenant} fingerprint {Fingerprint}", tenantId, fingerprint);
                return cached;
            }
        }

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
        var metadataFilters = BuildKnowledgeSearchFilters();
        var hits = await _vectorSearch.SearchAsync(new VectorSearchQuery(
            Vector: embedding.Vector,
            TenantId: tenantId,
            EntityType: EntityTypes.Knowledge,
            MetadataFilters: metadataFilters,
            Limit: _analysis.VectorCandidatePoolSize,
            QueryText: _analysis.HybridSearchEnabled ? embeddingContent : null,
            UseHybrid: _analysis.HybridSearchEnabled,
            HybridVectorWeight: _analysis.HybridVectorWeight,
            HybridKeywordWeight: _analysis.HybridKeywordWeight,
            RrfK: _analysis.HybridRrfK), ct);
        searchSw.Stop();

        // SQL already returns rows best-first (RRF for hybrid, cosine for vector-only).
        var filtered = hits
            .Where(h => h.Similarity >= _analysis.MinSimilarity)
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
            Id = analysisId ?? Guid.NewGuid().ToString("N"),
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

        // Upsert so async path can overwrite the pending record with the ready one.
        await _analyses.UpsertAsync(analysis, ct);

        var response = ToResponse(analysis);

        if (_analysis.AnalysisCacheEnabled && _analysisCache is not null && analysis.Status == "ready")
        {
            try { await _analysisCache.SetAsync(cacheKey, response, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to write analysis cache"); }
        }

        return response;
    }

    private static AnalyzeExceptionResponse ToResponse(AiAnalysis a) => new()
    {
        AnalysisId = a.Id,
        Status = a.Status,
        Summary = a.Summary,
        RootCause = new RootCauseDto { Text = a.RootCause, Confidence = a.RootCauseConfidence },
        Confidence = a.ApplicationConfidence,
        Evidence = a.Evidence.Select(e => new EvidenceDto
        {
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            Title = e.Title,
            Similarity = e.Similarity
        }).ToList(),
        Sources = a.Sources,
        RecommendedChecks = a.RecommendedChecks,
        RecommendedSolutions = a.RecommendedSolutions,
        Known = a.KnownStatement,
        Likely = a.LikelyStatement,
        Unknown = a.UnknownStatement,
        Usage = new UsageDto
        {
            EmbeddingModel = a.EmbeddingModel,
            LlmModel = a.LlmModel,
            PromptTokens = a.PromptTokens,
            CompletionTokens = a.CompletionTokens,
            TotalTokens = a.TotalTokens,
            EmbeddingLatencyMs = a.EmbeddingLatencyMs,
            VectorSearchLatencyMs = a.VectorSearchLatencyMs,
            LlmLatencyMs = a.LlmLatencyMs,
            TotalLatencyMs = a.TotalLatencyMs
        }
    };

    private Dictionary<string, string>? BuildKnowledgeSearchFilters()
    {
        if (!_analysis.RequireVerifiedKnowledge) return null;
        return new Dictionary<string, string> { ["status"] = "verified" };
    }

    private string NormalizeTenant(string tenantId)
        => string.IsNullOrWhiteSpace(tenantId) ? _analysis.DefaultTenantId : tenantId;

    public async Task<SearchExceptionsResponse> SearchAsync(
        string tenantId,
        SearchExceptionsRequest request,
        CancellationToken ct)
    {
        tenantId = NormalizeTenant(tenantId);
        var normalized = _normalizer.Normalize(_redactor.Redact(request.Query));
        var topK = Math.Max(1, request.TopK);
        var minSim = request.MinSimilarity ?? _analysis.MinSimilarity;

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(request.Service)) metadata["service"] = request.Service!;
        if (!string.IsNullOrWhiteSpace(request.Environment)) metadata["environment"] = request.Environment!;
        if (!string.IsNullOrWhiteSpace(request.Database)) metadata["database"] = request.Database!;

        // §36: cache identical (query + filters + topK + threshold) responses.
        var searchKey = new SearchCacheKey(
            tenantId,
            HashText($"{normalized}|{string.Join(",", metadata.OrderBy(m => m.Key).Select(m => $"{m.Key}={m.Value}"))}"),
            _openAi.EmbeddingModel,
            _openAi.EmbeddingModelVersion,
            topK,
            minSim);

        if (_analysis.AnalysisCacheEnabled && _searchCache is not null)
        {
            var cached = await _searchCache.GetAsync(searchKey, ct);
            if (cached is not null)
            {
                _logger.LogDebug("Search cache hit for tenant {Tenant}", tenantId);
                return cached;
            }
        }

        var embedding = await _embedding.EmbedAsync(normalized, ct);

        var hits = await _vectorSearch.SearchAsync(new VectorSearchQuery(
            Vector: embedding.Vector,
            TenantId: tenantId,
            EntityType: null,
            MetadataFilters: metadata.Count == 0 ? null : metadata,
            Limit: topK,
            QueryText: _analysis.HybridSearchEnabled ? normalized : null,
            UseHybrid: _analysis.HybridSearchEnabled,
            HybridVectorWeight: _analysis.HybridVectorWeight,
            HybridKeywordWeight: _analysis.HybridKeywordWeight,
            RrfK: _analysis.HybridRrfK), ct);

        var response = new SearchExceptionsResponse
        {
            Results = hits
                .Where(h => h.Similarity >= minSim)
                .Select(h => new SearchResultDto
                {
                    EntityType = h.EntityType,
                    EntityId = h.EntityId,
                    Snippet = Snippet(h.Content),
                    Similarity = Math.Round(h.Similarity, 4)
                })
                .ToList()
        };

        if (_analysis.AnalysisCacheEnabled && _searchCache is not null)
        {
            try { await _searchCache.SetAsync(searchKey, response, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to write search cache"); }
        }

        return response;
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

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
