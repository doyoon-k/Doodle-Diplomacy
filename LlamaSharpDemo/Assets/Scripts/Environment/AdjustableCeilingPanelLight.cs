using UnityEngine;
using UnityEngine.Rendering;

namespace DoodleDiplomacy.Lighting
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Doodle Diplomacy/Environment/Adjustable Ceiling Panel Light")]
    public sealed class AdjustableCeilingPanelLight : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Panel Geometry")]
        [SerializeField]
        [Tooltip("Physical panel footprint in local X and Z axes, measured in metres.")]
        private Vector2 panelSize = new(1.4f, 0.65f);

        [SerializeField, Min(0.02f)]
        [Tooltip("Distance that the housing projects down from the ceiling.")]
        private float housingDepth = 0.08f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Visible metal border around the diffuser.")]
        private float frameWidth = 0.055f;

        [Header("Light Appearance")]
        [SerializeField]
        private bool lightEnabled = true;

        [SerializeField]
        [Tooltip("Shared colour for the visible diffuser and the downward light.")]
        private Color lightColor = new(1f, 0.93f, 0.82f, 1f);

        [SerializeField, Min(0f)]
        [Tooltip("Intensity of the real-time Spot Light. This is independent of diffuser glow.")]
        private float lightIntensity = 1800f;

        [SerializeField, Min(0f)]
        [Tooltip("Brightness of the visible emissive diffuser.")]
        private float emissionIntensity = 5f;

        [SerializeField, Min(0.1f)]
        private float lightRange = 7f;

        [SerializeField, Range(1f, 179f)]
        private float spotAngle = 105f;

        [SerializeField]
        private bool castShadows = true;

        [SerializeField, HideInInspector] private Transform housing;
        [SerializeField, HideInInspector] private Transform diffuser;
        [SerializeField, HideInInspector] private Transform[] frameBars;
        [SerializeField, HideInInspector] private Transform[] dividerBars;
        [SerializeField, HideInInspector] private MeshRenderer diffuserRenderer;
        [SerializeField, HideInInspector] private Light downLight;

        private MaterialPropertyBlock _propertyBlock;

        public Vector2 PanelSize
        {
            get => panelSize;
            set
            {
                panelSize = value;
                Refresh();
            }
        }

        public Color LightColor
        {
            get => lightColor;
            set
            {
                lightColor = value;
                Refresh();
            }
        }

        public float LightIntensity
        {
            get => lightIntensity;
            set
            {
                lightIntensity = value;
                Refresh();
            }
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnValidate()
        {
            ClampSettings();
            Refresh();
        }

        private void OnDidApplyAnimationProperties()
        {
            Refresh();
        }

        [ContextMenu("Refresh Panel Light")]
        public void Refresh()
        {
            ClampSettings();
            ApplyGeometry();
            ApplyAppearance();
        }

        public void AssignAuthoredReferences(
            Transform authoredHousing,
            Transform authoredDiffuser,
            Transform[] authoredFrameBars,
            Transform[] authoredDividerBars,
            MeshRenderer authoredDiffuserRenderer,
            Light authoredDownLight)
        {
            housing = authoredHousing;
            diffuser = authoredDiffuser;
            frameBars = authoredFrameBars;
            dividerBars = authoredDividerBars;
            diffuserRenderer = authoredDiffuserRenderer;
            downLight = authoredDownLight;
            Refresh();
        }

        private void ClampSettings()
        {
            panelSize.x = Mathf.Max(0.2f, panelSize.x);
            panelSize.y = Mathf.Max(0.2f, panelSize.y);
            housingDepth = Mathf.Max(0.02f, housingDepth);

            float maximumFrameWidth = Mathf.Min(panelSize.x, panelSize.y) * 0.45f;
            frameWidth = Mathf.Clamp(frameWidth, 0.01f, maximumFrameWidth);
            lightIntensity = Mathf.Max(0f, lightIntensity);
            emissionIntensity = Mathf.Max(0f, emissionIntensity);
            lightRange = Mathf.Max(0.1f, lightRange);
            spotAngle = Mathf.Clamp(spotAngle, 1f, 179f);
        }

        private void ApplyGeometry()
        {
            float width = panelSize.x;
            float length = panelSize.y;
            float surfaceY = -housingDepth - 0.006f;

            SetPart(
                housing,
                new Vector3(0f, -housingDepth * 0.5f, 0f),
                new Vector3(width, housingDepth, length));

            SetPart(
                diffuser,
                new Vector3(0f, surfaceY, 0f),
                new Vector3(
                    Mathf.Max(0.02f, width - frameWidth * 2f),
                    0.012f,
                    Mathf.Max(0.02f, length - frameWidth * 2f)));

            if (frameBars != null && frameBars.Length >= 4)
            {
                float edgeY = surfaceY - 0.002f;
                SetPart(
                    frameBars[0],
                    new Vector3(-width * 0.5f + frameWidth * 0.5f, edgeY, 0f),
                    new Vector3(frameWidth, 0.022f, length));
                SetPart(
                    frameBars[1],
                    new Vector3(width * 0.5f - frameWidth * 0.5f, edgeY, 0f),
                    new Vector3(frameWidth, 0.022f, length));
                SetPart(
                    frameBars[2],
                    new Vector3(0f, edgeY, -length * 0.5f + frameWidth * 0.5f),
                    new Vector3(
                        Mathf.Max(0.02f, width - frameWidth * 2f),
                        0.022f,
                        frameWidth));
                SetPart(
                    frameBars[3],
                    new Vector3(0f, edgeY, length * 0.5f - frameWidth * 0.5f),
                    new Vector3(
                        Mathf.Max(0.02f, width - frameWidth * 2f),
                        0.022f,
                        frameWidth));
            }

            if (dividerBars != null)
            {
                float dividerLength = Mathf.Max(0.02f, length - frameWidth * 2f);
                float dividerWidth = Mathf.Min(0.018f, width * 0.025f);

                for (int i = 0; i < dividerBars.Length; i++)
                {
                    float normalizedPosition = (i + 1f) / (dividerBars.Length + 1f) - 0.5f;
                    SetPart(
                        dividerBars[i],
                        new Vector3(normalizedPosition * width, surfaceY - 0.009f, 0f),
                        new Vector3(dividerWidth, 0.014f, dividerLength));
                }
            }

            if (downLight != null)
            {
                Transform lightTransform = downLight.transform;
                lightTransform.localPosition = new Vector3(0f, surfaceY - 0.025f, 0f);
                lightTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        private void ApplyAppearance()
        {
            if (diffuserRenderer != null)
            {
                _propertyBlock ??= new MaterialPropertyBlock();
                diffuserRenderer.GetPropertyBlock(_propertyBlock);

                Color diffuserColor = Color.Lerp(Color.white, lightColor, 0.72f);
                diffuserColor.a = 1f;
                _propertyBlock.SetColor(BaseColorId, diffuserColor);
                _propertyBlock.SetColor(
                    EmissionColorId,
                    lightEnabled ? lightColor * emissionIntensity : Color.black);

                diffuserRenderer.SetPropertyBlock(_propertyBlock);
            }

            if (downLight == null)
            {
                return;
            }

            downLight.enabled = lightEnabled;
            downLight.type = LightType.Spot;
            downLight.color = lightColor;
            downLight.intensity = lightIntensity;
            downLight.range = lightRange;
            downLight.spotAngle = spotAngle;
            downLight.innerSpotAngle = spotAngle * 0.72f;
            downLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        }

        private static void SetPart(Transform part, Vector3 localPosition, Vector3 localScale)
        {
            if (part == null)
            {
                return;
            }

            part.localPosition = localPosition;
            part.localRotation = Quaternion.identity;
            part.localScale = localScale;
        }

        private void OnDrawGizmosSelected()
        {
            Color previousColor = Gizmos.color;
            Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.2f);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(
                new Vector3(0f, -housingDepth * 0.5f, 0f),
                new Vector3(panelSize.x, housingDepth, panelSize.y));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
