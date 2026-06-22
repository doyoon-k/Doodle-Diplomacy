using DoodleDiplomacy.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace DoodleDiplomacy.Core
{
    /// <summary>
    /// Development-only keyboard controls for advancing the active gameplay mode.
    /// Attach this to the same GameObject as GameplayModeHost, or any enabled scene object.
    /// </summary>
    public class DebugStateAdvancer : MonoBehaviour
    {
        [Header("Key Bindings")]
        [Tooltip("Advance the current gameplay state.")]
        [SerializeField] private Key advanceKey = Key.F5;

        [Tooltip("Restart gameplay from the beginning.")]
        [SerializeField] private Key restartKey = Key.F6;

        [Tooltip("Jump directly to the drawing state when supported by the active mode.")]
        [SerializeField] private Key jumpToDrawingKey = Key.F7;

        [Header("Settings")]
        [Tooltip("Disable this component automatically in non-editor builds.")]
        [SerializeField] private bool enableInBuild = false;

        private void Awake()
        {
#if !UNITY_EDITOR
            if (!enableInBuild)
            {
                enabled = false;
            }
#endif
        }

        private static bool IsPressed(Key key)
        {
            return Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        }

        private void Update()
        {
            if (IsPressed(advanceKey))
            {
                if (!TryResolveDebugController(out IGameplayDebugController debug, out GameState state))
                {
                    return;
                }

                Debug.Log($"[DebugStateAdvancer] F5 AdvanceState (current: {state})");
                debug.DebugAdvanceState();
            }

            if (IsPressed(restartKey))
            {
                if (!TryResolveSession(out IGameplaySessionController session))
                {
                    return;
                }

                Debug.Log("[DebugStateAdvancer] F6 StartGame(false)");
                session.StartGame(false);
            }

            if (IsPressed(jumpToDrawingKey))
            {
                if (!TryResolveDebugController(out IGameplayDebugController debug, out _))
                {
                    return;
                }

                Debug.Log("[DebugStateAdvancer] F7 JumpToDrawing");
                debug.DebugJumpToState(GameState.Drawing);
            }
        }

        private static bool TryResolveSession(out IGameplaySessionController session)
        {
            GameplayModeHost host = GameplayModeHost.Instance;
            host?.EnsureDefaultModeEntered();
            session = host?.ActiveMode as IGameplaySessionController;
            if (session != null)
            {
                return true;
            }

            Debug.LogWarning("[DebugStateAdvancer] Gameplay session controller not found.");
            return false;
        }

        private static bool TryResolveDebugController(out IGameplayDebugController debug, out GameState state)
        {
            GameplayModeHost host = GameplayModeHost.Instance;
            host?.EnsureDefaultModeEntered();
            debug = host?.ActiveMode as IGameplayDebugController;
            state = host != null ? host.CurrentState : GameState.Title;
            if (debug != null)
            {
                return true;
            }

            Debug.LogWarning("[DebugStateAdvancer] Gameplay debug controller not found.");
            return false;
        }
    }
}
#endif
