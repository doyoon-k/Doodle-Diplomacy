using System;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Dialogue
{
    public class SubtitleDisplay : MonoBehaviour
    {
        [Tooltip("Root subtitle panel toggled when subtitles are shown or hidden.")]
        [SerializeField] private GameObject panel;
        [Tooltip("Text element used for the speaker or narrator name.")]
        [SerializeField] private TextMeshProUGUI nameText;
        [Tooltip("Text element used for the subtitle body.")]
        [SerializeField] private TextMeshProUGUI bodyText;

        [Header("Advance Prompt")]
        [Tooltip("Optional authored prompt. A temporary text prompt is created at runtime when this is empty.")]
        [SerializeField] private TextMeshProUGUI advancePromptText;
        [Tooltip("Create a text-only SPACE key prompt when no authored prompt is assigned.")]
        [SerializeField] private bool buildAdvancePromptIfMissing = true;
        [Min(0.1f)]
        [SerializeField] private float advancePromptPulseSpeed = 1.8f;
        [Range(0.05f, 1f)]
        [SerializeField] private float advancePromptMinimumAlpha = 0.38f;
        [Tooltip("Allow clicking the subtitle panel as an alternative to pressing Space.")]
        [SerializeField] private bool allowPanelClickToAdvance = true;

        private const string AdvancePromptObjectName = "DialogueAdvancePrompt";
        private const string FirstAdvancePromptKey = "dialogue.advance.first";
        private const string RepeatAdvancePromptKey = "dialogue.advance.repeat";

        private Button _panelButton;
        private Color _advancePromptBaseColor = new(1f, 0.9f, 0.4f, 1f);
        private bool _advancePromptVisible;
        private bool _advanceRequestPending;
        private bool _hasShownFullAdvancePrompt;
        private bool _showingFullAdvancePrompt;
        private float _advancePromptPulseTime;

        public event Action AdvanceRequested;
        public bool IsAdvancePromptVisible => _advancePromptVisible;

        private void Awake()
        {
            if (panel == null)
            {
                panel = gameObject;
            }

            EnsureAdvancePrompt();
            EnsurePanelButton();
            Hide();
        }

        private void OnEnable()
        {
            L10n.LocaleChanged += OnLocaleChanged;
        }

        private void OnDisable()
        {
            L10n.LocaleChanged -= OnLocaleChanged;
        }

        private void OnDestroy()
        {
            L10n.LocaleChanged -= OnLocaleChanged;
            if (_panelButton != null)
            {
                _panelButton.onClick.RemoveListener(HandlePanelClicked);
            }
        }

        private void Update()
        {
            if (!_advancePromptVisible || advancePromptText == null)
            {
                return;
            }

            _advancePromptPulseTime += Time.unscaledDeltaTime * advancePromptPulseSpeed;
            float wave = 0.5f + 0.5f * Mathf.Sin(_advancePromptPulseTime * Mathf.PI * 2f);
            float alpha = Mathf.Lerp(advancePromptMinimumAlpha, 1f, Mathf.SmoothStep(0f, 1f, wave));
            ApplyAdvancePromptAlpha(alpha);
        }

        /// <summary>
        /// 화면 하단 자막 패널을 열고 캐릭터명 + 대사 초기값을 설정.
        /// </summary>
        public void Show(string characterName, string text)
        {
            if (panel != null)
            {
                panel.SetActive(true);
            }

            SetAdvancePromptVisible(false);
            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(characterName) ? "" : characterName;
            SetText(text);
        }

        public void SetText(string text)
        {
            if (bodyText != null)
                bodyText.text = text;
        }

        public void Hide()
        {
            SetAdvancePromptVisible(false);
            if (panel != null)
                panel.SetActive(false);
        }

        public void SetAdvancePromptVisible(bool visible)
        {
            EnsureAdvancePrompt();
            _advanceRequestPending = false;
            _advancePromptVisible = visible && advancePromptText != null;

            if (advancePromptText == null)
            {
                return;
            }

            if (!_advancePromptVisible)
            {
                advancePromptText.gameObject.SetActive(false);
                return;
            }

            _showingFullAdvancePrompt = !_hasShownFullAdvancePrompt;
            _hasShownFullAdvancePrompt = true;
            _advancePromptPulseTime = 0f;
            RefreshAdvancePromptText();
            ApplyAdvancePromptAlpha(1f);
            advancePromptText.gameObject.SetActive(true);
        }

        public bool ConsumeAdvanceRequest()
        {
            bool requested = _advanceRequestPending;
            _advanceRequestPending = false;
            return requested;
        }

        private void HandlePanelClicked()
        {
            if (!allowPanelClickToAdvance)
            {
                return;
            }

            _advanceRequestPending = true;
            AdvanceRequested?.Invoke();
        }

        private void EnsureAdvancePrompt()
        {
            if (advancePromptText != null || panel == null)
            {
                return;
            }

            Transform existing = panel.transform.Find(AdvancePromptObjectName);
            if (existing != null)
            {
                advancePromptText = existing.GetComponent<TextMeshProUGUI>();
            }

            if (advancePromptText == null && buildAdvancePromptIfMissing)
            {
                var promptObject = new GameObject(
                    AdvancePromptObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                promptObject.transform.SetParent(panel.transform, false);
                advancePromptText = promptObject.GetComponent<TextMeshProUGUI>();

                RectTransform rect = advancePromptText.rectTransform;
                rect.anchorMin = new Vector2(0.62f, 0.04f);
                rect.anchorMax = new Vector2(0.97f, 0.3f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                advancePromptText.alignment = TextAlignmentOptions.MidlineRight;
                advancePromptText.fontSize = 21f;
                advancePromptText.enableAutoSizing = true;
                advancePromptText.fontSizeMin = 12f;
                advancePromptText.fontSizeMax = 21f;
                advancePromptText.fontStyle = FontStyles.Bold;
                advancePromptText.textWrappingMode = TextWrappingModes.NoWrap;
                advancePromptText.raycastTarget = false;

                if (bodyText != null && bodyText.font != null)
                {
                    advancePromptText.font = bodyText.font;
                    advancePromptText.fontSharedMaterial = bodyText.fontSharedMaterial;
                }

                advancePromptText.color = _advancePromptBaseColor;
            }

            if (advancePromptText != null)
            {
                _advancePromptBaseColor = advancePromptText.color;
                advancePromptText.gameObject.SetActive(_advancePromptVisible);
            }
        }

        private void EnsurePanelButton()
        {
            if (!allowPanelClickToAdvance || panel == null)
            {
                return;
            }

            _panelButton = panel.GetComponent<Button>();
            if (_panelButton == null)
            {
                _panelButton = panel.AddComponent<Button>();
            }

            _panelButton.transition = Selectable.Transition.None;
            _panelButton.targetGraphic = panel.GetComponent<Graphic>();
            Navigation navigation = _panelButton.navigation;
            navigation.mode = Navigation.Mode.None;
            _panelButton.navigation = navigation;
            _panelButton.onClick.RemoveListener(HandlePanelClicked);
            _panelButton.onClick.AddListener(HandlePanelClicked);
        }

        private void RefreshAdvancePromptText()
        {
            if (advancePromptText == null)
            {
                return;
            }

            if (_advancePromptVisible)
            {
                UiCopyTrace.BeginScreen("dialogue.advance", "dialogue");
            }

            advancePromptText.text = _showingFullAdvancePrompt
                ? L10n.T(FirstAdvancePromptKey, "[ SPACE ]  NEXT  ▼")
                : L10n.T(RepeatAdvancePromptKey, "[ SPACE ]  ▼");

            if (_advancePromptVisible)
            {
                UiCopyTrace.EndScreen();
            }
        }

        private void ApplyAdvancePromptAlpha(float alpha)
        {
            if (advancePromptText == null)
            {
                return;
            }

            Color color = _advancePromptBaseColor;
            color.a *= Mathf.Clamp01(alpha);
            advancePromptText.color = color;
        }

        private void OnLocaleChanged(string _)
        {
            if (_advancePromptVisible)
            {
                RefreshAdvancePromptText();
            }
        }
    }
}
