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
        [Tooltip("Stable id for this gameplay scene/context. Falls back to the scene name when empty.")]
        [SerializeField] private string sceneId = "current-gameplay";
        [Tooltip("Default gameplay mode entered by GameplayModeHost when this scene is initialized.")]
        [SerializeField] private MonoBehaviour defaultModeBehaviour;

        [Header("Core")]
        [Tooltip("Legacy object-pair round mode and shared round manager for this scene.")]
        [SerializeField] private RoundManager roundManager;
        [Tooltip("Scene interaction manager used to raycast and route interactable clicks.")]
        [SerializeField] private InteractionManager interactionManager;
        [Tooltip("Camera controller used by gameplay modes for view transitions.")]
        [SerializeField] private CameraController cameraController;
        [Tooltip("Dialogue system used to play scripted dialogue sequences.")]
        [SerializeField] private DialogueSystem dialogueSystem;

        [Header("AI")]
        [Tooltip("Bridge that owns prompt pipelines and AI-driven gameplay requests.")]
        [SerializeField] private AIPipelineBridge aiPipelineBridge;

        [Header("Drawing")]
        [Tooltip("Player drawing board used by drawing modes.")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [Tooltip("Bridge that exports drawing textures into AI pipeline state.")]
        [SerializeField] private DrawingExportBridge drawingExportBridge;

        [Header("UI")]
        [Tooltip("Subtitle presenter used for dialogue and reaction captions.")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;
        [Tooltip("Object-pair preview submit/modify button panel.")]
        [SerializeField] private PreviewButtonPanel previewButtonPanel;
        [Tooltip("Ending screen controller shown when a gameplay mode finishes.")]
        [SerializeField] private EndingController endingController;

        [Header("Characters & Devices")]
        [Tooltip("Terminal device display used for interpreter text and Day1 brainwave readouts.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("Shared monitor device display used to show drawings and generated objects.")]
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [Tooltip("Alien reaction controller used for animation and reaction subtitles.")]
        [SerializeField] private AlienReactionController alienReactionController;

        [Header("Day 1")]
        [Tooltip("Runtime library that stores approved Day1 calibration drawings and reaction metadata.")]
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
