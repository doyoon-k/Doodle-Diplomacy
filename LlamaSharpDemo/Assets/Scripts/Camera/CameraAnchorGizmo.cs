using UnityEngine;

namespace DoodleDiplomacy.Camera
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class CameraAnchorGizmo : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private Color arrowColor = new(0.15f, 0.95f, 1f, 0.95f);

        [Header("Shape")]
        [SerializeField, Min(0.05f)] private float arrowLength = 0.6f;
        [SerializeField, Min(0.02f)] private float arrowHeadLength = 0.16f;
        [SerializeField, Range(5f, 75f)] private float arrowHeadAngle = 28f;
        [SerializeField, Min(0.005f)] private float originSphereRadius = 0.035f;

        [Header("Frustum Preview")]
        [SerializeField] private bool drawFrustum = true;
        [SerializeField] private bool drawFrustumOnlyWhenSelected = true;
        [SerializeField] private bool useMainCameraSettings = true;
        [SerializeField] private UnityEngine.Camera sourceCamera;
        [SerializeField, Min(0.2f)] private float previewFarLimit = 8f;
        [SerializeField] private Color frustumColor = new(1f, 0.75f, 0.15f, 0.9f);

        [Header("Manual Frustum (when Use Main Camera Settings is off)")]
        [SerializeField] private bool manualOrthographic = false;
        [SerializeField, Range(1f, 179f)] private float manualFieldOfView = 60f;
        [SerializeField, Min(0.1f)] private float manualAspect = 16f / 9f;
        [SerializeField, Min(0.01f)] private float manualNearClip = 0.03f;
        [SerializeField, Min(0.2f)] private float manualFarClip = 3f;
        [SerializeField, Min(0.01f)] private float manualOrthoHalfHeight = 1f;

        private void OnDrawGizmos()
        {
            DrawArrowGizmo();

            if (drawFrustum && !drawFrustumOnlyWhenSelected)
            {
                DrawFrustumGizmo();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (drawFrustum && drawFrustumOnlyWhenSelected)
            {
                DrawFrustumGizmo();
            }
        }

        private void DrawArrowGizmo()
        {
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward.sqrMagnitude > 0.0001f
                ? transform.forward.normalized
                : Vector3.forward;

            Vector3 up = transform.up.sqrMagnitude > 0.0001f
                ? transform.up.normalized
                : Vector3.up;

            Vector3 right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }
            else
            {
                right.Normalize();
            }

            float bodyLength = Mathf.Max(0.05f, arrowLength);
            float headLength = Mathf.Min(Mathf.Max(0.02f, arrowHeadLength), bodyLength * 0.9f);

            Vector3 tip = origin + forward * bodyLength;
            Vector3 headBase = -forward * headLength;

            Vector3 headUp = Quaternion.AngleAxis(arrowHeadAngle, right) * headBase;
            Vector3 headDown = Quaternion.AngleAxis(-arrowHeadAngle, right) * headBase;
            Vector3 headLeft = Quaternion.AngleAxis(-arrowHeadAngle, up) * headBase;
            Vector3 headRight = Quaternion.AngleAxis(arrowHeadAngle, up) * headBase;

            Gizmos.color = arrowColor;
            Gizmos.DrawSphere(origin, Mathf.Max(0.005f, originSphereRadius));
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip + headUp);
            Gizmos.DrawLine(tip, tip + headDown);
            Gizmos.DrawLine(tip, tip + headLeft);
            Gizmos.DrawLine(tip, tip + headRight);
        }

        private void DrawFrustumGizmo()
        {
            if (!drawFrustum)
            {
                return;
            }

            bool isOrthographic;
            float nearClip;
            float farClip;
            float aspect;
            float fieldOfView;
            float orthoHalfHeight;

            if (useMainCameraSettings && TryGetReferenceCamera(out UnityEngine.Camera referenceCamera))
            {
                isOrthographic = referenceCamera.orthographic;
                nearClip = referenceCamera.nearClipPlane;
                farClip = referenceCamera.farClipPlane;
                aspect = referenceCamera.aspect;
                fieldOfView = referenceCamera.fieldOfView;
                orthoHalfHeight = referenceCamera.orthographicSize;
            }
            else
            {
                isOrthographic = manualOrthographic;
                nearClip = manualNearClip;
                farClip = manualFarClip;
                aspect = manualAspect;
                fieldOfView = manualFieldOfView;
                orthoHalfHeight = manualOrthoHalfHeight;
            }

            nearClip = Mathf.Max(0.001f, nearClip);
            farClip = Mathf.Max(nearClip + 0.01f, farClip);
            farClip = Mathf.Min(farClip, Mathf.Max(nearClip + 0.01f, previewFarLimit));
            aspect = Mathf.Max(0.01f, aspect);
            fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
            orthoHalfHeight = Mathf.Max(0.01f, orthoHalfHeight);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = frustumColor;

            if (isOrthographic)
            {
                float halfWidth = orthoHalfHeight * aspect;
                Vector3 center = new(0f, 0f, (nearClip + farClip) * 0.5f);
                Vector3 size = new(halfWidth * 2f, orthoHalfHeight * 2f, farClip - nearClip);
                Gizmos.DrawWireCube(center, size);
            }
            else
            {
                Gizmos.DrawFrustum(Vector3.zero, fieldOfView, farClip, nearClip, aspect);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private bool TryGetReferenceCamera(out UnityEngine.Camera referenceCamera)
        {
            if (sourceCamera != null)
            {
                referenceCamera = sourceCamera;
                return true;
            }

            referenceCamera = UnityEngine.Camera.main;
            return referenceCamera != null;
        }
    }
}
