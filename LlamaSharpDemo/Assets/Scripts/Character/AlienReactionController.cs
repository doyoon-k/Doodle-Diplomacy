using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Localization;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Character
{
    [System.Serializable]
    public class ReactionAnimationBinding
    {
        public SatisfactionLevel level = SatisfactionLevel.Neutral;
        public AnimationClip clip;
        public string stateName;
    }

    public class AlienReactionController : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private List<ReactionAnimationBinding> reactionAnimations = new();
        [SerializeField] private int animatorLayerIndex = 0;
        [SerializeField] private float crossFadeDuration = 0.1f;
        [SerializeField] private string idleStateName = "Idle";

        [Header("Dialogue")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;

        [Header("Timing")]
        [SerializeField] private float lookAtMonitorDuration = 1f;
        [SerializeField] private float mutterDuration = 2f;
        [SerializeField] private float narratorLingerDuration = 2.5f;

        [Header("Events")]
        public UnityEvent OnReactionComplete = new();

        private Coroutine _reactionRoutine;
        private const string DefaultReactionSpeaker = "Alien";

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

        private void Awake()
        {
            if (targetAnimator == null)
            {
                targetAnimator = GetComponent<Animator>();
            }
        }

        private void Start()
        {
            PlayIdleIfAvailable();
        }

        public void PlayReaction(SatisfactionLevel satisfaction)
        {
            if (_reactionRoutine != null)
            {
                StopCoroutine(_reactionRoutine);
            }

            _reactionRoutine = StartCoroutine(ReactionRoutine(satisfaction, null, null, null));
        }

        public void PlayReaction(
            SatisfactionLevel satisfaction,
            string speakerOverride,
            string mutterOverride,
            string narrationOverride)
        {
            if (_reactionRoutine != null)
            {
                StopCoroutine(_reactionRoutine);
            }

            _reactionRoutine = StartCoroutine(ReactionRoutine(
                satisfaction,
                speakerOverride,
                mutterOverride,
                narrationOverride));
        }

        public void OnGameStateChanged(GameState state) { }

        private IEnumerator ReactionRoutine(
            SatisfactionLevel satisfaction,
            string speakerOverride,
            string mutterOverride,
            string narrationOverride)
        {
            float reactionStartedAt = Time.time;

            if (TryResolveBinding(satisfaction, out ReactionAnimationBinding binding, out bool usedNeutralFallback))
            {
                if (!TryPlayBinding(binding))
                {
                    if (!usedNeutralFallback &&
                        satisfaction != SatisfactionLevel.Neutral &&
                        TryResolveBinding(SatisfactionLevel.Neutral, out ReactionAnimationBinding neutralBinding, out _))
                    {
                        Debug.LogWarning(
                            $"[AlienReactionController] State '{binding.stateName}' is invalid for '{satisfaction}'. Falling back to Neutral.",
                            this);
                        binding = neutralBinding;
                        TryPlayBinding(binding);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[AlienReactionController] Could not play reaction state '{binding.stateName}' for '{satisfaction}'.",
                            this);
                    }
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[AlienReactionController] No animation binding configured for '{satisfaction}'.",
                    this);
            }

            float clipDuration = binding != null && binding.clip != null
                ? Mathf.Max(0f, binding.clip.length)
                : 0f;

            if (lookAtMonitorDuration > 0f)
            {
                yield return new WaitForSeconds(lookAtMonitorDuration);
            }

            string speaker = string.IsNullOrWhiteSpace(speakerOverride)
                ? GetReactionSpeaker()
                : speakerOverride.Trim();
            string mutterText = mutterOverride ?? GetMutterText(satisfaction);
            if (ShowSubtitleIfNotEmpty(speaker, mutterText))
            {
                yield return WaitForDialogueAdvance(mutterDuration);
            }
            else if (mutterDuration > 0f)
            {
                yield return new WaitForSeconds(mutterDuration);
            }

            string narrationText = narrationOverride ?? GetNarrationText(satisfaction);
            if (ShowSubtitleIfNotEmpty(speaker, narrationText))
            {
                yield return WaitForDialogueAdvance(narratorLingerDuration);
            }
            else if (narratorLingerDuration > 0f)
            {
                yield return new WaitForSeconds(narratorLingerDuration);
            }

            float elapsed = Time.time - reactionStartedAt;
            if (clipDuration > elapsed)
            {
                yield return new WaitForSeconds(clipDuration - elapsed);
            }

            PlayIdleIfAvailable();
            subtitleDisplay?.Hide();

            _reactionRoutine = null;
            OnReactionComplete?.Invoke();
        }

        private bool ShowSubtitleIfNotEmpty(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                subtitleDisplay?.Hide();
                return false;
            }

            subtitleDisplay?.Show(speaker, text);
            return true;
        }

        private IEnumerator WaitForDialogueAdvance(float minimumSeconds)
        {
            while (IsDialogueAdvancePressed())
            {
                yield return null;
            }

            float elapsed = 0f;
            float delay = Mathf.Max(0f, minimumSeconds);
            while (elapsed < delay)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            while (!GetDialogueAdvanceThisFrame())
            {
                yield return null;
            }
        }

        private static bool GetDialogueAdvanceThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool IsDialogueAdvancePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private bool TryResolveBinding(
            SatisfactionLevel level,
            out ReactionAnimationBinding binding,
            out bool usedNeutralFallback)
        {
            usedNeutralFallback = false;
            binding = FindBinding(level);

            if (binding != null)
            {
                return true;
            }

            if (level == SatisfactionLevel.Neutral)
            {
                return false;
            }

            ReactionAnimationBinding neutralBinding = FindBinding(SatisfactionLevel.Neutral);
            if (neutralBinding == null)
            {
                return false;
            }

            usedNeutralFallback = true;
            binding = neutralBinding;
            Debug.LogWarning(
                $"[AlienReactionController] Missing clip/state for '{level}'. Falling back to Neutral.",
                this);
            return true;
        }

        private void PlayIdleIfAvailable()
        {
            if (targetAnimator == null || string.IsNullOrWhiteSpace(idleStateName))
            {
                return;
            }

            int hash = Animator.StringToHash(idleStateName);
            if (!targetAnimator.HasState(animatorLayerIndex, hash))
            {
                return;
            }

            if (crossFadeDuration > 0f)
            {
                targetAnimator.CrossFadeInFixedTime(idleStateName, crossFadeDuration, animatorLayerIndex);
            }
            else
            {
                targetAnimator.Play(idleStateName, animatorLayerIndex, 0f);
            }
        }

        private ReactionAnimationBinding FindBinding(SatisfactionLevel level)
        {
            if (reactionAnimations == null)
            {
                return null;
            }

            for (int i = 0; i < reactionAnimations.Count; i++)
            {
                ReactionAnimationBinding candidate = reactionAnimations[i];
                if (candidate == null || candidate.level != level)
                {
                    continue;
                }

                if (candidate.clip == null || string.IsNullOrWhiteSpace(candidate.stateName))
                {
                    return null;
                }

                return candidate;
            }

            return null;
        }

        private bool TryPlayBinding(ReactionAnimationBinding binding)
        {
            if (targetAnimator == null || binding == null || string.IsNullOrWhiteSpace(binding.stateName))
            {
                return false;
            }

            if (!targetAnimator.HasState(animatorLayerIndex, Animator.StringToHash(binding.stateName)))
            {
                return false;
            }

            if (crossFadeDuration > 0f)
            {
                targetAnimator.CrossFadeInFixedTime(binding.stateName, crossFadeDuration, animatorLayerIndex);
            }
            else
            {
                targetAnimator.Play(binding.stateName, animatorLayerIndex, 0f);
            }

            return true;
        }

        private static string GetMappedText(Dictionary<SatisfactionLevel, string> map, SatisfactionLevel level)
        {
            if (map != null && map.TryGetValue(level, out string value))
            {
                return value;
            }

            return string.Empty;
        }

        private string GetReactionSpeaker()
        {
            return L10n.T("speaker.alien", DefaultReactionSpeaker);
        }

        private string GetMutterText(SatisfactionLevel level)
        {
            return L10n.T(GetMutterKey(level), GetMappedText(MutterMap, level));
        }

        private string GetNarrationText(SatisfactionLevel level)
        {
            return L10n.T(GetNarrationKey(level), GetMappedText(NarrationMap, level));
        }

        private static string GetMutterKey(SatisfactionLevel level)
        {
            return level switch
            {
                SatisfactionLevel.VeryDissatisfied => "alien.reaction.mutter.very_dissatisfied",
                SatisfactionLevel.Dissatisfied => "alien.reaction.mutter.dissatisfied",
                SatisfactionLevel.Neutral => "alien.reaction.mutter.neutral",
                SatisfactionLevel.Satisfied => "alien.reaction.mutter.satisfied",
                SatisfactionLevel.VerySatisfied => "alien.reaction.mutter.very_satisfied",
                _ => "alien.reaction.mutter.neutral"
            };
        }

        private static string GetNarrationKey(SatisfactionLevel level)
        {
            return level switch
            {
                SatisfactionLevel.VeryDissatisfied => "alien.reaction.narration.very_dissatisfied",
                SatisfactionLevel.Dissatisfied => "alien.reaction.narration.dissatisfied",
                SatisfactionLevel.Neutral => "alien.reaction.narration.neutral",
                SatisfactionLevel.Satisfied => "alien.reaction.narration.satisfied",
                SatisfactionLevel.VerySatisfied => "alien.reaction.narration.very_satisfied",
                _ => "alien.reaction.narration.neutral"
            };
        }

        [ContextMenu("Test: VeryDissatisfied")]
        private void TestVeryDissatisfied() =>
            PlayReaction(SatisfactionLevel.VeryDissatisfied);

        [ContextMenu("Test: Dissatisfied")]
        private void TestDissatisfied() =>
            PlayReaction(SatisfactionLevel.Dissatisfied);

        [ContextMenu("Test: Neutral")]
        private void TestNeutral() =>
            PlayReaction(SatisfactionLevel.Neutral);

        [ContextMenu("Test: Satisfied")]
        private void TestSatisfied() =>
            PlayReaction(SatisfactionLevel.Satisfied);

        [ContextMenu("Test: VerySatisfied")]
        private void TestVerySatisfied() =>
            PlayReaction(SatisfactionLevel.VerySatisfied);
    }
}
