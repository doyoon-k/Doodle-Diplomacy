using UnityEngine;

internal sealed class DrawingSurfaceTextureSampler
{
    private Color32[] _pixels;
    private int _width;
    private int _height;
    private Vector2 _sourceTextureScale = Vector2.one;
    private Vector2 _sourceTextureOffset = Vector2.zero;
    private Vector2 _canvasTextureScale = Vector2.one;
    private Vector2 _canvasTextureOffset = Vector2.zero;
    private TextureWrapMode _wrapMode = TextureWrapMode.Clamp;

    public void Configure(
        Material sourceMaterial,
        string texturePropertyName,
        Vector2 canvasTextureScale,
        Vector2 canvasTextureOffset)
    {
        Clear();
        _canvasTextureScale = SanitizeScale(canvasTextureScale);
        _canvasTextureOffset = canvasTextureOffset;

        if (sourceMaterial == null)
        {
            return;
        }

        Texture sourceTexture;
        if (!string.IsNullOrWhiteSpace(texturePropertyName) && sourceMaterial.HasProperty(texturePropertyName))
        {
            sourceTexture = sourceMaterial.GetTexture(texturePropertyName);
            _sourceTextureScale = SanitizeScale(sourceMaterial.GetTextureScale(texturePropertyName));
            _sourceTextureOffset = sourceMaterial.GetTextureOffset(texturePropertyName);
        }
        else
        {
            sourceTexture = sourceMaterial.mainTexture;
            _sourceTextureScale = SanitizeScale(sourceMaterial.mainTextureScale);
            _sourceTextureOffset = sourceMaterial.mainTextureOffset;
        }

        if (sourceTexture is not Texture2D sourceTexture2D)
        {
            return;
        }

        _wrapMode = sourceTexture2D.wrapMode;
        if (!TryExtractTexturePixels(sourceTexture2D, out _pixels, out _width, out _height))
        {
            Clear();
        }
    }

    public bool TrySample(float canvasU, float canvasV, out Color32 color)
    {
        color = default;
        if (_pixels == null || _pixels.Length == 0 || _width <= 0 || _height <= 0)
        {
            return false;
        }

        Vector2 surfaceUv = CanvasUvToSurfaceUv(new Vector2(canvasU, canvasV));
        float sourceU = (surfaceUv.x * _sourceTextureScale.x) + _sourceTextureOffset.x;
        float sourceV = (surfaceUv.y * _sourceTextureScale.y) + _sourceTextureOffset.y;

        color = SampleTextureBilinear(
            _pixels,
            _width,
            _height,
            WrapSampleUv(sourceU, _wrapMode),
            WrapSampleUv(sourceV, _wrapMode));
        return true;
    }

    public void Clear()
    {
        _pixels = null;
        _width = 0;
        _height = 0;
        _sourceTextureScale = Vector2.one;
        _sourceTextureOffset = Vector2.zero;
        _wrapMode = TextureWrapMode.Clamp;
    }

    private Vector2 CanvasUvToSurfaceUv(Vector2 canvasUv)
    {
        return new Vector2(
            (canvasUv.x - _canvasTextureOffset.x) / _canvasTextureScale.x,
            (canvasUv.y - _canvasTextureOffset.y) / _canvasTextureScale.y);
    }

    private static Vector2 SanitizeScale(Vector2 scale)
    {
        if (Mathf.Abs(scale.x) <= 0.0001f)
        {
            scale.x = 1f;
        }

        if (Mathf.Abs(scale.y) <= 0.0001f)
        {
            scale.y = 1f;
        }

        return scale;
    }

    private static float WrapSampleUv(float value, TextureWrapMode wrapMode)
    {
        switch (wrapMode)
        {
            case TextureWrapMode.Repeat:
                return Mathf.Repeat(value, 1f);
            case TextureWrapMode.Mirror:
                return Mathf.PingPong(value, 1f);
            case TextureWrapMode.MirrorOnce:
                return Mathf.Clamp01(Mathf.PingPong(value, 2f));
            case TextureWrapMode.Clamp:
            default:
                return Mathf.Clamp01(value);
        }
    }

    private static bool TryExtractTexturePixels(
        Texture2D texture,
        out Color32[] pixels,
        out int width,
        out int height)
    {
        pixels = null;
        width = 0;
        height = 0;
        if (texture == null)
        {
            return false;
        }

        width = texture.width;
        height = texture.height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        try
        {
            pixels = texture.GetPixels32();
            return pixels != null && pixels.Length > 0;
        }
        catch
        {
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);
                pixels = readable.GetPixels32();
                return pixels != null && pixels.Length > 0;
            }
            catch
            {
                pixels = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(readable);
                    }
                    else
                    {
                        Object.DestroyImmediate(readable);
                    }
                }
            }
        }
    }

    private static Color SampleTextureBilinear(
        Color32[] pixels,
        int width,
        int height,
        float u,
        float v)
    {
        if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
        {
            return Color.clear;
        }

        float x = Mathf.Clamp01(u) * (width - 1);
        float y = Mathf.Clamp01(v) * (height - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);
        float tx = x - x0;
        float ty = y - y0;

        Color c00 = pixels[(y0 * width) + x0];
        Color c10 = pixels[(y0 * width) + x1];
        Color c01 = pixels[(y1 * width) + x0];
        Color c11 = pixels[(y1 * width) + x1];
        Color bottom = Color.Lerp(c00, c10, tx);
        Color top = Color.Lerp(c01, c11, tx);
        return Color.Lerp(bottom, top, ty);
    }
}
