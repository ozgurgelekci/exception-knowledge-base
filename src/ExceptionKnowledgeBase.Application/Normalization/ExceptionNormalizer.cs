using System.Text.RegularExpressions;

namespace ExceptionKnowledgeBase.Application.Normalization;

// Section 10: replace volatile values with placeholders so embedding stays stable.
public interface IExceptionNormalizer
{
    string Normalize(string message);
}

public sealed class ExceptionNormalizer : IExceptionNormalizer
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        (new Regex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.Compiled), "{GUID}"),
        (new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled), "{IP}"),
        (new Regex(@"(?<=:)\d{2,5}\b", RegexOptions.Compiled), "{PORT}"),
        (new Regex(@"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?\b", RegexOptions.Compiled), "{TIMESTAMP}"),
        (new Regex(@"\bafter\s+\d+\s?ms\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "after {DURATION}ms"),
        (new Regex(@"\bin\s+\d+\s?ms\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "in {DURATION}ms"),
        (new Regex(@"\b\d{7,}\b", RegexOptions.Compiled), "{LARGE_NUMBER}"),
        (new Regex(@"([A-Za-z]:\\|/)[^\s""']+", RegexOptions.Compiled), "{PATH}"),
        (new Regex(@"https?://[^\s""']+", RegexOptions.Compiled), "{URL}"),
        (new Regex(@"\s+", RegexOptions.Compiled), " "),
    };

    public string Normalize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var text = message;
        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);

        return text.Trim();
    }
}
