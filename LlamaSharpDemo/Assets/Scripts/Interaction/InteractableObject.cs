using UnityEngine;
using UnityEngine.Events;

namespace DoodleDiplomacy.Interaction
{
    public enum InteractionType { Alien, Tablet, Adjutant, Terminal, Monitor }

    public class InteractableObject : MonoBehaviour
    {
        [Tooltip("Object interaction category.")]
        public InteractionType interactionType;

        [Tooltip("Optional point the camera should look at while this object is hovered.")]
        [SerializeField] private Transform cameraFocusPoint;

        [Tooltip("If false, interaction requests are ignored.")]
        public bool isActive = true;

        public UnityEvent OnInteracted = new();
        public UnityEvent OnHoverEntered = new();
        public UnityEvent OnHoverExited = new();

        private void Awake()
        {
            if (GetComponent<Collider>() == null)
            {
                Debug.LogWarning(
                    $"[InteractableObject] '{name}' has no Collider, so raycast interaction will not work.",
                    this);
            }
        }

        private void OnEnable()
        {
            InteractionManager.Instance?.Register(this);
        }

        private void OnDisable()
        {
            InteractionManager.Instance?.Unregister(this);
        }

        public void Interact()
        {
            if (!isActive)
            {
                return;
            }

            OnInteracted.Invoke();
        }

        public void SetInteractable(bool active)
        {
            isActive = active;
        }

        public Vector3 GetCameraFocusPosition()
        {
            if (cameraFocusPoint != null)
            {
                return cameraFocusPoint.position;
            }

            if (TryGetComponent(out Collider collider))
            {
                return collider.bounds.center;
            }

            if (TryGetComponent(out Renderer renderer))
            {
                return renderer.bounds.center;
            }

            return transform.position;
        }

        public void OnHoverEnter() => OnHoverEntered.Invoke();
        public void OnHoverExit() => OnHoverExited.Invoke();
    }
}
