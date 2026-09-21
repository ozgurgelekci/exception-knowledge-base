using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ExceptionKnowledgeBase.Application.Analysis;

// §75: proactive surge detection. Every occurrence pings the detector; when a
// fingerprint's short-window rate blows past its long-window baseline, we emit
// an alert. Fully in-process — Kafka/PubSub can subscribe to IAnomalyAlertSink later.
public interface IAnomalyDetector
{
    void RecordOccurrence(string tenantId, string fingerprint, DateTime occurredAtUtc);
}

public interface IAnomalyAlertSink
{
    void Publish(AnomalyAlert alert);
    IReadOnlyList<AnomalyAlert> RecentAlerts(string tenantId, int limit);
}

public sealed record AnomalyAlert(
    string TenantId,
    string Fingerprint,
    int ShortWindowCount,
    double BaselinePerWindow,
    double Multiplier,
    DateTime DetectedAtUtc);

public sealed class InMemoryAnomalyDetector : IAnomalyDetector
{
    // Ring of occurrence timestamps per (tenant, fingerprint). Bounded to LongWindow
    // so memory is predictable even under sustained load.
    private readonly ConcurrentDictionary<string, TimestampRing> _rings = new();
    private readonly IAnomalyAlertSink _sink;
    private readonly TimeSpan _short;
    private readonly TimeSpan _long;
    private readonly int _minShortCount;
    private readonly double _multiplierThreshold;
    private readonly TimeSpan _alertCooldown;
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertAt = new();

    public InMemoryAnomalyDetector(
        IAnomalyAlertSink sink,
        TimeSpan? shortWindow = null,
        TimeSpan? longWindow = null,
        int minShortCount = 5,
        double multiplierThreshold = 4.0,
        TimeSpan? alertCooldown = null)
    {
        _sink = sink;
        _short = shortWindow ?? TimeSpan.FromMinutes(10);
        _long = longWindow ?? TimeSpan.FromHours(24);
        _minShortCount = minShortCount;
        _multiplierThreshold = multiplierThreshold;
        _alertCooldown = alertCooldown ?? TimeSpan.FromMinutes(30);
    }

    public void RecordOccurrence(string tenantId, string fingerprint, DateTime occurredAtUtc)
    {
        var key = tenantId + "|" + fingerprint;
        var ring = _rings.GetOrAdd(key, _ => new TimestampRing(2048));
        ring.Add(occurredAtUtc);

        var now = DateTime.UtcNow;
        var shortCount = ring.CountSince(now - _short);
        if (shortCount < _minShortCount) return;

        var longCount = ring.CountSince(now - _long);
        // baseline: expected per-short-window count if the long window is uniformly distributed
        var baseline = longCount * (_short.TotalSeconds / _long.TotalSeconds);
        if (baseline <= 0.5) baseline = 0.5;  // guard against divide-by-tiny for first-seen bursts

        var multiplier = shortCount / baseline;
        if (multiplier < _multiplierThreshold) return;

        if (_lastAlertAt.TryGetValue(key, out var last) && now - last < _alertCooldown) return;
        _lastAlertAt[key] = now;

        _sink.Publish(new AnomalyAlert(tenantId, fingerprint, shortCount, baseline, multiplier, now));
    }

    private sealed class TimestampRing
    {
        private readonly object _lock = new();
        private readonly DateTime[] _buf;
        private int _head;
        private int _size;

        public TimestampRing(int capacity) => _buf = new DateTime[capacity];

        public void Add(DateTime t)
        {
            lock (_lock)
            {
                _buf[_head] = t;
                _head = (_head + 1) % _buf.Length;
                if (_size < _buf.Length) _size++;
            }
        }

        public int CountSince(DateTime cutoff)
        {
            lock (_lock)
            {
                var count = 0;
                for (var i = 0; i < _size; i++)
                {
                    var idx = (_head - 1 - i + _buf.Length) % _buf.Length;
                    if (_buf[idx] >= cutoff) count++;
                    else break; // ring is append-order; older entries fail the cutoff
                }
                return count;
            }
        }
    }
}

public sealed class InMemoryAnomalyAlertSink : IAnomalyAlertSink
{
    private readonly object _lock = new();
    private readonly LinkedList<AnomalyAlert> _alerts = new();
    private readonly int _capacity;
    private readonly ILogger<InMemoryAnomalyAlertSink> _logger;

    public InMemoryAnomalyAlertSink(ILogger<InMemoryAnomalyAlertSink> logger, int capacity = 500)
    {
        _logger = logger;
        _capacity = capacity;
    }

    public void Publish(AnomalyAlert alert)
    {
        _logger.LogWarning(
            "anomaly: tenant={Tenant} fingerprint={Fp} short={Short} baseline={Baseline:F2} multiplier={Mult:F2}",
            alert.TenantId, alert.Fingerprint, alert.ShortWindowCount, alert.BaselinePerWindow, alert.Multiplier);

        lock (_lock)
        {
            _alerts.AddFirst(alert);
            while (_alerts.Count > _capacity) _alerts.RemoveLast();
        }
    }

    public IReadOnlyList<AnomalyAlert> RecentAlerts(string tenantId, int limit)
    {
        lock (_lock)
        {
            return _alerts
                .Where(a => a.TenantId == tenantId)
                .Take(limit)
                .ToList();
        }
    }
}
