using System.Collections;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    internal interface IRoundStartupContext
    {
        IRoundAiGateway AiGateway { get; }
        ScoreManager ScoreManager { get; }
        float FirstRoundPrefetchTimeoutSeconds { get; }

        int CurrentRound { get; set; }
        SatisfactionLevel LastSatisfaction { get; set; }
        bool PreserveRoundIndexOnNextWaitingState { get; set; }
        bool HasOpenedInterpreterThisRound { get; set; }

        Coroutine StartStartupCoroutine(IEnumerator routine);
        void StopStartupCoroutine(Coroutine routine);
        void ResetPreviewInspectionState();
        void ResetTelepathyState(bool clearCachedText = true);
        void ChangeStateFromStartup(GameState state);
    }
}
