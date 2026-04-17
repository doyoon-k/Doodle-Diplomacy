using System.Collections.Generic;
using DoodleDiplomacy.AI;
using DoodleDiplomacy.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Interaction
{
    public class InteractionManager : MonoBehaviour
    {
        public static InteractionManager Instance { get; private set; }

        [SerializeField] private float raycastDistance = 100f;
        [SerializeField] private LayerMask interactableLayer = Physics.DefaultRaycastLayers;

        private readonly HashSet<InteractableObject> _registered = new();
        private readonly RaycastHit[] _raycastHits = new RaycastHit[32];
        private UnityEngine.Camera _mainCamera;
        private InteractableObject _hoveredObject;
        private InteractableObject _lastInteractedObject;
        private bool _inputLocked;

        public InteractableObject HoveredObject => _hoveredObject;
        public InteractableObject LastInteractedObject => _lastInteractedObject;

        private static readonly HashSet<InteractionType> s_WaitingAllowed = new() { InteractionType.Alien };
        private static readonly HashSet<InteractionType> s_ObjectPresentedAllowed = new()
        {
            InteractionType.Alien,
            InteractionType.Tablet,
            InteractionType.Monitor
        };
        private static readonly HashSet<InteractionType> s_DrawingAllowed = new() { InteractionType.Tablet };
        private static readonly HashSet<InteractionType> s_PreviewReadyAllowed = new()
        {
            InteractionType.Tablet,
            InteractionType.Adjutant,
            InteractionType.Monitor
        };
        private static readonly HashSet<InteractionType> s_InterpReadyAllowed = new() { InteractionType.Alien, InteractionType.Terminal };
        private static readonly HashSet<InteractionType> s_InterpreterAllowed = new() { InteractionType.Terminal };
        private static readonly HashSet<InteractionType> s_NoneAllowed = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _mainCamera = UnityEngine.Camera.main;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnDisable()
        {
            ClearHover();
        }

        private void Update()
        {
            bool clicked;
            Vector2 mousePos;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            clicked = mouse.leftButton.wasPressedThisFrame;
            mousePos = mouse.position.ReadValue();
#else
            clicked = Input.GetMouseButtonDown(0);
            mousePos = Input.mousePosition;
#endif

            if (_inputLocked)
            {
                ClearHover();
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            }

            if (!IsPointerWithinCameraBounds(mousePos))
            {
                ClearHover();
                return;
            }

            UpdateHover(mousePos);
            if (clicked)
            {
                TryInteract(mousePos);
            }
        }

        public void Register(InteractableObject obj) => _registered.Add(obj);
        public void Unregister(InteractableObject obj) => _registered.Remove(obj);

        public void SetInputLocked(bool locked)
        {
            if (_inputLocked == locked)
            {
                return;
            }

            _inputLocked = locked;
            if (locked)
            {
                ClearHover();
            }
        }

        public void SetInteractablesForState(GameState state)
        {
            HashSet<InteractionType> allowed = GetAllowedTypes(state);
            SetInputLocked(allowed.Count == 0);
            bool roundStartReady = AIPipelineBridge.Instance != null && AIPipelineBridge.Instance.IsRoundStartReady;
            bool interpreterInspectionCompleted = RoundManager.Instance != null && RoundManager.Instance.HasOpenedInterpreterThisRound;
            foreach (var obj in _registered)
            {
                bool isAllowedByState = allowed.Contains(obj.interactionType);
                bool passesRuntimeGuard = state != GameState.WaitingForRound ||
                                          obj.interactionType != InteractionType.Alien ||
                                          roundStartReady;
                passesRuntimeGuard = passesRuntimeGuard &&
                                    (state != GameState.InterpreterReady ||
                                     obj.interactionType != InteractionType.Alien ||
                                     interpreterInspectionCompleted);
                obj.SetInteractable(!_inputLocked && isAllowedByState && passesRuntimeGuard);
            }
        }

        private void UpdateHover(Vector2 screenPos)
        {
            if (_mainCamera == null)
            {
                return;
            }

            InteractableObject newHover = TryGetInteractableFromScreenPosition(screenPos, out InteractableObject hitInteractable)
                ? hitInteractable
                : null;

            if (newHover == _hoveredObject)
            {
                return;
            }

            _hoveredObject?.OnHoverExit();
            _hoveredObject = newHover;
            _hoveredObject?.OnHoverEnter();
        }

        private void TryInteract(Vector2 screenPos)
        {
            if (_mainCamera == null || _inputLocked)
            {
                return;
            }

            if (TryGetInteractableFromScreenPosition(screenPos, out InteractableObject interactable))
            {
                Debug.Log($"[InteractionManager] Click: {interactable.name} ({interactable.interactionType})");
                _lastInteractedObject = interactable;
                interactable.Interact();
            }
        }

        private bool TryGetInteractableFromScreenPosition(Vector2 screenPos, out InteractableObject interactable)
        {
            interactable = null;
            if (_mainCamera == null)
            {
                return false;
            }

            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                _raycastHits,
                raycastDistance,
                interactableLayer,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
            {
                return false;
            }

            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = _raycastHits[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                InteractableObject candidate = hitCollider.GetComponentInParent<InteractableObject>();
                if (candidate == null || !candidate.isActive)
                {
                    continue;
                }

                float distance = _raycastHits[i].distance;
                if (distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                interactable = candidate;
            }

            return interactable != null;
        }

        private bool IsPointerWithinCameraBounds(Vector2 screenPos)
        {
            if (float.IsNaN(screenPos.x) || float.IsNaN(screenPos.y))
            {
                return false;
            }

            Rect pixelRect = _mainCamera != null && _mainCamera.pixelRect.width > 0f && _mainCamera.pixelRect.height > 0f
                ? _mainCamera.pixelRect
                : new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
            if (pixelRect.width <= 0f || pixelRect.height <= 0f)
            {
                return false;
            }

            return screenPos.x >= pixelRect.xMin &&
                   screenPos.x <= pixelRect.xMax &&
                   screenPos.y >= pixelRect.yMin &&
                   screenPos.y <= pixelRect.yMax;
        }

        private static HashSet<InteractionType> GetAllowedTypes(GameState state) => state switch
        {
            GameState.WaitingForRound => s_WaitingAllowed,
            GameState.ObjectPresented => s_ObjectPresentedAllowed,
            GameState.Drawing => s_DrawingAllowed,
            GameState.PreviewReady => s_PreviewReadyAllowed,
            GameState.InterpreterReady => s_InterpReadyAllowed,
            GameState.Interpreter => s_InterpreterAllowed,
            _ => s_NoneAllowed,
        };

        private void ClearHover()
        {
            _hoveredObject?.OnHoverExit();
            _hoveredObject = null;
        }
    }
}
