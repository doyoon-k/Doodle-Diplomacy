using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace DoodleDiplomacy.Interaction
{
    /// <summary>
    /// Adds a temporary outline material and optional cursor change while an interactable is hovered.
    /// </summary>
    [RequireComponent(typeof(InteractableObject))]
    public class InteractableHighlighter : MonoBehaviour
    {
        [Header("Outline")]
        [Tooltip("Material used as an extra outline pass while hovered.")]
        [FormerlySerializedAs("_outlineMaterial")]
        [SerializeField] private Material outlineMaterial;
        [Tooltip("Include renderers on child objects when applying the hover outline.")]
        [FormerlySerializedAs("_includeChildRenderers")]
        [SerializeField] private bool includeChildRenderers = true;

        [Header("Cursor")]
        [Tooltip("Optional cursor to show while hovered.")]
        [FormerlySerializedAs("_hoverCursor")]
        [SerializeField] private Texture2D hoverCursor;
        [FormerlySerializedAs("_cursorHotspot")]
        [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

        private Renderer[] _renderers;
        private bool[] _highlightApplied;
        private DrawingBoardController _drawingBoard;

        private void Awake()
        {
            _renderers = includeChildRenderers
                ? GetComponentsInChildren<Renderer>()
                : GetComponents<Renderer>();
            _highlightApplied = new bool[_renderers.Length];
            _drawingBoard = GetComponentInChildren<DrawingBoardController>();

            var interactable = GetComponent<InteractableObject>();
            interactable.OnHoverEntered.AddListener(ShowHighlight);
            interactable.OnHoverExited.AddListener(HideHighlight);
        }

        private void OnDisable()
        {
            HideHighlight();
        }

        private void OnDestroy()
        {
            if (TryGetComponent<InteractableObject>(out var interactable))
            {
                interactable.OnHoverEntered.RemoveListener(ShowHighlight);
                interactable.OnHoverExited.RemoveListener(HideHighlight);
            }
        }

        private void ShowHighlight()
        {
            if (_drawingBoard != null && _drawingBoard.enabled)
            {
                return;
            }

            if (outlineMaterial != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer renderer = _renderers[i];
                    if (renderer == null)
                    {
                        _highlightApplied[i] = false;
                        continue;
                    }

                    Material[] currentMaterials = renderer.sharedMaterials;
                    if (currentMaterials == null || currentMaterials.Length == 0)
                    {
                        _highlightApplied[i] = false;
                        continue;
                    }

                    if (Array.IndexOf(currentMaterials, outlineMaterial) >= 0)
                    {
                        _highlightApplied[i] = false;
                        continue;
                    }

                    var nextMaterials = new Material[currentMaterials.Length + 1];
                    Array.Copy(currentMaterials, nextMaterials, currentMaterials.Length);
                    nextMaterials[currentMaterials.Length] = outlineMaterial;
                    renderer.sharedMaterials = nextMaterials;
                    _highlightApplied[i] = true;
                }
            }

            if (hoverCursor != null)
            {
                Cursor.SetCursor(hoverCursor, cursorHotspot, CursorMode.Auto);
            }
        }

        private void HideHighlight()
        {
            if (outlineMaterial != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (!_highlightApplied[i])
                    {
                        continue;
                    }

                    Renderer renderer = _renderers[i];
                    if (renderer == null)
                    {
                        _highlightApplied[i] = false;
                        continue;
                    }

                    Material[] currentMaterials = renderer.sharedMaterials;
                    int outlineIndex = Array.LastIndexOf(currentMaterials, outlineMaterial);
                    if (outlineIndex < 0)
                    {
                        _highlightApplied[i] = false;
                        continue;
                    }

                    if (currentMaterials.Length == 1)
                    {
                        renderer.sharedMaterials = Array.Empty<Material>();
                        _highlightApplied[i] = false;
                        continue;
                    }

                    var nextMaterials = new Material[currentMaterials.Length - 1];
                    if (outlineIndex > 0)
                    {
                        Array.Copy(currentMaterials, 0, nextMaterials, 0, outlineIndex);
                    }

                    if (outlineIndex < currentMaterials.Length - 1)
                    {
                        Array.Copy(
                            currentMaterials,
                            outlineIndex + 1,
                            nextMaterials,
                            outlineIndex,
                            currentMaterials.Length - outlineIndex - 1);
                    }

                    renderer.sharedMaterials = nextMaterials;
                    _highlightApplied[i] = false;
                }
            }

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
