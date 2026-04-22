using System.Text.RegularExpressions;

namespace AgentHub.API.Agents;

/// <summary>
/// Performs local semantic similarity checks using TF-IDF cosine similarity.
/// No network calls — runs entirely in-process using only BCL types.
/// Used to detect topic shifts: if the user's query is not similar to recent cached turns,
/// fall back to Foundry memory search for broader context.
/// </summary>
internal static partial class TopicRelevanceChecker
{
    private const double DefaultThreshold = 0.15;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "need", "must",
        "i", "me", "my", "we", "our", "you", "your", "he", "she", "it",
        "they", "them", "their", "this", "that", "these", "those",
        "in", "on", "at", "to", "for", "of", "with", "by", "from", "as",
        "into", "through", "during", "before", "after", "about", "between",
        "and", "but", "or", "nor", "not", "so", "yet", "both", "either",
        "if", "then", "else", "when", "where", "how", "what", "which", "who",
        "all", "each", "every", "any", "no", "some", "such", "only", "just",
        "than", "too", "very", "also", "up", "out", "off", "over", "under",
        "again", "further", "once", "here", "there", "why", "because",
        "while", "until", "although", "though", "however", "still",
        "please", "thanks", "thank", "hi", "hello", "hey", "ok", "okay",
        "yes", "no", "yeah", "sure", "right", "well", "now", "get", "got",
        "make", "know", "think", "want", "like", "tell", "give", "use"
    };

    /// <summary>
    /// Returns true if the query is relevant to recent conversation turns (on-topic).
    /// Returns false if the query appears to be a topic shift, signaling that a
    /// Foundry memory search should be performed for broader context.
    /// </summary>
    /// <param name="query">The current user message.</param>
    /// <param name="recentTurns">Recent conversation turns from the local cache.</param>
    /// <param name="maxTurnsToCompare">How many recent turns to compare against (from the end).</param>
    /// <param name="threshold">Cosine similarity threshold. Below this = topic shift.</param>
    /// <returns>true if on-topic; false if topic shift detected.</returns>
    internal static bool IsOnTopic(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        int maxTurnsToCompare = 5,
        double threshold = DefaultThreshold)
    {
        if (recentTurns.Count == 0)
            return false;

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return false;

        // Build a combined document from recent turns (both user messages and assistant responses)
        var turnsToCheck = recentTurns.Count <= maxTurnsToCompare
            ? recentTurns
            : recentTurns.Skip(recentTurns.Count - maxTurnsToCompare).ToList();

        var recentText = string.Join(" ",
            turnsToCheck.SelectMany(t => new[] { t.UserMessage, t.AssistantResponse }));
        var recentTokens = Tokenize(recentText);

        if (recentTokens.Count == 0)
            return false;

        var similarity = CosineSimilarity(
            BuildTfVector(queryTokens),
            BuildTfVector(recentTokens));

        return similarity >= threshold;
    }

    /// <summary>
    /// Computes the cosine similarity score without making a relevance decision.
    /// Useful for logging and diagnostics.
    /// </summary>
    internal static double ComputeSimilarity(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        int maxTurnsToCompare = 5)
    {
        if (recentTurns.Count == 0)
            return 0.0;

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return 0.0;

        var turnsToCheck = recentTurns.Count <= maxTurnsToCompare
            ? recentTurns
            : recentTurns.Skip(recentTurns.Count - maxTurnsToCompare).ToList();

        var recentText = string.Join(" ",
            turnsToCheck.SelectMany(t => new[] { t.UserMessage, t.AssistantResponse }));
        var recentTokens = Tokenize(recentText);

        if (recentTokens.Count == 0)
            return 0.0;

        return CosineSimilarity(
            BuildTfVector(queryTokens),
            BuildTfVector(recentTokens));
    }

    internal static List<string> Tokenize(string text)
    {
        return WordPattern().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !StopWords.Contains(w))
            .ToList();
    }

    private static Dictionary<string, double> BuildTfVector(List<string> tokens)
    {
        var tf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            tf.TryGetValue(token, out var count);
            tf[token] = count + 1;
        }

        // Normalize by total token count to get term frequency
        var total = (double)tokens.Count;
        foreach (var key in tf.Keys)
        {
            tf[key] /= total;
        }

        return tf;
    }

    private static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        var dotProduct = 0.0;
        var normA = 0.0;
        var normB = 0.0;

        foreach (var (key, value) in a)
        {
            normA += value * value;
            if (b.TryGetValue(key, out var bValue))
            {
                dotProduct += value * bValue;
            }
        }

        foreach (var (_, value) in b)
        {
            normB += value * value;
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator == 0.0 ? 0.0 : dotProduct / denominator;
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordPattern();
}
