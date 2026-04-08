using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drawing color picker supporting:
/// 1) Hue ring + saturation/value triangle (preferred)
/// 2) Legacy hue+saturation field + value slider (fallback)
/// </summary>
public class DrawingColorPickerController : MonoBehaviour
{
    private enum PickerLayoutMode
    {
        Auto,
        WheelTriangle,
        LegacyFieldSlider
    }

    private enum RuntimeMode
    {
        WheelTriangle,
        LegacyFieldSlider
    }

    private struct WheelGeometry
    {
        public Vector2 Center;
        public float OuterRadius;
        public float InnerRadius;
        public Vector2 HueVertex;
        public Vector2 WhiteVertex;
        public Vector2 BlackVertex;
    }

    [Header("Wheel + Triangle References")]
    [SerializeField] private RawImage hueWheelImage;
    [SerializeField] private RawImage svTriangleImage;
    [SerializeField] private RectTransform hueWheelCursor;
    [SerializeField] private RectTransform svTriangleCursor;

    [Header("Legacy References")]
    [SerializeField] private RawImage colorFieldImage;
    [SerializeField] private RawImage valueSliderImage;
    [SerializeField] private RectTransform colorFieldCursor;
    [SerializeField] private RectTransform valueSliderCursor;

    [Header("Common")]
    [SerializeField] private Image previewImage;

    [Header("Layout")]
    [SerializeField] private PickerLayoutMode layoutMode = PickerLayoutMode.Auto;
    [SerializeField] [Range(0.08f, 0.45f)] private float hueRingThicknessNormalized = 0.22f;
    [SerializeField] [Range(0.50f, 0.98f)] private float triangleRadiusNormalized = 0.90f;

    [Header("Texture")]
    [SerializeField] private int wheelResolution = 256;
    [SerializeField] private Vector2Int colorFieldResolution = new(256, 200);
    [SerializeField] private int valueSliderResolution = 256;

    private Texture2D _hueWheelTexture;
    private Texture2D _svTriangleTexture;
    private Texture2D _colorFieldTexture;
    private Texture2D _valueSliderTexture;
    private RuntimeMode _runtimeMode;
    private float _hue;
    private float _saturation;
    private float _value = 1f;
    private bool _isInitialized;

    public event Action<Color> ColorChanged;
    public Color SelectedColor => Color.HSVToRGB(_hue, _saturation, _value);

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        SafeDestroy(_hueWheelTexture);
        SafeDestroy(_svTriangleTexture);
        SafeDestroy(_colorFieldTexture);
        SafeDestroy(_valueSliderTexture);
    }

    public void SetColor(Color color, bool notify = false)
    {
        EnsureInitialized();
        Color.RGBToHSV(color, out _hue, out _saturation, out _value);
        RefreshAll(notify);
    }

    public void SetHueAndSaturation(float normalizedX, float normalizedY)
    {
        EnsureInitialized();
        if (_runtimeMode == RuntimeMode.LegacyFieldSlider)
        {
            _hue = Mathf.Clamp01(normalizedX);
            _saturation = Mathf.Clamp01(normalizedY);
            RefreshValueSliderTexture();
            RefreshPreview();
            RefreshCursors();
            NotifyColorChanged();
            return;
        }

        SetHueFromWheel(normalizedX, normalizedY);
    }

    public void SetValue(float normalizedY)
    {
        EnsureInitialized();
        if (_runtimeMode == RuntimeMode.LegacyFieldSlider)
        {
            _value = Mathf.Clamp01(normalizedY);
            RefreshPreview();
            RefreshCursors();
            NotifyColorChanged();
            return;
        }

        _value = Mathf.Clamp01(normalizedY);
        RefreshPreview();
        RefreshCursors();
        NotifyColorChanged();
    }

    public void SetHueFromWheel(float normalizedX, float normalizedY)
    {
        EnsureInitialized();
        if (_runtimeMode != RuntimeMode.WheelTriangle)
        {
            _hue = Mathf.Clamp01(normalizedX);
            RefreshValueSliderTexture();
            RefreshPreview();
            RefreshCursors();
            NotifyColorChanged();
            return;
        }

        Vector2 centered = new(
            (Mathf.Clamp01(normalizedX) * 2f) - 1f,
            (Mathf.Clamp01(normalizedY) * 2f) - 1f);
        if (centered.sqrMagnitude < 0.00001f)
        {
            return;
        }

        float angle = Mathf.Atan2(centered.y, centered.x);
        if (angle < 0f)
        {
            angle += Mathf.PI * 2f;
        }

        _hue = angle / (Mathf.PI * 2f);
        RefreshTriangleTexture();
        RefreshPreview();
        RefreshCursors();
        NotifyColorChanged();
    }

    public void SetSaturationValueFromTriangle(float normalizedX, float normalizedY)
    {
        EnsureInitialized();
        if (_runtimeMode != RuntimeMode.WheelTriangle || _svTriangleTexture == null)
        {
            SetHueAndSaturation(normalizedX, normalizedY);
            return;
        }

        int width = _svTriangleTexture.width;
        int height = _svTriangleTexture.height;
        var point = new Vector2(
            Mathf.Clamp01(normalizedX) * (width - 1f),
            Mathf.Clamp01(normalizedY) * (height - 1f));

        WheelGeometry geometry = GetWheelGeometry(width, height);
        Vector3 barycentric = ComputeBarycentric(point, geometry.HueVertex, geometry.WhiteVertex, geometry.BlackVertex);
        barycentric = ClampBarycentric(barycentric);

        float hueWeight = barycentric.x;
        float whiteWeight = barycentric.y;
        _value = Mathf.Clamp01(hueWeight + whiteWeight);
        _saturation = _value > 0.00001f ? Mathf.Clamp01(hueWeight / _value) : 0f;

        RefreshPreview();
        RefreshCursors();
        NotifyColorChanged();
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        CacheReferences();
        _runtimeMode = ResolveRuntimeMode();
        CreateTextures();
        RefreshPreview();
        RefreshCursors();
        _isInitialized = true;
    }

    private RuntimeMode ResolveRuntimeMode()
    {
        if (layoutMode == PickerLayoutMode.WheelTriangle)
        {
            return RuntimeMode.WheelTriangle;
        }

        if (layoutMode == PickerLayoutMode.LegacyFieldSlider)
        {
            return RuntimeMode.LegacyFieldSlider;
        }

        return hueWheelImage != null && svTriangleImage != null
            ? RuntimeMode.WheelTriangle
            : RuntimeMode.LegacyFieldSlider;
    }

    private void CacheReferences()
    {
        if (hueWheelImage == null)
        {
            hueWheelImage = FindNamedComponent<RawImage>("HueWheelImage");
        }

        if (svTriangleImage == null)
        {
            svTriangleImage = FindNamedComponent<RawImage>("SvTriangleImage");
        }

        if (hueWheelCursor == null)
        {
            hueWheelCursor = FindNamedComponent<RectTransform>("HueWheelCursor");
        }

        if (svTriangleCursor == null)
        {
            svTriangleCursor = FindNamedComponent<RectTransform>("SvTriangleCursor");
        }

        if (colorFieldImage == null)
        {
            colorFieldImage = FindNamedComponent<RawImage>("ColorFieldImage");
        }

        if (valueSliderImage == null)
        {
            valueSliderImage = FindNamedComponent<RawImage>("ValueSliderImage");
        }

        if (colorFieldCursor == null)
        {
            colorFieldCursor = FindNamedComponent<RectTransform>("ColorFieldCursor");
        }

        if (valueSliderCursor == null)
        {
            valueSliderCursor = FindNamedComponent<RectTransform>("ValueSliderCursor");
        }

        if (previewImage == null)
        {
            previewImage = FindNamedComponent<Image>("CustomColorPreview");
        }
    }

    private void CreateTextures()
    {
        if (_runtimeMode == RuntimeMode.WheelTriangle)
        {
            CreateWheelTextures();
            return;
        }

        CreateLegacyTextures();
    }

    private void CreateWheelTextures()
    {
        int resolution = Mathf.Max(96, wheelResolution);
        if (_hueWheelTexture == null)
        {
            _hueWheelTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "DrawingHueWheelTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        if (_svTriangleTexture == null)
        {
            _svTriangleTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "DrawingSvTriangleTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        if (hueWheelImage != null)
        {
            hueWheelImage.texture = _hueWheelTexture;
        }

        if (svTriangleImage != null)
        {
            svTriangleImage.texture = _svTriangleTexture;
        }

        PrepareWheelCursorAnchors(hueWheelCursor);
        PrepareWheelCursorAnchors(svTriangleCursor);

        RefreshHueWheelTexture();
        RefreshTriangleTexture();
    }

    private void CreateLegacyTextures()
    {
        if (_colorFieldTexture == null)
        {
            _colorFieldTexture = new Texture2D(
                Mathf.Max(2, colorFieldResolution.x),
                Mathf.Max(2, colorFieldResolution.y),
                TextureFormat.RGBA32,
                false)
            {
                name = "DrawingColorFieldTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        if (_valueSliderTexture == null)
        {
            _valueSliderTexture = new Texture2D(
                1,
                Mathf.Max(2, valueSliderResolution),
                TextureFormat.RGBA32,
                false)
            {
                name = "DrawingValueSliderTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        if (colorFieldImage != null)
        {
            colorFieldImage.texture = _colorFieldTexture;
        }

        if (valueSliderImage != null)
        {
            valueSliderImage.texture = _valueSliderTexture;
        }

        RefreshColorFieldTexture();
        RefreshValueSliderTexture();
    }

    private void RefreshAll(bool notify)
    {
        EnsureInitialized();
        if (_runtimeMode == RuntimeMode.WheelTriangle)
        {
            RefreshTriangleTexture();
        }
        else
        {
            RefreshValueSliderTexture();
        }

        RefreshPreview();
        RefreshCursors();

        if (notify)
        {
            NotifyColorChanged();
        }
    }

    private void RefreshHueWheelTexture()
    {
        if (_hueWheelTexture == null)
        {
            return;
        }

        int width = _hueWheelTexture.width;
        int height = _hueWheelTexture.height;
        WheelGeometry geometry = GetWheelGeometry(width, height);
        var pixels = new Color32[width * height];
        const float edgeSoftness = 1.35f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var point = new Vector2(x, y);
                float distance = Vector2.Distance(point, geometry.Center);
                int index = (y * width) + x;
                if (distance > geometry.OuterRadius + edgeSoftness ||
                    distance < geometry.InnerRadius - edgeSoftness)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float angle = Mathf.Atan2(point.y - geometry.Center.y, point.x - geometry.Center.x);
                if (angle < 0f)
                {
                    angle += Mathf.PI * 2f;
                }

                float hue = angle / (Mathf.PI * 2f);
                Color ringColor = Color.HSVToRGB(hue, 1f, 1f);

                float innerAlpha = Mathf.InverseLerp(geometry.InnerRadius - edgeSoftness, geometry.InnerRadius + edgeSoftness, distance);
                float outerAlpha = 1f - Mathf.InverseLerp(geometry.OuterRadius - edgeSoftness, geometry.OuterRadius + edgeSoftness, distance);
                ringColor.a = Mathf.Clamp01(innerAlpha * outerAlpha);
                pixels[index] = ringColor;
            }
        }

        _hueWheelTexture.SetPixels32(pixels);
        _hueWheelTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void RefreshTriangleTexture()
    {
        if (_svTriangleTexture == null)
        {
            return;
        }

        int width = _svTriangleTexture.width;
        int height = _svTriangleTexture.height;
        WheelGeometry geometry = GetWheelGeometry(width, height);
        Color hueColor = Color.HSVToRGB(_hue, 1f, 1f);
        var pixels = new Color32[width * height];
        const float edgeSoftness = 0.022f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                Vector3 barycentric = ComputeBarycentric(new Vector2(x, y), geometry.HueVertex, geometry.WhiteVertex, geometry.BlackVertex);
                float minWeight = Mathf.Min(barycentric.x, Mathf.Min(barycentric.y, barycentric.z));
                if (minWeight < -edgeSoftness)
                {
                    pixels[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float hueWeight = Mathf.Max(0f, barycentric.x);
                float whiteWeight = Mathf.Max(0f, barycentric.y);
                Color color = (hueColor * hueWeight) + (Color.white * whiteWeight);
                color.a = Mathf.Clamp01((minWeight + edgeSoftness) / edgeSoftness);
                pixels[index] = color;
            }
        }

        _svTriangleTexture.SetPixels32(pixels);
        _svTriangleTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void RefreshColorFieldTexture()
    {
        if (_colorFieldTexture == null)
        {
            return;
        }

        int width = _colorFieldTexture.width;
        int height = _colorFieldTexture.height;
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            float saturation = y / (float)(height - 1);
            for (int x = 0; x < width; x++)
            {
                float hue = x / (float)(width - 1);
                pixels[(y * width) + x] = Color.HSVToRGB(hue, saturation, 1f);
            }
        }

        _colorFieldTexture.SetPixels32(pixels);
        _colorFieldTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void RefreshValueSliderTexture()
    {
        if (_valueSliderTexture == null)
        {
            return;
        }

        int height = _valueSliderTexture.height;
        var pixels = new Color32[height];
        for (int y = 0; y < height; y++)
        {
            float value = y / (float)(height - 1);
            pixels[y] = Color.HSVToRGB(_hue, _saturation, value);
        }

        _valueSliderTexture.SetPixels32(pixels);
        _valueSliderTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void RefreshPreview()
    {
        if (previewImage != null)
        {
            previewImage.color = SelectedColor;
        }
    }

    private void RefreshCursors()
    {
        if (_runtimeMode == RuntimeMode.WheelTriangle)
        {
            RefreshWheelCursors();
            return;
        }

        RefreshLegacyCursors();
    }

    private void RefreshWheelCursors()
    {
        if (hueWheelCursor != null && hueWheelImage != null)
        {
            Rect rect = hueWheelImage.rectTransform.rect;
            float halfMin = Mathf.Min(rect.width, rect.height) * 0.5f;
            float outer = Mathf.Max(1f, halfMin - 1f);
            float inner = Mathf.Max(0f, outer - (outer * hueRingThicknessNormalized));
            float ringMid = (inner + outer) * 0.5f;
            Vector2 direction = AngleToDirection(_hue * Mathf.PI * 2f);
            hueWheelCursor.anchoredPosition = direction * ringMid;
        }

        if (svTriangleCursor != null && svTriangleImage != null && _svTriangleTexture != null)
        {
            Vector2 trianglePoint = GetTrianglePointFromCurrentColor(_svTriangleTexture.width, _svTriangleTexture.height);
            Rect rect = svTriangleImage.rectTransform.rect;
            svTriangleCursor.anchoredPosition = new Vector2(
                ((trianglePoint.x / (_svTriangleTexture.width - 1f)) - 0.5f) * rect.width,
                ((trianglePoint.y / (_svTriangleTexture.height - 1f)) - 0.5f) * rect.height);
        }
    }

    private void RefreshLegacyCursors()
    {
        if (colorFieldCursor != null && colorFieldImage != null)
        {
            Rect rect = colorFieldImage.rectTransform.rect;
            colorFieldCursor.anchoredPosition = new Vector2(
                _hue * rect.width,
                _saturation * rect.height);
        }

        if (valueSliderCursor != null && valueSliderImage != null)
        {
            Rect rect = valueSliderImage.rectTransform.rect;
            valueSliderCursor.anchoredPosition = new Vector2(0f, _value * rect.height);
        }
    }

    private WheelGeometry GetWheelGeometry(int width, int height)
    {
        Vector2 center = new((width - 1f) * 0.5f, (height - 1f) * 0.5f);
        float outerRadius = (Mathf.Min(width, height) * 0.5f) - 1f;
        float ringThickness = Mathf.Max(3f, outerRadius * hueRingThicknessNormalized);
        float innerRadius = Mathf.Max(1f, outerRadius - ringThickness);
        float triangleRadius = innerRadius * Mathf.Clamp(triangleRadiusNormalized, 0.5f, 0.98f);

        return new WheelGeometry
        {
            Center = center,
            OuterRadius = outerRadius,
            InnerRadius = innerRadius,
            HueVertex = center + (AngleToDirection(-90f * Mathf.Deg2Rad) * triangleRadius),
            WhiteVertex = center + (AngleToDirection(150f * Mathf.Deg2Rad) * triangleRadius),
            BlackVertex = center + (AngleToDirection(30f * Mathf.Deg2Rad) * triangleRadius)
        };
    }

    private Vector2 GetTrianglePointFromCurrentColor(int width, int height)
    {
        WheelGeometry geometry = GetWheelGeometry(width, height);
        float hueWeight = _value * _saturation;
        float whiteWeight = _value * (1f - _saturation);
        float blackWeight = 1f - _value;
        return (geometry.HueVertex * hueWeight) +
               (geometry.WhiteVertex * whiteWeight) +
               (geometry.BlackVertex * blackWeight);
    }

    private static Vector2 AngleToDirection(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static Vector3 ComputeBarycentric(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float denominator = ((b.y - c.y) * (a.x - c.x)) + ((c.x - b.x) * (a.y - c.y));
        if (Mathf.Abs(denominator) < 0.000001f)
        {
            return new Vector3(1f, 0f, 0f);
        }

        float u = (((b.y - c.y) * (point.x - c.x)) + ((c.x - b.x) * (point.y - c.y))) / denominator;
        float v = (((c.y - a.y) * (point.x - c.x)) + ((a.x - c.x) * (point.y - c.y))) / denominator;
        float w = 1f - u - v;
        return new Vector3(u, v, w);
    }

    private static Vector3 ClampBarycentric(Vector3 barycentric)
    {
        var clamped = new Vector3(
            Mathf.Max(0f, barycentric.x),
            Mathf.Max(0f, barycentric.y),
            Mathf.Max(0f, barycentric.z));
        float sum = clamped.x + clamped.y + clamped.z;
        if (sum > 0.00001f)
        {
            return clamped / sum;
        }

        if (barycentric.x >= barycentric.y && barycentric.x >= barycentric.z)
        {
            return new Vector3(1f, 0f, 0f);
        }

        if (barycentric.y >= barycentric.z)
        {
            return new Vector3(0f, 1f, 0f);
        }

        return new Vector3(0f, 0f, 1f);
    }

    private static void PrepareWheelCursorAnchors(RectTransform cursor)
    {
        if (cursor == null)
        {
            return;
        }

        cursor.anchorMin = new Vector2(0.5f, 0.5f);
        cursor.anchorMax = new Vector2(0.5f, 0.5f);
        cursor.pivot = new Vector2(0.5f, 0.5f);
    }

    private void NotifyColorChanged()
    {
        ColorChanged?.Invoke(SelectedColor);
    }

    private T FindNamedComponent<T>(string objectName) where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].name == objectName)
            {
                return components[i];
            }
        }

        return null;
    }

    private static void SafeDestroy(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
