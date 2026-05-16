using UnityEngine;
using UnityEngine.UI;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Gameplay;

namespace DoodleDiplomacy.UI
{
    /// <summary>
    /// Preview 상태에서만 표시되는 수정/제출 버튼 패널.
    /// GameplayModeHost 상태 변경을 구독해서 자동 표시/숨김.
    /// </summary>
    public class PreviewButtonPanel : MonoBehaviour
    {
        private const string ObjectPairModeId = "object-pair-drawing";

        [Header("References")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button modifyButton;
        [SerializeField] private GameplayModeHost gameplayModeHost;

        private bool _subscribedToHost;

        private void Awake()
        {
            if (panel == null) panel = gameObject;
            if (panel != null && panel.TryGetComponent(out Image panelImage))
            {
                // Keep only button graphics as raycast targets; transparent panel backgrounds
                // can otherwise block drawing input after returning from preview.
                panelImage.raycastTarget = false;
            }

            submitButton?.onClick.AddListener(OnSubmit);
            modifyButton?.onClick.AddListener(OnModify);

            Hide();
        }

        private void OnEnable()
        {
            SubscribeStateSource();
        }

        private void OnDestroy()
        {
            UnsubscribeStateSource();
            submitButton?.onClick.RemoveListener(OnSubmit);
            modifyButton?.onClick.RemoveListener(OnModify);
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Preview && IsObjectPairDrawingModeActive())
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private void Show() => GameStateUiHelper.SetVisible(panel, true);
        private void Hide() => GameStateUiHelper.SetVisible(panel, false);

        private bool IsObjectPairDrawingModeActive()
        {
            gameplayModeHost = GameStateUiHelper.ResolveGameplayModeHost(gameplayModeHost);
            return gameplayModeHost == null ||
                   string.Equals(gameplayModeHost.ActiveModeId, ObjectPairModeId, System.StringComparison.Ordinal);
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

        private void OnSubmit()
        {
            GameStateUiHelper.ResolveSessionController(gameplayModeHost)?.SubmitPreview();
        }

        private void OnModify()
        {
            GameStateUiHelper.ResolveSessionController(gameplayModeHost)?.ModifyPreview();
        }
    }
}
