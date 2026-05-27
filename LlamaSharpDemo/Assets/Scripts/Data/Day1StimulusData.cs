using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [Serializable]
    public sealed class Day1StimulusRecord
    {
        [Tooltip("1-based Day1 calibration slot number.")]
        public int slot;
        [Tooltip("Normalized label identified from the approved player drawing.")]
        public string label;
        [Tooltip("Absolute path to the saved PNG for this approved drawing.")]
        public string imagePath;
        [Tooltip("Stable key used to reference this drawing as a future sticker/memory item.")]
        public string stickerKey;
        [Tooltip("Alien reaction tier captured for this approved drawing.")]
        public ReactionTier reactionTier;
    }

    [Serializable]
    public sealed class Day1ProfileStimulus
    {
        [Tooltip("1-based Day1 calibration slot number.")]
        public int slot;
        [Tooltip("Normalized label identified from the approved player drawing.")]
        public string label;
        [Tooltip("Alien reaction tier captured for this approved drawing.")]
        public ReactionTier reactionTier;
    }

    [Serializable]
    public sealed class Day1ProfilePayload
    {
        [Tooltip("Approved Day1 stimuli written into the generated profile payload.")]
        public List<Day1ProfileStimulus> stimuli = new();
    }
}
