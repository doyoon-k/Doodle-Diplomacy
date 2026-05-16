using DoodleDiplomacy.AI;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.UI;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public class SceneReferenceHub : MonoBehaviour, IGameplaySceneInstaller
    {
        [Header("Gameplay Scene")]
        [SerializeField] private string sceneId = "current-gameplay";
        [SerializeField] private MonoBehaviour defaultModeBehaviour;

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

        [Header("Characters & Devices")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [SerializeField] private AlienReactionController alienReactionController;

        [Header("Day 1")]
        [SerializeField] private Day1StimulusLibrary day1StimulusLibrary;

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
        public TerminalDisplay TerminalDisplay => terminalDisplay;
        public SharedMonitorDisplay SharedMonitorDisplay => sharedMonitorDisplay;
        public AlienReactionController AlienReactionController => alienReactionController;
        public Day1StimulusLibrary Day1StimulusLibrary => day1StimulusLibrary;
        public string SceneId => string.IsNullOrWhiteSpace(sceneId) ? gameObject.scene.name : sceneId;
        public MonoBehaviour DefaultModeBehaviour => ResolveDefaultModeBehaviour();

        public void ConfigureRuntime(GameplayModeHost host)
        {
            if (interactionManager != null)
            {
                interactionManager.ConfigureGameplayModeHost(host);
            }
        }

        public GameplayModeContext CreateContext(GameplayModeHost host)
        {
            ConfigureRuntime(host);
            IInteractionPolicy interactionPolicy = new ObjectPairDrawingInteractionPolicy();

            return new GameplayModeContext(
                this,
                interactionManager,
                new InteractionStateService(interactionManager, interactionPolicy),
                new DrawingFeature(drawingBoard, drawingExportBridge),
                new AIPipelineRoundAiGateway(aiPipelineBridge),
                new CameraModeService(cameraController),
                new SubtitlePresenter(subtitleDisplay),
                dialogueSystem,
                previewButtonPanel,
                endingController,
                terminalDisplay,
                sharedMonitorDisplay,
                alienReactionController,
                day1StimulusLibrary,
                interactionPolicy);
        }

        public GameplayModeContext CreateContext()
        {
            return CreateContext(GameplayModeHost.Instance);
        }

        public MonoBehaviour GetDefaultModeBehaviour()
        {
            return ResolveDefaultModeBehaviour();
        }

        public bool ValidateReferences(bool logErrors = true)
        {
            bool valid = true;
            valid &= Require(interactionManager, nameof(interactionManager), logErrors);
            valid &= Require(cameraController, nameof(cameraController), logErrors);
            valid &= Require(dialogueSystem, nameof(dialogueSystem), logErrors);
            valid &= Require(aiPipelineBridge, nameof(aiPipelineBridge), logErrors);
            valid &= Require(drawingBoard, nameof(drawingBoard), logErrors);
            valid &= Require(drawingExportBridge, nameof(drawingExportBridge), logErrors);
            valid &= Require(subtitleDisplay, nameof(subtitleDisplay), logErrors);
            valid &= Require(previewButtonPanel, nameof(previewButtonPanel), logErrors);
            valid &= Require(endingController, nameof(endingController), logErrors);
            valid &= Require(terminalDisplay, nameof(terminalDisplay), logErrors);
            valid &= Require(sharedMonitorDisplay, nameof(sharedMonitorDisplay), logErrors);
            valid &= Require(alienReactionController, nameof(alienReactionController), logErrors);
            valid &= Require(day1StimulusLibrary, nameof(day1StimulusLibrary), logErrors);
            valid &= Require(ResolveDefaultModeBehaviour(), nameof(defaultModeBehaviour), logErrors);
            return valid;
        }

        private MonoBehaviour ResolveDefaultModeBehaviour()
        {
            if (defaultModeBehaviour is IGameplayMode)
            {
                return defaultModeBehaviour;
            }

            if (roundManager is IGameplayMode)
            {
                return roundManager;
            }

            foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour != this && behaviour is IGameplayMode)
                {
                    return behaviour;
                }
            }

            return null;
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
