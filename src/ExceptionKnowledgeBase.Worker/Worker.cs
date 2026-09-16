using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Domain.Common;
using ExceptionKnowledgeBase.Domain.Exceptions;

namespace ExceptionKnowledgeBase.Worker;

// Section 42: pull pending definitions, embed, upsert into pgvector.
public sealed class ExceptionEmbeddingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExceptionEmbeddingWorker> _logger;

    public ExceptionEmbeddingWorker(IServiceScopeFactory scopeFactory, ILogger<ExceptionEmbeddingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception embedding worker batch failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IExceptionDefinitionRepository>();
        var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
        var vectors = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var inputBuilder = scope.ServiceProvider.GetRequiredService<IEmbeddingInputBuilder>();

        var pending = await repo.GetPendingEmbeddingsAsync(20, ct);
        if (pending.Count == 0) return;

        _logger.LogInformation("Embedding {Count} exception definitions", pending.Count);
        foreach (var def in pending)
        {
            try
            {
                var content = inputBuilder.ForException(def);
                var result = await embedding.EmbedAsync(content, ct);
                await vectors.UpsertAsync(new EmbeddingUpsert(
                    EntityId: def.Id,
                    EntityType: EntityTypes.Exception,
                    TenantId: def.TenantId,
                    Content: content,
                    Vector: result.Vector,
                    Model: result.Model,
                    ModelVersion: result.ModelVersion,
                    Dimensions: result.Dimensions,
                    Metadata: BuildMetadata(def)), ct);
                await repo.MarkEmbeddedAsync(def.Id, result.Model, result.ModelVersion, result.Dimensions, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding failed for definition {Id}", def.Id);
                await repo.MarkFailedAsync(def.Id, ex.Message, ct);
            }
        }
    }

    private static Dictionary<string, string> BuildMetadata(ExceptionDefinition d)
    {
        var meta = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(d.Database)) meta["database"] = d.Database;
        if (!string.IsNullOrWhiteSpace(d.Module)) meta["module"] = d.Module;
        if (!string.IsNullOrWhiteSpace(d.Category)) meta["category"] = d.Category;
        if (d.Tags.Count > 0) meta["tags"] = string.Join(",", d.Tags);
        return meta;
    }
}
