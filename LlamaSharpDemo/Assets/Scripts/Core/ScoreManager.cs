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
                Instance = null;
        }

        /// <summary>
        /// 현재 라운드의 만족도 두 축을 받아 점수를 계산하고 기록.
        /// 마지막 라운드는 ScoreConfig.lastRoundMultiplier 가중치 적용.
        /// 기록 후 currentRound 증가.
        /// </summary>
        public void RecordRound(SatisfactionLevel axis1, SatisfactionLevel axis2)
        {
            if (config == null)
            {
                Debug.LogError("[ScoreManager] ScoreConfig가 할당되지 않았습니다.");
                return;
            }

            int baseScore = (int)axis1 + (int)axis2;

            bool isLastRound = _currentRound == config.totalRounds - 1;
            int roundScore = isLastRound
                ? Mathf.RoundToInt(baseScore * config.lastRoundMultiplier)
                : baseScore;

            _roundScores.Add(roundScore);
            _totalScore += roundScore;
            _currentRound++;

            Debug.Log($"[ScoreManager] 라운드 {_currentRound} 완료 — " +
                      $"기본점수: {baseScore}, 라운드점수: {roundScore}" +
                      (isLastRound ? $" (가중치 ×{config.lastRoundMultiplier})" : "") +
                      $", 누적: {_totalScore}");

            OnScoreUpdated?.Invoke(_totalScore);

            if (_currentRound >= config.totalRounds)
            {
                EndingType ending = GetEndingType();
                Debug.Log($"[ScoreManager] 게임 종료 — 결말: {ending} (총점: {_totalScore})");
                OnEndingDetermined?.Invoke(ending);
            }
        }

        public int GetCurrentRound() => _currentRound;

        public int GetTotalScore() => _totalScore;

        public EndingType GetEndingType()
        {
            if (config == null)
            {
                Debug.LogError("[ScoreManager] ScoreConfig가 할당되지 않았습니다.");
                return EndingType.Diplomacy;
            }
            return config.EvaluateEnding(_totalScore);
        }

        public void Reset()
        {
            _currentRound = 0;
            _roundScores.Clear();
            _totalScore = 0;
            Debug.Log("[ScoreManager] 초기화 완료.");
        }
    }
}
