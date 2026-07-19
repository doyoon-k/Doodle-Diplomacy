using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactProbeWorkingState
    {
        public FirstContactCardSource Source { get; private set; }
        public Texture2D Texture { get; private set; }
        public byte[] PngBytes { get; private set; }
        public string NormalizedLabel { get; private set; } = string.Empty;
        public string OriginalLabel { get; private set; } = string.Empty;

        public string CanonicalLabel => NormalizedLabel;
        public string DisplayLabel => OriginalLabel;
        public bool TranslationAvailable => false;

        public bool HasCapture => Texture != null && PngBytes != null && PngBytes.Length > 0;

        public string PreferredLabel => !string.IsNullOrWhiteSpace(OriginalLabel)
            ? OriginalLabel
            : NormalizedLabel;

        public void Reset(FirstContactCardSource source)
        {
            Source = source;
            Texture = null;
            PngBytes = null;
            NormalizedLabel = string.Empty;
            OriginalLabel = string.Empty;
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

        public bool TrySetSubmittedLabel(string normalizedLabel, string originalLabel)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel) || string.IsNullOrWhiteSpace(originalLabel))
            {
                return false;
            }

            NormalizedLabel = normalizedLabel;
            OriginalLabel = originalLabel;
            return true;
        }

        public bool TryApplyLabelAnalysis(FirstContactProbeLabelResult result)
        {
            if (result?.IsSuccess != true || string.IsNullOrWhiteSpace(result.NormalizedLabel))
            {
                return false;
            }

            NormalizedLabel = result.NormalizedLabel;
            return true;
        }

        public FirstContactProbeDraft CreateDraft()
        {
            return new FirstContactProbeDraft(
                Texture,
                NormalizedLabel,
                OriginalLabel);
        }
    }
}
