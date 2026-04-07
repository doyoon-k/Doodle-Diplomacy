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
    public enum CameraMode { Default, FreeLook, TabletView, TerminalZoom }

    [Serializable]
    public class CameraPreset
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        [Range(10f, 120f)]
        public float fieldOfView = 60f;

        public Quaternion Rotation => Quaternion.Euler(eulerAngles);
    }

    [Serializable]
    public class CameraModeUnityEvent : UnityEvent<CameraMode> { }

    public class CameraController : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Defaults to Camera.main when empty.")]
        [SerializeField] private UnityEngine.Camera targetCamera;

        [Header("Mode Presets")]
        [SerializeField] private CameraPreset defaultPreset = new() { fieldOfView = 60f };
        [SerializeField] private CameraPreset freeLookPreset = new() { fieldOfView = 60f };
        [SerializeField] private CameraPreset tabletViewPreset = new() { fieldOfView = 45f };
        [SerializeField] private CameraPreset terminalZoomPreset = new() { fieldOfView = 35f };

        [Header("Transition")]
        [SerializeField] private float transitionDuration = 0.5f;

        [Header("Hover Look")]
        [SerializeField] private float hoverLookLerpSpeed = 5f;
        [SerializeField] private float focusAcquireDelay = 0.15f;

        [Header("Edge Browse")]
        [SerializeField] private float edgeBrowseThresholdNormalized = 0.08f;
        [SerializeField] private float edgeBrowseYawSpeed = 55f;
        [SerializeField] private float maxBrowseYaw = 65f;

        [Header("Events")]
        public CameraModeUnityEvent OnTransitionComplete;

        public static CameraController Instance { get; private set; }

        private CameraMode _currentMode = CameraMode.Default;
        private bool _isTransitioning;
        private Coroutine _transitionRoutine;
        private InteractableObject _hoverCandidate;
        private InteractableObject _activeFocus;
        private float _hoverCandidateElapsed;
        private float _browseYaw;

        public CameraMode CurrentMode => _currentMode;

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
            if (_currentMode != CameraMode.FreeLook || _isTransitioning || targetCamera == null)
            {
                return;
            }

            UpdateHoverDrivenFocus(Time.deltaTime);
            UpdateEdgeBrowse(Time.deltaTime);

            Quaternion desiredRotation = ResolveFreeLookRotation();
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, hoverLookLerpSpeed) * Time.deltaTime);
            targetCamera.transform.rotation = Quaternion.Slerp(targetCamera.transform.rotation, desiredRotation, t);
        }

        public void SetMode(CameraMode mode)
        {
            if (_currentMode == mode)
            {
                return;
            }

            if (_transitionRoutine != null)
            {
                StopCoroutine(_transitionRoutine);
            }

            _currentMode = mode;
            ResetHoverFocusState();
            _transitionRoutine = StartCoroutine(TransitionRoutine(GetPreset(mode)));
        }

        private IEnumerator TransitionRoutine(CameraPreset target)
        {
            _isTransitioning = true;

            Vector3 startPos = targetCamera.transform.position;
            Quaternion startRot = targetCamera.transform.rotation;
            float startFov = targetCamera.fieldOfView;

            float elapsed = 0f;
            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / transitionDuration));

                targetCamera.transform.position = Vector3.Lerp(startPos, target.position, t);
                targetCamera.transform.rotation = Quaternion.Slerp(startRot, target.Rotation, t);
                targetCamera.fieldOfView = Mathf.Lerp(startFov, target.fieldOfView, t);

                yield return null;
            }

            targetCamera.transform.position = target.position;
            targetCamera.transform.rotation = target.Rotation;
            targetCamera.fieldOfView = target.fieldOfView;

            _isTransitioning = false;
            _transitionRoutine = null;

            OnTransitionComplete?.Invoke(_currentMode);
        }

        private void UpdateHoverDrivenFocus(float deltaTime)
        {
            InteractableObject hoveredObject = InteractionManager.Instance != null
                ? InteractionManager.Instance.HoveredObject
                : null;

            if (hoveredObject == _activeFocus && hoveredObject != null)
            {
                _hoverCandidate = null;
                _hoverCandidateElapsed = 0f;
                return;
            }

            if (hoveredObject == null)
            {
                _hoverCandidate = null;
                _hoverCandidateElapsed = 0f;
                return;
            }

            if (_hoverCandidate != hoveredObject)
            {
                _hoverCandidate = hoveredObject;
                _hoverCandidateElapsed = 0f;
                return;
            }

            _hoverCandidateElapsed += deltaTime;
            if (_hoverCandidateElapsed >= focusAcquireDelay)
            {
                _activeFocus = hoveredObject;
            }
        }

        private void UpdateEdgeBrowse(float deltaTime)
        {
            if (!TryGetPointerNormalizedPosition(out Vector2 pointerNormalized))
            {
                return;
            }

            float edgeThreshold = Mathf.Clamp(edgeBrowseThresholdNormalized, 0.01f, 0.45f);
            float edgeIntent = 0f;

            if (pointerNormalized.x <= edgeThreshold)
            {
                edgeIntent = -1f + Mathf.InverseLerp(0f, edgeThreshold, pointerNormalized.x);
            }
            else if (pointerNormalized.x >= 1f - edgeThreshold)
            {
                edgeIntent = Mathf.InverseLerp(1f - edgeThreshold, 1f, pointerNormalized.x);
            }

            if (Mathf.Approximately(edgeIntent, 0f))
            {
                return;
            }

            _activeFocus = null;
            _browseYaw += edgeIntent * edgeBrowseYawSpeed * deltaTime;
            _browseYaw = Mathf.Clamp(_browseYaw, -Mathf.Abs(maxBrowseYaw), Mathf.Abs(maxBrowseYaw));
        }

        private Quaternion ResolveFreeLookRotation()
        {
            InteractableObject focusObject = _activeFocus;
            if (focusObject != null)
            {
                Vector3 focusPosition = focusObject.GetCameraFocusPosition();
                Vector3 viewDirection = focusPosition - targetCamera.transform.position;
                if (viewDirection.sqrMagnitude > 0.0001f)
                {
                    return Quaternion.LookRotation(viewDirection.normalized, Vector3.up);
                }
            }

            Vector3 baseEuler = freeLookPreset.eulerAngles;
            return Quaternion.Euler(baseEuler.x, baseEuler.y + _browseYaw, baseEuler.z);
        }

        private void ResetHoverFocusState()
        {
            _hoverCandidate = null;
            _activeFocus = null;
            _hoverCandidateElapsed = 0f;
            _browseYaw = 0f;
        }

        private CameraPreset GetPreset(CameraMode mode) => mode switch
        {
            CameraMode.Default => defaultPreset,
            CameraMode.FreeLook => freeLookPreset,
            CameraMode.TabletView => tabletViewPreset,
            CameraMode.TerminalZoom => terminalZoomPreset,
            _ => defaultPreset
        };

        private static bool TryGetPointerNormalizedPosition(out Vector2 normalizedPosition)
        {
            Vector2 screenPosition;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
            }
            else
            {
                normalizedPosition = default;
                return false;
            }
#else
            screenPosition = Input.mousePosition;
#endif

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                normalizedPosition = default;
                return false;
            }

            normalizedPosition = new Vector2(
                Mathf.Clamp01(screenPosition.x / Screen.width),
                Mathf.Clamp01(screenPosition.y / Screen.height));
            return true;
        }
    }
}
