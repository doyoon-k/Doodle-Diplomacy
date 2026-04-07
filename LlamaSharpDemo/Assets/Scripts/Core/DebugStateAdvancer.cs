using UnityEngine;
using UnityEngine.InputSystem;

namespace DoodleDiplomacy.Core
{
    /// <summary>
    /// 개발 전용: 키보드로 게임 상태를 강제 진행.
    /// Phase 4(AIPipelineBridge) 구현 전까지 전체 흐름을 수동으로 테스트할 때 사용.
    /// RoundManager와 같은 GameObject에 붙이거나, 독립 GameObject에 붙여서 사용.
    /// </summary>
    public class DebugStateAdvancer : MonoBehaviour
    {
        [Header("Key Bindings")]
        [Tooltip("현재 상태를 다음 단계로 강제 진행")]
        [SerializeField] private Key advanceKey = Key.F5;

        [Tooltip("게임을 처음부터 다시 시작 (StartGame)")]
        [SerializeField] private Key restartKey = Key.F6;

        [Tooltip("특정 상태로 바로 점프 (Drawing → 태블릿 올라감)")]
        [SerializeField] private Key jumpToDrawingKey = Key.F7;

        [Header("Settings")]
        [Tooltip("false로 설정하면 빌드에서 비활성화")]
        [SerializeField] private bool enableInBuild = false;

        private void Awake()
        {
#if !UNITY_EDITOR
            if (!enableInBuild)
            {
                enabled = false;
                return;
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
                var rm = RoundManager.Instance;
                if (rm == null) { Debug.LogWarning("[DebugStateAdvancer] RoundManager 없음"); return; }
                Debug.Log($"[DebugStateAdvancer] F5 → AdvanceState (현재: {rm.CurrentState})");
                rm.Debug_AdvanceState();
            }

            if (IsPressed(restartKey))
            {
                var rm = RoundManager.Instance;
                if (rm == null) { Debug.LogWarning("[DebugStateAdvancer] RoundManager 없음"); return; }
                Debug.Log("[DebugStateAdvancer] F6 → StartGame(false) — WaitingForRound부터 재시작");
                rm.StartGame(false);
            }

            if (IsPressed(jumpToDrawingKey))
            {
                var rm = RoundManager.Instance;
                if (rm == null) { Debug.LogWarning("[DebugStateAdvancer] RoundManager 없음"); return; }
                Debug.Log("[DebugStateAdvancer] F7 → Drawing 상태로 강제 점프");
                rm.Debug_JumpToState(GameState.Drawing);
            }
        }
    }
}
