using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public readonly struct FirstContactProbeDraft
    {
        public FirstContactProbeDraft(
            Texture2D texture,
            string normalizedLabel,
            string originalLabel)
        {
            Texture = texture;
            NormalizedLabel = normalizedLabel ?? string.Empty;
            OriginalLabel = originalLabel ?? string.Empty;
        }

        public FirstContactProbeDraft(
            Texture2D texture,
            string canonicalLabel,
            string displayLabel,
            bool translationAvailable)
            : this(texture, canonicalLabel, displayLabel)
        {
        }

        public Texture2D Texture { get; }
        public string NormalizedLabel { get; }
        public string OriginalLabel { get; }

        public string CanonicalLabel => NormalizedLabel;
        public string DisplayLabel => OriginalLabel;
        public bool TranslationAvailable => false;
    }

    public sealed class FirstContactProbeProcessor
    {
        private static readonly JsonSerializerOptions PromptJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly GamePipelineRunner _pipelineRunner;
        private readonly FirstContactVlmSettings _settings;

        public FirstContactProbeProcessor(
            GamePipelineRunner pipelineRunner,
            FirstContactVlmSettings settings)
        {
            _pipelineRunner = pipelineRunner;
            _settings = settings;
        }

        public IEnumerator PrepareLabel(
            Texture2D texture,
            string displayLabel,
            string sourceLocale,
            Action<FirstContactProbeLabelResult> onComplete)
        {
            if (_settings?.probeLabelPipeline == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Probe label pipeline is not assigned."));
                yield break;
            }

            if (_pipelineRunner == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            if (texture == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Drawing texture is unavailable for label analysis."));
                yield break;
            }

            string normalizedDisplayLabel = NormalizePlayerLabelText(displayLabel);
            var state = new PipelineState();
            state.SetString("probe_display_label", normalizedDisplayLabel);
            state.SetString(
                "probe_display_label_json",
                SerializePromptLabel(normalizedDisplayLabel));
            state.SetString("probe_label", normalizedDisplayLabel);
            state.SetString("normalized_label", NormalizeProbeLabel(normalizedDisplayLabel));
            // Retained for compatibility with existing pipeline assets and older saved states.
            state.SetString("canonical_label", normalizedDisplayLabel);
            state.SetString(PromptPipelineConstants.SourceLocaleKey, sourceLocale ?? string.Empty);
            state.SetString(PromptPipelineConstants.TargetLocaleKey, "en-US");
            state.SetString(PromptPipelineConstants.TargetLanguageKey, "English");
            state.SetString(PromptPipelineConstants.TargetLanguageNativeNameKey, "English");

            bool done = false;
            PipelineState finalState = null;
            _pipelineRunner.RunPipeline(_settings.probeLabelPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (!FirstContactProbeLabelResult.TryFromPipelineState(
                    finalState,
                    out FirstContactProbeLabelResult labelResult))
            {
                onComplete?.Invoke(labelResult ?? FirstContactProbeLabelResult.Failed(
                    "Probe label pipeline failed."));
                yield break;
            }

            string normalizedLabel = NormalizeProbeLabel(labelResult.NormalizedLabel);
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Normalized label is empty."));
                yield break;
            }

            labelResult.NormalizedLabel = normalizedLabel;
            onComplete?.Invoke(labelResult);
        }

        public IEnumerator ValidateWithRetries(
            FirstContactProbeDraft draft,
            Action<FirstContactProbeValidationResult, string> onComplete)
        {
            int attempts = Mathf.Max(1, (_settings?.validatorRetryCount ?? 0) + 1);
            FirstContactProbeValidationResult lastResult = null;
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                FirstContactProbeValidationResult result = null;
                yield return Validate(draft, value => result = value);
                if (result != null && result.IsSuccess)
                {
                    onComplete?.Invoke(result, string.Empty);
                    yield break;
                }

                lastResult = result;
                lastError = string.IsNullOrWhiteSpace(result?.Error)
                    ? "Validator returned no result."
                    : result.Error.Trim();
                if (FirstContactProbeFeedback.IsFatalValidationFailure(lastError))
                {
                    onComplete?.Invoke(lastResult, lastError);
                    yield break;
                }

                if (attempt < attempts && (_settings?.technicalRetryDelaySeconds ?? 0f) > 0f)
                {
                    yield return new WaitForSeconds(_settings.technicalRetryDelaySeconds);
                }
            }

            onComplete?.Invoke(lastResult, lastError);
        }

        public IEnumerator EvaluateCategoryFit(
            SemanticCardRecord card,
            FirstContactBootstrapCategoryState category,
            string sourceLocale,
            Action<FirstContactBootstrapCategoryFitResult> onComplete)
        {
            if (category == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Accepted(
                    "No active category."));
                yield break;
            }

            if (_settings?.bootstrapCategoryFitPipeline == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Failed(
                    "Bootstrap category fit pipeline is not assigned."));
                yield break;
            }

            if (_pipelineRunner == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            state.SetString("category_id", category.Id);
            state.SetString("category_display_name", category.LocalizedDisplayName);
            state.SetString("category_definition", category.LocalizedDescriptorText);
            state.SetString("probe_label", card?.NormalizedLabel ?? string.Empty);
            string originalLabel = ResolveOriginalLabel(card);
            state.SetString(
                "probe_display_label",
                originalLabel);
            state.SetString(
                "probe_display_label_json",
                SerializePromptLabel(originalLabel));
            state.SetString(PromptPipelineConstants.SourceLocaleKey, sourceLocale ?? string.Empty);

            bool done = false;
            PipelineState finalState = null;
            _pipelineRunner.RunPipeline(_settings.bootstrapCategoryFitPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (FirstContactBootstrapCategoryFitResult.TryFromPipelineState(
                    finalState,
                    out FirstContactBootstrapCategoryFitResult fitResult))
            {
                onComplete?.Invoke(fitResult);
                yield break;
            }

            onComplete?.Invoke(fitResult ?? FirstContactBootstrapCategoryFitResult.Failed(
                "Category fit pipeline unstable."));
        }

        public IEnumerator EvaluateSemanticDuplicate(
            SemanticCardRecord left,
            SemanticCardRecord right,
            Action<FirstContactSemanticDuplicateReviewResult> onComplete)
        {
            if (left == null || right == null)
            {
                onComplete?.Invoke(FirstContactSemanticDuplicateReviewResult.Failed(
                    "Semantic duplicate candidates are missing."));
                yield break;
            }

            if (_settings?.semanticDuplicateReviewPipeline == null)
            {
                onComplete?.Invoke(FirstContactSemanticDuplicateReviewResult.Failed(
                    "Semantic duplicate review pipeline is not assigned."));
                yield break;
            }

            if (_pipelineRunner == null)
            {
                onComplete?.Invoke(FirstContactSemanticDuplicateReviewResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            state.SetString("left_label_json", SerializePromptLabel(ResolveOriginalLabel(left)));
            state.SetString("right_label_json", SerializePromptLabel(ResolveOriginalLabel(right)));

            bool done = false;
            PipelineState finalState = null;
            _pipelineRunner.RunPipeline(_settings.semanticDuplicateReviewPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (FirstContactSemanticDuplicateReviewResult.TryFromPipelineState(
                    finalState,
                    out FirstContactSemanticDuplicateReviewResult reviewResult))
            {
                onComplete?.Invoke(reviewResult);
                yield break;
            }

            onComplete?.Invoke(reviewResult ?? FirstContactSemanticDuplicateReviewResult.Failed(
                "Semantic duplicate review pipeline unstable."));
        }

        private static string SerializePromptLabel(string label)
        {
            return JsonSerializer.Serialize(label ?? string.Empty, PromptJsonOptions);
        }

        public static string NormalizeProbeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant();
        }

        public static string NormalizePlayerLabelText(string label)
        {
            string trimmed = label?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return trimmed.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
        }

        private static string ResolveOriginalLabel(SemanticCardRecord card)
        {
            return !string.IsNullOrWhiteSpace(card?.OriginalLabel)
                ? card.OriginalLabel.Trim()
                : card?.NormalizedLabel?.Trim() ?? string.Empty;
        }

        private IEnumerator Validate(
            FirstContactProbeDraft draft,
            Action<FirstContactProbeValidationResult> onComplete)
        {
            if (draft.Texture == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed(
                    "Drawing texture is unavailable."));
                yield break;
            }

            if (_settings?.probeValidationPipeline == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed(
                    "Probe validation pipeline is not assigned."));
                yield break;
            }

            if (_pipelineRunner == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            state.SetImage(
                string.IsNullOrWhiteSpace(_settings.imageStateKey)
                    ? "reference_image"
                    : _settings.imageStateKey,
                draft.Texture);
            state.SetString("probe_label", draft.NormalizedLabel);
            string originalLabel = string.IsNullOrWhiteSpace(draft.OriginalLabel)
                ? draft.NormalizedLabel
                : draft.OriginalLabel;
            state.SetString(
                "probe_display_label",
                originalLabel);
            state.SetString(
                "probe_display_label_json",
                SerializePromptLabel(originalLabel));

            bool done = false;
            PipelineState finalState = null;
            _pipelineRunner.RunPipeline(_settings.probeValidationPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (FirstContactProbeValidationResult.TryFromPipelineState(
                    finalState,
                    out FirstContactProbeValidationResult validation))
            {
                onComplete?.Invoke(validation);
                yield break;
            }

            onComplete?.Invoke(validation ?? FirstContactProbeValidationResult.Failed(
                "Probe validation unstable."));
        }
    }

    public readonly struct FirstContactProbeCaptureResult
    {
        private FirstContactProbeCaptureResult(
            Texture2D texture,
            byte[] pngBytes,
            string error)
        {
            Texture = texture;
            PngBytes = pngBytes;
            Error = error ?? string.Empty;
        }

        public Texture2D Texture { get; }
        public byte[] PngBytes { get; }
        public string Error { get; }
        public bool IsSuccess => Texture != null && PngBytes != null && PngBytes.Length > 0;

        public static FirstContactProbeCaptureResult Succeeded(
            Texture2D texture,
            byte[] pngBytes)
        {
            return new FirstContactProbeCaptureResult(texture, pngBytes, string.Empty);
        }

        public static FirstContactProbeCaptureResult Failed(string error)
        {
            return new FirstContactProbeCaptureResult(null, null, error);
        }
    }

    public sealed class FirstContactProbeCaptureService : IDisposable
    {
        private readonly FirstContactVlmSettings _settings;
        private readonly List<Texture2D> _ownedTextures = new();

        public FirstContactProbeCaptureService(FirstContactVlmSettings settings)
        {
            _settings = settings;
        }

        public IEnumerator Capture(
            IDrawingFeature drawing,
            Action<FirstContactProbeCaptureResult> onComplete)
        {
            if (drawing == null)
            {
                onComplete?.Invoke(FirstContactProbeCaptureResult.Failed(
                    "Drawing feature is missing."));
                yield break;
            }

            if (!drawing.HasVisibleDrawing)
            {
                onComplete?.Invoke(FirstContactProbeCaptureResult.Failed(
                    "Drawing is blank."));
                yield break;
            }

            int attempts = Mathf.Max(1, (_settings?.captureRetryCount ?? 0) + 1);
            string lastError = string.Empty;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (drawing.TryExportPngBytes(out byte[] pngBytes, out string error) &&
                    pngBytes != null &&
                    pngBytes.Length > 0)
                {
                    Texture2D texture = CreateTexture(pngBytes);
                    if (texture != null)
                    {
                        onComplete?.Invoke(FirstContactProbeCaptureResult.Succeeded(
                            texture,
                            pngBytes));
                        yield break;
                    }

                    lastError = "Exported PNG could not be loaded into a Texture2D.";
                }
                else
                {
                    lastError = string.IsNullOrWhiteSpace(error)
                        ? "Drawing export returned no PNG bytes."
                        : error.Trim();
                }

                if (attempt < attempts && (_settings?.technicalRetryDelaySeconds ?? 0f) > 0f)
                {
                    yield return new WaitForSeconds(_settings.technicalRetryDelaySeconds);
                }
            }

            onComplete?.Invoke(FirstContactProbeCaptureResult.Failed(lastError));
        }

        public void Dispose()
        {
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                {
                    UnityEngine.Object.Destroy(_ownedTextures[i]);
                }
            }

            _ownedTextures.Clear();
        }

        private Texture2D CreateTexture(byte[] pngBytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"FirstContactDrawing_{_ownedTextures.Count + 1:000}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(pngBytes, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            _ownedTextures.Add(texture);
            return texture;
        }
    }
}
