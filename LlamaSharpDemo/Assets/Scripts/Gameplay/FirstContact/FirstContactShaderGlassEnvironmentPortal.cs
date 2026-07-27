using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FirstContactShaderGlassEnvironmentPortal : MonoBehaviour
    {
        [Header("ShaderGlass")]
        [SerializeField] private UniversalRendererData rendererData;
        [SerializeField] private ShaderGlassPreset outdoorPreset;
        [SerializeField] private ShaderGlassPreset indoorPreset;

        [Header("Portal Direction")]
        [Tooltip("The pizza restaurant interior is on the portal's positive local Z side.")]
        [SerializeField] private bool interiorIsPositiveLocalZ = true;

        private ShaderGlassRendererFeature _feature;

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            ResolveFeature();
            ApplyPreset(indoor: false);
        }

        private void Start()
        {
            FirstContactIntroPlayerController player =
                FindFirstObjectByType<FirstContactIntroPlayerController>();
            if (player != null)
            {
                ApplyPreset(IsOnIndoorSide(player.transform.position));
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ApplyPreset(indoor: false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (TryGetPlayer(other, out _))
            {
                ApplyPreset(indoor: true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (TryGetPlayer(other, out FirstContactIntroPlayerController player))
            {
                ApplyPreset(IsOnIndoorSide(player.transform.position));
            }
        }

        private bool IsOnIndoorSide(Vector3 worldPosition)
        {
            float localZ = transform.InverseTransformPoint(worldPosition).z;
            return interiorIsPositiveLocalZ ? localZ >= 0f : localZ <= 0f;
        }

        private static bool TryGetPlayer(
            Collider other,
            out FirstContactIntroPlayerController player)
        {
            player = other != null
                ? other.GetComponentInParent<FirstContactIntroPlayerController>()
                : null;
            return player != null;
        }

        private void ResolveFeature()
        {
            if (_feature != null || rendererData == null)
            {
                return;
            }

            foreach (ScriptableRendererFeature rendererFeature in rendererData.rendererFeatures)
            {
                if (rendererFeature is ShaderGlassRendererFeature shaderGlass)
                {
                    _feature = shaderGlass;
                    break;
                }
            }

            if (_feature == null)
            {
                Debug.LogError(
                    "ShaderGlassRendererFeature was not found in the assigned renderer data.",
                    this);
            }
        }

        private void ApplyPreset(bool indoor)
        {
            ResolveFeature();
            if (_feature == null)
            {
                return;
            }

            ShaderGlassPreset preset = indoor ? indoorPreset : outdoorPreset;
            if (preset == null)
            {
                Debug.LogWarning(
                    indoor
                        ? "Indoor ShaderGlass preset is not assigned."
                        : "Outdoor ShaderGlass preset is not assigned.",
                    this);
                return;
            }

            if (_feature.settings.preset != preset)
            {
                _feature.settings.preset = preset;
            }
        }
    }
}
