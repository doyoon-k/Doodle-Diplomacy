using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public class SceneReferenceHub : MonoBehaviour, IGameplaySceneInstaller, IGameplaySceneModeResolver
    {
        [Header("Gameplay Scene")]
        [Tooltip("Stable id for this gameplay scene/context. Falls back to the scene name when empty.")]
        [SerializeField] private string sceneId = "current-gameplay";
        [Tooltip("Default gameplay mode entered by GameplayModeHost when this scene is initialized.")]
        [SerializeField] private MonoBehaviour defaultModeBehaviour;

        [Header("Core")]
        [Tooltip("Scene interaction manager used to raycast and route interactable clicks.")]
        [SerializeField] private InteractionManager interactionManager;
        [Tooltip("Camera controller used by gameplay modes for view transitions.")]
        [SerializeField] private CameraController cameraController;
        [Tooltip("Dialogue system used to play scripted dialogue sequences.")]
        [SerializeField] private DialogueSystem dialogueSystem;

        [Header("Drawing")]
        [Tooltip("Player drawing board used by drawing modes.")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [Tooltip("Bridge that exports drawing textures into AI pipeline state.")]
        [SerializeField] private DrawingExportBridge drawingExportBridge;

        [Header("UI")]
        [Tooltip("Subtitle presenter used for dialogue and reaction captions.")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;

        [Header("Characters & Devices")]
        [Tooltip("Terminal device display used for First Contact interpreter text.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("Shared monitor device display used to show drawings and generated objects.")]
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [Tooltip("Alien reaction controller used for animation and reaction subtitles.")]
        [SerializeField] private AlienReactionController alienReactionController;

        public InteractionManager InteractionManager => interactionManager;
        public CameraController CameraController => cameraController;
        public DialogueSystem DialogueSystem => dialogueSystem;
        public DrawingBoardController DrawingBoard => drawingBoard;
        public DrawingExportBridge DrawingExportBridge => drawingExportBridge;
        public SubtitleDisplay SubtitleDisplay => subtitleDisplay;
        public TerminalDisplay TerminalDisplay => terminalDisplay;
        public SharedMonitorDisplay SharedMonitorDisplay => sharedMonitorDisplay;
        public AlienReactionController AlienReactionController => alienReactionController;
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
            IInteractionPolicy interactionPolicy = new FirstContactInteractionPolicy();

            return new GameplayModeContext(
                this,
                interactionManager,
                new InteractionStateService(interactionManager, interactionPolicy),
                new DrawingFeature(drawingBoard, drawingExportBridge),
                new CameraModeService(cameraController),
                new SubtitlePresenter(subtitleDisplay),
                dialogueSystem,
                terminalDisplay,
                sharedMonitorDisplay,
                alienReactionController,
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

        public MonoBehaviour GetModeBehaviour(FlowEntryDefinition entry)
        {
            if (entry != null)
            {
                MonoBehaviour taggedMode = ResolveModeById(entry.entryTag);
                if (taggedMode != null)
                {
                    return taggedMode;
                }

                MonoBehaviour entryMode = ResolveModeById(entry.entryId);
                if (entryMode != null)
                {
                    return entryMode;
                }
            }

            return ResolveDefaultModeBehaviour();
        }

        public bool ValidateReferences(bool logErrors = true)
        {
            bool valid = true;
            valid &= Require(interactionManager, nameof(interactionManager), logErrors);
            valid &= Require(cameraController, nameof(cameraController), logErrors);
            valid &= Require(dialogueSystem, nameof(dialogueSystem), logErrors);
            valid &= Require(drawingBoard, nameof(drawingBoard), logErrors);
            valid &= Require(drawingExportBridge, nameof(drawingExportBridge), logErrors);
            valid &= Require(subtitleDisplay, nameof(subtitleDisplay), logErrors);
            valid &= Require(terminalDisplay, nameof(terminalDisplay), logErrors);
            valid &= Require(sharedMonitorDisplay, nameof(sharedMonitorDisplay), logErrors);
            valid &= Require(alienReactionController, nameof(alienReactionController), logErrors);
            valid &= Require(ResolveDefaultModeBehaviour(), nameof(defaultModeBehaviour), logErrors);
            return valid;
        }

        private MonoBehaviour ResolveDefaultModeBehaviour()
        {
            if (defaultModeBehaviour is IGameplayMode)
            {
                return defaultModeBehaviour;
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

        private MonoBehaviour ResolveModeById(string modeId)
        {
            if (string.IsNullOrWhiteSpace(modeId))
            {
                return null;
            }

            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IGameplayMode mode &&
                    string.Equals(mode.ModeId, modeId.Trim(), System.StringComparison.Ordinal))
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
