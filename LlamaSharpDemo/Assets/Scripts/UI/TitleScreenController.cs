using UnityEngine;
using UnityEngine.UI;
using DoodleDiplomacy.Core;

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

        private RoundManager _roundManager;

        private void Awake()
        {
            if (titleCanvas != null) titleCanvas.SetActive(false);
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        }

        private void Start()
        {
            // 게임 시작 시 자동 진입
            TryAutoStart();
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        /// <summary>RoundManager.OnStateChanged 이벤트에 연결.</summary>
        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Title)
                ShowTitle();
            else
                Hide();
        }

        public void ShowTitle()
        {
            if (titleCanvas != null) titleCanvas.SetActive(true);
            Debug.Log("[TitleScreenController] 타이틀 화면 표시");
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private void TryAutoStart()
        {
            if (_roundManager == null) _roundManager = RoundManager.Instance;
            if (_roundManager == null)
            {
                Debug.LogWarning("[TitleScreenController] RoundManager 없음 — 자동 시작 건너뜀");
                return;
            }

            bool isFirstPlay = !PlayerPrefs.HasKey(FirstPlayKey);
            if (isFirstPlay)
            {
                // 첫 플레이: 타이틀 건너뛰고 Intro부터 시작
                PlayerPrefs.SetInt(FirstPlayKey, 1);
                PlayerPrefs.Save();
                _roundManager.StartGame(isFirstPlay: true);
            }
            else
            {
                // 재플레이: 타이틀 화면 표시
                ShowTitle();
            }
        }

        private void Hide()
        {
            if (titleCanvas != null) titleCanvas.SetActive(false);
        }

        private void OnStartClicked()
        {
            Hide();
            if (_roundManager == null) _roundManager = RoundManager.Instance;
            _roundManager?.StartGame(isFirstPlay: false);
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
