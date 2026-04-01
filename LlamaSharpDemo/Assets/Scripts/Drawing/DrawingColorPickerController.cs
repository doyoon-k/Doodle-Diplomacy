using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paint-style picker: hue+saturation field plus value slider.
/// </summary>
public class DrawingColorPickerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RawImage colorFieldImage;
    [SerializeField] private RawImage valueSliderImage;
    [SerializeField] private RectTransform colorFieldCursor;
    [SerializeField] private RectTransform valueSliderCursor;
    [SerializeField] private Image previewImage;

    [Header("Texture")]
    [SerializeField] private Vector2Int colorFieldResolution = new(256, 200);
    [SerializeField] private int valueSliderResolution = 256;

    private Texture2D _colorFieldTexture;
    private Texture2D _valueSliderTexture;
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
        _hue = Mathf.Clamp01(normalizedX);
        _saturation = Mathf.Clamp01(normalizedY);
        RefreshValueSliderTexture();
        RefreshPreview();
        RefreshCursors();
        NotifyColorChanged();
    }

    public void SetValue(float normalizedY)
    {
        EnsureInitialized();
        _value = Mathf.Clamp01(normalizedY);
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
        CreateTextures();
        RefreshPreview();
        RefreshCursors();
        _isInitialized = true;
    }

    private void CacheReferences()
    {
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
        RefreshValueSliderTexture();
        RefreshPreview();
        RefreshCursors();

        if (notify)
        {
            NotifyColorChanged();
        }
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
