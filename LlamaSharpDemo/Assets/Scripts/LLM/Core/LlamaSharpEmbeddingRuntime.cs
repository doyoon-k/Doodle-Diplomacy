using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;

public readonly struct LlamaSharpEmbeddingRuntimeKey : IEquatable<LlamaSharpEmbeddingRuntimeKey>
{
    public LlamaSharpEmbeddingRuntimeKey(
        string modelPath,
        int contextSize,
        int gpuLayerCount,
        int threads,
        LLamaPoolingType poolingType)
    {
        ModelPath = modelPath ?? string.Empty;
        ContextSize = contextSize;
        GpuLayerCount = gpuLayerCount;
        Threads = threads;
        PoolingType = poolingType;
    }

    public string ModelPath { get; }
    public int ContextSize { get; }
    public int GpuLayerCount { get; }
    public int Threads { get; }
    public LLamaPoolingType PoolingType { get; }

    public bool Equals(LlamaSharpEmbeddingRuntimeKey other)
    {
        return string.Equals(ModelPath, other.ModelPath, StringComparison.OrdinalIgnoreCase) &&
               ContextSize == other.ContextSize &&
               GpuLayerCount == other.GpuLayerCount &&
               Threads == other.Threads &&
               PoolingType == other.PoolingType;
    }

    public override bool Equals(object obj)
    {
        return obj is LlamaSharpEmbeddingRuntimeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(ModelPath ?? string.Empty);
            hash = (hash * 397) ^ ContextSize;
            hash = (hash * 397) ^ GpuLayerCount;
            hash = (hash * 397) ^ Threads;
            hash = (hash * 397) ^ (int)PoolingType;
            return hash;
        }
    }
}

public sealed class LlamaSharpEmbeddingRuntime : IDisposable
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly LLamaWeights _weights;
    private readonly LLamaEmbedder _embedder;
    private bool _disposed;

    private LlamaSharpEmbeddingRuntime(
        LlamaSharpEmbeddingRuntimeKey key,
        LLamaWeights weights,
        LLamaEmbedder embedder)
    {
        Key = key;
        _weights = weights;
        _embedder = embedder;
    }

    public LlamaSharpEmbeddingRuntimeKey Key { get; }

    public static LlamaSharpEmbeddingRuntimeKey CreateKey(LlmEmbeddingProfile profile, string resolvedModelPath)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        return new LlamaSharpEmbeddingRuntimeKey(
            resolvedModelPath,
            Math.Max(128, profile.contextSize),
            Math.Max(0, profile.gpuLayerCount),
            LlamaSharpInterop.ResolveThreadCount(profile.threads),
            ResolvePoolingType(profile.poolingType));
    }

    public static LlamaSharpEmbeddingRuntime Create(LlmEmbeddingProfile profile, string resolvedModelPath)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            throw new ArgumentException("Embedding model path is required.", nameof(resolvedModelPath));
        }

        var key = CreateKey(profile, resolvedModelPath);

        var modelParams = new ModelParams(resolvedModelPath)
        {
            ContextSize = (uint)key.ContextSize,
            GpuLayerCount = key.GpuLayerCount,
            Embeddings = true,
            PoolingType = key.PoolingType
        };
        modelParams.Threads = key.Threads;
        modelParams.BatchThreads = LlamaSharpInterop.ResolveBatchThreadCount(key.Threads);

        LLamaWeights weights = null;
        LLamaEmbedder embedder = null;
        try
        {
            weights = LLamaWeights.LoadFromFile(modelParams);
            embedder = new LLamaEmbedder(weights, modelParams, null);
            return new LlamaSharpEmbeddingRuntime(key, weights, embedder);
        }
        catch
        {
            if (embedder is IDisposable disposableEmbedder)
            {
                disposableEmbedder.Dispose();
            }

            weights?.Dispose();
            throw;
        }
    }

    public async Task<float[][]> EmbedAsync(
        string[] inputs,
        bool normalizeOutput,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (inputs == null || inputs.Length == 0)
        {
            return Array.Empty<float[]>();
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<float[]>(inputs.Length);
            foreach (string input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<float[]> rawVectors = await _embedder
                    .GetEmbeddings(input ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                float[] vector = CollapseToSingleVector(rawVectors);
                if (normalizeOutput)
                {
                    NormalizeInPlace(vector);
                }

                results.Add(vector);
            }

            return results.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_embedder is IDisposable disposableEmbedder)
        {
            disposableEmbedder.Dispose();
        }

        _weights?.Dispose();
        _gate.Dispose();
    }

    private static LLamaPoolingType ResolvePoolingType(LLamaPoolingType poolingType)
    {
        return poolingType;
    }

    private static float[] CollapseToSingleVector(IReadOnlyList<float[]> vectors)
    {
        if (vectors == null || vectors.Count == 0)
        {
            return Array.Empty<float>();
        }

        if (vectors.Count == 1)
        {
            return vectors[0] ?? Array.Empty<float>();
        }

        int dims = 0;
        for (int i = 0; i < vectors.Count; i++)
        {
            if (vectors[i] != null && vectors[i].Length > 0)
            {
                dims = vectors[i].Length;
                break;
            }
        }

        if (dims == 0)
        {
            return Array.Empty<float>();
        }

        var pooled = new float[dims];
        int count = 0;
        for (int i = 0; i < vectors.Count; i++)
        {
            float[] vector = vectors[i];
            if (vector == null || vector.Length != dims)
            {
                continue;
            }

            for (int dim = 0; dim < dims; dim++)
            {
                pooled[dim] += vector[dim];
            }

            count++;
        }

        if (count <= 1)
        {
            return pooled;
        }

        float inv = 1f / count;
        for (int dim = 0; dim < pooled.Length; dim++)
        {
            pooled[dim] *= inv;
        }

        return pooled;
    }

    private static void NormalizeInPlace(float[] vector)
    {
        if (vector == null || vector.Length == 0)
        {
            return;
        }

        double sumSquares = 0d;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares <= double.Epsilon)
        {
            return;
        }

        float invMagnitude = (float)(1d / Math.Sqrt(sumSquares));
        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] *= invMagnitude;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LlamaSharpEmbeddingRuntime));
        }
    }
}
