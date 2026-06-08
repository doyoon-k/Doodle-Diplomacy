using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Executes a single embedding call and stores the resulting vector in PipelineState.
/// </summary>
public sealed class EmbeddingChainLink : IStateChainLink
{
    private readonly IEmbeddingService _embeddingService;
    private readonly LlmEmbeddingProfile _profile;
    private readonly PromptTemplate _inputTemplate;
    private readonly string _outputKey;
    private readonly bool _failOnEmptyInput;
    private readonly Action<string> _log;
    private readonly string _stepName;

    public EmbeddingChainLink(
        IEmbeddingService embeddingService,
        LlmEmbeddingProfile profile,
        string inputTemplate,
        string outputKey,
        bool failOnEmptyInput = true,
        Action<string> log = null,
        string stepName = null)
    {
        _embeddingService = embeddingService;
        _profile = profile;
        _inputTemplate = new PromptTemplate(inputTemplate ?? string.Empty);
        _outputKey = string.IsNullOrWhiteSpace(outputKey) ? "embedding" : outputKey.Trim();
        _failOnEmptyInput = failOnEmptyInput;
        _log = log;
        _stepName = string.IsNullOrWhiteSpace(stepName) ? "Embedding" : stepName.Trim();
    }

    public IEnumerator Execute(PipelineState state, Action<PipelineState> onDone)
    {
        state ??= new PipelineState();
        if (_profile == null)
        {
            yield return Fail(state, onDone, "[EmbeddingChainLink] Embedding profile is missing.");
            yield break;
        }

        if (_embeddingService == null)
        {
            yield return Fail(state, onDone, "[EmbeddingChainLink] IEmbeddingService is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(_outputKey))
        {
            yield return Fail(state, onDone, "[EmbeddingChainLink] Output key is empty.");
            yield break;
        }

        string input = _inputTemplate.Render(state)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            if (_failOnEmptyInput)
            {
                yield return Fail(state, onDone, $"[EmbeddingChainLink] Step '{_stepName}' rendered empty embedding input.");
                yield break;
            }

            state.SetFloatArray(_outputKey, Array.Empty<float>());
            onDone?.Invoke(state);
            yield break;
        }

        Log($"[EmbeddingChainLink] Step '{_stepName}' input:\n{input}");

        float[][] embeddings = null;
        yield return _embeddingService.Embed(
            _profile,
            new[] { input },
            result => embeddings = result);

        if (embeddings == null || embeddings.Length == 0 || embeddings[0] == null)
        {
            yield return Fail(state, onDone, $"[EmbeddingChainLink] Step '{_stepName}' produced no embedding.");
            yield break;
        }

        state.SetFloatArray(_outputKey, embeddings[0]);
        Log($"[EmbeddingChainLink] Step '{_stepName}' wrote '{_outputKey}' ({embeddings[0].Length} dims).");
        onDone?.Invoke(state);
    }

    private IEnumerator Fail(PipelineState state, Action<PipelineState> onDone, string error)
    {
        Debug.LogError(error);
        state.SetString(PromptPipelineConstants.ErrorKey, error);
        onDone?.Invoke(state);
        yield break;
    }

    private void Log(string message)
    {
        if (_log != null)
        {
            _log(message);
            return;
        }

        Debug.Log(message);
    }
}
