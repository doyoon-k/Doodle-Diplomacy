using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Audio;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
using DoodleDiplomacy.Narrative;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class FirstContactEncounterDirector : MonoBehaviour
    {
        private GameplayModeContext _context;
        private FirstContactNarrativeSettings _settings;
        private FirstContactAlienEnsemblePresenter _ensemble;
        private readonly FirstContactOnboardingMemory _onboardingMemory = new();
        private FirstContactCalibrationProfile _calibrationProfile;
        private Light _signalLight;
        private AudioSource _signalAudio;
        private AudioClip _signalBeep;
        private int _presentationBlockDepth;

        public bool IsBlocking => _presentationBlockDepth > 0;
        public FirstContactCalibrationProfile CalibrationProfile => _calibrationProfile;

#if UNITY_EDITOR
        public bool PlayNarrativeCheckpointPreview(string checkpointId)
        {
            if (!Application.isPlaying || string.IsNullOrWhiteSpace(checkpointId))
            {
                return false;
            }

            FirstContactEncounterCue cue;
            L10nArg[] args;
            switch (checkpointId)
            {
                case "preflight_intro":
                    cue = FirstContactEncounterCue.PreflightIntro;
                    args = Array.Empty<L10nArg>();
                    break;
                case "tablet_tools":
                    cue = FirstContactEncounterCue.TabletTools;
                    args = Array.Empty<L10nArg>();
                    break;
                case "tablet_style":
                    cue = FirstContactEncounterCue.TabletStyle;
                    args = Array.Empty<L10nArg>();
                    break;
                case "tablet_history_and_send":
                    cue = FirstContactEncounterCue.TabletHistoryAndSend;
                    args = Array.Empty<L10nArg>();
                    break;
                case "preflight_drawing":
                    cue = FirstContactEncounterCue.PreflightDrawing;
                    args = Array.Empty<L10nArg>();
                    break;
                case "preflight_passed":
                    cue = FirstContactEncounterCue.PreflightPassed;
                    args = Array.Empty<L10nArg>();
                    break;
                case "delegation_arrival":
                    cue = FirstContactEncounterCue.DelegationArrival;
                    args = Array.Empty<L10nArg>();
                    break;
                case "first_category":
                    cue = FirstContactEncounterCue.FirstCategory;
                    args = new[] { L10n.Arg("category", FirstContactTerminalLocalization.LocalizeBootstrapCategory("danger", "DANGER")) };
                    break;
                case "first_drawing":
                    cue = FirstContactEncounterCue.FirstDrawing;
                    args = new[] { L10n.Arg("category", FirstContactTerminalLocalization.LocalizeBootstrapCategory("danger", "DANGER")) };
                    break;
                case "first_trace":
                    cue = FirstContactEncounterCue.FirstTrace;
                    args = new[]
                    {
                        L10n.Arg("category", FirstContactTerminalLocalization.LocalizeBootstrapCategory("danger", "DANGER")),
                        L10n.Arg("count", 1),
                        L10n.Arg("required", 3)
                    };
                    break;
                case "category_calibrated":
                    cue = FirstContactEncounterCue.CategoryCalibrated;
                    args = new[] { L10n.Arg("category", FirstContactTerminalLocalization.LocalizeBootstrapCategory("danger", "DANGER")) };
                    break;
                case "bootstrap_calibrated":
                    cue = FirstContactEncounterCue.BootstrapCalibrated;
                    args = new[] { L10n.Arg("count", 4) };
                    break;
                case "translation_succeeded":
                    cue = FirstContactEncounterCue.TranslationSucceeded;
                    args = Array.Empty<L10nArg>();
                    break;
                default:
                    return false;
            }

            StartCoroutine(PlayCueRoutine(cue, args));
            return true;
        }
#endif

        public void Configure(
            GameplayModeContext context,
            FirstContactNarrativeSettings settings)
        {
            _context = context;
            _settings = settings != null
                ? settings
                : ScriptableObject.CreateInstance<FirstContactNarrativeSettings>();
            _ensemble ??= new FirstContactAlienEnsemblePresenter();
        }

        public void BeginSession()
        {
            StopAllCoroutines();
            _presentationBlockDepth = 0;
            _onboardingMemory.Reset();
            _calibrationProfile = FirstContactCalibrationStore.BeginNewSession();
            _context?.Services?.Register(_calibrationProfile);
            _ensemble ??= new FirstContactAlienEnsemblePresenter();
            _ensemble.RefreshAliens();
            _ensemble.PreparePlaceholders(GetSettings().createPlaceholderGeometry);
            _context?.SharedMonitorDisplay?.SetIdle();
            EnsureSignalEffects();
            SetSignalLightIntensity(0f);
        }

        public void StopPresentation()
        {
            StopAllCoroutines();
            _presentationBlockDepth = 0;
            _context?.Subtitles?.Hide();
            if (_context != null && _context.DialogueSystem != null)
            {
                _context.DialogueSystem.StopSequence();
            }

            if (_context != null && _context.SharedMonitorDisplay != null)
            {
                _context.SharedMonitorDisplay.SetIdle();
            }

            _ensemble?.RestoreAuthoredPositions();
            _ensemble?.ClearPlaceholders();
            SetSignalLightIntensity(0f);
        }

        public bool ShouldShowGuidance(string guidanceId)
        {
            return _onboardingMemory.TryMarkFirst("guidance:" + (guidanceId ?? string.Empty));
        }

        public bool ShouldRunPreflightTutorial(bool isFirstPlay)
        {
            FirstContactNarrativeSettings settings = GetSettings();
            return isFirstPlay && settings.enableEncounterOpening && settings.enablePreflightTutorial;
        }

        public IEnumerator PlayOpeningRoutine(bool isFirstPlay)
        {
            yield return PlayOpeningPreludeRoutine(isFirstPlay);
            yield return PlayDelegationArrivalRoutine(isFirstPlay);
        }

        public IEnumerator PlayOpeningPreludeRoutine(bool isFirstPlay)
        {
            FirstContactNarrativeSettings settings = GetSettings();
            _ensemble?.PreparePlaceholders(settings.createPlaceholderGeometry);
            if (!isFirstPlay || !settings.enableEncounterOpening)
            {
                yield break;
            }

            EnterBlock();
            try
            {
                _context?.Camera?.SetMode(CameraMode.Default);
                if (settings.playPlaceholderIntroMontage)
                {
                    yield return PlayPlaceholderIntroMontageRoutine(settings.placeholderIntroCardSeconds);
                }
            }
            finally
            {
                ExitBlock();
            }
        }

        public IEnumerator PlayDelegationArrivalRoutine(bool isFirstPlay)
        {
            FirstContactNarrativeSettings settings = GetSettings();
            if (!isFirstPlay || !settings.enableEncounterOpening)
            {
                yield break;
            }

            EnterBlock();
            try
            {
                _context?.Camera?.SetMode(CameraMode.AlienReaction);
                yield return WaitForCameraRoutine();
                if (_ensemble != null)
                {
                    yield return _ensemble.PlayEntranceRoutine(
                        settings.delegationEntranceSeconds,
                        settings.delegationEntranceDistance,
                        settings.createPlaceholderGeometry);
                }

                yield return PlayCueRoutine(FirstContactEncounterCue.DelegationArrival);
            }
            finally
            {
                ExitBlock();
            }
        }

        public IEnumerator PlayPreflightIntroRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.PreflightIntro);
        }

        public IEnumerator PlayTabletToolsRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.TabletTools);
        }

        public IEnumerator PlayTabletStyleRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.TabletStyle);
        }

        public IEnumerator PlayTabletHistoryAndSendRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.TabletHistoryAndSend);
        }

        public IEnumerator PlayPreflightDrawingRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.PreflightDrawing);
        }

        public IEnumerator PlayPreflightPassedRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.PreflightPassed);
        }

        public IEnumerator PlayCategoryOpenedRoutine(
            string categoryId,
            string categoryDisplayName,
            int traceCount)
        {
            if (traceCount != 0 || !_onboardingMemory.TryMarkFirst("cue:first_category"))
            {
                yield break;
            }

            yield return PlayCueRoutine(
                FirstContactEncounterCue.FirstCategory,
                L10n.Arg("category", LocalizeCategory(categoryId, categoryDisplayName)));
        }

        public IEnumerator PlayDrawingOpenedRoutine(
            string categoryId,
            string categoryDisplayName)
        {
            if (!_onboardingMemory.TryMarkFirst("cue:first_drawing"))
            {
                yield break;
            }

            yield return PlayCueRoutine(
                FirstContactEncounterCue.FirstDrawing,
                L10n.Arg("category", LocalizeCategory(categoryId, categoryDisplayName)));
        }

        public IEnumerator PlayLabelOpenedRoutine()
        {
            if (!_onboardingMemory.TryMarkFirst("cue:first_label"))
            {
                yield break;
            }

            yield return PlayCueRoutine(FirstContactEncounterCue.FirstLabel);
        }

        public IEnumerator PlayProbeTransmissionRoutine(
            Texture2D drawing,
            int existingTraceCount)
        {
            bool fullPresentation = _onboardingMemory.TryMarkFirst("presentation:first_transmission");
            FirstContactNarrativeSettings settings = GetSettings();
            float monitorSeconds = fullPresentation
                ? settings.firstTransmissionMonitorSeconds
                : settings.repeatedTransmissionMonitorSeconds;
            float reactionSeconds = fullPresentation
                ? settings.firstAlienReactionSeconds
                : settings.repeatedAlienReactionSeconds;

            EnterBlock();
            try
            {
                _context?.SharedMonitorDisplay?.ShowSubmission(drawing);
                PlaySignalBeep(fullPresentation ? 540f : 620f);
                _context?.Camera?.SetMode(CameraMode.SharedMonitorZoom);
                yield return WaitForCameraRoutine();
                yield return WaitWithSignalPulseRoutine(monitorSeconds, 2.8f);

                _context?.Camera?.SetMode(CameraMode.AlienReaction);
                yield return WaitForCameraRoutine();
                _ensemble?.PlayGroupReaction(
                    existingTraceCount <= 0 ? SatisfactionLevel.Neutral : SatisfactionLevel.Satisfied,
                    reactionSeconds);
                yield return WaitWithSignalPulseRoutine(reactionSeconds, 1.5f);

                SetSignalLightIntensity(0f);
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
                yield return WaitForCameraRoutine();
            }
            finally
            {
                SetSignalLightIntensity(0f);
                ExitBlock();
            }
        }

        public IEnumerator PlayTraceRecordedRoutine(
            string categoryId,
            string categoryDisplayName,
            int traceCount,
            int requiredTraceCount,
            bool accepted)
        {
            if (!accepted)
            {
                yield break;
            }

            if (traceCount == 1 && _onboardingMemory.TryMarkFirst("cue:first_trace"))
            {
                yield return PlayCueRoutine(
                    FirstContactEncounterCue.FirstTrace,
                    L10n.Arg("category", LocalizeCategory(categoryId, categoryDisplayName)),
                    L10n.Arg("count", traceCount),
                    L10n.Arg("required", requiredTraceCount));
                yield break;
            }

            if (traceCount == 2 && _onboardingMemory.TryMarkFirst("cue:more_samples"))
            {
                yield return PlayCueRoutine(
                    FirstContactEncounterCue.MoreSamples,
                    L10n.Arg("remaining", Mathf.Max(0, requiredTraceCount - traceCount)));
            }
        }

        public IEnumerator PlayCategoryCalibratedRoutine(
            string categoryId,
            string categoryDisplayName)
        {
            _calibrationProfile?.Calibrate(categoryId, categoryDisplayName);
            FirstContactNarrativeSettings settings = GetSettings();

            EnterBlock();
            try
            {
                _context?.Camera?.SetMode(CameraMode.AlienReaction);
                yield return WaitForCameraRoutine();
                _ensemble?.PlayGroupReaction(
                    SatisfactionLevel.Satisfied,
                    settings.categoryCalibrationReactionSeconds);
                PlaySignalBeep(760f);
                yield return WaitWithSignalPulseRoutine(
                    settings.categoryCalibrationReactionSeconds,
                    2.2f);
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
                yield return WaitForCameraRoutine();
                yield return PlayCueRoutine(
                    FirstContactEncounterCue.CategoryCalibrated,
                    L10n.Arg("category", LocalizeCategory(categoryId, categoryDisplayName)));
            }
            finally
            {
                SetSignalLightIntensity(0f);
                ExitBlock();
            }
        }

        public IEnumerator PlayBootstrapCalibratedRoutine()
        {
            yield return PlayCueRoutine(
                FirstContactEncounterCue.BootstrapCalibrated,
                L10n.Arg("count", _calibrationProfile?.CalibratedCategoryCount ?? 0));
        }

        public FirstContactTranslationResult BuildTranslationDemonstration()
        {
            return _calibrationProfile != null
                ? _calibrationProfile.Translate(GetSettings().translationDemoSegments)
                : default;
        }

        public IEnumerator PlayIncomingTranslationSignalRoutine(
            FirstContactTranslationResult translation)
        {
            FirstContactNarrativeSettings settings = GetSettings();
            EnterBlock();
            try
            {
                _context?.Camera?.SetMode(CameraMode.AlienReaction);
                yield return WaitForCameraRoutine();
                _ensemble?.PlayGroupReaction(SatisfactionLevel.Satisfied, settings.translationSignalSeconds);
                _context?.Subtitles?.Show(
                    L10n.T("speaker.alien", "Alien"),
                    translation.RawSignal);
                PlaySignalBeep(420f);
                yield return WaitWithSignalPulseRoutine(settings.translationSignalSeconds, 3.1f);
                _context?.Subtitles?.Hide();
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
                yield return WaitForCameraRoutine();
            }
            finally
            {
                _context?.Subtitles?.Hide();
                SetSignalLightIntensity(0f);
                ExitBlock();
            }
        }

        public IEnumerator PlayTranslationSucceededRoutine()
        {
            yield return PlayCueRoutine(FirstContactEncounterCue.TranslationSucceeded);
        }

        private IEnumerator PlayCueRoutine(
            FirstContactEncounterCue cue,
            params L10nArg[] args)
        {
            FirstContactNarrativeSettings settings = GetSettings();
            NarrativeScenarioAsset scenario = settings.narrativeScenario;
            NarrativeBeat beat = null;
            bool hasGeneratedBeat = scenario != null &&
                                    scenario.TryGetBeatByRuntimeCue(cue.ToString(), out beat);
            bool hasFallbackCue = settings.TryGetCue(cue, out FirstContactNarrativeCueDefinition definition) &&
                                  definition != null;
            if (!hasGeneratedBeat && !hasFallbackCue)
            {
                yield break;
            }

            string traceScenarioId = scenario != null ? scenario.ScenarioId : "first_contact_day1";
            string traceBeatId = hasGeneratedBeat ? beat.id : cue.ToString();
            NarrativeTrace.Emit(traceScenarioId, traceBeatId, "enter", args);
            EnterBlock();
            try
            {
                if (!hasGeneratedBeat &&
                    definition.dialogueSequence != null &&
                    _context?.DialogueSystem != null)
                {
                    yield return _context.DialogueSystem.PlaySequenceAndWait(
                        definition.dialogueSequence,
                        args ?? Array.Empty<L10nArg>());
                    yield break;
                }

                string speaker = hasGeneratedBeat
                    ? beat.ResolveSpeaker(args ?? Array.Empty<L10nArg>())
                    : string.IsNullOrWhiteSpace(definition.speakerLocalizationKey)
                        ? definition.speakerFallback
                        : L10n.T(
                            definition.speakerLocalizationKey,
                            string.IsNullOrWhiteSpace(definition.speakerFallback)
                                ? "Dr. Hwang"
                                : definition.speakerFallback,
                            args ?? Array.Empty<L10nArg>());
                string text = hasGeneratedBeat
                    ? beat.ResolveText(args ?? Array.Empty<L10nArg>())
                    : string.IsNullOrWhiteSpace(definition.textLocalizationKey)
                        ? FormatFallback(definition.textFallback, args)
                        : L10n.T(
                            definition.textLocalizationKey,
                            definition.textFallback ?? string.Empty,
                            args ?? Array.Empty<L10nArg>());
                if (string.IsNullOrWhiteSpace(text))
                {
                    yield break;
                }

                _context?.Subtitles?.Show(speaker, text);
                yield return WaitForLineRoutine(
                    Mathf.Max(0f, hasGeneratedBeat ? beat.minimumSeconds : definition.minimumSeconds),
                    hasGeneratedBeat ? beat.WaitForAdvance : definition.waitForAdvance);
                _context?.Subtitles?.Hide();
            }
            finally
            {
                ExitBlock();
                NarrativeTrace.Emit(traceScenarioId, traceBeatId, "exit", args);
            }
        }

        private IEnumerator PlayPlaceholderIntroMontageRoutine(float cardSeconds)
        {
            var overlay = new PlaceholderIntroOverlay();
            string[] keys =
            {
                "first_contact.placeholder.intro.news",
                "first_contact.placeholder.intro.pizza",
                "first_contact.placeholder.intro.elevator",
                "first_contact.placeholder.intro.briefing"
            };
            string[] fallbacks =
            {
                "REDMOND UFO NEWS",
                "ZAUCER PIZZA",
                "CLASSIFIED ELEVATOR",
                "TRANSLATOR BRIEFING"
            };

            try
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    overlay.ShowBeat(L10n.T(keys[i], fallbacks[i]), i);
                    float elapsed = 0f;
                    float duration = Mathf.Max(0.1f, cardSeconds);
                    while (elapsed < duration)
                    {
                        if (TerminalKeyboardInput.WasPressed(KeyCode.Space))
                        {
                            break;
                        }

                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }
            }
            finally
            {
                overlay.Dispose();
            }

            yield return null;
        }

        private IEnumerator WaitForLineRoutine(float minimumSeconds, bool waitForAdvance)
        {
            yield return null;
            float elapsed = 0f;
            while (elapsed < minimumSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!waitForAdvance)
            {
                yield break;
            }

            _context?.Subtitles?.SetAdvancePromptVisible(true);
            while (!TerminalKeyboardInput.WasPressed(KeyCode.Space) &&
                   _context?.Subtitles?.ConsumeAdvanceRequest() != true)
            {
                yield return null;
            }

            _context?.Subtitles?.SetAdvancePromptVisible(false);
        }

        private IEnumerator WaitWithSignalPulseRoutine(float seconds, float pulseSpeed)
        {
            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float envelope = Mathf.Sin(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration)) * Mathf.PI);
                float pulse = 0.55f + 0.45f * Mathf.Sin(elapsed * Mathf.PI * pulseSpeed);
                SetSignalLightIntensity(Mathf.Max(0f, envelope * pulse * 4.5f));
                yield return null;
            }

            SetSignalLightIntensity(0f);
        }

        private IEnumerator WaitForCameraRoutine()
        {
            yield return null;
            while (_context?.Camera?.IsTransitioning == true)
            {
                yield return null;
            }
        }

        private void EnsureSignalEffects()
        {
            if (_signalLight == null)
            {
                var lightObject = new GameObject("FC_TEMP_SignalLight");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.position = _ensemble != null
                    ? _ensemble.GetCenter() + Vector3.up * 1.5f
                    : transform.position + Vector3.up * 1.5f;
                _signalLight = lightObject.AddComponent<Light>();
                _signalLight.type = LightType.Point;
                _signalLight.color = new Color(0.2f, 0.9f, 1f);
                _signalLight.range = 6f;
                _signalLight.intensity = 0f;
            }

            if (_signalAudio == null)
            {
                _signalAudio = gameObject.GetComponent<AudioSource>();
                if (_signalAudio == null)
                {
                    _signalAudio = gameObject.AddComponent<AudioSource>();
                }

                if (_signalAudio != null)
                {
                    _signalAudio.playOnAwake = false;
                    _signalAudio.spatialBlend = 0f;
                    _signalAudio.volume = 0.08f;
                    GameAudio.Route(_signalAudio, GameAudioBus.Ui);
                }
            }
        }

        private void PlaySignalBeep(float frequency)
        {
            EnsureSignalEffects();
            if (_signalAudio == null)
            {
                return;
            }

            AudioClip temporaryClip = frequency switch
            {
                760f => FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.TerminalSuccess),
                540f => FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.AlienLight),
                420f => FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.AlienPulse),
                _ => FirstContactTemporaryAudio.LoadClip(
                    FirstContactTemporaryAudioClip.TerminalHover)
            };
            if (temporaryClip != null)
            {
                _signalAudio.PlayOneShot(temporaryClip);
                return;
            }

            if (_signalBeep != null)
            {
                Destroy(_signalBeep);
            }

            const int sampleRate = 22050;
            int sampleCount = Mathf.RoundToInt(sampleRate * 0.12f);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = 1f - i / (float)sampleCount;
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.35f;
            }

            _signalBeep = AudioClip.Create("FC_TEMP_SignalBeep", sampleCount, 1, sampleRate, false);
            _signalBeep.SetData(samples, 0);
            _signalAudio.PlayOneShot(_signalBeep);
        }

        private void SetSignalLightIntensity(float intensity)
        {
            if (_signalLight != null)
            {
                _signalLight.intensity = Mathf.Max(0f, intensity);
            }
        }

        private FirstContactNarrativeSettings GetSettings()
        {
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<FirstContactNarrativeSettings>();
            }

            return _settings;
        }

        private void EnterBlock()
        {
            _presentationBlockDepth++;
            _context?.InteractionManager?.SetInputLocked(true);
        }

        private void ExitBlock()
        {
            _presentationBlockDepth = Mathf.Max(0, _presentationBlockDepth - 1);
            if (_presentationBlockDepth == 0)
            {
                _context?.InteractionManager?.SetInputLocked(false);
            }
        }

        private static string LocalizeCategory(string categoryId, string fallback)
        {
            return FirstContactTerminalLocalization.LocalizeBootstrapCategory(categoryId, fallback);
        }

        private static string FormatFallback(string fallback, IReadOnlyList<L10nArg> args)
        {
            string result = fallback ?? string.Empty;
            if (args == null)
            {
                return result;
            }

            for (int i = 0; i < args.Count; i++)
            {
                L10nArg arg = args[i];
                result = result.Replace("{" + arg.Key + "}", arg.Value);
            }

            return result;
        }

        private void OnDestroy()
        {
            _ensemble?.ClearPlaceholders();
            if (_signalBeep != null)
            {
                Destroy(_signalBeep);
            }
        }

        private sealed class PlaceholderIntroOverlay : IDisposable
        {
            private readonly GameObject _root;
            private readonly TextMeshProUGUI _title;
            private readonly TextMeshProUGUI _footer;
            private readonly Image[] _shapes;

            public PlaceholderIntroOverlay()
            {
                _root = new GameObject("FC_TEMP_IntroStoryboard");
                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;
                CanvasScaler scaler = _root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                _root.AddComponent<GraphicRaycaster>();

                Image background = CreateImage(
                    "Background",
                    _root.transform,
                    new Color(0.015f, 0.02f, 0.03f, 0.985f));
                Stretch(background.rectTransform);

                _title = CreateText("Title", _root.transform, 56f, TextAlignmentOptions.Center);
                SetRect(_title.rectTransform, new Vector2(0.1f, 0.63f), new Vector2(0.9f, 0.84f));

                _footer = CreateText("Footer", _root.transform, 20f, TextAlignmentOptions.Center);
                _footer.text = L10n.T(
                    "first_contact.placeholder.intro.footer",
                    "TEMPORARY GEOMETRIC CUTSCENE  •  SPACE: ADVANCE");
                _footer.color = new Color(0.55f, 0.68f, 0.72f, 1f);
                SetRect(_footer.rectTransform, new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.16f));

                _shapes = new Image[4];
                Color[] colors =
                {
                    new(0.2f, 0.9f, 1f, 1f),
                    new(1f, 0.28f, 0.16f, 1f),
                    new(1f, 0.82f, 0.18f, 1f),
                    new(0.48f, 0.36f, 1f, 1f)
                };
                for (int i = 0; i < _shapes.Length; i++)
                {
                    _shapes[i] = CreateImage("Shape_" + i, _root.transform, colors[i]);
                }
            }

            public void ShowBeat(string title, int beatIndex)
            {
                _title.text = title ?? string.Empty;
                int pattern = Mathf.Abs(beatIndex) % 4;
                for (int i = 0; i < _shapes.Length; i++)
                {
                    RectTransform rect = _shapes[i].rectTransform;
                    float x = 0.28f + i * 0.145f;
                    float y = pattern switch
                    {
                        0 => 0.36f + (i % 2) * 0.08f,
                        1 => 0.34f + Mathf.Sin(i * 1.8f) * 0.07f,
                        2 => 0.31f + i * 0.045f,
                        _ => 0.39f - Mathf.Abs(1.5f - i) * 0.055f
                    };
                    Vector2 size = pattern switch
                    {
                        0 => new Vector2(0.075f, 0.11f),
                        1 => new Vector2(0.09f, 0.065f + i * 0.015f),
                        2 => new Vector2(0.055f, 0.16f),
                        _ => new Vector2(0.105f, 0.045f + i * 0.02f)
                    };
                    SetRect(
                        rect,
                        new Vector2(x - size.x * 0.5f, y - size.y * 0.5f),
                        new Vector2(x + size.x * 0.5f, y + size.y * 0.5f));
                    rect.localRotation = Quaternion.Euler(0f, 0f, pattern * 5f + i * 8f - 12f);
                }
            }

            public void Dispose()
            {
                if (_root != null)
                {
                    UnityEngine.Object.Destroy(_root);
                }
            }

            private static Image CreateImage(string name, Transform parent, Color color)
            {
                var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                gameObject.transform.SetParent(parent, false);
                Image image = gameObject.GetComponent<Image>();
                image.color = color;
                return image;
            }

            private static TextMeshProUGUI CreateText(
                string name,
                Transform parent,
                float fontSize,
                TextAlignmentOptions alignment)
            {
                var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                gameObject.transform.SetParent(parent, false);
                TextMeshProUGUI text = gameObject.GetComponent<TextMeshProUGUI>();
                text.fontSize = fontSize;
                text.alignment = alignment;
                text.color = Color.white;
                text.enableWordWrapping = true;
                return text;
            }

            private static void Stretch(RectTransform rect)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
            {
                rect.anchorMin = min;
                rect.anchorMax = max;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }
    }
}
