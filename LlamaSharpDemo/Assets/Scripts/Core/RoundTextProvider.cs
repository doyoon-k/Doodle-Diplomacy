using System;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundTextProvider
    {
        private const string DefaultObjectGenerationFailedRetryMessage = "Object generation failed. Click the alien to retry.";
        private const string DefaultObjectGenerationFailedPrefix = "Object generation failed: ";
        private const string DefaultDrawingReadyHintTemplate = "Press {key} when the drawing is ready to submit.";

        private readonly Func<KeyCode> _exitDrawingKeyProvider;

        internal RoundTextProvider(Func<KeyCode> exitDrawingKeyProvider)
        {
            _exitDrawingKeyProvider = exitDrawingKeyProvider;
        }

        public string GetDrawingReadyHintMessage()
        {
            return L10n.T(
                "round.drawing.ready_hint",
                DefaultDrawingReadyHintTemplate,
                L10n.Arg("key", _exitDrawingKeyProvider()));
        }

        public string BuildObjectGenerationFailureHint(string objectGenerationError)
        {
            if (string.IsNullOrWhiteSpace(objectGenerationError))
            {
                return L10n.T("round.objects.generation_failed_retry", DefaultObjectGenerationFailedRetryMessage);
            }

            string prefix = L10n.T("round.objects.generation_failed_prefix", DefaultObjectGenerationFailedPrefix);
            return $"{prefix}{objectGenerationError}";
        }
    }
}
