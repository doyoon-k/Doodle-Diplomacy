using System;
using DoodleDiplomacy.Data;

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
    }
}
