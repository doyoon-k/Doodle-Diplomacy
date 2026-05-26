using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "IngameTextTable", menuName = "DoodleDiplomacy/Ingame Text Table")]
    public class IngameTextTable : ScriptableObject
    {
        [Header("Round Hints")]
        [Tooltip("Hint shown while the alien is analyzing the submitted drawing preview.")]
        [TextArea(1, 3)] public string previewAnalyzingMessage = "The alien is trying to understand your drawing...";
        [Tooltip("Hint shown when preview analysis is ready and the terminal should be inspected.")]
        [TextArea(1, 3)] public string previewReadyToInspectMessage = "First-pass analysis is complete. Click the terminal to inspect the result.";
        [Tooltip("Hint shown when a terminal signal is ready to inspect.")]
        [TextArea(1, 3)] public string terminalSignalReadyMessage = "A signal has reached the terminal. Click the terminal to inspect it.";
        [Tooltip("Hint shown when no readable terminal signal was recovered.")]
        [TextArea(1, 3)] public string noSignalMessage = "No readable signal was recovered. Open the terminal to continue.";
        [Tooltip("Hint shown while object references are regenerated from the same prompts.")]
        [TextArea(1, 3)] public string regeneratingReferencesMessage = "Regenerating the object references with the same prompts...";
        [Tooltip("Hint shown when the player must inspect the terminal before continuing.")]
        [TextArea(1, 3)] public string openTerminalFirstMessage = "Open the terminal first.";
        [Tooltip("Hint shown when adjutant review is disabled and alien review should be used.")]
        [TextArea(1, 3)] public string adjutantDisabledMessage = "The adjutant can no longer review drawings. Click the alien for first-pass review.";
        [Tooltip("Hint shown while alien object references are being generated.")]
        [TextArea(1, 3)] public string generatingAlienObjectsMessage = "Generating the alien objects...";
        [Tooltip("Hint shown when object generation is not configured.")]
        [TextArea(1, 3)] public string objectGeneratorMissingMessage = "Object generator is missing. Assign AIPipelineBridge before starting the round.";
        [Tooltip("Hint shown when object generation failed and can be retried.")]
        [TextArea(1, 3)] public string objectGenerationFailedRetryMessage = "Object generation failed. Click the alien to retry.";
        [Tooltip("Prefix used before object generation failure details.")]
        [TextArea(1, 3)] public string objectGenerationFailedPrefix = "Object generation failed: ";
        [Tooltip("Hint shown after object references are visible.")]
        [TextArea(1, 3)] public string objectPresentedHintMessage = "Click the tablet to start drawing, or click the alien to regenerate the references.";
        [Tooltip("Hint template shown when drawing is ready. {0} is replaced by the submit input label.")]
        [TextArea(1, 3)] public string drawingReadyHintTemplate = "Press {0} when the drawing is ready to submit.";
        [Tooltip("Hint shown in preview when the player can request alien review or keep drawing.")]
        [TextArea(1, 3)] public string previewReadyHintMessage = "Click the alien to get a first-pass read, or click the tablet to keep drawing.";
        [Tooltip("Hint shown while a drawing is being submitted.")]
        [TextArea(1, 3)] public string submittingHintMessage = "Submitting the drawing to the alien delegation...";

        [Header("Round Start Readiness")]
        [Tooltip("Hint shown when the alien starts the round.")]
        [TextArea(1, 3)] public string clickAlienToBeginRoundMessage = "Click the alien to begin the round.";
        [Tooltip("Hint shown while the bundled Stable Diffusion server is preparing.")]
        [TextArea(1, 3)] public string preparingSdServerMessage = "Preparing the bundled SD server. Wait until the alien becomes interactable.";
        [Tooltip("Prefix used before object generator availability error details.")]
        [TextArea(1, 3)] public string objectGeneratorUnavailablePrefix = "Object generator unavailable: ";
        [Tooltip("Hint shown when object generation exists but is not ready.")]
        [TextArea(1, 3)] public string objectGeneratorNotReadyMessage = "Object generator is not ready yet.";
        [Tooltip("Hint shown while round object prompts/images are preparing.")]
        [TextArea(1, 3)] public string preparingRoundObjectsMessage = "Preparing the round objects. Wait until the alien becomes interactable.";
        [Tooltip("Hint shown when the player should review generated objects before starting.")]
        [TextArea(1, 3)] public string studyObjectsAndClickAlienMessage = "Study the two object images. Click the alien to begin the round.";
        [Tooltip("Hint shown when the LLM runtime has finished preparing.")]
        [TextArea(1, 3)] public string llmRuntimeReadyMessage = "LLM runtime is ready.";
        [Tooltip("Hint shown while the LLM runtime is loading.")]
        [TextArea(1, 3)] public string llmRuntimeLoadingMessage = "Loading LLM runtime. Game will start after preload completes.";
        [Tooltip("Prefix used before LLM preload failure details.")]
        [TextArea(1, 3)] public string llmPreloadFailedPrefix = "LLM preload failed: ";
        [Tooltip("Hint shown when LLM runtime is not yet ready.")]
        [TextArea(1, 3)] public string llmRuntimeNotReadyMessage = "LLM runtime is not ready yet.";

        [Header("Alien Reaction Speaker")]
        [Tooltip("Speaker label used for alien reaction subtitle lines.")]
        public string alienReactionSpeaker = "Alien";

        [Header("Alien Reaction - Mutter")]
        [Tooltip("Mutter subtitle shown for a very dissatisfied reaction.")]
        [TextArea(1, 3)] public string mutterVeryDissatisfied = "The aliens are conferring with each other...";
        [Tooltip("Mutter subtitle shown for a dissatisfied reaction.")]
        [TextArea(1, 3)] public string mutterDissatisfied = "The aliens exchange glances...";
        [Tooltip("Mutter subtitle shown for a neutral reaction.")]
        [TextArea(1, 3)] public string mutterNeutral = "The aliens observe quietly...";
        [Tooltip("Mutter subtitle shown for a satisfied reaction.")]
        [TextArea(1, 3)] public string mutterSatisfied = "The aliens murmur amongst themselves...";
        [Tooltip("Mutter subtitle shown for a very satisfied reaction.")]
        [TextArea(1, 3)] public string mutterVerySatisfied = "The aliens react with visible excitement!";

        [Header("Alien Reaction - Narration")]
        [Tooltip("Narration subtitle shown for a very dissatisfied reaction.")]
        [TextArea(1, 3)] public string narrationVeryDissatisfied = "They seem very displeased...";
        [Tooltip("Narration subtitle shown for a dissatisfied reaction.")]
        [TextArea(1, 3)] public string narrationDissatisfied = "They do not appear to like it.";
        [Tooltip("Narration subtitle shown for a neutral reaction.")]
        [TextArea(1, 3)] public string narrationNeutral = "Not much of a reaction.";
        [Tooltip("Narration subtitle shown for a satisfied reaction.")]
        [TextArea(1, 3)] public string narrationSatisfied = "A positive response.";
        [Tooltip("Narration subtitle shown for a very satisfied reaction.")]
        [TextArea(1, 3)] public string narrationVerySatisfied = "They seem quite impressed!";

        [Header("Intro Dialogue (Game Start)")]
        [Tooltip("Speaker label used for the intro adjutant dialogue.")]
        public string introAdjutantSpeaker = "Adjutant";
        [Tooltip("First intro line shown at game start.")]
        [TextArea(2, 5)] public string introAdjutantLine1 = "Ambassador, the alien delegation has arrived.";
        [Tooltip("Second intro line shown at game start.")]
        [TextArea(2, 5)] public string introAdjutantLine2 = "They cannot understand our language.\n\nWe must communicate through drawings.";
        [Tooltip("Third intro line shown at game start.")]
        [TextArea(2, 5)] public string introAdjutantLine3 = "Press the button in front of the alien to begin negotiations.";

        public static IngameTextTable LoadDefault()
        {
            // Runtime systems should receive this asset through inspector wiring.
            return null;
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
