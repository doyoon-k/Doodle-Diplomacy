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
}
