using System;
using System.IO;
using LLama.Native;
using UnityEngine;

[CreateAssetMenu(fileName = "LlmEmbeddingProfile", menuName = "LLM/Embedding Profile")]
public class LlmEmbeddingProfile : ScriptableObject
{
    [Header("Local Embedding Model")]
    [Tooltip("GGUF embedding model path. Absolute path, or relative to StreamingAssets when enabled.")]
    public string model = "Models/Embeddings/bge-small-en-v1.5.Q8_0.gguf";

    [Tooltip("When true, model path is resolved from Application.streamingAssetsPath.")]
    public bool modelPathRelativeToStreamingAssets = true;

    [Header("Runtime Parameters")]
    [Min(128)]
    [Tooltip("Context window size used when loading the embedding model.")]
    public int contextSize = 512;

    [Min(0)]
    [Tooltip("Number of model layers to offload to GPU. 0 keeps embedding inference on CPU.")]
    public int gpuLayerCount = 0;

    [Min(0)]
    [Tooltip("CPU thread count for embedding inference. 0 lets the backend choose.")]
    public int threads = 0;

    [Tooltip("Pooling strategy used to collapse token embeddings into one vector per input. Unspecified uses the GGUF model default.")]
    public LLamaPoolingType poolingType = LLamaPoolingType.Unspecified;

    [Tooltip("L2-normalize vectors before returning them to the pipeline.")]
    public bool normalizeOutput = true;

    private void OnValidate()
    {
        contextSize = Math.Max(128, contextSize);
        gpuLayerCount = Math.Max(0, gpuLayerCount);
        threads = Math.Max(0, threads);
    }

    public string ResolveModelPath()
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        string candidate = model.Trim();
        if (Path.IsPathRooted(candidate))
        {
            return candidate;
        }

        if (modelPathRelativeToStreamingAssets)
        {
            return Path.Combine(Application.streamingAssetsPath, candidate);
        }

        return Path.GetFullPath(candidate);
    }
}
