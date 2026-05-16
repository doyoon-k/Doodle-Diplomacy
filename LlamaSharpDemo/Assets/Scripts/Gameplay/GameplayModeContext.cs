using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.UI;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class GameplayModeContext
    {
        public GameplayModeContext(
            SceneReferenceHub sceneReferences,
            InteractionManager interactionManager,
            IInteractionStateService interactionState,
            IDrawingFeature drawing,
            IRoundAiGateway aiGateway,
            ICameraModeService camera,
            ISubtitlePresenter subtitles,
            DialogueSystem dialogueSystem,
            PreviewButtonPanel previewButtonPanel,
            EndingController endingController,
            TerminalDisplay terminalDisplay,
            SharedMonitorDisplay sharedMonitorDisplay,
            AlienReactionController alienReactionController,
            Day1StimulusLibrary day1StimulusLibrary,
            IInteractionPolicy interactionPolicy)
        {
            SceneReferences = sceneReferences;
            InteractionManager = interactionManager;
            InteractionState = interactionState;
            Drawing = drawing;
            AiGateway = aiGateway;
            Camera = camera;
            Subtitles = subtitles;
            DialogueSystem = dialogueSystem;
            PreviewButtonPanel = previewButtonPanel;
            EndingController = endingController;
            TerminalDisplay = terminalDisplay;
            SharedMonitorDisplay = sharedMonitorDisplay;
            AlienReactionController = alienReactionController;
            Day1StimulusLibrary = day1StimulusLibrary;
            InteractionPolicy = interactionPolicy;
            Services = new GameplayServiceRegistry();
            Services.Register(interactionManager);
            Services.Register(interactionState);
            Services.Register(drawing);
            Services.Register(aiGateway);
            Services.Register(camera);
            Services.Register(subtitles);
            Services.Register(dialogueSystem);
            Services.Register(previewButtonPanel);
            Services.Register(endingController);
            Services.Register(terminalDisplay);
            Services.Register(sharedMonitorDisplay);
            Services.Register(alienReactionController);
            Services.Register(day1StimulusLibrary);
            Services.Register(interactionPolicy);
        }

        public SceneReferenceHub SceneReferences { get; }
        public InteractionManager InteractionManager { get; }
        public IInteractionStateService InteractionState { get; }
        public IDrawingFeature Drawing { get; }
        public IRoundAiGateway AiGateway { get; }
        public ICameraModeService Camera { get; }
        public ISubtitlePresenter Subtitles { get; }
        public DialogueSystem DialogueSystem { get; }
        public PreviewButtonPanel PreviewButtonPanel { get; }
        public EndingController EndingController { get; }
        public TerminalDisplay TerminalDisplay { get; }
        public SharedMonitorDisplay SharedMonitorDisplay { get; }
        public AlienReactionController AlienReactionController { get; }
        public Day1StimulusLibrary Day1StimulusLibrary { get; }
        public IInteractionPolicy InteractionPolicy { get; }
        public GameplayServiceRegistry Services { get; }
    }
}
