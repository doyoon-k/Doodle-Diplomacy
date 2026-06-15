using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Core
{
    internal interface IRoundPlayerActionContext
    {
        GameState CurrentState { get; }
        IRoundAiGateway AiGateway { get; }
        ScoreConfig ScoreConfig { get; }
        int CurrentRound { get; }
        bool HasOpenedInterpreterThisRound { get; }
        bool IsPreviewTerminalOpen { get; set; }
        bool IsSharedMonitorZoomActive { get; }
        float InterpreterOpenedAt { get; set; }

        bool TryConsumeSharedMonitorClick();
        bool CanUseSharedMonitorZoom();
        void EnterSharedMonitorZoom();
        void ExitSharedMonitorZoom();
        void ChangeStateFromPlayerAction(GameState state);
        void ApplyInteractionPolicyForPlayerAction();
        void ApplyCameraModeForPlayerAction(GameState state);
        void ApplyCameraModeForPlayerAction(CameraMode mode);
        void ResetCachedInterpretationForRedraw();
        void ShowPreviewTerminal();
        void OnInterpreterClose();
        void ShowHint(string speaker, string text);
    }
}
