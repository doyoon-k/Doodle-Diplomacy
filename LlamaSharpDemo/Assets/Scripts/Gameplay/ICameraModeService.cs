using DoodleDiplomacy.Camera;

namespace DoodleDiplomacy.Gameplay
{
    public interface ICameraModeService
    {
        CameraMode CurrentMode { get; }
        void SetMode(CameraMode mode);
        bool HasValidPreset(CameraMode mode);
    }
}