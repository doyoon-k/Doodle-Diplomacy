namespace DoodleDiplomacy.Core
{
    internal sealed class RoundRuntimeServices
    {
        public RoundFlowController FlowController;
        public RoundHintPresenter HintPresenter;
        public RoundCameraModeApplier CameraModeApplier;
        public RoundInteractionGate InteractionGate;
        public RoundDrawingInteractionGate DrawingInteractionGate;
        public RoundStateEntryActions StateEntryActions;
        public RoundInteractableEventBinder InteractionBinder;
        public RoundInputRouter InputRouter;
        public RoundPlayerActionHandler PlayerActionHandler;
        public RoundStartupFlow StartupFlow;
        public RoundPreviewTerminalPresenter PreviewTerminalPresenter;
        public RoundTextProvider TextProvider;
        public RoundIntroSequenceProvider IntroSequenceProvider;
    }
}
