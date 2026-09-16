using System.Security.Cryptography;
using System.Text;

namespace ExceptionKnowledgeBase.Application.Normalization;

// Section 9: SHA256 over normalized signal to dedupe repeat exceptions.
public interface IFingerprintGenerator
{
    string Generate(string exceptionType, string normalizedMessage);
}

public sealed class FingerprintGenerator : IFingerprintGenerator
{
    public string Generate(string exceptionType, string normalizedMessage)
    {
        var payload = $"{exceptionType ?? string.Empty}::{normalizedMessage ?? string.Empty}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
