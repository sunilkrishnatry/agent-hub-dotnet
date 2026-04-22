using AgentHub.API.Agents;

namespace AgentHub.Tests;

public class TopicRelevanceCheckerTests
{
    [Fact]
    public void IsOnTopic_EmptyTurns_ReturnsFalse()
    {
        var result = TopicRelevanceChecker.IsOnTopic("What is motor temperature?", []);
        Assert.False(result);
    }

    [Fact]
    public void IsOnTopic_EmptyQuery_ReturnsFalse()
    {
        var turns = new[] { new ConversationTurn("hello", "hi there") };
        var result = TopicRelevanceChecker.IsOnTopic("", turns);
        Assert.False(result);
    }

    [Fact]
    public void IsOnTopic_SameTopic_ReturnsTrue()
    {
        var turns = new[]
        {
            new ConversationTurn(
                "What is the motor temperature threshold?",
                "The motor temperature threshold is typically 85°C for standard operation.")
        };

        var result = TopicRelevanceChecker.IsOnTopic("How do I configure motor temperature alerts?", turns);
        Assert.True(result);
    }

    [Fact]
    public void IsOnTopic_DifferentTopic_ReturnsFalse()
    {
        var turns = new[]
        {
            new ConversationTurn(
                "What is the motor temperature threshold?",
                "The motor temperature threshold is typically 85°C for standard operation."),
            new ConversationTurn(
                "Can I adjust the motor temperature limit?",
                "Yes, you can configure the temperature limit in the device settings panel.")
        };

        var result = TopicRelevanceChecker.IsOnTopic("How do I update the firmware on the gateway?", turns);
        Assert.False(result);
    }

    [Fact]
    public void IsOnTopic_FollowUpQuestion_ReturnsTrue()
    {
        var turns = new[]
        {
            new ConversationTurn(
                "Show me the power consumption report for building A",
                "Here is the power consumption report for building A showing 450 kWh average daily usage.")
        };

        var result = TopicRelevanceChecker.IsOnTopic("What about building B power consumption?", turns);
        Assert.True(result);
    }

    [Fact]
    public void IsOnTopic_CompleteTopicShift_ReturnsFalse()
    {
        var turns = new[]
        {
            new ConversationTurn(
                "Show me the power consumption report for building A",
                "Here is the power consumption report for building A showing 450 kWh average daily usage."),
            new ConversationTurn(
                "Compare it with last month",
                "Last month building A consumed 420 kWh daily average, showing a 7% increase.")
        };

        var result = TopicRelevanceChecker.IsOnTopic("What circuit breaker models are compatible with panel XR400?", turns);
        Assert.False(result);
    }

    [Fact]
    public void ComputeSimilarity_IdenticalText_ReturnsHigh()
    {
        var turns = new[] { new ConversationTurn("motor temperature threshold", "the threshold is 85 degrees") };
        var (score, method) = TopicRelevanceChecker.ComputeSimilarity("motor temperature threshold", turns);
        Assert.Equal("tfidf", method);
        // TF-IDF dilutes because the assistant response adds extra tokens to the turn vector
        Assert.True(score > 0.5, $"Expected > 0.5 but got {score:F3}");
    }

    [Fact]
    public void ComputeSimilarity_CompletelyDifferent_ReturnsNearZero()
    {
        var turns = new[]
        {
            new ConversationTurn(
                "Configure the motor temperature alerts for zone 3",
                "Motor temperature alerts for zone 3 have been configured with a threshold of 90°C.")
        };

        var (score, _) = TopicRelevanceChecker.ComputeSimilarity(
            "What circuit breaker models are compatible with panel XR400?", turns);
        Assert.True(score < 0.1, $"Expected < 0.1 but got {score:F3}");
    }

    [Fact]
    public void ComputeSimilarity_EmptyTurns_ReturnsZero()
    {
        var (score, method) = TopicRelevanceChecker.ComputeSimilarity("anything", []);
        Assert.Equal(0.0, score);
        Assert.Equal("none", method);
    }

    [Fact]
    public void ComputeSimilarity_EmptyQuery_ReturnsZero()
    {
        var turns = new[] { new ConversationTurn("hello", "hi") };
        var (score, _) = TopicRelevanceChecker.ComputeSimilarity("", turns);
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void IsOnTopic_RespectsMaxTurnsToCompare()
    {
        var turns = new[]
        {
            new ConversationTurn("motor temperature threshold", "85 degrees celsius"),
            new ConversationTurn("motor vibration analysis", "vibration is within normal range"),
            new ConversationTurn("firmware update process", "download firmware from portal"),
            new ConversationTurn("firmware version check", "current firmware is v2.3.1"),
            new ConversationTurn("firmware rollback steps", "to rollback firmware use the recovery menu"),
        };

        var result = TopicRelevanceChecker.IsOnTopic("How to schedule firmware updates?", turns, queryEmbedding: null, maxTurnsToCompare: 3);
        Assert.True(result);
    }

    [Fact]
    public void Tokenize_RemovesStopWords()
    {
        var tokens = TopicRelevanceChecker.Tokenize("What is the motor temperature threshold for this device?");
        Assert.DoesNotContain("what", tokens);
        Assert.DoesNotContain("is", tokens);
        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("for", tokens);
        Assert.DoesNotContain("this", tokens);
        Assert.Contains("motor", tokens);
        Assert.Contains("temperature", tokens);
        Assert.Contains("threshold", tokens);
        Assert.Contains("device", tokens);
    }

    [Fact]
    public void Tokenize_LowercasesAndSplits()
    {
        var tokens = TopicRelevanceChecker.Tokenize("Motor TEMPERATURE Alert");
        Assert.All(tokens, t => Assert.Equal(t, t.ToLowerInvariant()));
    }

    [Fact]
    public void Tokenize_FiltersSingleCharTokens()
    {
        var tokens = TopicRelevanceChecker.Tokenize("I want a motor");
        Assert.DoesNotContain("a", tokens);
    }

    [Fact]
    public void IsOnTopic_CustomThreshold_AffectsDecision()
    {
        var turns = new[]
        {
            new ConversationTurn("power consumption analysis for building A", "consumption is 450 kWh")
        };

        // With a very high threshold, even related queries should fail
        var strictResult = TopicRelevanceChecker.IsOnTopic("power consumption analysis for building B", turns, threshold: 0.99);
        Assert.False(strictResult);

        // With a very low threshold, related queries should pass
        var lenientResult = TopicRelevanceChecker.IsOnTopic("power consumption analysis for building B", turns, threshold: 0.01);
        Assert.True(lenientResult);
    }

    [Fact]
    public void IsOnTopic_StopWordsOnlyQuery_ReturnsFalse()
    {
        var turns = new[] { new ConversationTurn("motor temperature", "85 degrees") };
        var result = TopicRelevanceChecker.IsOnTopic("is it the one?", turns);
        Assert.False(result);
    }

    // --- Embedding-based tests (using synthetic vectors, no ONNX model required) ---

    [Fact]
    public void IsOnTopic_WithEmbeddings_SimilarVectors_ReturnsTrue()
    {
        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var turns = new[]
        {
            new ConversationTurn("motor temp", "85 degrees") { Embedding = new float[] { 0.95f, 0.1f, 0.0f, 0.0f } }
        };

        var result = TopicRelevanceChecker.IsOnTopic("motor alerts", turns, queryEmbedding);
        Assert.True(result);
    }

    [Fact]
    public void IsOnTopic_WithEmbeddings_DissimilarVectors_ReturnsFalse()
    {
        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var turns = new[]
        {
            new ConversationTurn("firmware update", "download from portal") { Embedding = new float[] { 0.0f, 0.0f, 1.0f, 0.0f } }
        };

        var result = TopicRelevanceChecker.IsOnTopic("motor alerts", turns, queryEmbedding);
        Assert.False(result);
    }

    [Fact]
    public void IsOnTopic_WithEmbeddings_UsesMaxSimilarityAcrossTurns()
    {
        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var turns = new[]
        {
            // Dissimilar turn
            new ConversationTurn("firmware", "update") { Embedding = new float[] { 0.0f, 0.0f, 1.0f, 0.0f } },
            // Similar turn
            new ConversationTurn("motor temp", "85 degrees") { Embedding = new float[] { 0.98f, 0.05f, 0.0f, 0.0f } }
        };

        // Should be on-topic because at least one turn is similar
        var result = TopicRelevanceChecker.IsOnTopic("motor alerts", turns, queryEmbedding);
        Assert.True(result);
    }

    [Fact]
    public void IsOnTopic_WithEmbeddings_FallsBackToTfIdfWhenNoTurnEmbeddings()
    {
        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        // Turns without embeddings — should fall back to TF-IDF
        var turns = new[]
        {
            new ConversationTurn("motor temperature threshold", "The motor temperature threshold is typically 85 degrees.")
        };

        // queryEmbedding is provided but turns have no embeddings → falls back to TF-IDF
        var result = TopicRelevanceChecker.IsOnTopic("How do I configure motor temperature alerts?", turns, queryEmbedding);
        Assert.True(result); // TF-IDF should match on "motor" and "temperature"
    }

    [Fact]
    public void ComputeSimilarity_WithEmbeddings_ReturnsEmbeddingMethod()
    {
        var queryEmbedding = new float[] { 1.0f, 0.0f, 0.0f };
        var turns = new[]
        {
            new ConversationTurn("test", "response") { Embedding = new float[] { 0.9f, 0.1f, 0.0f } }
        };

        var (score, method) = TopicRelevanceChecker.ComputeSimilarity("test", turns, queryEmbedding);
        Assert.Equal("embedding", method);
        Assert.True(score > 0.9);
    }

    [Fact]
    public void EmbeddingCosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 1.0f, 0.0f, 0.0f };
        var similarity = TopicRelevanceChecker.EmbeddingCosineSimilarity(a, b);
        Assert.Equal(1.0, similarity, precision: 5);
    }

    [Fact]
    public void EmbeddingCosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f, 0.0f };
        var similarity = TopicRelevanceChecker.EmbeddingCosineSimilarity(a, b);
        Assert.Equal(0.0, similarity, precision: 5);
    }

    [Fact]
    public void EmbeddingCosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1.0f, 0.0f, 0.0f };
        var b = new float[] { -1.0f, 0.0f, 0.0f };
        var similarity = TopicRelevanceChecker.EmbeddingCosineSimilarity(a, b);
        Assert.Equal(-1.0, similarity, precision: 5);
    }

    [Fact]
    public void EmbeddingCosineSimilarity_DifferentLengths_ReturnsZero()
    {
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { 1.0f, 0.0f, 0.0f };
        var similarity = TopicRelevanceChecker.EmbeddingCosineSimilarity(a, b);
        Assert.Equal(0.0, similarity);
    }
}
