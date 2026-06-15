using DoodleDiplomacy.Camera;

namespace DoodleDiplomacy.Core
{
    public static class RoundCameraModeResolver
    {
        public static CameraMode Resolve(GameState state)
        {
            return state switch
            {
                GameState.ObjectPresented => CameraMode.Default,
                GameState.Drawing => CameraMode.TabletView,
                GameState.PreviewReady => CameraMode.FreeLook,
                GameState.PreviewAnalyzing => CameraMode.AlienReaction,
                GameState.Preview => CameraMode.FreeLook,
                GameState.AlienReaction => CameraMode.AlienReaction,
                GameState.InterpreterReady => CameraMode.FreeLook,
                GameState.Interpreter => CameraMode.TerminalZoom,
                _ => CameraMode.Default
            };
        }
    }
}
