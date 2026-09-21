namespace ExceptionKnowledgeBase.Contracts.Exceptions;

// Phase 4 (§75): fingerprint counts bucketed by UTC day.
public sealed class TrendsResponse
{
    public int Days { get; set; }
    public DateTime SinceUtc { get; set; }
    public List<FingerprintTrendDto> Fingerprints { get; set; } = new();
}

public sealed class FingerprintTrendDto
{
    public string Fingerprint { get; set; } = string.Empty;
    public long Total { get; set; }
    public List<TrendPointDto> Series { get; set; } = new();
}

public sealed class TrendPointDto
{
    public DateTime Day { get; set; }
    public long Count { get; set; }
}
