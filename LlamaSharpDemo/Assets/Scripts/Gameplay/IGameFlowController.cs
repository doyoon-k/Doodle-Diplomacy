using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameFlowController
    {
        int CurrentEntryIndex { get; }
        FlowEntryDefinition CurrentEntry { get; }

        void LoadEntry(int index);
        void LoadNextEntry();
        void CompleteCurrentEntry();
    }

    public interface IGameFlowPreloader
    {
        void PreloadNextEntry();
    }

    /// <summary>
    /// Optional contract implemented by scene installers that can preserve a
    /// small amount of presentation state across an additive scene handoff.
    /// </summary>
    public interface IGameplaySceneHandoff
    {
        object CaptureHandoffState();
        void ApplyHandoffState(object handoffState);
    }

    public sealed class GameplaySceneTransitionContext
    {
        public GameplaySceneTransitionContext(bool wasPreloaded, bool appliedHandoffState)
        {
            WasPreloaded = wasPreloaded;
            AppliedHandoffState = appliedHandoffState;
        }

        public bool WasPreloaded { get; }
        public bool AppliedHandoffState { get; }
    }
}
