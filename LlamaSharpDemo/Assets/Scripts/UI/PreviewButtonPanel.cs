using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.UI
{
    /// <summary>
    /// Preview 상태에서만 표시되는 수정/제출 버튼 패널.
    /// RoundManager.OnStateChanged에 연결해서 자동 표시/숨김.
    /// </summary>
    public class PreviewButtonPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button modifyButton;
        [SerializeField] private RoundManager roundManager;

        private void Awake()
        {
            if (panel == null) panel = gameObject;
            if (panel != null && panel.TryGetComponent(out Image panelImage))
            {
                // Keep only button graphics as raycast targets; transparent panel backgrounds
                // can otherwise block drawing input after returning from preview.
                panelImage.raycastTarget = false;
            }

            EnsureEventSystem();
            submitButton?.onClick.AddListener(OnSubmit);
            modifyButton?.onClick.AddListener(OnModify);

            Hide();
        }

        private void OnEnable()
        {
            EnsureEventSystem();
        }

        private void OnDestroy()
        {
            submitButton?.onClick.RemoveListener(OnSubmit);
            modifyButton?.onClick.RemoveListener(OnModify);
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Preview)
                Show();
            else
                Hide();
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private void Show() => panel?.SetActive(true);
        private void Hide() => panel?.SetActive(false);

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private void OnSubmit()
        {
            if (roundManager == null) roundManager = RoundManager.Instance;
            roundManager?.OnPreviewSubmit();
        }

        private void OnModify()
        {
            if (roundManager == null) roundManager = RoundManager.Instance;
            roundManager?.OnPreviewModify();
        }
    }
}
