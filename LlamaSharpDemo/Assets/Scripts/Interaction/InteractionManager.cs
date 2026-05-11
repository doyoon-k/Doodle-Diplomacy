using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Gameplay;
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
        [SerializeField] private UnityEngine.Camera mainCamera;
        [SerializeField] private GameplayModeHost gameplayModeHost;

        private readonly HashSet<InteractableObject> _registered = new();
        private readonly RaycastHit[] _raycastHits = new RaycastHit[32];
        private InteractableObject _hoveredObject;
        private InteractableObject _lastInteractedObject;
        private bool _inputLocked;
        private IInteractionPolicy _interactionPolicy = new LegacyRoundInteractionPolicy();

        public InteractableObject HoveredObject => _hoveredObject;
        public InteractableObject LastInteractedObject => _lastInteractedObject;
        public GameplayModeHost GameplayModeHost => gameplayModeHost;

        public void ConfigureGameplayModeHost(GameplayModeHost host)
        {
            gameplayModeHost = host;
        }

        public void ConfigureInteractionPolicy(IInteractionPolicy interactionPolicy)
        {
            _interactionPolicy = interactionPolicy ?? new LegacyRoundInteractionPolicy();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
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

            if (mainCamera == null)
            {
                Debug.LogError("[InteractionManager] Main camera reference is missing. Assign it in the inspector.", this);
                ClearHover();
                return;
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

        public void ApplyStatePolicy(InteractionStateContext context)
        {
            IInteractionPolicy policy = _interactionPolicy ?? new LegacyRoundInteractionPolicy();
            SetInputLocked(!HasAnyAllowedInteraction(context, policy));

            foreach (var obj in _registered)
            {
                if (obj.interactionType == InteractionType.Adjutant)
                {
                    obj.SetInteractable(false);
                    continue;
                }

                bool isAllowed = policy.IsAllowed(context, obj.interactionType);
                obj.SetInteractable(!_inputLocked && isAllowed);
            }

        }

        // Backward-compatible entry point while callers migrate to the richer context API.
        public void SetInteractablesForState(GameState state)
        {
            ApplyStatePolicy(new InteractionStateContext(
                state,
                roundStartReady: true,
                interpreterInspectionCompleted: true));
        }

        private void UpdateHover(Vector2 screenPos)
        {
            if (mainCamera == null)
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
            if (mainCamera == null || _inputLocked)
            {
                return;
            }

            if (TryGetInteractableFromScreenPosition(screenPos, out InteractableObject interactable))
            {
                Debug.Log($"[InteractionManager] Click: {interactable.name} ({interactable.interactionType})");
                _lastInteractedObject = interactable;
                if (gameplayModeHost != null && gameplayModeHost.TryHandleInteraction(interactable.interactionType, interactable))
                {
                    return;
                }

                // Legacy fallback for scenes that have not been migrated to GameplayModeHost yet.
                interactable.Interact();
            }
        }

        private bool TryGetInteractableFromScreenPosition(Vector2 screenPos, out InteractableObject interactable)
        {
            interactable = null;
            if (mainCamera == null)
            {
                return false;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
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

            Rect pixelRect = mainCamera != null && mainCamera.pixelRect.width > 0f && mainCamera.pixelRect.height > 0f
                ? mainCamera.pixelRect
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

        private static bool HasAnyAllowedInteraction(InteractionStateContext context, IInteractionPolicy policy)
        {
            if (policy == null)
            {
                return false;
            }

            foreach (InteractionType interactionType in Enum.GetValues(typeof(InteractionType)))
            {
                if (interactionType == InteractionType.Adjutant)
                {
                    continue;
                }

                if (policy.IsAllowed(context, interactionType))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearHover()
        {
            _hoveredObject?.OnHoverExit();
            _hoveredObject = null;
        }
    }
}
