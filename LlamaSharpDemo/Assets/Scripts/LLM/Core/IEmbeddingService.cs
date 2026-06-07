using System;
using System.Collections;

/// <summary>
/// Provider-agnostic embedding service contract used by prompt pipeline embedding steps.
/// </summary>
public interface IEmbeddingService
{
    IEnumerator Embed(
        LlmEmbeddingProfile profile,
        string[] inputs,
        Action<float[][]> onEmbeddings);
}
