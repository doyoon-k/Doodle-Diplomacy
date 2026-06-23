using DoodleDiplomacy.Camera;

namespace DoodleDiplomacy.Gameplay
{
    public interface ICameraModeService
    {
        CameraMode CurrentMode { get; }
        bool IsTransitioning { get; }
        void SetMode(CameraMode mode);
        bool HasValidPreset(CameraMode mode);
    }
}
