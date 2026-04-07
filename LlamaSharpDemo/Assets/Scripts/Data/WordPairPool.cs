using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [System.Serializable]
    public struct WordPair
    {
        public string wordA;
        public string wordB;
    }

    [CreateAssetMenu(fileName = "WordPairPool", menuName = "DoodleDiplomacy/Word Pair Pool")]
    public class WordPairPool : ScriptableObject
    {
        [SerializeField] private WordPair[] pairs;

        private readonly List<int> _remaining = new();

        /// <summary>
        /// Returns a random pair without repeating until all pairs have been drawn.
        /// Returns false only if the pool is empty.
        /// </summary>
        public bool TryGetRandomPair(out string wordA, out string wordB)
        {
            if (pairs == null || pairs.Length == 0)
            {
                wordA = string.Empty;
                wordB = string.Empty;
                return false;
            }

            if (_remaining.Count == 0)
                RefillPool();

            int idx = Random.Range(0, _remaining.Count);
            int pairIndex = _remaining[idx];
            _remaining.RemoveAt(idx);

            wordA = pairs[pairIndex].wordA;
            wordB = pairs[pairIndex].wordB;
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

        private void RefillPool()
        {
            _remaining.Clear();
            for (int i = 0; i < pairs.Length; i++)
                _remaining.Add(i);
        }
    }
}
