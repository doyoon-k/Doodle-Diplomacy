using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Dialogue;

namespace DoodleDiplomacy.Character
{
    public class AlienReactionController : MonoBehaviour
    {
        [Header("Characters")]
        [SerializeField] private PortraitDisplay alienLeaderPortrait;
        [SerializeField] private List<PortraitDisplay> alienFollowerPortraits = new();

        [Header("Dialogue")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;

        [Header("Timing")]
        [SerializeField] private float lookAtMonitorDuration = 1f;
        [SerializeField] private float mutterDuration = 2f;
        [SerializeField] private float narratorLingerDuration = 2.5f;

        [Header("Events")]
        public UnityEvent OnReactionComplete = new();

        private Coroutine _reactionRoutine;

        private static readonly Dictionary<SatisfactionLevel, string> EmotionMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "angry" },
            { SatisfactionLevel.Dissatisfied, "angry" },
            { SatisfactionLevel.Neutral, "neutral" },
            { SatisfactionLevel.Satisfied, "satisfied" },
            { SatisfactionLevel.VerySatisfied, "satisfied" },
        };

        private static readonly Dictionary<SatisfactionLevel, string> MutterMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "The aliens are conferring with each other..." },
            { SatisfactionLevel.Dissatisfied, "The aliens exchange glances..." },
            { SatisfactionLevel.Neutral, "The aliens observe quietly..." },
            { SatisfactionLevel.Satisfied, "The aliens murmur amongst themselves..." },
            { SatisfactionLevel.VerySatisfied, "The aliens react with visible excitement!" },
        };

        private static readonly Dictionary<SatisfactionLevel, string> NarrationMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "They seem very displeased..." },
            { SatisfactionLevel.Dissatisfied, "They don't appear to like it." },
            { SatisfactionLevel.Neutral, "Not much of a reaction." },
            { SatisfactionLevel.Satisfied, "A positive response." },
            { SatisfactionLevel.VerySatisfied, "They seem quite impressed!" },
        };

        public void PlayReaction(SatisfactionLevel satisfaction)
        {
            if (_reactionRoutine != null)
            {
                StopCoroutine(_reactionRoutine);
            }

            _reactionRoutine = StartCoroutine(ReactionRoutine(satisfaction));
        }

        public void OnGameStateChanged(GameState state) { }

        private IEnumerator ReactionRoutine(SatisfactionLevel satisfaction)
        {
            SetAllEmotions("neutral");
            yield return new WaitForSeconds(lookAtMonitorDuration);

            foreach (PortraitDisplay follower in alienFollowerPortraits)
            {
                follower?.SetEmotion(EmotionMap[satisfaction]);
            }

            subtitleDisplay?.Show("Adjutant", MutterMap[satisfaction]);
            yield return new WaitForSeconds(mutterDuration);

            alienLeaderPortrait?.SetEmotion(EmotionMap[satisfaction]);

            subtitleDisplay?.Show("Adjutant", NarrationMap[satisfaction]);
            yield return new WaitForSeconds(narratorLingerDuration);

            subtitleDisplay?.Hide();

            _reactionRoutine = null;
            OnReactionComplete?.Invoke();
        }

        private void SetAllEmotions(string emotionId)
        {
            alienLeaderPortrait?.SetEmotion(emotionId);
            foreach (PortraitDisplay follower in alienFollowerPortraits)
            {
                follower?.SetEmotion(emotionId);
            }
        }

        [ContextMenu("Test: VeryDissatisfied")]
        private void TestVeryDissatisfied() =>
            PlayReaction(SatisfactionLevel.VeryDissatisfied);

        [ContextMenu("Test: Neutral")]
        private void TestNeutral() =>
            PlayReaction(SatisfactionLevel.Neutral);

        [ContextMenu("Test: VerySatisfied")]
        private void TestVerySatisfied() =>
            PlayReaction(SatisfactionLevel.VerySatisfied);
    }
}
