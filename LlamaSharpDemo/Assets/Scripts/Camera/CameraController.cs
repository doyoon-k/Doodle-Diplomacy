using System;
using System.Collections;
using DoodleDiplomacy.Interaction;
using UnityEngine;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Camera
{
    public enum CameraMode { Default, FreeLook, TabletView, TerminalZoom, AlienReaction, SharedMonitorZoom }

    [Serializable]
    public class CameraPreset
    {
        [Tooltip("Camera anchor transform. Camera moves to this position and aligns forward to this transform's world-space +Z.")]
        public Transform target;
        [Range(10f, 120f)]
        [Tooltip("Field of view applied when this camera preset becomes active.")]
        public float fieldOfView = 60f;

        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            if (target == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            Vector3 forward = target.forward;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            position = target.position;

            Vector3 up = target.up;
            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.up;
            }

            rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            return true;
        }
    }

    [Serializable]
    public class CameraModeUnityEvent : UnityEvent<CameraMode> { }

    public class CameraController : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Defaults to Camera.main when empty.")]
        [SerializeField] private UnityEngine.Camera targetCamera;

        [Header("Mode Presets")]
        [Tooltip("Camera pose used by the default room view.")]
        [SerializeField] private CameraPreset defaultPreset = new() { fieldOfView = 60f };
        [Tooltip("Camera pose used for normal free-look interaction around the room.")]
        [SerializeField] private CameraPreset freeLookPreset = new() { fieldOfView = 60f };
        [Tooltip("Camera pose used while the player is drawing on the tablet.")]
        [SerializeField] private CameraPreset tabletViewPreset = new() { fieldOfView = 45f };
        [Tooltip("Camera pose used when zooming into the terminal screen.")]
        [SerializeField] private CameraPreset terminalZoomPreset = new() { fieldOfView = 35f };
        [Tooltip("Camera pose used during the alien reaction cutaway.")]
        [SerializeField] private CameraPreset alienReactionPreset = new() { fieldOfView = 42f };
        [Tooltip("Camera pose used when inspecting the shared monitor.")]
        [SerializeField] private CameraPreset sharedMonitorZoomPreset = new() { fieldOfView = 38f };

        [Header("Transition")]
        [Tooltip("Seconds for camera moves between mode presets.")]
        [SerializeField] private float transitionDuration = 0.5f;

        [Header("Hover Look")]
        [Tooltip("How quickly the free-look camera rotates toward hovered interactable focus points.")]
        [SerializeField] private float hoverLookLerpSpeed = 5f;
        [Tooltip("Seconds the cursor must hover an interactable before the camera starts focusing it.")]
        [SerializeField] private float focusAcquireDelay = 0.15f;

        [Header("Edge Browse")]
        [Tooltip("Normalized screen-edge band that triggers free-look browsing.")]
        [SerializeField] private float edgeBrowseThresholdNormalized = 0.08f;
        [Tooltip("Yaw speed, in degrees per second, while browsing from the screen edge.")]
        [SerializeField] private float edgeBrowseYawSpeed = 55f;
        [Tooltip("Maximum yaw offset, in degrees, allowed from edge browsing.")]
        [SerializeField] private float maxBrowseYaw = 65f;

        [Header("Events")]
        [Tooltip("UnityEvent invoked after a camera mode transition completes.")]
        public CameraModeUnityEvent OnTransitionComplete;

        public static CameraController Instance { get; private set; }

        private CameraMode _currentMode = CameraMode.Default;
        private bool _isTransitioning;
        private Coroutine _transitionRoutine;
        private bool _isCustomViewActive;
        private CameraHoverFocusController _hoverFocusController;
        private CameraEdgeBrowseController _edgeBrowseController;

        public CameraMode CurrentMode => _currentMode;
        public UnityEngine.Camera TargetCamera => targetCamera;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Camera.main;
            }

            if (targetCamera == null)
            {
                Debug.LogError("[CameraController] No camera found. Assign one in the inspector.", this);
            }

            _hoverFocusController = new CameraHoverFocusController(focusAcquireDelay);
            _edgeBrowseController = new CameraEdgeBrowseController();
            SyncFreeLookControllers();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (targetCamera == null)
            {
                return;
            }

            // Tablet anchor is intentionally parented under the tablet, so keep following it
            // after transitions to avoid stale one-time snapshots.
            if (!_isCustomViewActive && _currentMode == CameraMode.TabletView && !_isTransitioning)
            {
                UpdateTabletViewFollowPose();
                return;
            }

            if (_isCustomViewActive || _currentMode != CameraMode.FreeLook || _isTransitioning)
            {
                return;
            }

            SyncFreeLookControllers();
            UpdateFreeLookAssistControllers(Time.deltaTime);

            Quaternion desiredRotation = ResolveFreeLookRotation();
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, hoverLookLerpSpeed) * Time.deltaTime);
            targetCamera.transform.rotation = Quaternion.Slerp(targetCamera.transform.rotation, desiredRotation, t);
        }

        private void UpdateTabletViewFollowPose()
        {
            CameraPreset preset = GetPreset(CameraMode.TabletView);
            if (preset == null || !preset.TryGetPose(out Vector3 position, out Quaternion rotation))
            {
                return;
            }

            targetCamera.transform.position = position;
            targetCamera.transform.rotation = rotation;
            targetCamera.fieldOfView = preset.fieldOfView;
        }

        public void SetMode(CameraMode mode)
        {
            if (_currentMode == mode && !_isCustomViewActive)
            {
                return;
            }

            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
            }

            _isCustomViewActive = false;
            _currentMode = mode;
            ResetHoverFocusState();
            if (TryResolvePresetPose(mode, out Vector3 targetPosition, out Quaternion targetRotation, out float targetFov))
            {
                _transitionRoutine = StartCoroutine(TransitionRoutine(targetPosition, targetRotation, targetFov));
            }
        }

        public void SetCustomView(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            if (targetCamera == null)
            {
                return;
            }

            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
            }

            _isCustomViewActive = true;
            ResetHoverFocusState();
            _transitionRoutine = StartCoroutine(TransitionRoutine(position, rotation, fieldOfView));
        }

        public bool HasValidPreset(CameraMode mode)
        {
            return TryResolvePresetPose(mode, out _, out _, out _);
        }

        private IEnumerator TransitionRoutine(Vector3 targetPosition, Quaternion targetRotation, float targetFieldOfView)
        {
            _isTransitioning = true;

            Vector3 startPos = targetCamera.transform.position;
            Quaternion startRot = targetCamera.transform.rotation;
            float startFov = targetCamera.fieldOfView;
            bool shouldFollowDynamicPreset = !_isCustomViewActive && _currentMode == CameraMode.TabletView;

            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                if (shouldFollowDynamicPreset &&
                    TryResolvePresetPose(CameraMode.TabletView, out Vector3 dynamicPosition, out Quaternion dynamicRotation, out float dynamicFov))
                {
                    targetPosition = dynamicPosition;
                    targetRotation = dynamicRotation;
                    targetFieldOfView = dynamicFov;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));

                targetCamera.transform.position = Vector3.Lerp(startPos, targetPosition, t);
                targetCamera.transform.rotation = Quaternion.Slerp(startRot, targetRotation, t);
                targetCamera.fieldOfView = Mathf.Lerp(startFov, targetFieldOfView, t);

                yield return null;
            }

            targetCamera.transform.position = targetPosition;
            targetCamera.transform.rotation = targetRotation;
            targetCamera.fieldOfView = targetFieldOfView;

            _isTransitioning = false;
            _transitionRoutine = null;

            OnTransitionComplete?.Invoke(_currentMode);
        }

        private void UpdateFreeLookAssistControllers(float deltaTime)
        {
            InteractableObject hoveredObject = InteractionManager.Instance != null
                ? InteractionManager.Instance.HoveredObject
                : null;
            _hoverFocusController?.Update(hoveredObject, deltaTime);

            if (_edgeBrowseController == null || _hoverFocusController == null)
            {
                return;
            }

            if (!TryGetPointerNormalizedPosition(out Vector2 pointerNormalized))
            {
                return;
            }

            if (_edgeBrowseController.TryApplyBrowse(pointerNormalized, deltaTime))
            {
                _hoverFocusController.ClearActiveFocus();
            }
        }

        private void SyncFreeLookControllers()
        {
            if (_hoverFocusController != null)
            {
                _hoverFocusController.SetFocusAcquireDelay(focusAcquireDelay);
            }

            if (_edgeBrowseController != null)
            {
                _edgeBrowseController.Configure(
                    edgeBrowseThresholdNormalized,
                    edgeBrowseYawSpeed,
                    maxBrowseYaw);
            }
        }

        private Quaternion ResolveFreeLookRotation()
        {
            InteractableObject focusObject = _hoverFocusController != null ? _hoverFocusController.ActiveFocus : null;
            if (focusObject != null)
            {
                Vector3 focusPosition = focusObject.GetCameraFocusPosition();
                Vector3 viewDirection = focusPosition - targetCamera.transform.position;
                if (viewDirection.sqrMagnitude > 0.0001f)
                {
                    return Quaternion.LookRotation(viewDirection.normalized, Vector3.up);
                }
            }

            Quaternion baseRotation = ResolvePresetRotation(freeLookPreset);
            Vector3 baseEuler = baseRotation.eulerAngles;
            float browseYaw = _edgeBrowseController != null ? _edgeBrowseController.BrowseYaw : 0f;
            return Quaternion.Euler(baseEuler.x, baseEuler.y + browseYaw, baseEuler.z);
        }

        private bool TryResolvePresetPose(
            CameraMode mode,
            out Vector3 position,
            out Quaternion rotation,
            out float fieldOfView)
        {
            CameraPreset preset = GetPreset(mode);
            return TryResolvePresetPose(preset, out position, out rotation, out fieldOfView);
        }

        private bool TryResolvePresetPose(
            CameraPreset preset,
            out Vector3 position,
            out Quaternion rotation,
            out float fieldOfView)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            fieldOfView = 60f;

            if (preset == null)
            {
                return false;
            }

            fieldOfView = preset.fieldOfView;
            return preset.TryGetPose(out position, out rotation);
        }

        private static Quaternion ResolvePresetRotation(CameraPreset preset)
        {
            if (preset != null && preset.TryGetPose(out _, out Quaternion rotation))
            {
                return rotation;
            }

            return Quaternion.identity;
        }

        private void ResetHoverFocusState()
        {
            _hoverFocusController?.Reset();
            _edgeBrowseController?.Reset();
        }

        private CameraPreset GetPreset(CameraMode mode) => mode switch
        {
            CameraMode.Default => defaultPreset,
            CameraMode.FreeLook => freeLookPreset,
            CameraMode.TabletView => tabletViewPreset,
            CameraMode.TerminalZoom => terminalZoomPreset,
            CameraMode.AlienReaction => alienReactionPreset,
            CameraMode.SharedMonitorZoom => sharedMonitorZoomPreset,
            _ => defaultPreset
        };

        private bool TryGetPointerNormalizedPosition(out Vector2 normalizedPosition)
        {
            if (!TryGetPointerScreenPosition(out Vector2 screenPosition))
            {
                normalizedPosition = default;
                return false;
            }

            Rect pixelRect = targetCamera != null && targetCamera.pixelRect.width > 0f && targetCamera.pixelRect.height > 0f
                ? targetCamera.pixelRect
                : new Rect(0f, 0f, Screen.width, Screen.height);
            if (screenPosition.x < pixelRect.xMin || screenPosition.x > pixelRect.xMax ||
                screenPosition.y < pixelRect.yMin || screenPosition.y > pixelRect.yMax)
            {
                normalizedPosition = default;
                return false;
            }

            normalizedPosition = new Vector2(
                Mathf.InverseLerp(pixelRect.xMin, pixelRect.xMax, screenPosition.x),
                Mathf.InverseLerp(pixelRect.yMin, pixelRect.yMax, screenPosition.y));
            return true;
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
            }
            else
            {
                screenPosition = default;
                return false;
            }
#else
            screenPosition = Input.mousePosition;
#endif

            if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
            {
                return false;
            }

            Rect screenRect = new(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
            if (screenRect.width <= 0f || screenRect.height <= 0f)
            {
                return false;
            }

            return true;
        }
    }
}
