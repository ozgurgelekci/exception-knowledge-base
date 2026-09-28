using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Domain.Knowledge;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Analysis;

public sealed class LexicalOverlapReRankerTests
{
    private readonly LexicalOverlapReRanker _sut = new(weight: 0.2);

    [Fact]
    public void Boosts_candidate_with_shared_tokens()
    {
        var relevant = Candidate("kb-a", "MongoDB timeout waiting for server", similarity: 0.80);
        var noise    = Candidate("kb-b", "SQL deadlock during commit", similarity: 0.80);

        var ranked = _sut.ReRank("MongoDB timeout waiting for server selection", new[] { noise, relevant });
        Assert.Equal("kb-a", ranked[0].Entry.Id);
    }

    [Fact]
    public void Empty_query_returns_input_untouched()
    {
        var a = Candidate("x", "anything", 0.7);
        var ranked = _sut.ReRank(string.Empty, new[] { a });
        Assert.Same(a, ranked[0]);
    }

    [Fact]
    public void Empty_candidates_returned_as_is()
    {
        var ranked = _sut.ReRank("some query", Array.Empty<KnowledgeCandidate>());
        Assert.Empty(ranked);
    }

    private static KnowledgeCandidate Candidate(string id, string title, double similarity)
    {
        var entry = new KnowledgeEntry
        {
            Id = id,
            Title = title,
            RootCause = title,
            ExceptionTypes = new() { "System.TimeoutException" },
            Solution = new() { "retry with backoff" }
        };
        return new KnowledgeCandidate(entry, similarity, SolutionSuccessRate: null, RankScore: similarity);
    }
}
