using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Domain.Common;

namespace ExceptionKnowledgeBase.Worker;

// Section 41: same pipeline for knowledge entries.
public sealed class KnowledgeEmbeddingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KnowledgeEmbeddingWorker> _logger;

    public KnowledgeEmbeddingWorker(IServiceScopeFactory scopeFactory, ILogger<KnowledgeEmbeddingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "Knowledge embedding batch failed"); }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnowledgeEntryRepository>();
        var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var vectors = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var inputBuilder = scope.ServiceProvider.GetRequiredService<IEmbeddingInputBuilder>();

        var pending = await repo.GetPendingEmbeddingsAsync(20, ct);
        if (pending.Count == 0) return;

        _logger.LogInformation("Embedding {Count} knowledge entries", pending.Count);
        foreach (var entry in pending)
        {
            try
            {
                var content = inputBuilder.ForKnowledge(entry);
                var result = await embedding.EmbedAsync(content, ct);
                await vectors.UpsertAsync(new EmbeddingUpsert(
                    EntityId: entry.Id,
                    EntityType: EntityTypes.Knowledge,
                    TenantId: entry.TenantId,
                    Content: content,
                    Vector: result.Vector,
                    Model: result.Model,
                    ModelVersion: result.ModelVersion,
                    Dimensions: result.Dimensions,
                    Metadata: BuildMetadata(entry)), ct);
                await repo.MarkEmbeddedAsync(entry.Id, result.Model, result.ModelVersion, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Knowledge embedding failed for {Id}", entry.Id);
            }
        }
    }

    private static Dictionary<string, string> BuildMetadata(Domain.Knowledge.KnowledgeEntry entry)
    {
        var meta = new Dictionary<string, string>
        {
            ["status"] = entry.Status,
            ["version"] = entry.Version.ToString()
        };
        if (entry.Tags.Count > 0) meta["tags"] = string.Join(",", entry.Tags);
        return meta;
    }
}
