using System.Threading.Channels;
using ExceptionKnowledgeBase.Application.Abstractions;

namespace ExceptionKnowledgeBase.Api.Infrastructure;

// Phase 2 (§64): in-process bounded queue for async analyze jobs.
// Trade-off: process crash drops in-flight jobs — acceptable for MVP since the
// AiAnalysis record stays "pending" and can be retried by resubmitting.
public sealed class ChannelAnalysisJobQueue : IAnalysisJobQueue
{
    private readonly Channel<AnalysisJob> _channel;

    public ChannelAnalysisJobQueue()
    {
        _channel = Channel.CreateBounded<AnalysisJob>(new BoundedChannelOptions(capacity: 128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(AnalysisJob job, CancellationToken ct)
        => _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<AnalysisJob> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
