using ExceptionKnowledgeBase.Domain.Knowledge;

namespace ExceptionKnowledgeBase.Application.Analysis;

// §74: pluggable re-ranker slot. Vector recall returns a pool; the re-ranker
// re-orders that pool before TopK truncation. A cross-encoder (ONNX / hosted)
// can drop in later without touching ExceptionAnalysisService.
public interface IReRanker
{
    IReadOnlyList<KnowledgeCandidate> ReRank(string query, IReadOnlyList<KnowledgeCandidate> candidates);
}

// No-op fallback preserves the incoming order (already ranked by similarity + success boost).
public sealed class PassThroughReRanker : IReRanker
{
    public IReadOnlyList<KnowledgeCandidate> ReRank(string query, IReadOnlyList<KnowledgeCandidate> candidates) => candidates;
}

// Default: cheap lexical overlap boost over title + rootCause + solution steps.
// Idea — vectors nail semantic similarity but miss surface-form cues (specific
// error codes, symbol names). Give each candidate a small nudge for token
// overlap with the query, blended into RankScore.
public sealed class LexicalOverlapReRanker : IReRanker
{
    private readonly double _weight;

    public LexicalOverlapReRanker(double weight = 0.1)
    {
        _weight = weight;
    }

    public IReadOnlyList<KnowledgeCandidate> ReRank(string query, IReadOnlyList<KnowledgeCandidate> candidates)
    {
        if (candidates.Count == 0) return candidates;
        var qTokens = Tokenize(query);
        if (qTokens.Count == 0) return candidates;

        return candidates
            .Select(c =>
            {
                var overlap = OverlapScore(qTokens, c.Entry);
                var rescored = c.RankScore + _weight * overlap;
                return c with { RankScore = rescored };
            })
            .OrderByDescending(c => c.RankScore)
            .ToList();
    }

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return set;
        var buf = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) buf.Append(char.ToLowerInvariant(ch));
            else if (buf.Length >= 3) { set.Add(buf.ToString()); buf.Clear(); }
            else buf.Clear();
        }
        if (buf.Length >= 3) set.Add(buf.ToString());
        return set;
    }

    private static double OverlapScore(HashSet<string> qTokens, KnowledgeEntry entry)
    {
        var candidateTokens = Tokenize(string.Join(' ',
            entry.Title,
            entry.RootCause,
            string.Join(' ', entry.ExceptionTypes),
            string.Join(' ', entry.Symptoms),
            string.Join(' ', entry.Solution)));
        if (candidateTokens.Count == 0) return 0d;
        var shared = 0;
        foreach (var t in qTokens) if (candidateTokens.Contains(t)) shared++;
        // normalize by query size — bounded [0,1]; big candidates shouldn't win by volume.
        return (double)shared / qTokens.Count;
    }
}
