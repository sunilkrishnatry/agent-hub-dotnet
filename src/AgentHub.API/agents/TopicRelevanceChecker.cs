using System.Numerics.Tensors;
using System.Text.RegularExpressions;

namespace AgentHub.API.Agents;

/// <summary>
/// Performs local topic relevance checks to detect topic shifts.
/// Uses ONNX embedding cosine similarity when available, TF-IDF fallback otherwise.
/// No network calls — runs entirely in-process.
/// </summary>
internal static partial class TopicRelevanceChecker
{
    private const double DefaultEmbeddingThreshold = 0.5;
    private const double DefaultTfIdfThreshold = 0.15;

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
    /// Uses embedding cosine similarity when queryEmbedding and turn embeddings are available.
    /// Falls back to TF-IDF when embeddings are not available.
    /// </summary>
    internal static bool IsOnTopic(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        float[]? queryEmbedding = null,
        int maxTurnsToCompare = 5,
        double? threshold = null)
    {
        if (recentTurns.Count == 0)
            return false;

        // Try embedding-based comparison first
        if (queryEmbedding != null)
        {
            var turnsWithEmbeddings = recentTurns
                .TakeLast(maxTurnsToCompare)
                .Where(t => t.Embedding != null)
                .ToList();

            if (turnsWithEmbeddings.Count > 0)
            {
                var maxSimilarity = turnsWithEmbeddings
                    .Max(t => EmbeddingCosineSimilarity(queryEmbedding, t.Embedding!));
                var thresholdValue = threshold ?? DefaultEmbeddingThreshold;
                var isOnTopic = maxSimilarity >= thresholdValue;
                
                // Log the decision for diagnostics
                if (!isOnTopic)
                {
                    System.Diagnostics.Debug.WriteLine($"Topic shift detected. Similarity={maxSimilarity:F3}, Threshold={thresholdValue:F3}, TurnsCompared={turnsWithEmbeddings.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"On-topic continuation. Similarity={maxSimilarity:F3}, Threshold={thresholdValue:F3}");
                }
                
                return isOnTopic;
            }
        }

        // TF-IDF fallback
        return IsOnTopicTfIdf(query, recentTurns, maxTurnsToCompare, threshold ?? DefaultTfIdfThreshold);
    }

    /// <summary>
    /// Computes the similarity score for logging/diagnostics.
    /// Returns the embedding-based score when available, TF-IDF score otherwise.
    /// </summary>
    internal static (double Similarity, string Method) ComputeSimilarity(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        float[]? queryEmbedding = null,
        int maxTurnsToCompare = 5)
    {
        if (recentTurns.Count == 0)
            return (0.0, "none");

        // Try embedding-based
        if (queryEmbedding != null)
        {
            var turnsWithEmbeddings = recentTurns
                .TakeLast(maxTurnsToCompare)
                .Where(t => t.Embedding != null)
                .ToList();

            if (turnsWithEmbeddings.Count > 0)
            {
                var maxSimilarity = turnsWithEmbeddings
                    .Max(t => EmbeddingCosineSimilarity(queryEmbedding, t.Embedding!));
                System.Diagnostics.Debug.WriteLine($"Topic relevance: method=embedding, similarity={maxSimilarity:F3}, turnsCompared={turnsWithEmbeddings.Count}");
                return (maxSimilarity, "embedding");
            }
        }

        // TF-IDF fallback
        var tfidfScore = ComputeTfIdfSimilarity(query, recentTurns, maxTurnsToCompare);
        System.Diagnostics.Debug.WriteLine($"Topic relevance: method=tfidf, similarity={tfidfScore:F3}");
        return (tfidfScore, "tfidf");
    }

    /// <summary>
    /// Cosine similarity between two embedding vectors. SIMD-optimized via TensorPrimitives.
    /// </summary>
    internal static double EmbeddingCosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        return TensorPrimitives.CosineSimilarity(a, b);
    }

    // --- TF-IDF fallback methods ---

    internal static bool IsOnTopicTfIdf(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        int maxTurnsToCompare = 5,
        double threshold = DefaultTfIdfThreshold)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return false;

        var turnsToCheck = recentTurns.Count <= maxTurnsToCompare
            ? recentTurns
            : recentTurns.Skip(recentTurns.Count - maxTurnsToCompare).ToList();

        var recentText = string.Join(" ",
            turnsToCheck.SelectMany(t => new[] { t.UserMessage, t.AssistantResponse }));
        var recentTokens = Tokenize(recentText);

        if (recentTokens.Count == 0)
            return false;

        var similarity = TfIdfCosineSimilarity(BuildTfVector(queryTokens), BuildTfVector(recentTokens));
        var isOnTopic = similarity >= threshold;
        
        if (!isOnTopic)
        {
            System.Diagnostics.Debug.WriteLine($"TF-IDF topic shift detected. Similarity={similarity:F3}, Threshold={threshold:F3}, QueryTokens={queryTokens.Count}, RecentTokens={recentTokens.Count}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"TF-IDF on-topic continuation. Similarity={similarity:F3}, Threshold={threshold:F3}");
        }
        
        return isOnTopic;
    }

    private static double ComputeTfIdfSimilarity(
        string query,
        IReadOnlyList<ConversationTurn> recentTurns,
        int maxTurnsToCompare)
    {
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

        return TfIdfCosineSimilarity(BuildTfVector(queryTokens), BuildTfVector(recentTokens));
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

    private static double TfIdfCosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
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
