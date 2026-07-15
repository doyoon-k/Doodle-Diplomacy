using System;
using System.Collections;
using DoodleDiplomacy.Interaction;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Camera
{
    public enum CameraMode { Default, FreeLook, TabletView, TerminalZoom, AlienReaction, SharedMonitorZoom }

    [Serializable]
    public class CameraModeUnityEvent : UnityEvent<CameraMode> { }

    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Header("Output Camera")]
        [Tooltip("Physical gameplay camera. It keeps rendering, raycasting, and post-processing while Cinemachine drives its pose.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Cinemachine Brain attached to the physical gameplay camera.")]
        [SerializeField] private CinemachineBrain brain;

        [Header("Cinemachine Mode Cameras")]
        [Tooltip("Default room view used before free-look interaction begins.")]
        [SerializeField] private CinemachineCamera defaultCamera;
        [Tooltip("Normal room view that receives hover-focus and edge-browse rotation.")]
        [SerializeField] private CinemachineCamera freeLookCamera;
        [Tooltip("View used while the player draws on the tablet.")]
        [SerializeField] private CinemachineCamera tabletViewCamera;
        [Tooltip("Close view of the First Contact terminal.")]
        [SerializeField] private CinemachineCamera terminalZoomCamera;
        [Tooltip("Cutaway view used for alien reactions.")]
        [SerializeField] private CinemachineCamera alienReactionCamera;
        [Tooltip("Close view of the shared monitor.")]
        [SerializeField] private CinemachineCamera sharedMonitorZoomCamera;

        [Header("Free Look Assist")]
        [Tooltip("How quickly the free-look Cinemachine camera rotates toward hovered interaction focus points.")]
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
        [Tooltip("UnityEvent invoked after Cinemachine finishes a gameplay mode transition.")]
        public CameraModeUnityEvent OnTransitionComplete;

        public static CameraController Instance { get; private set; }

        private CameraMode _currentMode = CameraMode.Default;
        private bool _isTransitioning;
        private Coroutine _transitionRoutine;
        private CameraHoverFocusController _hoverFocusController;
        private CameraEdgeBrowseController _edgeBrowseController;
        private Quaternion _freeLookAuthoredRotation = Quaternion.identity;

        public CameraMode CurrentMode => _currentMode;
        public UnityEngine.Camera TargetCamera => targetCamera;
        public bool IsTransitioning => _isTransitioning;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveOutputReferences();

            _hoverFocusController = new CameraHoverFocusController(focusAcquireDelay);
            _edgeBrowseController = new CameraEdgeBrowseController();
            SyncFreeLookControllers();

            CinemachineCamera freeLookCamera = GetModeCamera(CameraMode.FreeLook);
            if (freeLookCamera != null)
            {
                _freeLookAuthoredRotation = freeLookCamera.transform.rotation;
            }

            ActivateInitialMode();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            hoverLookLerpSpeed = Mathf.Max(0.01f, hoverLookLerpSpeed);
            focusAcquireDelay = Mathf.Max(0f, focusAcquireDelay);
            edgeBrowseThresholdNormalized = Mathf.Clamp(edgeBrowseThresholdNormalized, 0.01f, 0.45f);
            edgeBrowseYawSpeed = Mathf.Max(0f, edgeBrowseYawSpeed);
            maxBrowseYaw = Mathf.Max(0f, maxBrowseYaw);

            SyncFreeLookControllers();
        }

        private void Update()
        {
            CinemachineCamera freeLookCamera = GetModeCamera(CameraMode.FreeLook);
            if (freeLookCamera == null || _currentMode != CameraMode.FreeLook || _isTransitioning)
            {
                return;
            }

            SyncFreeLookControllers();
            UpdateFreeLookAssistControllers(Time.deltaTime);

            Quaternion desiredRotation = ResolveFreeLookRotation(freeLookCamera);
            float t = 1f - Mathf.Exp(-hoverLookLerpSpeed * Time.deltaTime);
            freeLookCamera.transform.rotation = Quaternion.Slerp(
                freeLookCamera.transform.rotation,
                desiredRotation,
                t);
        }

        public void SetMode(CameraMode mode)
        {
            CinemachineCamera modeCamera = GetModeCamera(mode);
            if (modeCamera == null)
            {
                Debug.LogError($"[CameraController] No Cinemachine camera is assigned for mode {mode}.", this);
                return;
            }

            if (_currentMode == mode && modeCamera.enabled)
            {
                return;
            }

            CancelTransition();
            _currentMode = mode;
            ResetFreeLookState(modeCamera, mode);
            ActivateOnly(modeCamera);
            _transitionRoutine = StartCoroutine(WaitForBlendRoutine(modeCamera, mode));
        }

        public bool HasValidPreset(CameraMode mode)
        {
            return GetModeCamera(mode) != null;
        }

        public bool ValidateConfiguration(bool logErrors = true)
        {
            ResolveOutputReferences();
            bool valid = targetCamera != null && brain != null;

            if (logErrors && targetCamera == null)
            {
                Debug.LogError("[CameraController] No physical gameplay camera is assigned.", this);
            }

            if (logErrors && brain == null)
            {
                Debug.LogError("[CameraController] The gameplay camera needs a Cinemachine Brain.", this);
            }

            foreach (CameraMode mode in Enum.GetValues(typeof(CameraMode)))
            {
                if (GetModeCamera(mode) != null)
                {
                    continue;
                }

                valid = false;
                if (logErrors)
                {
                    Debug.LogError($"[CameraController] Missing Cinemachine camera for mode {mode}.", this);
                }
            }

            return valid;
        }

        private void ResolveOutputReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = UnityEngine.Camera.main;
            }

            if (brain == null && targetCamera != null)
            {
                brain = targetCamera.GetComponent<CinemachineBrain>();
            }
        }

        private void ActivateInitialMode()
        {
            CinemachineCamera initialCamera = GetModeCamera(CameraMode.Default);
            if (initialCamera == null)
            {
                ValidateConfiguration();
                return;
            }

            _currentMode = CameraMode.Default;
            ActivateOnly(initialCamera);
            brain?.ResetState();
        }

        private void ActivateOnly(CinemachineCamera selectedCamera)
        {
            foreach (CinemachineCamera modeCamera in EnumerateModeCameras())
            {
                if (modeCamera != null)
                {
                    modeCamera.enabled = modeCamera == selectedCamera;
                }
            }

            if (selectedCamera != null)
            {
                selectedCamera.enabled = true;
                selectedCamera.Prioritize();
            }
        }

        private IEnumerator WaitForBlendRoutine(CinemachineCamera selectedCamera, CameraMode mode)
        {
            _isTransitioning = true;

            // CinemachineBrain resolves activation and starts its blend in LateUpdate.
            yield return null;

            while (brain != null && brain.IsBlending && brain.IsLiveChild(selectedCamera))
            {
                yield return null;
            }

            _isTransitioning = false;
            _transitionRoutine = null;
            OnTransitionComplete?.Invoke(mode);
        }

        private void CancelTransition()
        {
            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
                _transitionRoutine = null;
            }

            _isTransitioning = false;
        }

        private void ResetFreeLookState(CinemachineCamera modeCamera, CameraMode mode)
        {
            ResetHoverFocusState();
            if (mode == CameraMode.FreeLook && modeCamera != null)
            {
                modeCamera.transform.rotation = _freeLookAuthoredRotation;
            }
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
            _hoverFocusController?.SetFocusAcquireDelay(focusAcquireDelay);
            _edgeBrowseController?.Configure(
                edgeBrowseThresholdNormalized,
                edgeBrowseYawSpeed,
                maxBrowseYaw);
        }

        private Quaternion ResolveFreeLookRotation(CinemachineCamera freeLookCamera)
        {
            InteractableObject focusObject = _hoverFocusController != null ? _hoverFocusController.ActiveFocus : null;
            if (focusObject != null)
            {
                Vector3 viewDirection = focusObject.GetCameraFocusPosition() - freeLookCamera.transform.position;
                if (viewDirection.sqrMagnitude > 0.0001f)
                {
                    return Quaternion.LookRotation(viewDirection.normalized, Vector3.up);
                }
            }

            Vector3 baseEuler = _freeLookAuthoredRotation.eulerAngles;
            float browseYaw = _edgeBrowseController != null ? _edgeBrowseController.BrowseYaw : 0f;
            return Quaternion.Euler(baseEuler.x, baseEuler.y + browseYaw, baseEuler.z);
        }

        private void ResetHoverFocusState()
        {
            _hoverFocusController?.Reset();
            _edgeBrowseController?.Reset();
        }

        private CinemachineCamera GetModeCamera(CameraMode mode) => mode switch
        {
            CameraMode.Default => defaultCamera,
            CameraMode.FreeLook => freeLookCamera,
            CameraMode.TabletView => tabletViewCamera,
            CameraMode.TerminalZoom => terminalZoomCamera,
            CameraMode.AlienReaction => alienReactionCamera,
            CameraMode.SharedMonitorZoom => sharedMonitorZoomCamera,
            _ => defaultCamera
        };

        private CinemachineCamera[] EnumerateModeCameras()
        {
            return new[]
            {
                defaultCamera,
                freeLookCamera,
                tabletViewCamera,
                terminalZoomCamera,
                alienReactionCamera,
                sharedMonitorZoomCamera
            };
        }

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

            return Screen.width > 0 && Screen.height > 0;
        }
    }
}
