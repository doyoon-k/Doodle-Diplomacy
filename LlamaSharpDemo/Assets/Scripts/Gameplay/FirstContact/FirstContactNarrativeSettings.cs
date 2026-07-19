using System;
using System.Collections.Generic;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Narrative;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactEncounterCue
    {
        DelegationArrival,
        FirstCategory,
        FirstDrawing,
        FirstLabel,
        FirstTrace,
        MoreSamples,
        CategoryCalibrated,
        BootstrapCalibrated,
        TranslationSucceeded,
        PreflightIntro,
        TabletTools,
        TabletStyle,
        TabletHistoryAndSend,
        PreflightDrawing,
        PreflightPassed
    }

    [Serializable]
    public sealed class FirstContactNarrativeCueDefinition
    {
        [Tooltip("Gameplay moment that triggers this authored line or dialogue sequence.")]
        public FirstContactEncounterCue cue;
        [Tooltip("Optional authored sequence. When empty, the localized fallback line below is used.")]
        public DialogueSequence dialogueSequence;
        [Tooltip("Localization key for the fallback speaker name.")]
        public string speakerLocalizationKey = "speaker.doctor_hwang";
        [Tooltip("Speaker name used when the localization key is unavailable.")]
        public string speakerFallback = "Dr. Hwang";
        [Tooltip("Localization key for the fallback line.")]
        public string textLocalizationKey;
        [TextArea(2, 5)]
        [Tooltip("Source line used when no DialogueSequence or localized value is available.")]
        public string textFallback;
        [Min(0f)]
        [Tooltip("Minimum time the fallback subtitle remains visible.")]
        public float minimumSeconds = 0.6f;
        [Tooltip("Wait for Space after the minimum display time. Disable for short automatic reactions.")]
        public bool waitForAdvance = true;
    }

    [CreateAssetMenu(
        fileName = "FirstContactNarrativeSettings",
        menuName = "DoodleDiplomacy/First Contact/Narrative Settings")]
    public sealed class FirstContactNarrativeSettings : ScriptableObject
    {
        [Header("Narrative Desk")]
        [Tooltip("Generated from Assets/Narrative/first_contact_day1.narrative.json. The cue list below remains as a safe fallback.")]
        public NarrativeScenarioAsset narrativeScenario;

        [Header("Intro Bridge")]
        [Tooltip("Play the encounter opening before the terminal bootstrap begins.")]
        public bool enableEncounterOpening = true;
        [Tooltip("Show a temporary geometric storyboard for the unfinished car, pizza shop, elevator, and briefing scenes.")]
        public bool playPlaceholderIntroMontage = true;
        [Tooltip("Create temporary doorway, neckties, and signal-light geometry when final art is unavailable.")]
        public bool createPlaceholderGeometry = true;
        [Tooltip("Before the delegation enters, run the real drawing and label checks as a local preflight tutorial.")]
        public bool enablePreflightTutorial = true;
        [Min(0.1f)] public float placeholderIntroCardSeconds = 0.9f;
        [Min(0.1f)] public float delegationEntranceSeconds = 1.8f;
        [Min(0f)] public float delegationEntranceDistance = 1.75f;

        [Header("Interactive Presentation")]
        [Min(0f)] public float firstTransmissionMonitorSeconds = 1.1f;
        [Min(0f)] public float repeatedTransmissionMonitorSeconds = 0.35f;
        [Min(0f)] public float firstAlienReactionSeconds = 1.35f;
        [Min(0f)] public float repeatedAlienReactionSeconds = 0.55f;
        [Min(0f)] public float categoryCalibrationReactionSeconds = 0.9f;
        [Min(0f)] public float translationSignalSeconds = 1.35f;

        [Header("Dialogue Cues")]
        public List<FirstContactNarrativeCueDefinition> cues = new();

        [Header("Translation Demonstration")]
        [Tooltip("Authored semantic segments used to prove that calibrated categories now translate automatically.")]
        public List<FirstContactAlienSignalSegment> translationDemoSegments = new();

        public bool TryGetCue(
            FirstContactEncounterCue cue,
            out FirstContactNarrativeCueDefinition definition)
        {
            definition = null;
            if (cues == null)
            {
                return false;
            }

            for (int i = 0; i < cues.Count; i++)
            {
                FirstContactNarrativeCueDefinition candidate = cues[i];
                if (candidate != null && candidate.cue == cue)
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        private void OnValidate()
        {
            placeholderIntroCardSeconds = Mathf.Max(0.1f, placeholderIntroCardSeconds);
            delegationEntranceSeconds = Mathf.Max(0.1f, delegationEntranceSeconds);
            delegationEntranceDistance = Mathf.Max(0f, delegationEntranceDistance);
            firstTransmissionMonitorSeconds = Mathf.Max(0f, firstTransmissionMonitorSeconds);
            repeatedTransmissionMonitorSeconds = Mathf.Max(0f, repeatedTransmissionMonitorSeconds);
            firstAlienReactionSeconds = Mathf.Max(0f, firstAlienReactionSeconds);
            repeatedAlienReactionSeconds = Mathf.Max(0f, repeatedAlienReactionSeconds);
            categoryCalibrationReactionSeconds = Mathf.Max(0f, categoryCalibrationReactionSeconds);
            translationSignalSeconds = Mathf.Max(0f, translationSignalSeconds);
        }
    }
}
