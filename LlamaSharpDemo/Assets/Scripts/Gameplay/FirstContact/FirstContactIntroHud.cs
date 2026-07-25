using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroHud : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI objectiveText;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private TextMeshProUGUI crosshairText;

        private string _objectiveKey = string.Empty;
        private string _objectiveFallback = string.Empty;
        private string _promptKey = string.Empty;
        private string _promptFallback = string.Empty;
        private bool _crosshairVisible = true;

        public void Configure(
            TextMeshProUGUI objective,
            TextMeshProUGUI prompt,
            TextMeshProUGUI crosshair)
        {
            objectiveText = objective;
            promptText = prompt;
            crosshairText = crosshair;
            RefreshFont();
            RefreshLocalizedText();
            RefreshCrosshairVisibility();
        }

        private void OnEnable()
        {
            L10n.LocaleChanged += HandleLocaleChanged;
            RefreshFont();
            RefreshLocalizedText();
            RefreshCrosshairVisibility();
        }

        private void OnDisable()
        {
            L10n.LocaleChanged -= HandleLocaleChanged;
        }

        public void SetObjective(string localizationKey, string fallback)
        {
            _objectiveKey = localizationKey ?? string.Empty;
            _objectiveFallback = fallback ?? string.Empty;
            RefreshObjective();
        }

        public void ClearObjective()
        {
            SetObjective(string.Empty, string.Empty);
        }

        public void SetPrompt(string localizationKey, string fallback)
        {
            _promptKey = localizationKey ?? string.Empty;
            _promptFallback = fallback ?? string.Empty;
            RefreshPrompt();
        }

        public void ClearPrompt()
        {
            SetPrompt(string.Empty, string.Empty);
        }

        public void SetCrosshairVisible(bool visible)
        {
            _crosshairVisible = visible;
            RefreshCrosshairVisibility();
        }

        private void HandleLocaleChanged(string locale)
        {
            RefreshFont();
            RefreshLocalizedText();
        }

        private void RefreshLocalizedText()
        {
            RefreshObjective();
            RefreshPrompt();
        }

        private void RefreshObjective()
        {
            if (objectiveText == null)
            {
                return;
            }

            bool visible = !string.IsNullOrWhiteSpace(_objectiveFallback);
            objectiveText.gameObject.SetActive(visible);
            objectiveText.text = visible
                ? L10n.T(_objectiveKey, _objectiveFallback)
                : string.Empty;
        }

        private void RefreshPrompt()
        {
            if (promptText == null)
            {
                return;
            }

            bool visible = !string.IsNullOrWhiteSpace(_promptFallback);
            promptText.gameObject.SetActive(visible);
            promptText.text = visible
                ? L10n.T(_promptKey, _promptFallback)
                : string.Empty;
        }

        private void RefreshCrosshairVisibility()
        {
            if (crosshairText != null)
            {
                crosshairText.gameObject.SetActive(_crosshairVisible);
            }
        }

        private void RefreshFont()
        {
            TMP_FontAsset font = L10n.CurrentFont;
            if (font == null)
            {
                return;
            }

            if (objectiveText != null)
            {
                objectiveText.font = font;
            }

            if (promptText != null)
            {
                promptText.font = font;
            }

            if (crosshairText != null)
            {
                crosshairText.font = font;
            }
        }
    }
}
