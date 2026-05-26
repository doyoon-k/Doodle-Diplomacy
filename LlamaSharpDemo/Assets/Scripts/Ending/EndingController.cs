using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.UI;

namespace DoodleDiplomacy.Ending
{
    /// <summary>
    /// Ending 상태 진입 시 EndingCanvas에 결말 이미지·제목·설명을 표시한다.
    /// 화면 클릭 시 현재 gameplay session을 Title 상태로 전환한다.
    /// </summary>
    public class EndingController : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("Root canvas or panel displayed during the ending state.")]
        [SerializeField] private GameObject    endingCanvas;
        [Tooltip("Image component that shows the selected ending background sprite.")]
        [SerializeField] private Image         backgroundImage;
        [Tooltip("Text element that displays the ending title.")]
        [SerializeField] private TextMeshProUGUI titleText;
        [Tooltip("Text element that displays the ending description.")]
        [SerializeField] private TextMeshProUGUI descriptionText;
        [Tooltip("클릭 감지용 전면 버튼 (EndingCanvas 전체를 덮는 Button)")]
        [SerializeField] private Button        anyClickButton;

        [Header("Ending Data")]
        [Tooltip("Available ending definitions searched by EndingType when the game finishes.")]
        [SerializeField] private List<EndingData> endingDataList = new();

        [Header("State Source")]
        [Tooltip("Gameplay mode host whose state changes control ending screen visibility.")]
        [SerializeField] private GameplayModeHost gameplayModeHost;

        private bool _subscribedToHost;

        private void Awake()
        {
            if (endingCanvas != null) endingCanvas.SetActive(false);

            if (anyClickButton != null)
                anyClickButton.onClick.AddListener(OnAnyClick);
        }

        private void OnDestroy()
        {
            UnsubscribeStateSource();
            if (anyClickButton != null)
                anyClickButton.onClick.RemoveListener(OnAnyClick);
        }

        private void OnEnable()
        {
            SubscribeStateSource();
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        /// <summary>GameplayModeHost 상태 변경 이벤트에 연결.</summary>
        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Ending)
            {
                EndingType type = ScoreManager.Instance != null
                    ? ScoreManager.Instance.GetEndingType()
                    : EndingType.Diplomacy;
                ShowEnding(type);
            }
            else
            {
                Hide();
            }
        }

        public void ShowEnding(EndingType type)
        {
            EndingData data = FindEndingData(type);

            if (backgroundImage != null)
                backgroundImage.sprite = data?.backgroundImage;

            if (titleText != null)
                titleText.text = data != null ? data.title : type.ToString();

            if (descriptionText != null)
                descriptionText.text = data != null ? data.description : "";

            GameStateUiHelper.SetVisible(endingCanvas, true);

            Debug.Log($"[EndingController] 결말 표시: {type}");
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private void Hide()
        {
            GameStateUiHelper.SetVisible(endingCanvas, false);
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

        private void OnAnyClick()
        {
            GameStateUiHelper.ResolveSessionController(gameplayModeHost)?.ChangeToTitle();
        }

        private EndingData FindEndingData(EndingType type)
        {
            foreach (var data in endingDataList)
                if (data != null && data.endingType == type) return data;
            return null;
        }

        // ── Inspector 컨텍스트 메뉴 테스트 ───────────────────────────────────

        [ContextMenu("Test: Show Diplomacy")]
        private void TestDiplomacy() => ShowEnding(EndingType.Diplomacy);

        [ContextMenu("Test: Show Alliance")]
        private void TestAlliance() => ShowEnding(EndingType.Alliance);

        [ContextMenu("Test: Show Destruction")]
        private void TestDestruction() => ShowEnding(EndingType.Destruction);
    }
}
