using System;
using DoodleDiplomacy.Devices;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundPreviewTerminalPresenter
    {
        private const string PreviewFallbackText = "(analysis unavailable)";
        private const string PreviewTerminalHeader = "[ALIEN FIRST PASS]";

        private readonly TerminalDisplay _terminalDisplay;
        private readonly Func<RoundHintPresenter> _hintPresenterProvider;

        private bool _hasShownOnce;
        private string _cachedOutput = string.Empty;

        public RoundPreviewTerminalPresenter(
            TerminalDisplay terminalDisplay,
            Func<RoundHintPresenter> hintPresenterProvider)
        {
            _terminalDisplay = terminalDisplay;
            _hintPresenterProvider = hintPresenterProvider;
        }

        public bool IsOpen { get; set; }

        public void Reset()
        {
            IsOpen = false;
            _hasShownOnce = false;
            _cachedOutput = string.Empty;
        }

        public void ClearTerminal()
        {
            _terminalDisplay?.Clear();
        }

        public void CacheResult(string analysis)
        {
            string resolvedAnalysis = string.IsNullOrWhiteSpace(analysis)
                ? PreviewFallbackText
                : analysis.Trim();
            _cachedOutput = BuildOutput(resolvedAnalysis);
        }

        public void Show()
        {
            string output = string.IsNullOrWhiteSpace(_cachedOutput)
                ? BuildOutput(PreviewFallbackText)
                : _cachedOutput;

            _terminalDisplay?.ShowText(output, _hasShownOnce);
            _hasShownOnce = true;
            _hintPresenterProvider?.Invoke()?.Hide();
        }

        private static string BuildOutput(string previewLine)
        {
            string line = string.IsNullOrWhiteSpace(previewLine)
                ? PreviewFallbackText
                : previewLine.Trim();
            return $"{PreviewTerminalHeader}\n> {line}\n> _";
        }
    }
}
