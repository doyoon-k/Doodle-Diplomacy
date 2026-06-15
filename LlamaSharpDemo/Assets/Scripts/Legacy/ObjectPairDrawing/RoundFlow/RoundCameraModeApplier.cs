using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Gameplay;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundCameraModeApplier
    {
        private readonly ICameraModeService _camera;

        public RoundCameraModeApplier(ICameraModeService camera)
        {
            _camera = camera;
        }

        public void Apply(GameState state)
        {
            _camera?.SetMode(RoundCameraModeResolver.Resolve(state));
        }

        public void Apply(CameraMode mode)
        {
            _camera?.SetMode(mode);
        }

        public bool TryApplySharedMonitorZoom(Object logContext)
        {
            if (_camera == null)
            {
                return false;
            }

            if (!_camera.HasValidPreset(CameraMode.SharedMonitorZoom))
            {
                Debug.LogWarning("[RoundCameraModeApplier] Shared monitor zoom preset is not configured on CameraController.", logContext);
                return false;
            }

            _camera.SetMode(CameraMode.SharedMonitorZoom);
            return true;
        }
    }
}
