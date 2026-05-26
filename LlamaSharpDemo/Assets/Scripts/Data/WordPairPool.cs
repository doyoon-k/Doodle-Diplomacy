using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [System.Serializable]
    public struct WordPair
    {
        [Tooltip("Specific object prompt for Stable Diffusion image A.")]
        /// <summary>SD 이미지 생성용 구체적 묘사 (예: "white dog bone")</summary>
        public string wordA;
        [Tooltip("Specific object prompt for Stable Diffusion image B.")]
        /// <summary>SD 이미지 생성용 구체적 묘사 (예: "erupting volcano")</summary>
        public string wordB;
        [Tooltip("Simple display/LLM label for object A. Falls back to Word A when empty.")]
        /// <summary>LLM/UI 표시용 단순 명사 (예: "bone"). 비어있으면 wordA로 폴백.</summary>
        public string labelA;
        [Tooltip("Simple display/LLM label for object B. Falls back to Word B when empty.")]
        /// <summary>LLM/UI 표시용 단순 명사 (예: "volcano"). 비어있으면 wordB로 폴백.</summary>
        public string labelB;
    }

    [CreateAssetMenu(fileName = "WordPairPool", menuName = "DoodleDiplomacy/Word Pair Pool")]
    public class WordPairPool : ScriptableObject
    {
        [Tooltip("Prompt/label pairs available for object-pair drawing rounds.")]
        [SerializeField] private WordPair[] pairs;

        private readonly List<int> _remaining = new();
        public int PairCount => pairs?.Length ?? 0;

        /// <summary>
        /// Returns a random pair without repeating until all pairs have been drawn.
        /// wordA/wordB are the SD image generation prompts.
        /// labelA/labelB are the simple noun labels for LLM and UI display.
        /// Returns false only if the pool is empty.
        /// </summary>
        public bool TryGetRandomPair(
            out string wordA, out string wordB,
            out string labelA, out string labelB)
        {
            if (pairs == null || pairs.Length == 0)
            {
                wordA = string.Empty;
                wordB = string.Empty;
                labelA = string.Empty;
                labelB = string.Empty;
                return false;
            }

            if (_remaining.Count == 0)
                RefillPool();

            int idx = Random.Range(0, _remaining.Count);
            int pairIndex = _remaining[idx];
            _remaining.RemoveAt(idx);

            WordPair pair = pairs[pairIndex];
            wordA = pair.wordA;
            wordB = pair.wordB;
            labelA = string.IsNullOrWhiteSpace(pair.labelA) ? pair.wordA : pair.labelA;
            labelB = string.IsNullOrWhiteSpace(pair.labelB) ? pair.wordB : pair.labelB;
            return true;
        }

        /// <summary>
        /// Resets the draw history so all pairs become available again.
        /// Called automatically when the pool is exhausted.
        /// </summary>
        public void ResetPool()
        {
            _remaining.Clear();
        }

        public bool TryGetPairAt(
            int index,
            out string wordA,
            out string wordB,
            out string labelA,
            out string labelB)
        {
            if (pairs == null || pairs.Length == 0)
            {
                wordA = string.Empty;
                wordB = string.Empty;
                labelA = string.Empty;
                labelB = string.Empty;
                return false;
            }

            if (index < 0 || index >= pairs.Length)
            {
                wordA = string.Empty;
                wordB = string.Empty;
                labelA = string.Empty;
                labelB = string.Empty;
                return false;
            }

            WordPair pair = pairs[index];
            wordA = pair.wordA?.Trim() ?? string.Empty;
            wordB = pair.wordB?.Trim() ?? string.Empty;
            labelA = string.IsNullOrWhiteSpace(pair.labelA) ? wordA : pair.labelA.Trim();
            labelB = string.IsNullOrWhiteSpace(pair.labelB) ? wordB : pair.labelB.Trim();
            return true;
        }

        private void RefillPool()
        {
            _remaining.Clear();
            for (int i = 0; i < pairs.Length; i++)
                _remaining.Add(i);
        }
    }
}
