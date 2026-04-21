using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "IngameTextTable", menuName = "DoodleDiplomacy/Ingame Text Table")]
    public class IngameTextTable : ScriptableObject
    {
        [Header("Round Hints")]
        [TextArea(1, 3)] public string previewAnalyzingMessage = "The alien is trying to understand your drawing...";
        [TextArea(1, 3)] public string previewReadyToInspectMessage = "First-pass analysis is complete. Click the terminal to inspect the result.";
        [TextArea(1, 3)] public string terminalSignalReadyMessage = "A signal has reached the terminal. Click the terminal to inspect it.";
        [TextArea(1, 3)] public string noSignalMessage = "No readable signal was recovered. Open the terminal to continue.";
        [TextArea(1, 3)] public string regeneratingReferencesMessage = "Regenerating the object references with the same prompts...";
        [TextArea(1, 3)] public string openTerminalFirstMessage = "Open the terminal first.";
        [TextArea(1, 3)] public string adjutantDisabledMessage = "The adjutant can no longer review drawings. Click the alien for first-pass review.";
        [TextArea(1, 3)] public string generatingAlienObjectsMessage = "Generating the alien objects...";
        [TextArea(1, 3)] public string objectGeneratorMissingMessage = "Object generator is missing. Assign AIPipelineBridge before starting the round.";
        [TextArea(1, 3)] public string objectGenerationFailedRetryMessage = "Object generation failed. Click the alien to retry.";
        [TextArea(1, 3)] public string objectGenerationFailedPrefix = "Object generation failed: ";
        [TextArea(1, 3)] public string objectPresentedHintMessage = "Click the tablet to start drawing, or click the alien to regenerate the references.";
        [TextArea(1, 3)] public string drawingReadyHintTemplate = "Press {0} when the drawing is ready to submit.";
        [TextArea(1, 3)] public string previewReadyHintMessage = "Click the alien to get a first-pass read, or click the tablet to keep drawing.";
        [TextArea(1, 3)] public string submittingHintMessage = "Submitting the drawing to the alien delegation...";

        [Header("Round Start Readiness")]
        [TextArea(1, 3)] public string clickAlienToBeginRoundMessage = "Click the alien to begin the round.";
        [TextArea(1, 3)] public string preparingSdServerMessage = "Preparing the bundled SD server. Wait until the alien becomes interactable.";
        [TextArea(1, 3)] public string objectGeneratorUnavailablePrefix = "Object generator unavailable: ";
        [TextArea(1, 3)] public string objectGeneratorNotReadyMessage = "Object generator is not ready yet.";
        [TextArea(1, 3)] public string preparingRoundObjectsMessage = "Preparing the round objects. Wait until the alien becomes interactable.";
        [TextArea(1, 3)] public string studyObjectsAndClickAlienMessage = "Study the two object images. Click the alien to begin the round.";
        [TextArea(1, 3)] public string llmRuntimeReadyMessage = "LLM runtime is ready.";
        [TextArea(1, 3)] public string llmRuntimeLoadingMessage = "Loading LLM runtime. Game will start after preload completes.";
        [TextArea(1, 3)] public string llmPreloadFailedPrefix = "LLM preload failed: ";
        [TextArea(1, 3)] public string llmRuntimeNotReadyMessage = "LLM runtime is not ready yet.";

        [Header("Alien Reaction Speaker")]
        public string alienReactionSpeaker = "Alien";

        [Header("Alien Reaction - Mutter")]
        [TextArea(1, 3)] public string mutterVeryDissatisfied = "The aliens are conferring with each other...";
        [TextArea(1, 3)] public string mutterDissatisfied = "The aliens exchange glances...";
        [TextArea(1, 3)] public string mutterNeutral = "The aliens observe quietly...";
        [TextArea(1, 3)] public string mutterSatisfied = "The aliens murmur amongst themselves...";
        [TextArea(1, 3)] public string mutterVerySatisfied = "The aliens react with visible excitement!";

        [Header("Alien Reaction - Narration")]
        [TextArea(1, 3)] public string narrationVeryDissatisfied = "They seem very displeased...";
        [TextArea(1, 3)] public string narrationDissatisfied = "They do not appear to like it.";
        [TextArea(1, 3)] public string narrationNeutral = "Not much of a reaction.";
        [TextArea(1, 3)] public string narrationSatisfied = "A positive response.";
        [TextArea(1, 3)] public string narrationVerySatisfied = "They seem quite impressed!";

        [Header("Intro Dialogue (Game Start)")]
        public string introAdjutantSpeaker = "Adjutant";
        [TextArea(2, 5)] public string introAdjutantLine1 = "Ambassador, the alien delegation has arrived.";
        [TextArea(2, 5)] public string introAdjutantLine2 = "They cannot understand our language.\n\nWe must communicate through drawings.";
        [TextArea(2, 5)] public string introAdjutantLine3 = "Press the button in front of the alien to begin negotiations.";

        private static IngameTextTable s_cachedDefault;

        public static IngameTextTable LoadDefault()
        {
            if (s_cachedDefault == null)
            {
                s_cachedDefault = Resources.Load<IngameTextTable>("IngameTextTable");
            }

            return s_cachedDefault;
        }

        public string GetMutterText(SatisfactionLevel level)
        {
            return level switch
            {
                SatisfactionLevel.VeryDissatisfied => mutterVeryDissatisfied,
                SatisfactionLevel.Dissatisfied => mutterDissatisfied,
                SatisfactionLevel.Neutral => mutterNeutral,
                SatisfactionLevel.Satisfied => mutterSatisfied,
                SatisfactionLevel.VerySatisfied => mutterVerySatisfied,
                _ => mutterNeutral
            };
        }

        public string GetNarrationText(SatisfactionLevel level)
        {
            return level switch
            {
                SatisfactionLevel.VeryDissatisfied => narrationVeryDissatisfied,
                SatisfactionLevel.Dissatisfied => narrationDissatisfied,
                SatisfactionLevel.Neutral => narrationNeutral,
                SatisfactionLevel.Satisfied => narrationSatisfied,
                SatisfactionLevel.VerySatisfied => narrationVerySatisfied,
                _ => narrationNeutral
            };
        }
    }
}
