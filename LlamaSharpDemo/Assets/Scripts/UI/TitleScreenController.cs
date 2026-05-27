using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.UI
{
    public class TitleScreenController : MonoBehaviour
    {
        private const string FirstPlayKey = "DD_HasPlayed";
        private const string EnglishLocale = "en-US";
        private const string KoreanLocale = "ko-KR";

        [Header("UI References")]
        [Tooltip("Root canvas or panel shown while the game is in the title state.")]
        [SerializeField] private GameObject titleCanvas;
        [Tooltip("Button that starts or resumes the gameplay flow.")]
        [SerializeField] private Button startButton;
        [Tooltip("Button that opens the language/settings panel.")]
        [SerializeField] private Button settingsButton;
        [Tooltip("Panel containing title-screen settings controls.")]
        [SerializeField] private GameObject settingsPanel;
        [Tooltip("Button that switches the game locale to English.")]
        [SerializeField] private Button englishButton;
        [Tooltip("Button that switches the game locale to Korean.")]
        [SerializeField] private Button koreanButton;
        [Tooltip("Button that closes the settings panel.")]
        [SerializeField] private Button closeSettingsButton;

        [Header("Startup")]
        [Tooltip("When enabled, the first gameplay entry is treated as intro every time the title starts.")]
        [SerializeField] private bool alwaysPlayIntroOnStart = true;
        [Tooltip("Automatically create missing settings UI controls at runtime for lightweight scenes.")]
        [SerializeField] private bool buildSettingsUiIfMissing = true;

        [Header("State Source")]
        [Tooltip("Gameplay mode host whose state changes control title screen visibility.")]
        [SerializeField] private GameplayModeHost gameplayModeHost;

        private bool _subscribedToHost;

        private void Awake()
        {
            if (titleCanvas != null)
            {
                titleCanvas.SetActive(false);
            }

            EnsureSettingsUi();
            RegisterButtonListeners();
            RefreshLocalizedText();
            HideSettings();
        }

        private void Start()
        {
            SubscribeStateSource();
            if (gameplayModeHost == null)
            {
                ShowTitle();
            }
        }

        private void OnEnable()
        {
            SubscribeStateSource();
            L10n.LocaleChanged += OnLocaleChanged;
        }

        private void OnDisable()
        {
            L10n.LocaleChanged -= OnLocaleChanged;
        }

        private void OnDestroy()
        {
            UnsubscribeStateSource();
            L10n.LocaleChanged -= OnLocaleChanged;
            UnregisterButtonListeners();
        }

        public void OnGameStateChanged(GameState state)
        {
            if (state == GameState.Title)
            {
                ShowTitle();
            }
            else
            {
                Hide();
            }
        }

        public void ShowTitle()
        {
            GameStateUiHelper.SetVisible(titleCanvas, true);
            HideSettings();
            RefreshLocalizedText();
            Debug.Log("[TitleScreenController] Main menu shown.");
        }

        private void Hide()
        {
            HideSettings();
            GameStateUiHelper.SetVisible(titleCanvas, false);
        }

        private void RegisterButtonListeners()
        {
            startButton?.onClick.AddListener(OnStartClicked);
            settingsButton?.onClick.AddListener(ShowSettings);
            englishButton?.onClick.AddListener(() => SelectLocale(EnglishLocale));
            koreanButton?.onClick.AddListener(() => SelectLocale(KoreanLocale));
            closeSettingsButton?.onClick.AddListener(HideSettings);
        }

        private void UnregisterButtonListeners()
        {
            startButton?.onClick.RemoveListener(OnStartClicked);
            settingsButton?.onClick.RemoveListener(ShowSettings);
            englishButton?.onClick.RemoveAllListeners();
            koreanButton?.onClick.RemoveAllListeners();
            closeSettingsButton?.onClick.RemoveListener(HideSettings);
        }

        private void OnStartClicked()
        {
            Hide();

            bool isFirstPlay = alwaysPlayIntroOnStart || !PlayerPrefs.HasKey(FirstPlayKey);
            PlayerPrefs.SetInt(FirstPlayKey, 1);
            PlayerPrefs.Save();

            IGameplaySessionController session = GameStateUiHelper.ResolveSessionController(gameplayModeHost);
            session?.StartGame(isFirstPlay);
        }

        private void ShowSettings()
        {
            GameStateUiHelper.SetVisible(settingsPanel, true);
            RefreshLocalizedText();
        }

        private void HideSettings()
        {
            GameStateUiHelper.SetVisible(settingsPanel, false);
        }

        private void SelectLocale(string locale)
        {
            L10n.SetLocale(locale);
            RefreshLocalizedText();
        }

        private void OnLocaleChanged(string locale)
        {
            RefreshLocalizedText();
        }

        private void RefreshLocalizedText()
        {
            SetButtonText(startButton, L10n.T("ui.title.start", "START"));
            SetButtonText(settingsButton, L10n.T("ui.title.settings", "SETTINGS"));
            SetButtonText(englishButton, L10n.T("ui.settings.english", "English"));
            SetButtonText(koreanButton, L10n.T("ui.settings.korean", "Korean"));
            SetButtonText(closeSettingsButton, L10n.T("ui.settings.back", "Back"));
            RefreshLocaleSelectionVisuals();

            TextMeshProUGUI title = settingsPanel != null
                ? settingsPanel.transform.Find("Panel/Title")?.GetComponent<TextMeshProUGUI>()
                : null;
            if (title != null)
            {
                title.text = L10n.T("ui.settings.title", "Settings");
            }

            TextMeshProUGUI languageLabel = settingsPanel != null
                ? settingsPanel.transform.Find("Panel/LanguageLabel")?.GetComponent<TextMeshProUGUI>()
                : null;
            if (languageLabel != null)
            {
                languageLabel.text = L10n.T("ui.settings.language", "Language");
            }
        }

        private void RefreshLocaleSelectionVisuals()
        {
            SetSelected(englishButton, GameLocalizationSettings.LocaleEquals(L10n.CurrentLocale, EnglishLocale));
            SetSelected(koreanButton, GameLocalizationSettings.LocaleEquals(L10n.CurrentLocale, KoreanLocale));
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected
                    ? new Color(0.27f, 0.41f, 0.36f, 0.98f)
                    : new Color(0.11f, 0.14f, 0.16f, 0.96f);
            }
        }

        private static void SetButtonText(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                label.text = text;
            }
        }

        private void EnsureSettingsUi()
        {
            if (!buildSettingsUiIfMissing || titleCanvas == null)
            {
                return;
            }

            Transform parent = titleCanvas.transform;

            if (settingsButton == null)
            {
                settingsButton = CreateMenuButton("SettingsButton", parent, new Vector2(0f, -210f), new Vector2(260f, 58f));
            }

            if (settingsPanel == null)
            {
                settingsPanel = CreateSettingsPanel(parent);
            }
        }

        private GameObject CreateSettingsPanel(Transform parent)
        {
            GameObject overlay = new("SettingsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlay.transform.SetParent(parent, false);

            RectTransform overlayRect = (RectTransform)overlay.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            Image overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.52f);
            overlayImage.raycastTarget = true;

            GameObject panel = new("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(overlay.transform, false);

            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(520f, 360f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.035f, 0.045f, 0.05f, 0.96f);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 34, 34);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TextMeshProUGUI title = CreatePanelText("Title", panel.transform, 34f, FontStyles.Bold);
            AddLayout(title.gameObject, 56f);

            TextMeshProUGUI languageLabel = CreatePanelText("LanguageLabel", panel.transform, 22f, FontStyles.Normal);
            AddLayout(languageLabel.gameObject, 36f);

            englishButton = CreateStackButton("EnglishButton", panel.transform);
            koreanButton = CreateStackButton("KoreanButton", panel.transform);
            closeSettingsButton = CreateStackButton("BackButton", panel.transform);

            return overlay;
        }

        private static Button CreateMenuButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.1f, 0.13f, 0.15f, 0.94f);

            Button button = buttonObject.GetComponent<Button>();
            ConfigureButtonColors(button);

            TextMeshProUGUI label = CreateButtonLabel(buttonObject.transform, 24f);
            label.fontStyle = FontStyles.Bold;
            return button;
        }

        private static Button CreateStackButton(string name, Transform parent)
        {
            Button button = CreateMenuButton(name, parent, Vector2.zero, new Vector2(420f, 54f));
            AddLayout(button.gameObject, 54f);
            return button;
        }

        private static TextMeshProUGUI CreateButtonLabel(Transform parent, float fontSize)
        {
            GameObject labelObject = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = fontSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            label.characterSpacing = 0f;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(14f, 4f);
            rect.offsetMax = new Vector2(-14f, -4f);
            return label;
        }

        private static TextMeshProUGUI CreatePanelText(string name, Transform parent, float fontSize, FontStyles style)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private static void ConfigureButtonColors(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.11f, 0.14f, 0.16f, 0.96f);
            colors.highlightedColor = new Color(0.2f, 0.27f, 0.3f, 1f);
            colors.pressedColor = new Color(0.06f, 0.09f, 0.11f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
        }

        private static void AddLayout(GameObject target, float preferredHeight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = 1f;
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

        [ContextMenu("Test: Reset First Play Flag")]
        private void ResetFirstPlayFlag()
        {
            PlayerPrefs.DeleteKey(FirstPlayKey);
            PlayerPrefs.Save();
            Debug.Log("[TitleScreenController] First play flag reset.");
        }
    }
}
