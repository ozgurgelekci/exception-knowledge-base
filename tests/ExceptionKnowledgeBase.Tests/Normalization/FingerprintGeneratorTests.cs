using ExceptionKnowledgeBase.Application.Normalization;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Normalization;

public sealed class FingerprintGeneratorTests
{
    private readonly FingerprintGenerator _sut = new();

    [Fact]
    public void Identical_inputs_produce_identical_fingerprint()
    {
        var a = _sut.Generate("System.TimeoutException", "operation timed out after {DURATION}ms");
        var b = _sut.Generate("System.TimeoutException", "operation timed out after {DURATION}ms");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_exception_types_produce_different_fingerprint()
    {
        var a = _sut.Generate("System.TimeoutException", "operation timed out");
        var b = _sut.Generate("System.HttpRequestException", "operation timed out");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Output_is_lowercase_hex_64_chars()
    {
        var fp = _sut.Generate("t", "m");
        Assert.Equal(64, fp.Length);
        Assert.Matches("^[0-9a-f]{64}$", fp);
    }
}
