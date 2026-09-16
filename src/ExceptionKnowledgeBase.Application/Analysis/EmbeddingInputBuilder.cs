using System.Text;
using ExceptionKnowledgeBase.Domain.Exceptions;
using ExceptionKnowledgeBase.Domain.Knowledge;

namespace ExceptionKnowledgeBase.Application.Analysis;

// Section 12: hand-tuned embedding input; keeps operational noise out.
public interface IEmbeddingInputBuilder
{
    string ForException(ExceptionDefinition definition);
    string ForKnowledge(KnowledgeEntry entry);
}

public sealed class EmbeddingInputBuilder : IEmbeddingInputBuilder
{
    public string ForException(ExceptionDefinition definition)
    {
        var sb = new StringBuilder();
        sb.Append("Exception Type: ").AppendLine(definition.ExceptionType);
        sb.Append("Message: ").AppendLine(definition.NormalizedMessage);
        if (!string.IsNullOrWhiteSpace(definition.Database))
            sb.Append("Database: ").AppendLine(definition.Database);
        if (!string.IsNullOrWhiteSpace(definition.Module))
            sb.Append("Module: ").AppendLine(definition.Module);
        if (!string.IsNullOrWhiteSpace(definition.Category))
            sb.Append("Category: ").AppendLine(definition.Category);
        if (definition.Tags.Count > 0)
            sb.Append("Tags: ").AppendLine(string.Join(", ", definition.Tags));
        return sb.ToString().TrimEnd();
    }

    public string ForKnowledge(KnowledgeEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append("Title: ").AppendLine(entry.Title);
        if (entry.ExceptionTypes.Count > 0)
            sb.Append("Exception Types: ").AppendLine(string.Join(", ", entry.ExceptionTypes));
        if (entry.Symptoms.Count > 0)
            sb.Append("Symptoms: ").AppendLine(string.Join(" | ", entry.Symptoms));
        sb.Append("Root Cause: ").AppendLine(entry.RootCause);
        if (entry.Solution.Count > 0)
            sb.Append("Solution: ").AppendLine(string.Join(" | ", entry.Solution));
        if (entry.Tags.Count > 0)
            sb.Append("Tags: ").AppendLine(string.Join(", ", entry.Tags));
        return sb.ToString().TrimEnd();
    }
}
