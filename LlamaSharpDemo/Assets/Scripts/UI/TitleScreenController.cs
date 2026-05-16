using UnityEngine;
using UnityEngine.UI;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Gameplay;

namespace DoodleDiplomacy.UI
{
    /// <summary>
    /// Title 상태에서 시작 버튼을 표시하고 재플레이를 처리한다.
    /// PlayerPrefs로 첫 플레이 여부를 판별한다.
    /// </summary>
    public class TitleScreenController : MonoBehaviour
    {
        private const string FirstPlayKey = "DD_HasPlayed";

        [Header("UI References")]
        [SerializeField] private GameObject titleCanvas;
        [SerializeField] private Button     startButton;

        [Header("Startup")]
        [SerializeField] private bool alwaysPlayIntroOnStart = true;

        [Header("State Source")]
        [SerializeField] private GameplayModeHost gameplayModeHost;

        private bool _subscribedToHost;

        private void Awake()
        {
            if (titleCanvas != null) titleCanvas.SetActive(false);
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        }

        private void Start()
        {
            SubscribeStateSource();
            // 게임 시작 시 자동 진입
            TryAutoStart();
        }

        private void OnEnable()
        {
            SubscribeStateSource();
        }

        private void OnDestroy()
        {
            UnsubscribeStateSource();
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        /// <summary>GameplayModeHost 상태 변경 이벤트에 연결.</summary>
        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Title)
                ShowTitle();
            else
                Hide();
        }

        public void ShowTitle()
        {
            GameStateUiHelper.SetVisible(titleCanvas, true);
            Debug.Log("[TitleScreenController] 타이틀 화면 표시");
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private void TryAutoStart()
        {
            gameplayModeHost = GameStateUiHelper.ResolveGameplayModeHost(gameplayModeHost);
            if (IsFlowOwnedSession())
            {
                return;
            }

            IGameplaySessionController session = GameStateUiHelper.ResolveSessionController(gameplayModeHost);
            if (session == null)
            {
                Debug.LogWarning("[TitleScreenController] Gameplay session 없음 — 자동 시작 건너뜀");
                return;
            }

            bool isFirstPlay = alwaysPlayIntroOnStart || !PlayerPrefs.HasKey(FirstPlayKey);
            if (isFirstPlay)
            {
                // 첫 플레이: 타이틀 건너뛰고 Intro부터 시작
                PlayerPrefs.SetInt(FirstPlayKey, 1);
                PlayerPrefs.Save();
                session.StartGame(isFirstPlay: true);
            }
            else
            {
                // 재플레이: 타이틀 화면 표시
                ShowTitle();
            }
        }

        private bool IsFlowOwnedSession()
        {
            GameplayModeContext context = gameplayModeHost != null ? gameplayModeHost.Context : null;
            return context?.Services?.TryGet<FlowEntryDefinition>(out _) == true;
        }

        private void Hide()
        {
            GameStateUiHelper.SetVisible(titleCanvas, false);
        }

        private void SubscribeStateSource()
        {
            if (_subscribedToHost)
            {
                return;
            }

            gameplayModeHost = GameStateUiHelper.ResolveGameplayModeHost(gameplayModeHost);
            if (gameplayModeHost != null)
            {
                gameplayModeHost.StateChanged += OnGameStateChanged;
                _subscribedToHost = true;
                OnGameStateChanged(gameplayModeHost.CurrentState);
                return;
            }
        }

        private void UnsubscribeStateSource()
        {
            if (_subscribedToHost && gameplayModeHost != null)
            {
                gameplayModeHost.StateChanged -= OnGameStateChanged;
            }

            _subscribedToHost = false;
        }

        private void OnStartClicked()
        {
            Hide();
            IGameplaySessionController session = GameStateUiHelper.ResolveSessionController(gameplayModeHost);
            session?.StartGame(isFirstPlay: alwaysPlayIntroOnStart);
        }

        // ── Inspector 컨텍스트 메뉴 테스트 ───────────────────────────────────

        [ContextMenu("Test: Reset First Play Flag")]
        private void ResetFirstPlayFlag()
        {
            PlayerPrefs.DeleteKey(FirstPlayKey);
            PlayerPrefs.Save();
            Debug.Log("[TitleScreenController] 첫 플레이 플래그 초기화 완료");
        }
    }
}
