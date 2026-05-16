using DoodleDiplomacy.Gameplay;
using UnityEngine;

namespace DoodleDiplomacy.UI
{
    internal static class GameStateUiHelper
    {
        public static GameplayModeHost ResolveGameplayModeHost(GameplayModeHost current)
        {
            return current != null ? current : GameplayModeHost.Instance;
        }

        public static IGameplaySessionController ResolveSessionController(GameplayModeHost host)
        {
            host = ResolveGameplayModeHost(host);
            host?.EnsureDefaultModeEntered();
            return host?.ActiveMode as IGameplaySessionController;
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
