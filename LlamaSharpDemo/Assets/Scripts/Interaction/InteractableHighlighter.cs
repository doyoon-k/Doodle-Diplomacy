using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace DoodleDiplomacy.Interaction
{
    /// <summary>
    /// Changes emission color and optional cursor while an interactable is hovered.
    /// </summary>
    [RequireComponent(typeof(InteractableObject))]
    public class InteractableHighlighter : MonoBehaviour
    {
        private const string EmissionKeyword = "_EMISSION";
        private static readonly string[] EmissionPropertyNames = { "_EmissionColor", "_EmissiveColor" };

        [Header("Emission Highlight")]
        [Tooltip("Legacy outline material field kept for scene/prefab serialization compatibility.")]
        [FormerlySerializedAs("_outlineMaterial")]
        [HideInInspector]
        [SerializeField] private Material outlineMaterial;
        [Tooltip("Emission color applied while hovered.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color hoverEmissionColor = new(0.15f, 0.9f, 1f, 1f);
        [Tooltip("Multiplier applied to the emission color while hovered.")]
        [Min(0f)]
        [SerializeField] private float hoverEmissionIntensity = 1f;
        [Tooltip("Include renderers on child objects when applying the hover highlight.")]
        [FormerlySerializedAs("_includeChildRenderers")]
        [SerializeField] private bool includeChildRenderers = true;
        [Tooltip("Optional extra roots whose renderers should receive the same hover highlight.")]
        [SerializeField] private Transform[] additionalRendererRoots = Array.Empty<Transform>();

        [Header("Cursor")]
        [Tooltip("Optional cursor to show while hovered.")]
        [FormerlySerializedAs("_hoverCursor")]
        [SerializeField] private Texture2D hoverCursor;
        [FormerlySerializedAs("_cursorHotspot")]
        [SerializeField] private Vector2 cursorHotspot = Vector2.zero;

        private Renderer[] _renderers;
        private DrawingBoardController _drawingBoard;
        private readonly Dictionary<Material, EmissionSnapshot> _emissionSnapshots = new();
        private bool _isHighlighted;

        private readonly struct EmissionSnapshot
        {
            public EmissionSnapshot(string propertyName, Color color, bool keywordEnabled)
            {
                PropertyName = propertyName;
                Color = color;
                KeywordEnabled = keywordEnabled;
            }

            public string PropertyName { get; }
            public Color Color { get; }
            public bool KeywordEnabled { get; }
        }

        private void Awake()
        {
            CacheRenderers();
            _drawingBoard = GetComponentInChildren<DrawingBoardController>();

            var interactable = GetComponent<InteractableObject>();
            interactable.OnHoverEntered.AddListener(ShowHighlight);
            interactable.OnHoverExited.AddListener(HideHighlight);
        }

        private void OnEnable()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                CacheRenderers();
            }
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

        private void CacheRenderers()
        {
            var rendererSet = new HashSet<Renderer>();
            CollectRenderers(transform, rendererSet);

            if (additionalRendererRoots != null)
            {
                foreach (Transform root in additionalRendererRoots)
                {
                    CollectRenderers(root, rendererSet);
                }
            }

            _renderers = new Renderer[rendererSet.Count];
            rendererSet.CopyTo(_renderers);
            _isHighlighted = false;
        }

        private void CollectRenderers(Transform root, HashSet<Renderer> rendererSet)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] found = includeChildRenderers
                ? root.GetComponentsInChildren<Renderer>()
                : root.GetComponents<Renderer>();

            foreach (Renderer renderer in found)
            {
                if (renderer != null)
                {
                    rendererSet.Add(renderer);
                }
            }
        }

        private void ShowHighlight()
        {
            if (ShouldSuppressTabletHighlight())
            {
                return;
            }

            if (_isHighlighted)
            {
                return;
            }

            _emissionSnapshots.Clear();
            bool appliedHighlight = false;
            Color targetEmission = hoverEmissionColor * Mathf.Max(0f, hoverEmissionIntensity);

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Material[] currentMaterials = renderer.materials;
                if (currentMaterials == null || currentMaterials.Length == 0)
                {
                    continue;
                }

                for (int materialIndex = 0; materialIndex < currentMaterials.Length; materialIndex++)
                {
                    Material material = currentMaterials[materialIndex];
                    if (material == null || _emissionSnapshots.ContainsKey(material))
                    {
                        continue;
                    }

                    if (!TryGetEmissionPropertyName(material, out string emissionPropertyName))
                    {
                        continue;
                    }

                    _emissionSnapshots[material] = new EmissionSnapshot(
                        emissionPropertyName,
                        material.GetColor(emissionPropertyName),
                        material.IsKeywordEnabled(EmissionKeyword));

                    material.EnableKeyword(EmissionKeyword);
                    material.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    material.SetColor(emissionPropertyName, targetEmission);
                    appliedHighlight = true;
                }
            }

            _isHighlighted = appliedHighlight;

            if (hoverCursor != null)
            {
                Cursor.SetCursor(hoverCursor, cursorHotspot, CursorMode.Auto);
            }
        }

        private bool ShouldSuppressTabletHighlight()
        {
            if (_drawingBoard == null)
            {
                return false;
            }

            RoundManager manager = RoundManager.Instance;
            if (manager != null)
            {
                return manager.CurrentState == GameState.Drawing;
            }

            return _drawingBoard.enabled && !_drawingBoard.IsInteractionLocked;
        }

        private void HideHighlight()
        {
            if (_isHighlighted)
            {
                foreach (KeyValuePair<Material, EmissionSnapshot> pair in _emissionSnapshots)
                {
                    Material material = pair.Key;
                    if (material == null)
                    {
                        continue;
                    }

                    EmissionSnapshot snapshot = pair.Value;
                    if (material.HasProperty(snapshot.PropertyName))
                    {
                        material.SetColor(snapshot.PropertyName, snapshot.Color);
                    }

                    if (snapshot.KeywordEnabled)
                    {
                        material.EnableKeyword(EmissionKeyword);
                    }
                    else
                    {
                        material.DisableKeyword(EmissionKeyword);
                    }
                }

                _emissionSnapshots.Clear();
                _isHighlighted = false;
            }

            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private static bool TryGetEmissionPropertyName(Material material, out string propertyName)
        {
            if (material == null)
            {
                propertyName = string.Empty;
                return false;
            }

            for (int i = 0; i < EmissionPropertyNames.Length; i++)
            {
                string emissionPropertyName = EmissionPropertyNames[i];
                if (material.HasProperty(emissionPropertyName))
                {
                    propertyName = emissionPropertyName;
                    return true;
                }
            }

            propertyName = string.Empty;
            return false;
        }
    }
}
