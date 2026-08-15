using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityCamera = UnityEngine.Camera;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstContactIntroPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private UnityCamera viewCamera;
        [SerializeField] private FirstContactIntroHud hud;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 3.4f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 1.45f;
        [SerializeField] private float gravity = -22f;

        [Header("Look")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.09f;
        [SerializeField, Min(1f)] private float gamepadLookSpeed = 120f;
        [SerializeField, Range(30f, 89f)] private float pitchLimit = 82f;

        [Header("Interaction")]
        [SerializeField, Min(0.5f)] private float interactionDistance = 2.6f;
        [SerializeField] private LayerMask interactionMask = ~0;
        [Tooltip("Only used when an interaction explicitly seats the player. Standing camera position comes from the authored camera Transform.")]
        [SerializeField, Min(0.5f)] private float seatedEyeHeight = 1.18f;

        private CharacterController _characterController;
        private float _pitch;
        private float _verticalVelocity;
        private bool _controlEnabled;
        private bool _lookEnabled;
        private bool _movementEnabled;
        private bool _interactionEnabled;
        private bool _cursorCaptured;
        private bool _externalPointerInputActive;
        private bool _restoreCursorCaptureAfterExternalInput;
        private FirstContactIntroInteractable _contextualInteraction;
        private Transform _lockedViewAnchor;
        private Vector3 _savedCameraLocalPosition;
        private Quaternion _savedCameraLocalRotation;
        private bool _hasSavedCameraPose;
        private bool _gazeViewLocked;
        private Vector3 _gazeLockedCameraPosition;
        private Quaternion _gazeLockedCameraRotation;
        private Vector3 _savedGazeCameraLocalPosition;
        private Quaternion _savedGazeCameraLocalRotation;
        private bool _hasSavedGazeCameraPose;
        private Vector3 _authoredCameraLocalPosition;
        private Quaternion _authoredCameraLocalRotation;
        private bool _hasAuthoredCameraPose;
        private Vector3 _shakeBaseCameraLocalPosition;
        private Quaternion _shakeBaseCameraLocalRotation;
        private bool _viewShakeActive;

        public UnityCamera ViewCamera => viewCamera;
        public bool ControlEnabled => _controlEnabled;
        public bool CursorCaptured => _cursorCaptured;
        public bool ExternalPointerInputActive => _externalPointerInputActive;
        public bool IsViewLocked => _lockedViewAnchor != null || _gazeViewLocked;

        public void Configure(UnityCamera camera, FirstContactIntroHud introHud)
        {
            if (viewCamera != camera)
            {
                _hasAuthoredCameraPose = false;
            }

            viewCamera = camera;
            hud = introHud;
            EnsureReferences();
            CaptureAuthoredCameraPose();
        }

        private void Awake()
        {
            EnsureReferences();
            CaptureAuthoredCameraPose();
            if (viewCamera != null)
            {
                _pitch = NormalizePitch(viewCamera.transform.localEulerAngles.x);
            }
        }

        private void OnDisable()
        {
            StopViewShake();
            if (_cursorCaptured)
            {
                ReleaseCursor();
            }

            _externalPointerInputActive = false;
            _restoreCursorCaptureAfterExternalInput = false;
        }

        private void Update()
        {
            HandleCursorInput();
            if (!_controlEnabled)
            {
                hud?.ClearPrompt();
                return;
            }

            if (_lookEnabled)
            {
                HandleLook();
            }

            HandleMovement();
            HandleInteraction();
        }

        private void LateUpdate()
        {
            ApplyLockedView();
        }

        public void SetControlEnabled(bool enabled)
        {
            _controlEnabled = enabled;
            _lookEnabled = enabled;
            _movementEnabled = enabled;
            _interactionEnabled = enabled;
            _verticalVelocity = 0f;

            if (enabled && !_externalPointerInputActive)
            {
                CaptureCursor();
            }
            else
            {
                hud?.ClearPrompt();
                ReleaseCursor();
            }
        }

        public void SetExternalPointerInputActive(bool active)
        {
            if (_externalPointerInputActive == active)
            {
                return;
            }

            if (active)
            {
                _restoreCursorCaptureAfterExternalInput = _cursorCaptured;
                _externalPointerInputActive = true;
                ReleaseCursor();
                return;
            }

            _externalPointerInputActive = false;
            if (_restoreCursorCaptureAfterExternalInput && _controlEnabled)
            {
                CaptureCursor();
            }

            _restoreCursorCaptureAfterExternalInput = false;
        }

        public void SetMovementEnabled(bool enabled)
        {
            _movementEnabled = enabled;
            _verticalVelocity = 0f;
        }

        public void SetLookEnabled(bool enabled)
        {
            _lookEnabled = enabled;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            if (!enabled)
            {
                hud?.ClearPrompt();
            }
        }

        public void ApplyViewWorldRotation(Quaternion worldRotation)
        {
            EnsureReferences();
            StopViewShake();
            if (viewCamera == null)
            {
                return;
            }

            Vector3 forward = worldRotation * Vector3.forward;
            Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    planarForward.normalized,
                    Vector3.up);
            }

            float planarLength = Mathf.Sqrt(
                forward.x * forward.x + forward.z * forward.z);
            _pitch = Mathf.Clamp(
                Mathf.Atan2(-forward.y, planarLength) * Mathf.Rad2Deg,
                -pitchLimit,
                pitchLimit);
            viewCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        public void SetContextualInteraction(FirstContactIntroInteractable interactable)
        {
            _contextualInteraction = interactable;
            if (interactable == null)
            {
                hud?.ClearPrompt();
            }
        }

        public void LockViewTo(Transform cameraAnchor)
        {
            if (cameraAnchor == null)
            {
                return;
            }

            EnsureReferences();
            if (viewCamera == null)
            {
                return;
            }

            if (!_hasSavedCameraPose)
            {
                _savedCameraLocalPosition = viewCamera.transform.localPosition;
                _savedCameraLocalRotation = viewCamera.transform.localRotation;
                _hasSavedCameraPose = true;
            }

            _lockedViewAnchor = cameraAnchor;
            ApplyLockedView();
        }

        public IEnumerator BlendViewToAnchor(Transform cameraAnchor, float seconds)
        {
            if (cameraAnchor == null)
            {
                yield break;
            }

            EnsureReferences();
            if (viewCamera == null)
            {
                yield break;
            }

            if (!_hasSavedCameraPose)
            {
                _savedCameraLocalPosition = viewCamera.transform.localPosition;
                _savedCameraLocalRotation = viewCamera.transform.localRotation;
                _hasSavedCameraPose = true;
            }

            Transform cameraTransform = viewCamera.transform;
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            _lockedViewAnchor = null;
            _gazeViewLocked = false;

            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(elapsed / duration);
                float easedProgress = progress * progress * (3f - 2f * progress);
                cameraTransform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, cameraAnchor.position, easedProgress),
                    Quaternion.Slerp(startRotation, cameraAnchor.rotation, easedProgress));
                yield return null;
            }

            _lockedViewAnchor = cameraAnchor;
            ApplyLockedView();
        }

        public void RestoreView()
        {
            StopViewShake();
            _lockedViewAnchor = null;
            _gazeViewLocked = false;
            if (viewCamera == null)
            {
                _hasSavedCameraPose = false;
                _hasSavedGazeCameraPose = false;
                return;
            }

            if (_hasSavedCameraPose)
            {
                viewCamera.transform.localPosition = _savedCameraLocalPosition;
                viewCamera.transform.localRotation = _savedCameraLocalRotation;
                _pitch = NormalizePitch(_savedCameraLocalRotation.eulerAngles.x);
                _hasSavedCameraPose = false;
                return;
            }

            if (_hasSavedGazeCameraPose)
            {
                viewCamera.transform.localPosition = _savedGazeCameraLocalPosition;
                viewCamera.transform.localRotation = _savedGazeCameraLocalRotation;
                _pitch = NormalizePitch(_savedGazeCameraLocalRotation.eulerAngles.x);
                _hasSavedGazeCameraPose = false;
            }
        }

        /// <summary>
        /// Applies a small local camera vibration and restores the exact authored
        /// camera pose afterward. Intended for short in-world impacts such as an
        /// elevator arriving at a floor.
        /// </summary>
        public IEnumerator ShakeView(
            float seconds,
            float positionAmplitude,
            float rotationAmplitudeDegrees,
            float frequency)
        {
            EnsureReferences();
            if (viewCamera == null || seconds <= 0f)
            {
                yield break;
            }

            StopViewShake();
            Transform cameraTransform = viewCamera.transform;
            _shakeBaseCameraLocalPosition = cameraTransform.localPosition;
            _shakeBaseCameraLocalRotation = cameraTransform.localRotation;
            _viewShakeActive = true;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, seconds);
            float safeFrequency = Mathf.Max(0.1f, frequency);
            while (_viewShakeActive && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float envelope = Mathf.Sin(progress * Mathf.PI) *
                                 Mathf.Lerp(1f, 0.55f, progress);
                float phase = elapsed * safeFrequency * Mathf.PI * 2f;

                Vector3 positionOffset = new(
                    Mathf.Sin(phase * 0.73f) * positionAmplitude * 0.35f,
                    Mathf.Sin(phase * 1.17f) * positionAmplitude,
                    Mathf.Cos(phase * 0.61f) * positionAmplitude * 0.18f);
                Vector3 rotationOffset = new(
                    Mathf.Sin(phase * 0.91f) * rotationAmplitudeDegrees,
                    Mathf.Cos(phase * 0.67f) * rotationAmplitudeDegrees * 0.45f,
                    Mathf.Sin(phase * 1.09f) * rotationAmplitudeDegrees * 0.35f);

                cameraTransform.localPosition =
                    _shakeBaseCameraLocalPosition + positionOffset * envelope;
                cameraTransform.localRotation =
                    _shakeBaseCameraLocalRotation *
                    Quaternion.Euler(rotationOffset * envelope);
                yield return null;
            }

            StopViewShake();
        }

        public void StopViewShake()
        {
            if (!_viewShakeActive)
            {
                return;
            }

            if (viewCamera != null)
            {
                viewCamera.transform.localPosition = _shakeBaseCameraLocalPosition;
                viewCamera.transform.localRotation = _shakeBaseCameraLocalRotation;
                _pitch = NormalizePitch(_shakeBaseCameraLocalRotation.eulerAngles.x);
            }

            _viewShakeActive = false;
        }

        public IEnumerator BlendToRestoredView(float seconds)
        {
            EnsureReferences();
            if (!_hasSavedCameraPose || viewCamera == null)
            {
                if (_hasSavedGazeCameraPose)
                {
                    yield return BlendToRestoredGazeView(seconds);
                    yield break;
                }

                RestoreView();
                yield break;
            }

            Transform cameraTransform = viewCamera.transform;
            Vector3 startLocalPosition = cameraTransform.localPosition;
            Quaternion startLocalRotation = cameraTransform.localRotation;

            _lockedViewAnchor = null;
            if (seconds <= 0f)
            {
                RestoreView();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / seconds);
                float easedProgress = progress * progress * (3f - 2f * progress);
                cameraTransform.localPosition = Vector3.Lerp(
                    startLocalPosition,
                    _savedCameraLocalPosition,
                    easedProgress);
                cameraTransform.localRotation = Quaternion.Slerp(
                    startLocalRotation,
                    _savedCameraLocalRotation,
                    easedProgress);
                yield return null;
            }

            cameraTransform.localPosition = _savedCameraLocalPosition;
            cameraTransform.localRotation = _savedCameraLocalRotation;
            _pitch = NormalizePitch(_savedCameraLocalRotation.eulerAngles.x);
            _hasSavedCameraPose = false;
        }

        /// <summary>
        /// Keeps the camera at the player's current position while aiming it at a target.
        /// Unlike <see cref="LockViewTo"/>, this does not move the camera to the target transform.
        /// </summary>
        public IEnumerator BlendViewToLookAt(Transform target, float seconds)
        {
            EnsureReferences();
            if (target == null || viewCamera == null)
            {
                yield break;
            }

            if (!_hasSavedGazeCameraPose)
            {
                _savedGazeCameraLocalPosition = viewCamera.transform.localPosition;
                _savedGazeCameraLocalRotation = viewCamera.transform.localRotation;
                _hasSavedGazeCameraPose = true;
            }

            Transform cameraTransform = viewCamera.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Vector3 lookDirection = target.position - cameraPosition;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                yield break;
            }

            Quaternion startRotation = cameraTransform.rotation;
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            _gazeLockedCameraPosition = cameraPosition;
            _gazeLockedCameraRotation = targetRotation;
            _gazeViewLocked = false;

            if (seconds > 0f)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(elapsed / seconds);
                    float easedProgress = progress * progress * (3f - 2f * progress);
                    cameraTransform.SetPositionAndRotation(
                        cameraPosition,
                        Quaternion.Slerp(startRotation, targetRotation, easedProgress));
                    yield return null;
                }
            }

            _gazeViewLocked = true;
            ApplyLockedView();
        }

        /// <summary>
        /// Smoothly returns from a gaze-only focus to the camera pose saved before it began.
        /// </summary>
        public IEnumerator BlendToRestoredGazeView(float seconds)
        {
            EnsureReferences();
            if (!_hasSavedGazeCameraPose || viewCamera == null)
            {
                _gazeViewLocked = false;
                _hasSavedGazeCameraPose = false;
                yield break;
            }

            Transform cameraTransform = viewCamera.transform;
            Transform cameraParent = cameraTransform.parent;
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            Vector3 targetPosition = cameraParent != null
                ? cameraParent.TransformPoint(_savedGazeCameraLocalPosition)
                : _savedGazeCameraLocalPosition;
            Quaternion targetRotation = cameraParent != null
                ? cameraParent.rotation * _savedGazeCameraLocalRotation
                : _savedGazeCameraLocalRotation;

            _gazeViewLocked = false;
            if (seconds > 0f)
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(elapsed / seconds);
                    float easedProgress = progress * progress * (3f - 2f * progress);
                    cameraTransform.SetPositionAndRotation(
                        Vector3.Lerp(startPosition, targetPosition, easedProgress),
                        Quaternion.Slerp(startRotation, targetRotation, easedProgress));
                    yield return null;
                }
            }

            cameraTransform.localPosition = _savedGazeCameraLocalPosition;
            cameraTransform.localRotation = _savedGazeCameraLocalRotation;
            _pitch = NormalizePitch(_savedGazeCameraLocalRotation.eulerAngles.x);
            _hasSavedGazeCameraPose = false;
        }

        public void Teleport(Transform target, bool seated = false)
        {
            if (target == null)
            {
                return;
            }

            EnsureReferences();
            StopViewShake();
            bool controllerWasEnabled = _characterController != null && _characterController.enabled;
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(
                target.position,
                Quaternion.Euler(0f, target.eulerAngles.y, 0f));
            if (viewCamera != null)
            {
                CaptureAuthoredCameraPose();
                Vector3 cameraPosition = _hasAuthoredCameraPose
                    ? _authoredCameraLocalPosition
                    : viewCamera.transform.localPosition;
                Quaternion cameraRotation = _hasAuthoredCameraPose
                    ? _authoredCameraLocalRotation
                    : viewCamera.transform.localRotation;
                if (seated)
                {
                    cameraPosition.y = seatedEyeHeight;
                }

                viewCamera.transform.localPosition = cameraPosition;
                viewCamera.transform.localRotation = cameraRotation;
                _pitch = NormalizePitch(cameraRotation.eulerAngles.x);
            }

            if (_characterController != null)
            {
                _characterController.enabled = controllerWasEnabled;
            }

            _verticalVelocity = 0f;
        }

        /// <summary>
        /// Moves the existing player rig between matching authored spaces without
        /// resetting the camera's local pose. This keeps the exact yaw and pitch
        /// the player chose while an additive scene handoff happens around them.
        /// </summary>
        public void RepositionPreservingView(
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            EnsureReferences();
            bool controllerWasEnabled =
                _characterController != null && _characterController.enabled;
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);

            if (_characterController != null)
            {
                _characterController.enabled = controllerWasEnabled;
            }

            _verticalVelocity = 0f;
        }

        /// <summary>
        /// Moves the player through the current world to a target pose. This is used
        /// for the short physical step from the rear seat to the outside of the car.
        /// </summary>
        public IEnumerator MoveToWorldPose(Transform target, float seconds)
        {
            if (target == null)
            {
                yield break;
            }

            EnsureReferences();
            bool controllerWasEnabled =
                _characterController != null && _characterController.enabled;
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            Vector3 startPosition = transform.position;
            Quaternion startRotation = transform.rotation;
            Vector3 startCameraLocalPosition = viewCamera != null
                ? viewCamera.transform.localPosition
                : Vector3.zero;
            Quaternion startCameraLocalRotation = viewCamera != null
                ? viewCamera.transform.localRotation
                : Quaternion.identity;

            float elapsed = 0f;
            float safeSeconds = Mathf.Max(0f, seconds);
            while (elapsed < safeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = safeSeconds <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(elapsed / safeSeconds);
                float easedProgress =
                    progress * progress * (3f - 2f * progress);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, target.position, easedProgress),
                    Quaternion.Slerp(startRotation, target.rotation, easedProgress));

                yield return null;
            }

            transform.SetPositionAndRotation(target.position, target.rotation);
            if (viewCamera != null)
            {
                viewCamera.transform.localPosition = startCameraLocalPosition;
                viewCamera.transform.localRotation = startCameraLocalRotation;
            }

            _pitch = NormalizePitch(startCameraLocalRotation.eulerAngles.x);
            _verticalVelocity = 0f;
            if (_characterController != null)
            {
                _characterController.enabled = controllerWasEnabled;
            }
        }

        private void ApplyLockedView()
        {
            if (viewCamera == null)
            {
                return;
            }

            if (_lockedViewAnchor != null)
            {
                viewCamera.transform.SetPositionAndRotation(
                    _lockedViewAnchor.position,
                    _lockedViewAnchor.rotation);
                return;
            }

            if (_gazeViewLocked)
            {
                viewCamera.transform.SetPositionAndRotation(
                    _gazeLockedCameraPosition,
                    _gazeLockedCameraRotation);
            }
        }

        private void EnsureReferences()
        {
            _characterController = _characterController != null
                ? _characterController
                : GetComponent<CharacterController>();
            viewCamera = viewCamera != null
                ? viewCamera
                : GetComponentInChildren<UnityCamera>(true);
        }

        private void CaptureAuthoredCameraPose()
        {
            if (_hasAuthoredCameraPose || viewCamera == null)
            {
                return;
            }

            _authoredCameraLocalPosition = viewCamera.transform.localPosition;
            _authoredCameraLocalRotation = viewCamera.transform.localRotation;
            _hasAuthoredCameraPose = true;
        }

        private void HandleCursorInput()
        {
            if (_externalPointerInputActive)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseCursor();
                return;
            }

            if (!_cursorCaptured && Mouse.current?.leftButton.wasPressedThisFrame == true)
            {
                CaptureCursor();
            }
        }

        private void HandleLook()
        {
            if (!_cursorCaptured || viewCamera == null)
            {
                return;
            }

            Vector2 lookDelta = Vector2.zero;
            if (Mouse.current != null)
            {
                lookDelta += Mouse.current.delta.ReadValue() * mouseSensitivity;
            }

            if (Gamepad.current != null)
            {
                lookDelta += Gamepad.current.rightStick.ReadValue() * (gamepadLookSpeed * Time.deltaTime);
            }

            transform.Rotate(Vector3.up, lookDelta.x, Space.World);
            _pitch = Mathf.Clamp(_pitch - lookDelta.y, -pitchLimit, pitchLimit);
            viewCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleMovement()
        {
            if (!_movementEnabled || _characterController == null || !_characterController.enabled)
            {
                return;
            }

            Vector2 moveInput = ReadMoveInput();
            Vector3 planar = transform.right * moveInput.x + transform.forward * moveInput.y;
            if (planar.sqrMagnitude > 1f)
            {
                planar.Normalize();
            }

            bool sprinting = Keyboard.current?.leftShiftKey.isPressed == true ||
                             Gamepad.current?.leftStickButton.isPressed == true;
            float speed = moveSpeed * (sprinting ? sprintMultiplier : 1f);

            if (_characterController.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _verticalVelocity += gravity * Time.deltaTime;
            Vector3 velocity = planar * speed + Vector3.up * _verticalVelocity;
            _characterController.Move(velocity * Time.deltaTime);
        }

        private void HandleInteraction()
        {
            if (!_interactionEnabled || !_cursorCaptured || viewCamera == null)
            {
                hud?.ClearPrompt();
                return;
            }

            FirstContactIntroInteractable interactable = FindFocusedInteractable();
            if (interactable == null &&
                _contextualInteraction != null &&
                _contextualInteraction.IsAvailable)
            {
                interactable = _contextualInteraction;
            }

            if (interactable == null)
            {
                hud?.ClearPrompt();
                return;
            }

            hud?.SetPrompt(interactable.PromptLocalizationKey, interactable.PromptFallback);
            bool interactPressed = Keyboard.current?.eKey.wasPressedThisFrame == true ||
                                   Gamepad.current?.buttonSouth.wasPressedThisFrame == true;
            if (interactPressed)
            {
                interactable.TryInteract(this);
            }
        }

        private FirstContactIntroInteractable FindFocusedInteractable()
        {
            Ray ray = new(viewCamera.transform.position, viewCamera.transform.forward);
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    interactionDistance,
                    interactionMask,
                    QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            FirstContactIntroInteractable interactable = hit.collider.GetComponentInParent<FirstContactIntroInteractable>();
            return interactable != null && interactable.IsAvailable ? interactable : null;
        }

        private static Vector2 ReadMoveInput()
        {
            Vector2 input = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.ClampMagnitude(input, 1f);
            }

            float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                               (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                             (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            input += new Vector2(horizontal, vertical);
            return Vector2.ClampMagnitude(input, 1f);
        }

        private void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _cursorCaptured = true;
        }

        private void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorCaptured = false;
        }

        private static float NormalizePitch(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
