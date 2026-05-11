using DoodleDiplomacy.Core;
using DoodleDiplomacy.Gameplay;
using UnityEngine;

namespace DoodleDiplomacy.UI
{
    internal static class GameStateUiHelper
    {
        public static RoundManager ResolveRoundManager(RoundManager current)
        {
            return current ?? RoundManager.Instance;
        }

        public static GameplayModeHost ResolveGameplayModeHost(GameplayModeHost current)
        {
            return current ?? GameplayModeHost.Instance;
        }

        public static void SetVisible(GameObject target, bool visible)
        {
            if (target == null || target.activeSelf == visible)
            {
                return;
            }

            target.SetActive(visible);
        }
    }
}
