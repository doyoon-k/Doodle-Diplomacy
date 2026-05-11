using DoodleDiplomacy.AI;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.UI;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public class SceneReferenceHub : MonoBehaviour
    {
        [Header("Core")]
        [SerializeField] private RoundManager roundManager;
        [SerializeField] private InteractionManager interactionManager;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private DialogueSystem dialogueSystem;

        [Header("AI")]
        [SerializeField] private AIPipelineBridge aiPipelineBridge;

        [Header("Drawing")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [SerializeField] private DrawingExportBridge drawingExportBridge;

        [Header("UI")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;
        [SerializeField] private PreviewButtonPanel previewButtonPanel;
        [SerializeField] private EndingController endingController;

        public RoundManager RoundManager => roundManager;
        public InteractionManager InteractionManager => interactionManager;
        public CameraController CameraController => cameraController;
        public DialogueSystem DialogueSystem => dialogueSystem;
        public AIPipelineBridge AiPipelineBridge => aiPipelineBridge;
        public DrawingBoardController DrawingBoard => drawingBoard;
        public DrawingExportBridge DrawingExportBridge => drawingExportBridge;
        public SubtitleDisplay SubtitleDisplay => subtitleDisplay;
        public PreviewButtonPanel PreviewButtonPanel => previewButtonPanel;
        public EndingController EndingController => endingController;

        public void ConfigureRuntime(GameplayModeHost host)
        {
            if (interactionManager != null)
            {
                interactionManager.ConfigureGameplayModeHost(host);
            }
        }

        public GameplayModeContext CreateContext()
        {
            IInteractionPolicy interactionPolicy = new LegacyRoundInteractionPolicy();

            return new GameplayModeContext(
                this,
                roundManager,
                interactionManager,
                new InteractionStateService(interactionManager, interactionPolicy),
                new DrawingFeature(drawingBoard, drawingExportBridge),
                new AIPipelineRoundAiGateway(aiPipelineBridge),
                new CameraModeService(cameraController),
                new SubtitlePresenter(subtitleDisplay),
                dialogueSystem,
                previewButtonPanel,
                endingController,
                interactionPolicy);
        }

        public bool ValidateReferences(bool logErrors = true)
        {
            bool valid = true;
            valid &= Require(roundManager, nameof(roundManager), logErrors);
            valid &= Require(interactionManager, nameof(interactionManager), logErrors);
            valid &= Require(cameraController, nameof(cameraController), logErrors);
            valid &= Require(dialogueSystem, nameof(dialogueSystem), logErrors);
            valid &= Require(aiPipelineBridge, nameof(aiPipelineBridge), logErrors);
            valid &= Require(drawingBoard, nameof(drawingBoard), logErrors);
            valid &= Require(drawingExportBridge, nameof(drawingExportBridge), logErrors);
            valid &= Require(subtitleDisplay, nameof(subtitleDisplay), logErrors);
            valid &= Require(previewButtonPanel, nameof(previewButtonPanel), logErrors);
            valid &= Require(endingController, nameof(endingController), logErrors);
            return valid;
        }

        private bool Require(Object reference, string fieldName, bool logErrors)
        {
            if (reference != null)
            {
                return true;
            }

            if (logErrors)
            {
                Debug.LogError($"[SceneReferenceHub] Missing required reference: {fieldName}.", this);
            }

            return false;
        }
    }
}
