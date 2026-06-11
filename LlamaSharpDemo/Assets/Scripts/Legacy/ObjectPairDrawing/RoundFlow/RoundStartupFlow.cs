using System.Collections;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundStartupFlow
    {
        private readonly IRoundStartupContext _context;
        private Coroutine _warmupRoutine;

        internal RoundStartupFlow(IRoundStartupContext context)
        {
            _context = context;
        }

        public void StartGame(bool isFirstPlay)
        {
            Stop();
            CancelActiveAiOperations();

            _context.CurrentRound = 0;
            _context.LastSatisfaction = SatisfactionLevel.Neutral;
            _context.PreserveRoundIndexOnNextWaitingState = false;
            _context.HasOpenedInterpreterThisRound = false;
            _context.ResetPreviewInspectionState();

            _context.ScoreManager?.Reset();
            _context.AiGateway?.ResetRound();
            _context.ResetTelepathyState();

            // Enter gameplay immediately so Intro/Waiting UI is not blocked by AI warm-up.
            _context.ChangeStateFromStartup(isFirstPlay ? GameState.Intro : GameState.WaitingForRound);

            if (_context.AiGateway == null || !_context.AiGateway.IsAvailable)
            {
                return;
            }

            _warmupRoutine = _context.StartStartupCoroutine(StartGameWarmupRoutine());
        }

        public void ChangeToTitle()
        {
            Stop();
            CancelActiveAiOperations();
            _context.ResetTelepathyState();
            _context.ResetPreviewInspectionState();
            _context.HasOpenedInterpreterThisRound = false;
            _context.ChangeStateFromStartup(GameState.Title);
        }

        public void Stop()
        {
            if (_warmupRoutine == null)
            {
                return;
            }

            _context.StopStartupCoroutine(_warmupRoutine);
            _warmupRoutine = null;
        }

        private IEnumerator StartGameWarmupRoutine()
        {
            try
            {
                IRoundAiGateway aiGateway = _context.AiGateway;
                if (aiGateway == null || !aiGateway.IsAvailable)
                {
                    yield break;
                }

                Debug.Log("[RoundManager] Preparing first-round objects and LLM runtime before starting gameplay.");
                aiGateway.EnsureObjectGenerationPreparation(
                    forceRetry: aiGateway.HasObjectGenerationPreparationFailed);
                aiGateway.EnsureLlmPreparation(forceRetry: aiGateway.HasLlmPreparationFailed);
                aiGateway.StartNextRoundPrefetch();

                float timeout = Mathf.Max(0f, _context.FirstRoundPrefetchTimeoutSeconds);
                float elapsed = 0f;
                while (aiGateway.IsNextRoundPrefetchRunning)
                {
                    aiGateway.RefreshLlmPreparationStatus();
                    if (timeout > 0f && elapsed >= timeout)
                    {
                        break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                aiGateway.RefreshLlmPreparationStatus();

                if (aiGateway.IsNextRoundPrefetchReady)
                {
                    Debug.Log("[RoundManager] First-round prefetch finished. Starting gameplay.");
                }
                else if (aiGateway.HasNextRoundPrefetchFailed)
                {
                    Debug.LogWarning(
                        $"[RoundManager] First-round prefetch failed before start: {aiGateway.NextRoundPrefetchError}. " +
                        "Will fall back to normal presenting generation.");
                }
                else if (aiGateway.IsNextRoundPrefetchRunning)
                {
                    Debug.LogWarning(
                        $"[RoundManager] First-round prefetch timed out after {timeout:0.##}s. " +
                        "Will fall back to normal presenting generation.");
                }

                if (aiGateway.IsLlmPreparationReady)
                {
                    Debug.Log("[RoundManager] LLM preload finished. Starting gameplay.");
                }
                else if (aiGateway.HasLlmPreparationFailed)
                {
                    Debug.LogWarning(
                        $"[RoundManager] LLM preload failed before start: {aiGateway.LastLlmPreparationError}. " +
                        "Gameplay will continue and LLM calls may fall back or stall.");
                }
                else if (aiGateway.IsLlmPreparationRunning)
                {
                    Debug.Log(
                        "[RoundManager] LLM preload is still running in background. " +
                        "The first LLM call may still warm up.");
                }
            }
            finally
            {
                _warmupRoutine = null;
            }
        }

        private void CancelActiveAiOperations()
        {
            _context.AiGateway?.CancelActiveOperations();
        }
    }
}
