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

        // ── 만족도 → 표정/대사 매핑 ──────────────────────────────────────────
        // 나중에 ScriptableObject(ReactionProfile)로 분리할 수 있게 딕셔너리로 관리

        private static readonly Dictionary<SatisfactionLevel, string> EmotionMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "angry"     },
            { SatisfactionLevel.Dissatisfied,     "angry"     },
            { SatisfactionLevel.Neutral,          "neutral"   },
            { SatisfactionLevel.Satisfied,        "satisfied" },
            { SatisfactionLevel.VerySatisfied,    "satisfied" },
        };

        private static readonly Dictionary<SatisfactionLevel, string> MutterMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "The aliens are conferring with each other..." },
            { SatisfactionLevel.Dissatisfied,     "The aliens exchange glances..." },
            { SatisfactionLevel.Neutral,          "The aliens observe quietly..." },
            { SatisfactionLevel.Satisfied,        "The aliens murmur amongst themselves..." },
            { SatisfactionLevel.VerySatisfied,    "The aliens react with visible excitement!" },
        };

        private static readonly Dictionary<SatisfactionLevel, string> NarrationMap = new()
        {
            { SatisfactionLevel.VeryDissatisfied, "They seem very displeased..." },
            { SatisfactionLevel.Dissatisfied,     "They don't appear to like it." },
            { SatisfactionLevel.Neutral,          "Not much of a reaction." },
            { SatisfactionLevel.Satisfied,        "A positive response." },
            { SatisfactionLevel.VerySatisfied,    "They seem quite impressed!" },
        };

        // ── 공개 API ──────────────────────────────────────────────────────────

        public void PlayReaction(SatisfactionLevel axis1, SatisfactionLevel axis2)
        {
            if (_reactionRoutine != null) StopCoroutine(_reactionRoutine);
            _reactionRoutine = StartCoroutine(ReactionRoutine(axis1, axis2));
        }

        /// <summary>
        /// RoundManager.OnStateChanged 이벤트에서 AlienReaction 진입 시 호출.
        /// axis 값은 AIPipelineBridge가 주입하기 전까지 Neutral 더미 사용.
        /// </summary>
        /// <summary>
        /// RoundManager.OnStateChanged 이벤트 수신용.
        /// 실제 axis 값은 RoundManager가 AIPipelineBridge 판정 결과를 받아
        /// PlayReaction()을 직접 호출한다 — 이 메서드는 비워둔다.
        /// </summary>
        public void OnGameStateChanged(GameState state) { }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private IEnumerator ReactionRoutine(SatisfactionLevel axis1, SatisfactionLevel axis2)
        {
            SatisfactionLevel combined = CombineAxes(axis1, axis2);

            // 1. 모니터 주시 — 전원 neutral 표정
            SetAllEmotions("neutral");
            yield return new WaitForSeconds(lookAtMonitorDuration);

            // 2. 웅성웅성 — 팔로워 표정 변화 + 나레이션 자막
            foreach (var follower in alienFollowerPortraits)
                follower?.SetEmotion(EmotionMap[combined]);

            subtitleDisplay?.Show("Adjutant", MutterMap[combined]);
            yield return new WaitForSeconds(mutterDuration);

            // 3. 리더 최종 표정 고정
            alienLeaderPortrait?.SetEmotion(EmotionMap[combined]);

            // 4. 부관 최종 판정 나레이션
            subtitleDisplay?.Show("Adjutant", NarrationMap[combined]);
            yield return new WaitForSeconds(narratorLingerDuration);

            subtitleDisplay?.Hide();

            // 5. 완료 이벤트
            _reactionRoutine = null;
            OnReactionComplete?.Invoke();
        }

        private static SatisfactionLevel CombineAxes(SatisfactionLevel axis1, SatisfactionLevel axis2)
        {
            float avg = ((int)axis1 + (int)axis2) * 0.5f;
            if (avg >=  1.5f) return SatisfactionLevel.VerySatisfied;
            if (avg >=  0.5f) return SatisfactionLevel.Satisfied;
            if (avg >= -0.5f) return SatisfactionLevel.Neutral;
            if (avg >= -1.5f) return SatisfactionLevel.Dissatisfied;
            return SatisfactionLevel.VeryDissatisfied;
        }

        private void SetAllEmotions(string emotionID)
        {
            alienLeaderPortrait?.SetEmotion(emotionID);
            foreach (var f in alienFollowerPortraits)
                f?.SetEmotion(emotionID);
        }

        // ── Inspector 컨텍스트 메뉴 테스트 ───────────────────────────────────

        [ContextMenu("Test: VeryDissatisfied")]
        private void TestVeryDissatisfied() =>
            PlayReaction(SatisfactionLevel.VeryDissatisfied, SatisfactionLevel.VeryDissatisfied);

        [ContextMenu("Test: Neutral")]
        private void TestNeutral() =>
            PlayReaction(SatisfactionLevel.Neutral, SatisfactionLevel.Neutral);

        [ContextMenu("Test: VerySatisfied")]
        private void TestVerySatisfied() =>
            PlayReaction(SatisfactionLevel.VerySatisfied, SatisfactionLevel.VerySatisfied);
    }
}
