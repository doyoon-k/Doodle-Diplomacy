using System;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Dialogue;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundIntroSequenceProvider
    {
        private readonly Func<DialogueSequence> _sourceSequenceProvider;
        private readonly Func<IngameTextTable> _textTableProvider;

        public DialogueSequence RuntimeSequence { get; private set; }

        internal RoundIntroSequenceProvider(
            Func<DialogueSequence> sourceSequenceProvider,
            Func<IngameTextTable> textTableProvider)
        {
            _sourceSequenceProvider = sourceSequenceProvider;
            _textTableProvider = textTableProvider;
        }

        public void Rebuild()
        {
            Release();

            DialogueSequence sourceSequence = _sourceSequenceProvider?.Invoke();
            if (sourceSequence == null)
            {
                return;
            }

            IngameTextTable table = _textTableProvider?.Invoke();
            if (table == null)
            {
                return;
            }

            RuntimeSequence = UnityEngine.Object.Instantiate(sourceSequence);
            RuntimeSequence.name = $"{sourceSequence.name} (Runtime)";

            ApplyIntroSpeakerOverride(RuntimeSequence, table.introAdjutantSpeaker);
            ApplyIntroLineOverride(RuntimeSequence, 0, table.introAdjutantLine1);
            ApplyIntroLineOverride(RuntimeSequence, 1, table.introAdjutantLine2);
            ApplyIntroLineOverride(RuntimeSequence, 2, table.introAdjutantLine3);
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
