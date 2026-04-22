using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace AgentHub.API.Agents;

/// <summary>
/// Local embedding service using ONNX Runtime + BERT tokenizer.
/// Produces normalized embedding vectors entirely in-process — no network calls.
/// Expects an all-MiniLM-L6-v2 (or compatible) model directory containing model.onnx and vocab.txt.
/// </summary>
public sealed class LocalEmbeddingService : IDisposable
{
    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly ILogger _logger;
    private const int MaxTokens = 128;

    private LocalEmbeddingService(InferenceSession session, BertTokenizer tokenizer, ILogger logger)
    {
        _session = session;
        _tokenizer = tokenizer;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to create the service. Returns null if model files are missing or loading fails.
    /// Falls back gracefully — the caller should use TF-IDF when this returns null.
    /// </summary>
    public static LocalEmbeddingService? TryCreate(string? modelDirectoryPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(modelDirectoryPath))
        {
            logger.LogInformation("Local embedding model path not configured. Using TF-IDF fallback for topic relevance.");
            return null;
        }

        var modelPath = Path.Combine(modelDirectoryPath, "model.onnx");
        var vocabPath = Path.Combine(modelDirectoryPath, "vocab.txt");

        if (!File.Exists(modelPath) || !File.Exists(vocabPath))
        {
            logger.LogWarning(
                "Local embedding model files not found at {ModelPath}. Expected model.onnx and vocab.txt. Using TF-IDF fallback.",
                modelDirectoryPath);
            return null;
        }

        try
        {
            var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var session = new InferenceSession(modelPath, sessionOptions);
            var tokenizer = BertTokenizer.Create(vocabPath);

            logger.LogInformation("Local embedding service initialized. Model={ModelPath}", modelPath);
            return new LocalEmbeddingService(session, tokenizer, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize local embedding service. Using TF-IDF fallback.");
            return null;
        }
    }

    /// <summary>
    /// Produces a normalized embedding vector for the given text.
    /// Runs entirely on CPU, typically ~5-10ms for short texts.
    /// </summary>
    public float[] Embed(string text)
    {
        try
        {
            var startTime = Environment.TickCount;
            
            // Tokenize with special tokens ([CLS] ... [SEP])
            var ids = _tokenizer.EncodeToIds(text, MaxTokens, addSpecialTokens: true,
                out _, out _, considerPreTokenization: true, considerNormalization: true);
            var seqLen = ids.Count;
            
            _logger.LogDebug("Embedding text. TextLength={TextLength}, TokenCount={TokenCount}", text.Length, seqLen);

        // Build input tensors
        var inputIds = new DenseTensor<long>(seqLen);
        var attentionMask = new DenseTensor<long>(seqLen);
        var tokenTypeIds = new DenseTensor<long>(seqLen);

        for (var i = 0; i < seqLen; i++)
        {
            inputIds[i] = ids[i];
            attentionMask[i] = 1;
            // tokenTypeIds stays 0 (single sentence)
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds.Reshape([1, seqLen])),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask.Reshape([1, seqLen])),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds.Reshape([1, seqLen]))
        };

            using var results = _session.Run(inputs);

            // Try direct sentence_embedding output first (some ONNX exports include it)
            var sentenceOutput = results.FirstOrDefault(r => r.Name == "sentence_embedding");
            if (sentenceOutput != null)
            {
                var embedding = Normalize(sentenceOutput.AsEnumerable<float>().ToArray());
                var elapsed = Environment.TickCount - startTime;
                _logger.LogDebug("Embedding computation completed via sentence_embedding. ElapsedMs={ElapsedMs}", elapsed);
                return embedding;
            }

            // Fall back to mean pooling of last_hidden_state
            var lastHiddenState = results.First().AsTensor<float>();
            var result = MeanPoolAndNormalize(lastHiddenState, seqLen);
            var elapsedMs = Environment.TickCount - startTime;
            _logger.LogDebug("Embedding computation completed via mean pooling. ElapsedMs={ElapsedMs}", elapsedMs);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute embedding for text. TextLength={TextLength}", text.Length);
            throw;
        }
    }

    private static float[] MeanPoolAndNormalize(Tensor<float> lastHiddenState, int seqLen)
    {
        var hiddenSize = lastHiddenState.Dimensions[2]; // 384 for MiniLM-L6-v2
        var pooled = new float[hiddenSize];

        for (var i = 0; i < seqLen; i++)
        {
            for (var j = 0; j < hiddenSize; j++)
            {
                pooled[j] += lastHiddenState[0, i, j];
            }
        }

        var invSeqLen = 1.0f / seqLen;
        for (var j = 0; j < hiddenSize; j++)
        {
            pooled[j] *= invSeqLen;
        }

        return Normalize(pooled);
    }

    internal static float[] Normalize(float[] vector)
    {
        var norm = 0.0f;
        for (var i = 0; i < vector.Length; i++)
        {
            norm += vector[i] * vector[i];
        }

        norm = MathF.Sqrt(norm);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    public void Dispose()
    {
        _session.Dispose();
    }
}
