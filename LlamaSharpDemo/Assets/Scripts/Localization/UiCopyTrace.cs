using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DoodleDiplomacy.Localization
{
    [Serializable]
    public sealed class UiCopyTraceEvent
    {
        public string screenId = string.Empty;
        public string surface = string.Empty;
        public string focusKey = string.Empty;
        public string phase = string.Empty;
        public string timestampUtc = string.Empty;
        public List<string> keys = new();
    }

    /// <summary>
    /// Editor-only UI authoring trace. Localized calls are collected only while an
    /// explicitly declared screen context is active, so ordinary L10n lookups do
    /// not produce a noisy runtime log.
    /// </summary>
    public static class UiCopyTrace
    {
#if UNITY_EDITOR
        private static readonly List<string> ActiveKeys = new();
        private static readonly HashSet<string> ActiveKeySet = new(StringComparer.OrdinalIgnoreCase);
        private static string _screenId = string.Empty;
        private static string _surface = string.Empty;
        private static string _focusKey = string.Empty;
        private static string _lastSignature = string.Empty;
        private static bool _collecting;

        public static event Action<UiCopyTraceEvent> Emitted;
#endif

        [Conditional("UNITY_EDITOR")]
        public static void BeginScreen(string screenId, string surface, string focusKey = "")
        {
#if UNITY_EDITOR
            _screenId = screenId?.Trim() ?? string.Empty;
            _surface = surface?.Trim() ?? string.Empty;
            _focusKey = focusKey?.Trim() ?? string.Empty;
            ActiveKeys.Clear();
            ActiveKeySet.Clear();
            _collecting = !string.IsNullOrWhiteSpace(_screenId);
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public static void Record(string key)
        {
#if UNITY_EDITOR
            if (!_collecting || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string normalized = key.Trim();
            if (ActiveKeySet.Add(normalized))
            {
                ActiveKeys.Add(normalized);
            }
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public static void EndScreen(string phase = "visible")
        {
#if UNITY_EDITOR
            if (!_collecting)
            {
                return;
            }

            _collecting = false;
            string signature = _screenId + "\n" + _focusKey + "\n" + string.Join("\n", ActiveKeys);
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastSignature = signature;
            Emitted?.Invoke(new UiCopyTraceEvent
            {
                screenId = _screenId,
                surface = _surface,
                focusKey = _focusKey,
                phase = phase ?? "visible",
                timestampUtc = DateTime.UtcNow.ToString("O"),
                keys = new List<string>(ActiveKeys)
            });
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public static void Focus(string screenId, string surface, string key, string phase = "focus")
        {
#if UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string normalized = key.Trim();
            string normalizedScreen = screenId?.Trim() ?? string.Empty;
            var keys = string.Equals(normalizedScreen, _screenId, StringComparison.OrdinalIgnoreCase)
                ? new List<string>(ActiveKeys)
                : new List<string>();
            if (!keys.Exists(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                keys.Add(normalized);
            }

            Emitted?.Invoke(new UiCopyTraceEvent
            {
                screenId = normalizedScreen,
                surface = surface?.Trim() ?? string.Empty,
                focusKey = normalized,
                phase = phase ?? "focus",
                timestampUtc = DateTime.UtcNow.ToString("O"),
                keys = keys
            });
#endif
        }
    }
}
