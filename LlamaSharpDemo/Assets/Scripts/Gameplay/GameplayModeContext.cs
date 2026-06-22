using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class GameplayModeContext
    {
        public GameplayModeContext(
            SceneReferenceHub sceneReferences,
            InteractionManager interactionManager,
            IInteractionStateService interactionState,
            IDrawingFeature drawing,
            ICameraModeService camera,
            ISubtitlePresenter subtitles,
            DialogueSystem dialogueSystem,
            TerminalDisplay terminalDisplay,
            SharedMonitorDisplay sharedMonitorDisplay,
            AlienReactionController alienReactionController,
            IInteractionPolicy interactionPolicy)
        {
            SceneReferences = sceneReferences;
            InteractionManager = interactionManager;
            InteractionState = interactionState;
            Drawing = drawing;
            Camera = camera;
            Subtitles = subtitles;
            DialogueSystem = dialogueSystem;
            TerminalDisplay = terminalDisplay;
            SharedMonitorDisplay = sharedMonitorDisplay;
            AlienReactionController = alienReactionController;
            InteractionPolicy = interactionPolicy;
            Services = new GameplayServiceRegistry();
            Services.Register(interactionManager);
            Services.Register(interactionState);
            Services.Register(drawing);
            Services.Register(camera);
            Services.Register(subtitles);
            Services.Register(dialogueSystem);
            Services.Register(terminalDisplay);
            Services.Register(sharedMonitorDisplay);
            Services.Register(alienReactionController);
            Services.Register(interactionPolicy);
        }

        public SceneReferenceHub SceneReferences { get; }
        public InteractionManager InteractionManager { get; }
        public IInteractionStateService InteractionState { get; }
        public IDrawingFeature Drawing { get; }
        public ICameraModeService Camera { get; }
        public ISubtitlePresenter Subtitles { get; }
        public DialogueSystem DialogueSystem { get; }
        public TerminalDisplay TerminalDisplay { get; }
        public SharedMonitorDisplay SharedMonitorDisplay { get; }
        public AlienReactionController AlienReactionController { get; }
        public IInteractionPolicy InteractionPolicy { get; }
        public GameplayServiceRegistry Services { get; }
    }
}
