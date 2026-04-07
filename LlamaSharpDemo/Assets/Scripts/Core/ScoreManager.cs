using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public class IntUnityEvent : UnityEvent<int> { }

    [Serializable]
    public class EndingTypeUnityEvent : UnityEvent<EndingType> { }

    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        [SerializeField] private ScoreConfig config;

        [Header("Events")]
        public IntUnityEvent OnScoreUpdated;
        public EndingTypeUnityEvent OnEndingDetermined;

        private int _currentRound;
        private readonly List<int> _roundScores = new List<int>();
        private int _totalScore;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Records one round using the alien's single satisfaction score.
        /// The last round applies ScoreConfig.lastRoundMultiplier before storing the value.
        /// </summary>
        public void RecordRound(SatisfactionLevel satisfaction)
        {
            if (config == null)
            {
                Debug.LogError("[ScoreManager] ScoreConfig is not assigned.");
                return;
            }

            int baseScore = (int)satisfaction;
            bool isLastRound = _currentRound == config.totalRounds - 1;
            int roundScore = isLastRound
                ? Mathf.RoundToInt(baseScore * config.lastRoundMultiplier)
                : baseScore;

            _roundScores.Add(roundScore);
            _totalScore += roundScore;
            _currentRound++;

            Debug.Log($"[ScoreManager] Round {_currentRound} recorded. " +
                      $"satisfaction={satisfaction}, baseScore={baseScore}, roundScore={roundScore}" +
                      (isLastRound ? $" (x{config.lastRoundMultiplier})" : string.Empty) +
                      $", total={_totalScore}");

            OnScoreUpdated?.Invoke(_totalScore);

            if (_currentRound >= config.totalRounds)
            {
                EndingType ending = GetEndingType();
                Debug.Log($"[ScoreManager] Game finished with ending {ending} (totalScore={_totalScore}).");
                OnEndingDetermined?.Invoke(ending);
            }
        }

        public int GetCurrentRound() => _currentRound;

        public int GetTotalScore() => _totalScore;

        public EndingType GetEndingType()
        {
            if (config == null)
            {
                Debug.LogError("[ScoreManager] ScoreConfig is not assigned.");
                return EndingType.Diplomacy;
            }

            return config.EvaluateEnding(_totalScore);
        }

        public void Reset()
        {
            _currentRound = 0;
            _roundScores.Clear();
            _totalScore = 0;
            Debug.Log("[ScoreManager] Score state reset.");
        }
    }
}
