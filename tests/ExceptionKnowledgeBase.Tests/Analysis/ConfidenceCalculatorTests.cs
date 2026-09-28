using ExceptionKnowledgeBase.Application.Analysis;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Analysis;

public sealed class ConfidenceCalculatorTests
{
    private readonly ConfidenceCalculator _sut = new();

    [Fact]
    public void Empty_similarities_scales_llm_confidence_down()
    {
        var score = _sut.Compute(Array.Empty<double>(), llmConfidence: 0.9, topSolutionSuccessRate: null);
        Assert.Equal(Math.Clamp(0.9 * 0.4, 0, 1), score, 3);
    }

    [Fact]
    public void High_top_similarity_dominates_score()
    {
        var low = _sut.Compute(new[] { 0.5, 0.5, 0.5 }, 0.5, 0.5);
        var high = _sut.Compute(new[] { 0.95, 0.5, 0.5 }, 0.5, 0.5);
        Assert.True(high > low);
    }

    [Fact]
    public void Score_stays_in_unit_interval()
    {
        var score = _sut.Compute(new[] { 1.0, 1.0, 1.0, 1.0, 1.0 }, 1.0, 1.0);
        Assert.InRange(score, 0d, 1d);
    }

    [Fact]
    public void Success_rate_boost_improves_score()
    {
        var withoutBoost = _sut.Compute(new[] { 0.8 }, 0.5, 0.0);
        var withBoost = _sut.Compute(new[] { 0.8 }, 0.5, 1.0);
        Assert.True(withBoost > withoutBoost);
    }
}
