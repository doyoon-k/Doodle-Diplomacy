using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactProbeWorkingState
    {
        public FirstContactCardSource Source { get; private set; }
        public Texture2D Texture { get; private set; }
        public byte[] PngBytes { get; private set; }
        public string CanonicalLabel { get; private set; } = string.Empty;
        public string DisplayLabel { get; private set; } = string.Empty;
        public bool TranslationAvailable { get; private set; }

        public bool HasCapture => Texture != null && PngBytes != null && PngBytes.Length > 0;

        public string PreferredLabel => !string.IsNullOrWhiteSpace(DisplayLabel)
            ? DisplayLabel
            : CanonicalLabel;

        public void Reset(FirstContactCardSource source)
        {
            Source = source;
            Texture = null;
            PngBytes = null;
            CanonicalLabel = string.Empty;
            DisplayLabel = string.Empty;
            TranslationAvailable = false;
        }

        public bool TryApplyCapture(FirstContactProbeCaptureResult result)
        {
            if (!result.IsSuccess)
            {
                return false;
            }

            Texture = result.Texture;
            PngBytes = result.PngBytes;
            return true;
        }

        public bool TrySetSubmittedLabel(string canonicalLabel, string displayLabel)
        {
            if (string.IsNullOrWhiteSpace(canonicalLabel) || string.IsNullOrWhiteSpace(displayLabel))
            {
                return false;
            }

            CanonicalLabel = canonicalLabel;
            DisplayLabel = displayLabel;
            TranslationAvailable = false;
            return true;
        }

        public bool TryApplyLabelAnalysis(FirstContactProbeLabelResult result)
        {
            if (result?.IsSuccess != true || string.IsNullOrWhiteSpace(result.CanonicalLabel))
            {
                return false;
            }

            CanonicalLabel = result.CanonicalLabel;
            TranslationAvailable = result.TranslationAvailable;
            return true;
        }

        public FirstContactProbeDraft CreateDraft()
        {
            return new FirstContactProbeDraft(
                Texture,
                CanonicalLabel,
                DisplayLabel,
                TranslationAvailable);
        }
    }
}
