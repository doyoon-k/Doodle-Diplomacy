using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "GameFlow", menuName = "DoodleDiplomacy/Game Flow/Flow")]
    public sealed class GameFlowAsset : ScriptableObject
    {
        [Tooltip("Ordered flow entries loaded by GameFlowDirector.")]
        public FlowEntryDefinition[] entries = System.Array.Empty<FlowEntryDefinition>();
    }
}
