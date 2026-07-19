using System;
using System.Collections.Generic;
using System.Text;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [Serializable]
    public sealed class FirstContactAlienSignalSegment
    {
        public string categoryId;
        public string rawSignal = "[???]";
        public string meaningFallback;
    }

    public readonly struct FirstContactTranslationResult
    {
        public FirstContactTranslationResult(
            string rawSignal,
            string renderedMeaning,
            int translatedSegmentCount,
            int unknownSegmentCount)
        {
            RawSignal = rawSignal ?? string.Empty;
            RenderedMeaning = renderedMeaning ?? string.Empty;
            TranslatedSegmentCount = Math.Max(0, translatedSegmentCount);
            UnknownSegmentCount = Math.Max(0, unknownSegmentCount);
        }

        public string RawSignal { get; }
        public string RenderedMeaning { get; }
        public int TranslatedSegmentCount { get; }
        public int UnknownSegmentCount { get; }
        public bool HasTranslation => TranslatedSegmentCount > 0;
    }

    public sealed class FirstContactCalibrationProfile
    {
        private readonly Dictionary<string, string> _calibratedCategories =
            new(StringComparer.OrdinalIgnoreCase);

        public int CalibratedCategoryCount => _calibratedCategories.Count;
        public IEnumerable<string> CalibratedCategoryIds => _calibratedCategories.Keys;

        public void Reset()
        {
            _calibratedCategories.Clear();
        }

        public bool Calibrate(string categoryId, string displayName)
        {
            string id = NormalizeId(categoryId);
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            _calibratedCategories[id] = string.IsNullOrWhiteSpace(displayName)
                ? id.ToUpperInvariant()
                : displayName.Trim();
            return true;
        }

        public bool IsCalibrated(string categoryId)
        {
            return _calibratedCategories.ContainsKey(NormalizeId(categoryId));
        }

        public FirstContactTranslationResult Translate(
            IReadOnlyList<FirstContactAlienSignalSegment> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return new FirstContactTranslationResult(string.Empty, string.Empty, 0, 0);
            }

            var raw = new StringBuilder();
            var meaning = new StringBuilder();
            int translatedCount = 0;
            int unknownCount = 0;

            for (int i = 0; i < segments.Count; i++)
            {
                FirstContactAlienSignalSegment segment = segments[i];
                if (segment == null)
                {
                    continue;
                }

                AppendSeparated(raw, string.IsNullOrWhiteSpace(segment.rawSignal)
                    ? "[???]"
                    : segment.rawSignal.Trim());

                string categoryId = NormalizeId(segment.categoryId);
                if (!string.IsNullOrWhiteSpace(categoryId) &&
                    _calibratedCategories.TryGetValue(categoryId, out string displayName))
                {
                    string translated = FirstContactTerminalLocalization.LocalizeBootstrapCategory(
                        categoryId,
                        string.IsNullOrWhiteSpace(segment.meaningFallback)
                            ? displayName
                            : segment.meaningFallback);
                    AppendSeparated(meaning, translated.ToUpperInvariant());
                    translatedCount++;
                }
                else
                {
                    AppendSeparated(meaning, string.IsNullOrWhiteSpace(segment.rawSignal)
                        ? "[???]"
                        : segment.rawSignal.Trim());
                    unknownCount++;
                }
            }

            return new FirstContactTranslationResult(
                raw.ToString(),
                meaning.ToString(),
                translatedCount,
                unknownCount);
        }

        private static void AppendSeparated(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append("  ");
            }

            builder.Append(value ?? string.Empty);
        }

        private static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }

    public static class FirstContactCalibrationStore
    {
        private static FirstContactCalibrationProfile _current = new();

        public static FirstContactCalibrationProfile Current => _current;

        public static FirstContactCalibrationProfile BeginNewSession()
        {
            _current = new FirstContactCalibrationProfile();
            return _current;
        }
    }

    public sealed class FirstContactOnboardingMemory
    {
        private readonly HashSet<string> _shown = new(StringComparer.Ordinal);

        public void Reset()
        {
            _shown.Clear();
        }

        public bool TryMarkFirst(string cueId)
        {
            return !string.IsNullOrWhiteSpace(cueId) && _shown.Add(cueId.Trim());
        }
    }
}
