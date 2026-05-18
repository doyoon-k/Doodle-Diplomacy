using System;

namespace DoodleDiplomacy.Core
{
    public interface IRoundAiGateway
    {
        bool IsAvailable { get; }
        bool HasObjectGenerationPreparationFailed { get; }
        bool IsRoundStartReady { get; }
        bool IsNextRoundPrefetchRunning { get; }
        bool IsNextRoundPrefetchReady { get; }
        bool HasNextRoundPrefetchFailed { get; }
        string NextRoundPrefetchError { get; }
        bool IsLlmPreparationReady { get; }
        bool HasLlmPreparationFailed { get; }
        bool IsLlmPreparationRunning { get; }
        string LastLlmPreparationError { get; }
        string LastObjectGenerationError { get; }
        bool HasTelepathyResult { get; }
        string LastTelepathy { get; }

        event Action<bool> RoundStartReadinessChanged;

        void EnsureObjectGenerationPreparation(bool forceRetry = false);
        void EnsureLlmPreparation(bool forceRetry = false);
        void RefreshLlmPreparationStatus();
        void StartNextRoundPrefetch();
        bool TryAdoptPrefetchedRound();
        void PrepareRoundKeywords(bool forceRefresh = true);
        string GetRoundStartAvailabilityMessage();
        void GenerateObjects(Action<bool> onComplete);
        void GetPreview(Action<string> onComplete);
        void GetJudgment(Action<SatisfactionLevel> onComplete);
        void ClassifyVisualStimulus(Action<VisualStimulusClassificationResult> onComplete);
        void EvaluateDay1ReactionTier(string label, Action<Day1ReactionEvaluationResult> onComplete);
        void CancelActiveOperations();
        void ResetRound();
    }
}
