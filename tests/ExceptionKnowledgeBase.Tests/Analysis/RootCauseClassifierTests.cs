using ExceptionKnowledgeBase.Application.Analysis;
using Xunit;

namespace ExceptionKnowledgeBase.Tests.Analysis;

public sealed class RootCauseClassifierTests
{
    private readonly RootCauseClassifier _sut = new();

    [Theory]
    [InlineData("System.TimeoutException", "operation timed out", "timeout")]
    [InlineData("Npgsql.PostgresException", "connection reset by peer", "database")]
    [InlineData("System.UnauthorizedAccessException", "token expired", "auth")]
    [InlineData("System.Net.Http.HttpRequestException", "dns error", "network")]
    [InlineData("System.NullReferenceException", "", "null")]
    [InlineData("System.ArgumentException", "value cannot be parse-d", "validation")]
    [InlineData("MyCustom.Exception", "meh", "unknown")]
    public void Classifies_from_type_or_message(string type, string message, string expected)
    {
        Assert.Equal(expected, _sut.Classify(type, message));
    }
}
