using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactLabelAnalysisChainLink : IStateChainLink, ICustomLinkStateProvider
    {
        private const int DefaultMaxSemanticAttempts = 2;

        private readonly BaseLlmGenerationProfile _profile;
        private readonly ILlmService _service;
        private readonly int _maxSemanticAttempts;

        public FirstContactLabelAnalysisChainLink()
            : this(null, null, null)
        {
        }

        public FirstContactLabelAnalysisChainLink(ScriptableObject profileAsset)
            : this(null, profileAsset, null)
        {
        }

        public FirstContactLabelAnalysisChainLink(
            Dictionary<string, string> parameters,
            ScriptableObject profileAsset,
            ILlmService service)
        {
            _profile = profileAsset as BaseLlmGenerationProfile;
            _service = service;
            _maxSemanticAttempts = Math.Max(
                1,
                ReadInt(parameters, "maxSemanticAttempts", DefaultMaxSemanticAttempts));
        }

        public IEnumerator Execute(PipelineState state, Action<PipelineState> onDone)
        {
            state ??= new PipelineState();
            if (_profile == null)
            {
                CompleteWithError(state, onDone, "Label analysis profile is missing.");
                yield break;
            }

            ILlmService service = _service ?? LlmServiceLocator.Current;
            if (service == null)
            {
                CompleteWithError(state, onDone, "Label analysis LLM service is missing.");
                yield break;
            }

            string prompt = BuildInitialPrompt(state);
            string lastContractError = string.Empty;
            for (int attempt = 1; attempt <= _maxSemanticAttempts; attempt++)
            {
                PipelineState attemptState = state.Clone();
                PipelineState attemptResult = null;
                var link = new JSONLLMStateChainLink(
                    service,
                    _profile,
                    prompt,
                    maxRetries: 1,
                    delayBetweenRetries: 0f,
                    useVision: false,
                    imageStateKey: null,
                    requireImage: false,
                    resizeLongestSide: 512,
                    log: null,
                    stepName: "FirstContactProbeLabelAnalysis");

                yield return link.Execute(attemptState, result => attemptResult = result);

                if (attemptResult != null &&
                    !attemptResult.TryGetString(PromptPipelineConstants.ErrorKey, out _))
                {
                    if (FirstContactLabelAnalysisContract.TryValidate(
                            attemptResult,
                            out FirstContactLabelAnalysisData data,
                            out lastContractError))
                    {
                        ApplyValidatedResult(state, data);
                        onDone?.Invoke(state);
                        yield break;
                    }

                    prompt = BuildCorrectionPrompt(state, attemptResult, lastContractError);
                }
                else
                {
                    lastContractError = attemptResult?.GetString(
                        PromptPipelineConstants.ErrorKey,
                        "Label analysis returned no state.") ?? "Label analysis returned no state.";
                    prompt = BuildCorrectionPrompt(state, attemptResult, lastContractError);
                }
            }

            CompleteWithError(
                state,
                onDone,
                $"Label analysis contract failed: {lastContractError}");
        }

        public IEnumerable<string> GetWrites()
        {
            yield return FirstContactLabelAnalysisContract.DecisionKey;
            yield return FirstContactLabelAnalysisContract.ClassificationClaimTextKey;
            yield return FirstContactLabelAnalysisContract.NeutralSubjectLabelKey;
            yield return FirstContactLabelAnalysisContract.InconclusiveKey;
            yield return FirstContactLabelAnalysisContract.ContractErrorKey;
        }

        private static string BuildInitialPrompt(PipelineState state)
        {
            string sourceLocale = JsonSerializer.Serialize(
                state.GetString(PromptPipelineConstants.SourceLocaleKey, string.Empty));
            string original = JsonSerializer.Serialize(state.GetString("probe_display_label", string.Empty));
            return $"Expected source locale: {sourceLocale}" +
                   $"\nOriginal player label JSON string: {original}";
        }

        private static string BuildCorrectionPrompt(
            PipelineState state,
            PipelineState previousResult,
            string contractError)
        {
            string previousJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [FirstContactLabelAnalysisContract.DecisionKey] = previousResult?.GetString(
                    FirstContactLabelAnalysisContract.DecisionKey,
                    string.Empty) ?? string.Empty,
                [FirstContactLabelAnalysisContract.ClassificationClaimTextKey] = previousResult?.GetString(
                    FirstContactLabelAnalysisContract.ClassificationClaimTextKey,
                    string.Empty) ?? string.Empty,
                [FirstContactLabelAnalysisContract.NeutralSubjectLabelKey] = previousResult?.GetString(
                    FirstContactLabelAnalysisContract.NeutralSubjectLabelKey,
                    string.Empty) ?? string.Empty
            });

            return BuildInitialPrompt(state) +
                   $"\nPrevious response: {previousJson}" +
                   $"\nContract violation: {contractError}" +
                   "\nReturn one corrected JSON response that satisfies the system rules.";
        }

        private static void ApplyValidatedResult(PipelineState state, FirstContactLabelAnalysisData data)
        {
            state.SetString(FirstContactLabelAnalysisContract.DecisionKey, data.Decision);
            state.SetString(
                FirstContactLabelAnalysisContract.ClassificationClaimTextKey,
                data.ClassificationClaimText);
            state.SetString(
                FirstContactLabelAnalysisContract.NeutralSubjectLabelKey,
                data.NeutralSubjectLabel);
            state.SetString(FirstContactLabelAnalysisContract.InconclusiveKey, "false");
            state.Remove(FirstContactLabelAnalysisContract.ContractErrorKey);
            state.Remove(PromptPipelineConstants.ErrorKey);
        }

        private static void CompleteWithError(
            PipelineState state,
            Action<PipelineState> onDone,
            string error)
        {
            state.SetString(
                PromptPipelineConstants.ErrorKey,
                string.IsNullOrWhiteSpace(error) ? "Label analysis failed." : error.Trim());
            onDone?.Invoke(state);
        }

        private static int ReadInt(
            IReadOnlyDictionary<string, string> parameters,
            string key,
            int fallback)
        {
            return parameters != null &&
                   parameters.TryGetValue(key, out string raw) &&
                   int.TryParse(raw, out int value)
                ? value
                : fallback;
        }
    }
}
