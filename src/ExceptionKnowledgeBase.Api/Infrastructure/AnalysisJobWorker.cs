using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Services;

namespace ExceptionKnowledgeBase.Api.Infrastructure;

// Phase 2 (§64): drains the analysis queue in the background so /analyze/async
// can return 202 immediately.
public sealed class AnalysisJobWorker : BackgroundService
{
    private readonly IAnalysisJobQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AnalysisJobWorker> _logger;

    public AnalysisJobWorker(
        IAnalysisJobQueue queue,
        IServiceScopeFactory scopes,
        ILogger<AnalysisJobWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnalysisJobWorker started");
        await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IExceptionAnalysisService>();
                await service.RunPendingAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error processing analysis job {Id}", job.AnalysisId);
            }
        }
    }
}
