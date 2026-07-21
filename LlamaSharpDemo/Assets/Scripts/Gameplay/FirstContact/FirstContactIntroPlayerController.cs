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
        private bool _movementEnabled;
        private bool _interactionEnabled;
        private bool _cursorCaptured;
        private FirstContactIntroInteractable _contextualInteraction;

        public UnityCamera ViewCamera => viewCamera;
        public bool ControlEnabled => _controlEnabled;

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

            HandleLook();
            HandleMovement();
            HandleInteraction();
        }

        public void SetControlEnabled(bool enabled)
        {
            _controlEnabled = enabled;
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
