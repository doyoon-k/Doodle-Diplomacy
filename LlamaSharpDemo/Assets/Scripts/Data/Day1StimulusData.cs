using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Data
{
    [Serializable]
    public sealed class Day1StimulusRecord
    {
        public int slot;
        public string label;
        public string imagePath;
        public string stickerKey;
        public ReactionTier reactionTier;
    }

    [Serializable]
    public sealed class Day1ProfileStimulus
    {
        public int slot;
        public string label;
        public ReactionTier reactionTier;
    }

    [Serializable]
    public sealed class Day1ProfilePayload
    {
        public List<Day1ProfileStimulus> stimuli = new();
    }
}
