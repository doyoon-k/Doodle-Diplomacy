using System;
using DoodleDiplomacy.Data;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundTextProvider
    {
        private const string DefaultObjectGenerationFailedRetryMessage = "Object generation failed. Click the alien to retry.";
        private const string DefaultObjectGenerationFailedPrefix = "Object generation failed: ";
        private const string DefaultDrawingReadyHintTemplate = "Press {0} when the drawing is ready to submit.";

        private readonly Func<IngameTextTable> _textTableProvider;
        private readonly Func<KeyCode> _exitDrawingKeyProvider;

        internal RoundTextProvider(
            Func<IngameTextTable> textTableProvider,
            Func<KeyCode> exitDrawingKeyProvider)
        {
            _textTableProvider = textTableProvider;
            _exitDrawingKeyProvider = exitDrawingKeyProvider;
        }

        public string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback)
        {
            IngameTextTable table = _textTableProvider?.Invoke();
            if (table == null)
            {
                return fallback;
            }

            string configured = selector(table);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        public string GetDrawingReadyHintMessage()
        {
            string template = GetConfiguredText(
                table => table.drawingReadyHintTemplate,
                DefaultDrawingReadyHintTemplate);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = DefaultDrawingReadyHintTemplate;
            }

            try
            {
                return string.Format(template, _exitDrawingKeyProvider());
            }
            catch (FormatException)
            {
                Debug.LogWarning(
                    $"[RoundTextProvider] drawingReadyHintTemplate is invalid ('{template}'). Falling back to default template.");
                return string.Format(DefaultDrawingReadyHintTemplate, _exitDrawingKeyProvider());
            }
        }

        public string BuildObjectGenerationFailureHint(string objectGenerationError)
        {
            if (string.IsNullOrWhiteSpace(objectGenerationError))
            {
                return GetConfiguredText(
                    table => table.objectGenerationFailedRetryMessage,
                    DefaultObjectGenerationFailedRetryMessage);
            }

            string prefix = GetConfiguredText(
                table => table.objectGenerationFailedPrefix,
                DefaultObjectGenerationFailedPrefix);
            return $"{prefix}{objectGenerationError}";
        }
    }
}
