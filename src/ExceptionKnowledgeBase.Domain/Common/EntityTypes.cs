namespace ExceptionKnowledgeBase.Domain.Common;

public static class EntityTypes
{
    public const string Exception = "exception";
    public const string Knowledge = "knowledge";
    public const string Solution = "solution";
}

public static class Environments
{
    public const string Production = "production";
    public const string Staging = "staging";
    public const string Development = "development";
}

public static class Severities
{
    public const string Critical = "critical";
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
}
