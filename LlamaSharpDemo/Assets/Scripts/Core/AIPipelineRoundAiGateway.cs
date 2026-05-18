using System;
using DoodleDiplomacy.AI;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public sealed class AIPipelineRoundAiGateway : IRoundAiGateway
    {
        private readonly AIPipelineBridge _bridge;

        public AIPipelineRoundAiGateway(AIPipelineBridge bridge)
        {
            _bridge = bridge;
        }

        public bool IsAvailable => _bridge != null;
        public bool HasObjectGenerationPreparationFailed => _bridge != null && _bridge.HasObjectGenerationPreparationFailed;
        public bool IsRoundStartReady => _bridge != null && _bridge.IsRoundStartReady;
        public bool IsNextRoundPrefetchRunning => _bridge != null && _bridge.IsNextRoundPrefetchRunning;
        public bool IsNextRoundPrefetchReady => _bridge != null && _bridge.IsNextRoundPrefetchReady;
        public bool HasNextRoundPrefetchFailed => _bridge != null && _bridge.HasNextRoundPrefetchFailed;
        public string NextRoundPrefetchError => _bridge != null ? _bridge.NextRoundPrefetchError : string.Empty;
        public bool IsLlmPreparationReady => _bridge != null && _bridge.IsLlmPreparationReady;
        public bool HasLlmPreparationFailed => _bridge != null && _bridge.HasLlmPreparationFailed;
        public bool IsLlmPreparationRunning => _bridge != null && _bridge.IsLlmPreparationRunning;
        public string LastLlmPreparationError => _bridge != null ? _bridge.LastLlmPreparationError : string.Empty;
        public string LastObjectGenerationError => _bridge != null ? _bridge.LastObjectGenerationError : string.Empty;
        public bool HasTelepathyResult => _bridge != null && _bridge.HasTelepathyResult;
        public string LastTelepathy => _bridge != null ? _bridge.LastTelepathy : string.Empty;

        public event Action<bool> RoundStartReadinessChanged
        {
            add
            {
                if (_bridge != null)
                {
                    _bridge.RoundStartReadinessChanged += value;
                }
            }
            remove
            {
                if (_bridge != null)
                {
                    _bridge.RoundStartReadinessChanged -= value;
                }
            }
        }

        public void EnsureObjectGenerationPreparation(bool forceRetry = false)
        {
            _bridge?.EnsureObjectGenerationPreparation(forceRetry);
        }

        public void EnsureLlmPreparation(bool forceRetry = false)
        {
            _bridge?.EnsureLlmPreparation(forceRetry);
        }

        public void RefreshLlmPreparationStatus()
        {
            _bridge?.RefreshLlmPreparationStatus();
        }

        public void StartNextRoundPrefetch()
        {
            _bridge?.StartNextRoundPrefetch();
        }

        public bool TryAdoptPrefetchedRound()
        {
            return _bridge != null && _bridge.TryAdoptPrefetchedRound();
        }

        public void PrepareRoundKeywords(bool forceRefresh = true)
        {
            _bridge?.PrepareRoundKeywords(forceRefresh);
        }

        public string GetRoundStartAvailabilityMessage()
        {
            return _bridge != null
                ? _bridge.GetRoundStartAvailabilityMessage()
                : string.Empty;
        }

        public void GenerateObjects(Action<bool> onComplete)
        {
            if (_bridge == null)
            {
                onComplete?.Invoke(false);
                return;
            }

            _bridge.GenerateObjects(onComplete);
        }

        public void GetPreview(Action<string> onComplete)
        {
            if (_bridge == null)
            {
                onComplete?.Invoke(string.Empty);
                return;
            }

            _bridge.GetPreview(onComplete);
        }

        public void GetJudgment(Action<SatisfactionLevel> onComplete)
        {
            if (_bridge == null)
            {
                onComplete?.Invoke(SatisfactionLevel.Neutral);
                return;
            }

            _bridge.GetJudgment(onComplete);
        }

        public void ClassifyVisualStimulus(Action<VisualStimulusClassificationResult> onComplete)
        {
            if (_bridge == null)
            {
                onComplete?.Invoke(VisualStimulusClassificationResult.Failed("AI bridge is missing."));
                return;
            }

            _bridge.ClassifyVisualStimulus(onComplete);
        }

        public void EvaluateDay1ReactionTier(string label, Action<Day1ReactionEvaluationResult> onComplete)
        {
            if (_bridge == null)
            {
                onComplete?.Invoke(Day1ReactionEvaluationResult.Failed("AI bridge is missing."));
                return;
            }

            _bridge.EvaluateDay1ReactionTier(label, onComplete);
        }

        public void CancelActiveOperations()
        {
            _bridge?.CancelActiveOperations();
        }

        public void ResetRound()
        {
            _bridge?.ResetRound();
        }
    }
}
