using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Core;
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
            RoundManager roundManager,
            InteractionManager interactionManager,
            IInteractionStateService interactionState,
            IDrawingFeature drawing,
            IRoundAiGateway aiGateway,
            ICameraModeService camera,
            ISubtitlePresenter subtitles,
            DialogueSystem dialogueSystem,
            PreviewButtonPanel previewButtonPanel,
            EndingController endingController,
            IInteractionPolicy interactionPolicy)
        {
            SceneReferences = sceneReferences;
            RoundManager = roundManager;
            InteractionManager = interactionManager;
            InteractionState = interactionState;
            Drawing = drawing;
            AiGateway = aiGateway;
            Camera = camera;
            Subtitles = subtitles;
            DialogueSystem = dialogueSystem;
            PreviewButtonPanel = previewButtonPanel;
            EndingController = endingController;
            InteractionPolicy = interactionPolicy;
        }

        public SceneReferenceHub SceneReferences { get; }
        public RoundManager RoundManager { get; }
        public InteractionManager InteractionManager { get; }
        public IInteractionStateService InteractionState { get; }
        public IDrawingFeature Drawing { get; }
        public IRoundAiGateway AiGateway { get; }
        public ICameraModeService Camera { get; }
        public ISubtitlePresenter Subtitles { get; }
        public DialogueSystem DialogueSystem { get; }
        public PreviewButtonPanel PreviewButtonPanel { get; }
        public EndingController EndingController { get; }
        public IInteractionPolicy InteractionPolicy { get; }
    }
}
