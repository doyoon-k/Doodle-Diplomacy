using UnityEngine;

namespace DoodleDiplomacy.Camera
{
    public sealed class CameraEdgeBrowseController
    {
        private float _edgeBrowseThresholdNormalized = 0.08f;
        private float _edgeBrowseYawSpeed = 55f;
        private float _maxBrowseYaw = 65f;
        private float _browseYaw;

        public float BrowseYaw => _browseYaw;

        public void Configure(float thresholdNormalized, float yawSpeed, float maxYaw)
        {
            _edgeBrowseThresholdNormalized = thresholdNormalized;
            _edgeBrowseYawSpeed = yawSpeed;
            _maxBrowseYaw = maxYaw;
        }

        public bool TryApplyBrowse(Vector2 pointerNormalized, float deltaTime)
        {
            float edgeIntent = EvaluateEdgeIntent(pointerNormalized.x, _edgeBrowseThresholdNormalized);
            if (Mathf.Approximately(edgeIntent, 0f))
            {
                return false;
            }

            _browseYaw += edgeIntent * _edgeBrowseYawSpeed * Mathf.Max(0f, deltaTime);
            _browseYaw = Mathf.Clamp(_browseYaw, -Mathf.Abs(_maxBrowseYaw), Mathf.Abs(_maxBrowseYaw));
            return true;
        }

        public void Reset()
        {
            _browseYaw = 0f;
        }

        private static float EvaluateEdgeIntent(float normalizedX, float thresholdNormalized)
        {
            float edgeThreshold = Mathf.Clamp(thresholdNormalized, 0.01f, 0.45f);
            if (normalizedX <= edgeThreshold)
            {
                return -1f + Mathf.InverseLerp(0f, edgeThreshold, normalizedX);
            }

            if (normalizedX >= 1f - edgeThreshold)
            {
                return Mathf.InverseLerp(1f - edgeThreshold, 1f, normalizedX);
            }

            return 0f;
        }
    }
}
