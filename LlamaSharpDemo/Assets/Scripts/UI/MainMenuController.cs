using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DoodleDiplomacy.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string EnglishLocale = "en-US";
        private const string KoreanLocale = "ko-KR";
        private const string FirstPlayKey = "DD_HasPlayed";

        [Header("Scene Flow")]
        [SerializeField] private string gameRootSceneName = "GameRoot";

        [Header("UI")]
        [SerializeField] private bool buildUiIfMissing = true;
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI settingsTitleText;
        [SerializeField] private TextMeshProUGUI languageLabelText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button englishButton;
        [SerializeField] private Button koreanButton;
        [SerializeField] private Button closeSettingsButton;

        private void Awake()
        {
            EnsureCamera();
            EnsureEventSystem();
            EnsureUi();
            RegisterButtonListeners();
            HideSettings();
            RefreshLocalizedText();
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
            UnregisterButtonListeners();
        }

        private void RegisterButtonListeners()
        {
            startButton?.onClick.AddListener(StartGame);
            settingsButton?.onClick.AddListener(ShowSettings);
            englishButton?.onClick.AddListener(() => SelectLocale(EnglishLocale));
            koreanButton?.onClick.AddListener(() => SelectLocale(KoreanLocale));
            closeSettingsButton?.onClick.AddListener(HideSettings);
        }

        private void UnregisterButtonListeners()
        {
            startButton?.onClick.RemoveListener(StartGame);
            settingsButton?.onClick.RemoveListener(ShowSettings);
            englishButton?.onClick.RemoveAllListeners();
            koreanButton?.onClick.RemoveAllListeners();
            closeSettingsButton?.onClick.RemoveListener(HideSettings);
        }

        private void StartGame()
        {
            PlayerPrefs.SetInt(FirstPlayKey, 1);
            PlayerPrefs.Save();

            if (string.IsNullOrWhiteSpace(gameRootSceneName))
            {
                Debug.LogError("[MainMenuController] Game root scene name is empty.", this);
                return;
            }

            SceneManager.LoadScene(gameRootSceneName, LoadSceneMode.Single);
        }

        private void ShowSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
            }

            RefreshLocalizedText();
        }

        private void HideSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
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
            if (titleText != null)
            {
                titleText.text = "DOODLE DIPLOMACY";
            }

            SetButtonText(startButton, L10n.T("ui.title.start", "START"));
            SetButtonText(settingsButton, L10n.T("ui.title.settings", "SETTINGS"));
            SetButtonText(englishButton, L10n.T("ui.settings.english", "English"));
            SetButtonText(koreanButton, L10n.T("ui.settings.korean", "Korean"));
            SetButtonText(closeSettingsButton, L10n.T("ui.settings.back", "Back"));

            if (settingsTitleText != null)
            {
                settingsTitleText.text = L10n.T("ui.settings.title", "Settings");
            }

            if (languageLabelText != null)
            {
                languageLabelText.text = L10n.T("ui.settings.language", "Language");
            }

            RefreshLocaleSelectionVisuals();
        }

        private void RefreshLocaleSelectionVisuals()
        {
            SetSelected(englishButton, GameLocalizationSettings.LocaleEquals(L10n.CurrentLocale, EnglishLocale));
            SetSelected(koreanButton, GameLocalizationSettings.LocaleEquals(L10n.CurrentLocale, KoreanLocale));
        }

        private void EnsureUi()
        {
            if (!buildUiIfMissing)
            {
                return;
            }

            Canvas canvas = menuRoot != null ? menuRoot.GetComponent<Canvas>() : null;
            if (canvas == null)
            {
                canvas = CreateCanvas();
                menuRoot = canvas.gameObject;
            }

            Transform root = canvas.transform;

            if (titleText == null)
            {
                titleText = CreateText("Title", root, 56f, FontStyles.Bold);
                RectTransform rect = titleText.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 220f);
                rect.sizeDelta = new Vector2(820f, 96f);
            }

            if (startButton == null)
            {
                startButton = CreateButton("StartButton", root, new Vector2(0f, 24f), new Vector2(300f, 64f));
            }

            if (settingsButton == null)
            {
                settingsButton = CreateButton("SettingsButton", root, new Vector2(0f, -60f), new Vector2(300f, 64f));
            }

            if (settingsPanel == null)
            {
                settingsPanel = CreateSettingsPanel(root);
            }
        }

        private static Canvas CreateCanvas()
        {
            GameObject canvasObject = new("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject background = new("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(canvasObject.transform, false);
            RectTransform backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            Image backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0.025f, 0.035f, 0.04f, 1f);

            return canvas;
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
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;

            GameObject panel = new("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(overlay.transform, false);

            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -16f);
            panelRect.sizeDelta = new Vector2(520f, 360f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.055f, 0.07f, 0.075f, 0.98f);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 34, 34);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            settingsTitleText = CreateText("Title", panel.transform, 34f, FontStyles.Bold);
            AddLayout(settingsTitleText.gameObject, 56f);

            languageLabelText = CreateText("LanguageLabel", panel.transform, 22f, FontStyles.Normal);
            AddLayout(languageLabelText.gameObject, 36f);

            englishButton = CreateStackButton("EnglishButton", panel.transform);
            koreanButton = CreateStackButton("KoreanButton", panel.transform);
            closeSettingsButton = CreateStackButton("BackButton", panel.transform);

            return overlay;
        }

        private static Button CreateStackButton(string name, Transform parent)
        {
            Button button = CreateButton(name, parent, Vector2.zero, new Vector2(420f, 54f));
            AddLayout(button.gameObject, 54f);
            return button;
        }

        private static Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
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
            image.color = new Color(0.12f, 0.15f, 0.16f, 0.98f);

            Button button = buttonObject.GetComponent<Button>();
            ConfigureButtonColors(button);

            TextMeshProUGUI label = CreateText("Label", buttonObject.transform, 24f, FontStyles.Bold);
            label.raycastTarget = false;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(16f, 4f);
            labelRect.offsetMax = new Vector2(-16f, -4f);

            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, FontStyles style)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.characterSpacing = 0f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = fontSize;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureButtonColors(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.12f, 0.15f, 0.16f, 0.98f);
            colors.highlightedColor = new Color(0.2f, 0.28f, 0.3f, 1f);
            colors.pressedColor = new Color(0.06f, 0.09f, 0.1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
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
                    : new Color(0.12f, 0.15f, 0.16f, 0.98f);
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

        private static void AddLayout(GameObject target, float preferredHeight)
        {
            LayoutElement element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = 1f;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemObject = new("EventSystem", typeof(EventSystem));
            InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private static void EnsureCamera()
        {
            if (FindFirstObjectByType<UnityEngine.Camera>() != null)
            {
                return;
            }

            GameObject cameraObject = new("MainMenuCamera", typeof(UnityEngine.Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            UnityEngine.Camera camera = cameraObject.GetComponent<UnityEngine.Camera>();
            camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.04f, 1f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.cullingMask = 0;
        }
    }
}
