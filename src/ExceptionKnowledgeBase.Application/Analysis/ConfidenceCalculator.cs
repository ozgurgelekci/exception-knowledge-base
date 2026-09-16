namespace ExceptionKnowledgeBase.Application.Analysis;

// Section 25: application-level confidence beats a raw LLM score.
public interface IConfidenceCalculator
{
    double Compute(IReadOnlyList<double> similarities, double llmConfidence, double? topSolutionSuccessRate);
}

public sealed class ConfidenceCalculator : IConfidenceCalculator
{
    public double Compute(IReadOnlyList<double> similarities, double llmConfidence, double? topSolutionSuccessRate)
    {
        if (similarities is null || similarities.Count == 0)
            return Math.Clamp(llmConfidence * 0.4, 0, 1);

        var top = similarities.Max();
        var avg = similarities.Average();
        var support = Math.Min(similarities.Count / 5d, 1d);
        var successBoost = topSolutionSuccessRate ?? 0.5;

        var score =
            0.45 * top +
            0.15 * avg +
            0.15 * support +
            0.10 * successBoost +
            0.15 * Math.Clamp(llmConfidence, 0, 1);

        return Math.Round(Math.Clamp(score, 0, 1), 3);
    }
}
