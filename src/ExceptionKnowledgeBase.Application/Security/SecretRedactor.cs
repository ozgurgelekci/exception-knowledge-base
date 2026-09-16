using System.Text.RegularExpressions;

namespace ExceptionKnowledgeBase.Application.Security;

// Sections 49–51: strip secrets/PII before anything leaves the trust boundary.
public interface ISecretRedactor
{
    string Redact(string input);
}

public sealed class SecretRedactor : ISecretRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        (new Regex(@"(?i)(password|pwd)\s*[=:]\s*[""']?[^""'\s;,]+", RegexOptions.Compiled), "$1={REDACTED}"),
        (new Regex(@"(?i)(api[_-]?key|apikey|secret|token)\s*[=:]\s*[""']?[^""'\s;,]+", RegexOptions.Compiled), "$1={REDACTED}"),
        (new Regex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled), "Bearer {TOKEN_REDACTED}"),
        (new Regex(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{5,}", RegexOptions.Compiled), "{JWT_REDACTED}"),
        (new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled), "{EMAIL_REDACTED}"),
        (new Regex(@"(?i)(mongodb(\+srv)?|postgres(ql)?|amqp|redis)://[^\s""']+", RegexOptions.Compiled), "{CONN_STRING_REDACTED}"),
    };

    public string Redact(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        var text = input;
        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);

        return text;
    }
}
