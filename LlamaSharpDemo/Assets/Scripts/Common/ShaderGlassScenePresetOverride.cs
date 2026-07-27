using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DoodleDiplomacy.Rendering
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)]
    public sealed class ShaderGlassScenePresetOverride : MonoBehaviour
    {
        [Header("ShaderGlass")]
        [SerializeField] private UniversalRendererData rendererData;
        [SerializeField] private ShaderGlassPreset defaultPreset;
        [SerializeField] private ShaderGlassPreset scenePreset;

        [Header("Preview")]
        [Tooltip("Apply the scene preset while editing, without entering Play Mode.")]
        [SerializeField] private bool applyInEditMode = true;

        private ShaderGlassRendererFeature _feature;

        private void OnEnable()
        {
            _feature = null;
            ApplyScenePreset();
        }

        private void OnDisable()
        {
            RestoreDefaultPreset();
            _feature = null;
        }

        private void OnValidate()
        {
            _feature = null;

            if (isActiveAndEnabled)
            {
                ApplyScenePreset();
            }
        }

        [ContextMenu("Apply Scene Preset")]
        public void ApplyScenePreset()
        {
            if (!Application.isPlaying && !applyInEditMode)
            {
                return;
            }

            ApplyPreset(scenePreset, "Scene");
        }

        [ContextMenu("Restore Default Preset")]
        public void RestoreDefaultPreset()
        {
            ApplyPreset(defaultPreset, "Default");
        }

        private void ApplyPreset(ShaderGlassPreset preset, string presetLabel)
        {
            if (preset == null)
            {
                Debug.LogWarning($"{presetLabel} ShaderGlass preset is not assigned.", this);
                return;
            }

            if (!ResolveFeature())
            {
                return;
            }

            if (_feature.settings.preset != preset)
            {
                _feature.settings.preset = preset;
            }
        }

        private bool ResolveFeature()
        {
            if (_feature != null)
            {
                return true;
            }

            if (rendererData == null)
            {
                Debug.LogError("ShaderGlass renderer data is not assigned.", this);
                return false;
            }

            foreach (ScriptableRendererFeature rendererFeature in rendererData.rendererFeatures)
            {
                if (rendererFeature is ShaderGlassRendererFeature shaderGlass)
                {
                    _feature = shaderGlass;
                    return true;
                }
            }

            Debug.LogError(
                "ShaderGlassRendererFeature was not found in the assigned renderer data.",
                this);
            return false;
        }
    }
}
