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
        [SerializeField, Min(0.5f)] private float standingEyeHeight = 1.65f;
        [SerializeField, Min(0.5f)] private float seatedEyeHeight = 1.18f;

        private CharacterController _characterController;
        private float _pitch;
        private float _verticalVelocity;
        private bool _controlEnabled;
        private bool _lookEnabled;
        private bool _movementEnabled;
        private bool _interactionEnabled;
        private bool _cursorCaptured;
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

        public UnityCamera ViewCamera => viewCamera;
        public bool ControlEnabled => _controlEnabled;
        public bool IsViewLocked => _lockedViewAnchor != null || _gazeViewLocked;

        public void Configure(UnityCamera camera, FirstContactIntroHud introHud)
        {
            viewCamera = camera;
            hud = introHud;
            EnsureReferences();
        }

        private void Awake()
        {
            EnsureReferences();
            if (viewCamera != null)
            {
                _pitch = NormalizePitch(viewCamera.transform.localEulerAngles.x);
            }
        }

        private void OnDisable()
        {
            if (_cursorCaptured)
            {
                ReleaseCursor();
            }
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

            if (enabled)
            {
                CaptureCursor();
            }
            else
            {
                hud?.ClearPrompt();
                ReleaseCursor();
            }
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
            bool controllerWasEnabled = _characterController != null && _characterController.enabled;
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(
                target.position,
                Quaternion.Euler(0f, target.eulerAngles.y, 0f));
            _pitch = NormalizePitch(target.eulerAngles.x);
            if (viewCamera != null)
            {
                viewCamera.transform.localPosition = new Vector3(
                    0f,
                    seated ? seatedEyeHeight : standingEyeHeight,
                    0f);
                viewCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

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

        private void HandleCursorInput()
        {
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
