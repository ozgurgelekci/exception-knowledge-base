using ExceptionKnowledgeBase.Application.Normalization;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Normalization;

public sealed class ExceptionNormalizerTests
{
    private readonly ExceptionNormalizer _sut = new();

    [Fact]
    public void Replaces_guids_ips_ports_and_timestamps()
    {
        var input = "Timed out at 2025-04-10T09:30:00Z reaching 10.0.0.5:5432 for req 550e8400-e29b-41d4-a716-446655440000";
        var normalized = _sut.Normalize(input);
        Assert.Contains("{TIMESTAMP}", normalized);
        Assert.Contains("{IP}:{PORT}", normalized);
        Assert.Contains("{GUID}", normalized);
    }

    [Fact]
    public void Replaces_urls_and_paths()
    {
        var input = "Fetching https://api.example.com/orders wrote /var/log/app.log";
        var normalized = _sut.Normalize(input);
        Assert.Contains("{URL}", normalized);
        Assert.Contains("{PATH}", normalized);
    }

    [Fact]
    public void Replaces_bare_large_numbers()
    {
        var normalized = _sut.Normalize("order 12345678 failed");
        Assert.Contains("{LARGE_NUMBER}", normalized);
    }

    [Fact]
    public void Collapses_whitespace_and_trims()
    {
        var normalized = _sut.Normalize("  hello    world  \n");
        Assert.Equal("hello world", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Returns_empty_for_missing_input(string? input)
    {
        Assert.Equal(string.Empty, _sut.Normalize(input!));
    }
}
