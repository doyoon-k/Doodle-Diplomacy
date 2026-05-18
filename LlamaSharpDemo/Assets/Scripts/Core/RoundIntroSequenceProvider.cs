using System;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundIntroSequenceProvider
    {
        private readonly Func<DialogueSequence> _sourceSequenceProvider;

        public DialogueSequence RuntimeSequence { get; private set; }

        internal RoundIntroSequenceProvider(Func<DialogueSequence> sourceSequenceProvider)
        {
            _sourceSequenceProvider = sourceSequenceProvider;
        }

        public void Rebuild()
        {
            Release();

            DialogueSequence sourceSequence = _sourceSequenceProvider?.Invoke();
            if (sourceSequence == null)
            {
                return;
            }

            RuntimeSequence = UnityEngine.Object.Instantiate(sourceSequence);
            RuntimeSequence.name = $"{sourceSequence.name} (Runtime)";

            ApplyIntroSpeakerOverride(RuntimeSequence, L10n.T("speaker.adjutant", "Adjutant"));
            ApplyIntroLineOverride(
                RuntimeSequence,
                0,
                L10n.T("intro.adjutant.line1", "Ambassador, the alien delegation has arrived."));
            ApplyIntroLineOverride(
                RuntimeSequence,
                1,
                L10n.T("intro.adjutant.line2", "They cannot understand our language.\n\nWe must communicate through drawings."));
            ApplyIntroLineOverride(
                RuntimeSequence,
                2,
                L10n.T("intro.adjutant.line3", "Press the button in front of the alien to begin negotiations."));
        }

        public void Release()
        {
            if (RuntimeSequence == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(RuntimeSequence);
            RuntimeSequence = null;
        }

        private static void ApplyIntroSpeakerOverride(DialogueSequence sequence, string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
            {
                return;
            }

            ApplyIntroSpeakerOverride(sequence, 0, speaker);
            ApplyIntroSpeakerOverride(sequence, 1, speaker);
            ApplyIntroSpeakerOverride(sequence, 2, speaker);
        }

        private static void ApplyIntroSpeakerOverride(DialogueSequence sequence, int index, string speaker)
        {
            if (!TryGetDialogueLine(sequence, index, out DialogueLineData line))
            {
                return;
            }

            line.characterID = speaker;
        }

        private static void ApplyIntroLineOverride(DialogueSequence sequence, int index, string overrideText)
        {
            if (string.IsNullOrWhiteSpace(overrideText))
            {
                return;
            }

            if (!TryGetDialogueLine(sequence, index, out DialogueLineData line))
            {
                return;
            }

            line.text = overrideText;
        }

        private static bool TryGetDialogueLine(DialogueSequence sequence, int index, out DialogueLineData line)
        {
            line = null;
            if (sequence == null || sequence.lines == null || index < 0 || index >= sequence.lines.Count)
            {
                return false;
            }

            line = sequence.lines[index];
            return line != null;
        }
    }
}
