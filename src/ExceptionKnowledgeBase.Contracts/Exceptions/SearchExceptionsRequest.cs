namespace ExceptionKnowledgeBase.Contracts.Exceptions;

// POST /api/exceptions/search (Section 38).
public sealed class SearchExceptionsRequest
{
    public string Query { get; set; } = string.Empty;
    public string? Service { get; set; }
    public string? Environment { get; set; }
    public string? Database { get; set; }
    public List<string>? Tags { get; set; }
    public int TopK { get; set; } = 5;
    public double? MinSimilarity { get; set; }
}

public sealed class SearchExceptionsResponse
{
    public List<SearchResultDto> Results { get; set; } = new();
}

public sealed class SearchResultDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Snippet { get; set; }
    public double Similarity { get; set; }
}
