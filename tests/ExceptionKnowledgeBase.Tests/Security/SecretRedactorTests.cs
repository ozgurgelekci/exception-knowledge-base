using ExceptionKnowledgeBase.Application.Security;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Security;

public sealed class SecretRedactorTests
{
    private readonly SecretRedactor _sut = new();

    [Theory]
    [InlineData("password=hunter2", "password={REDACTED}")]
    [InlineData("apiKey: sk-abc123", "apiKey={REDACTED}")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: Bearer {TOKEN_REDACTED}")]
    public void Strips_common_secret_shapes(string input, string expectedFragment)
    {
        var redacted = _sut.Redact(input);
        Assert.Contains(expectedFragment, redacted);
    }

    [Fact]
    public void Strips_connection_strings()
    {
        var redacted = _sut.Redact("mongodb+srv://user:pw@cluster.example.net/db");
        Assert.Contains("{CONN_STRING_REDACTED}", redacted);
        Assert.DoesNotContain("user:pw", redacted);
    }

    [Fact]
    public void Strips_jwt_looking_tokens()
    {
        var redacted = _sut.Redact("token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");
        Assert.Contains("{JWT_REDACTED}", redacted);
    }

    [Fact]
    public void Strips_email_addresses()
    {
        var redacted = _sut.Redact("user o.gelekci@teamsystem.com hit the bug");
        Assert.Contains("{EMAIL_REDACTED}", redacted);
    }

    [Fact]
    public void Passes_through_when_no_secrets_present()
    {
        Assert.Equal("plain error", _sut.Redact("plain error"));
    }
}
