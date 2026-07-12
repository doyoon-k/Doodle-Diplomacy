using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    /// <summary>
    /// Produces one optional English helper label without making translation a
    /// prerequisite for accepting the player's original label or drawing.
    /// </summary>
    public sealed class FirstContactBestEffortTranslationChainLink : IStateChainLink, ICustomLinkStateProvider
    {
        public const string CanonicalLabelKey = "canonical_label";
        public const string TranslationAvailableKey = "translation_available";
        public const string TranslationWarningKey = "translation_warning";

        private const int DefaultMaxTranslationAttempts = 2;
        private const int MaxCanonicalLabelLength = 64;

        private readonly BaseLlmGenerationProfile _profile;
        private readonly ILlmService _service;
        private readonly int _maxTranslationAttempts;

        public FirstContactBestEffortTranslationChainLink()
            : this(null, null, null)
        {
        }

        public FirstContactBestEffortTranslationChainLink(ScriptableObject profileAsset)
            : this(null, profileAsset, null)
        {
        }

        public FirstContactBestEffortTranslationChainLink(
            Dictionary<string, string> parameters,
            ScriptableObject profileAsset,
            ILlmService service)
        {
            _profile = profileAsset as BaseLlmGenerationProfile;
            _service = service;
            _maxTranslationAttempts = Math.Max(
                1,
                ReadInt(parameters, "maxTranslationAttempts", DefaultMaxTranslationAttempts));
        }

        public IEnumerator Execute(PipelineState state, Action<PipelineState> onDone)
        {
            state ??= new PipelineState();
            string originalLabel = state.GetString("probe_display_label", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(originalLabel))
            {
                CompleteWithError(state, onDone, "Original player label is empty.");
                yield break;
            }

            ILlmService service = _service ?? LlmServiceLocator.Current;
            if (_profile == null || service == null)
            {
                ApplyFallback(
                    state,
                    originalLabel,
                    _profile == null
                        ? "Translation profile is missing."
                        : "Translation LLM service is missing.");
                onDone?.Invoke(state);
                yield break;
            }

            string prompt = BuildInitialPrompt(state, originalLabel);
            string lastError = "Translation returned no result.";
            for (int attempt = 1; attempt <= _maxTranslationAttempts; attempt++)
            {
                PipelineState attemptState = state.Clone();
                attemptState.Remove(CanonicalLabelKey);
                attemptState.Remove(PromptPipelineConstants.ErrorKey);
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
                    stepName: "FirstContactProbeLabelTranslation");

                yield return link.Execute(attemptState, result => attemptResult = result);

                if (attemptResult != null &&
                    !attemptResult.TryGetString(PromptPipelineConstants.ErrorKey, out _) &&
                    attemptResult.TryGetString(CanonicalLabelKey, out string canonicalLabel) &&
                    TryValidateEnglishHelperLabel(canonicalLabel, out string normalizedLabel, out lastError))
                {
                    state.SetString(CanonicalLabelKey, normalizedLabel);
                    state.SetString(TranslationAvailableKey, "true");
                    state.Remove(TranslationWarningKey);
                    state.Remove(PromptPipelineConstants.ErrorKey);
                    onDone?.Invoke(state);
                    yield break;
                }

                if (attemptResult == null)
                {
                    lastError = "Translation returned no state.";
                }
                else if (attemptResult.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError))
                {
                    lastError = pipelineError;
                }

                prompt = BuildCorrectionPrompt(state, originalLabel, lastError);
            }

            ApplyFallback(state, originalLabel, lastError);
            onDone?.Invoke(state);
        }

        public IEnumerable<string> GetWrites()
        {
            yield return CanonicalLabelKey;
            yield return TranslationAvailableKey;
            yield return TranslationWarningKey;
        }

        private static bool TryValidateEnglishHelperLabel(
            string value,
            out string normalized,
            out string error)
        {
            normalized = value?.Trim() ?? string.Empty;
            error = string.Empty;
            if (normalized.Length == 0)
            {
                error = "canonical_label is empty.";
                return false;
            }

            if (normalized.Length > MaxCanonicalLabelLength)
            {
                error = $"canonical_label exceeds {MaxCanonicalLabelLength} characters.";
                return false;
            }

            bool hasLatinLetterOrDigit = false;
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (char.IsControl(c) || c == '\r' || c == '\n')
                {
                    error = "canonical_label contains a control character or line break.";
                    return false;
                }

                if (char.IsDigit(c))
                {
                    hasLatinLetterOrDigit = true;
                    continue;
                }

                if (!char.IsLetter(c))
                {
                    continue;
                }

                bool isLatin = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
                               >= '\u00c0' and <= '\u024f' or >= '\u1e00' and <= '\u1eff';
                if (!isLatin)
                {
                    error = "canonical_label is not an English helper label.";
                    return false;
                }

                hasLatinLetterOrDigit = true;
            }

            if (!hasLatinLetterOrDigit)
            {
                error = "canonical_label contains no English letter or digit.";
                return false;
            }

            return true;
        }

        private static string BuildInitialPrompt(PipelineState state, string originalLabel)
        {
            string sourceLocale = JsonSerializer.Serialize(
                state.GetString(PromptPipelineConstants.SourceLocaleKey, string.Empty));
            string original = JsonSerializer.Serialize(originalLabel);
            return $"Expected UI locale hint: {sourceLocale}\nOriginal player label JSON string: {original}";
        }

        private static string BuildCorrectionPrompt(
            PipelineState state,
            string originalLabel,
            string error)
        {
            return BuildInitialPrompt(state, originalLabel) +
                   $"\nPrevious output was unusable: {error}" +
                   "\nReturn one corrected concise English helper translation in canonical_label.";
        }

        private static void ApplyFallback(PipelineState state, string originalLabel, string warning)
        {
            state.SetString(CanonicalLabelKey, originalLabel);
            state.SetString(TranslationAvailableKey, "false");
            state.SetString(TranslationWarningKey, warning ?? string.Empty);
            state.Remove(PromptPipelineConstants.ErrorKey);
            if (Application.isEditor || Debug.isDebugBuild)
            {
                Debug.LogWarning(
                    $"[FirstContactBestEffortTranslation] Using original label because translation was unavailable. " +
                    $"Reason='{warning ?? string.Empty}'");
            }
        }

        private static void CompleteWithError(PipelineState state, Action<PipelineState> onDone, string error)
        {
            state.SetString(PromptPipelineConstants.ErrorKey, error ?? "Label translation failed.");
            onDone?.Invoke(state);
        }

        private static int ReadInt(
            IReadOnlyDictionary<string, string> parameters,
            string key,
            int fallback)
        {
            return parameters != null &&
                   parameters.TryGetValue(key, out string raw) &&
                   int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }
    }
}
