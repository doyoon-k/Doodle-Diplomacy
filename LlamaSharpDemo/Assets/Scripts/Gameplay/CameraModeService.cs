using DoodleDiplomacy.Camera;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class CameraModeService : ICameraModeService
    {
        private readonly CameraController _cameraController;

        public CameraModeService(CameraController cameraController)
        {
            _cameraController = cameraController;
        }

        public CameraMode CurrentMode => _cameraController != null ? _cameraController.CurrentMode : CameraMode.Default;
        public bool IsTransitioning => _cameraController != null && _cameraController.IsTransitioning;
        public void SetMode(CameraMode mode) => _cameraController?.SetMode(mode);
        public bool HasValidPreset(CameraMode mode) => _cameraController != null && _cameraController.HasValidPreset(mode);
    }
}
