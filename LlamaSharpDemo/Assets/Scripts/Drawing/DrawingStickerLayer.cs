using UnityEngine;

/// <summary>
/// One non-destructive sticker layer rendered as a textured board-space quad.
/// </summary>
public sealed class DrawingStickerLayer : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    private static Mesh s_SharedStickerMesh;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private MeshCollider _meshCollider;
    private LineRenderer _outlineRenderer;
    private Material _runtimeMaterial;
    private Texture2D _texture;
    private Color32[] _texturePixels;

    public Texture2D Texture => _texture;
    public RectInt SourceRegion { get; private set; }
    public float Opacity { get; private set; } = 1f;
    public int SortingOrder { get; private set; }
    public string LayerName { get; private set; } = "Sticker";

    public void Initialize(
        Texture2D sourceTexture,
        RectInt sourceRegion,
        string layerName,
        int sortingOrder,
        Color outlineColor)
    {
        EnsureComponents(outlineColor);
        ReplaceTexture(sourceTexture);

        SourceRegion = sourceRegion;
        LayerName = string.IsNullOrWhiteSpace(layerName) ? "Sticker" : layerName.Trim();
        gameObject.name = LayerName;

        SetSortingOrder(sortingOrder);
        SetOpacity(1f);
        SetSelected(false);
    }

    public void ReplaceStickerTexture(
        Texture2D sourceTexture,
        RectInt sourceRegion,
        string layerName)
    {
        ReplaceTexture(sourceTexture);
        SourceRegion = sourceRegion;

        if (!string.IsNullOrWhiteSpace(layerName))
        {
            LayerName = layerName.Trim();
            gameObject.name = LayerName;
        }

        SetOpacity(Opacity);
    }

    public void SetSelected(bool selected)
    {
        if (_outlineRenderer != null)
        {
            _outlineRenderer.enabled = selected;
        }
    }

    public void SetOpacity(float opacity)
    {
        Opacity = Mathf.Clamp01(opacity);
        if (_runtimeMaterial == null)
        {
            return;
        }

        Color color = Color.white;
        color.a = Opacity;
        if (_runtimeMaterial.HasProperty("_Color"))
        {
            _runtimeMaterial.SetColor("_Color", color);
        }

        if (_runtimeMaterial.HasProperty("_BaseColor"))
        {
            _runtimeMaterial.SetColor("_BaseColor", color);
        }
    }

    public void FlipHorizontal()
    {
        Vector3 scale = transform.localScale;
        scale.x = -scale.x;
        transform.localScale = scale;
    }

    public bool TryEraseAlphaAtWorldPoint(Vector3 worldPoint, float worldRadius)
    {
        if (_texture == null || _texturePixels == null || _texturePixels.Length != _texture.width * _texture.height)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        if (localPoint.x < -0.5f || localPoint.x > 0.5f ||
            localPoint.z < -0.5f || localPoint.z > 0.5f)
        {
            return false;
        }

        int width = _texture.width;
        int height = _texture.height;
        int centerX = Mathf.Clamp(
            Mathf.RoundToInt((localPoint.x + 0.5f) * (width - 1)),
            0,
            Mathf.Max(0, width - 1));
        int centerY = Mathf.Clamp(
            Mathf.RoundToInt((localPoint.z + 0.5f) * (height - 1)),
            0,
            Mathf.Max(0, height - 1));

        Vector3 lossyScale = transform.lossyScale;
        float localRadiusX = Mathf.Max(0.0001f, worldRadius / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x)));
        float localRadiusY = Mathf.Max(0.0001f, worldRadius / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z)));
        int radiusPixelsX = Mathf.Max(1, Mathf.CeilToInt(localRadiusX * Mathf.Max(1, width - 1)));
        int radiusPixelsY = Mathf.Max(1, Mathf.CeilToInt(localRadiusY * Mathf.Max(1, height - 1)));

        int minX = Mathf.Max(0, centerX - radiusPixelsX);
        int maxX = Mathf.Min(width - 1, centerX + radiusPixelsX);
        int minY = Mathf.Max(0, centerY - radiusPixelsY);
        int maxY = Mathf.Min(height - 1, centerY + radiusPixelsY);

        bool changed = false;
        for (int y = minY; y <= maxY; y++)
        {
            float normalizedY = (y - centerY) / (float)radiusPixelsY;
            float normalizedYSqr = normalizedY * normalizedY;
            int rowOffset = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                float normalizedX = (x - centerX) / (float)radiusPixelsX;
                if ((normalizedX * normalizedX) + normalizedYSqr > 1f)
                {
                    continue;
                }

                int pixelIndex = rowOffset + x;
                if (_texturePixels[pixelIndex].a == 0)
                {
                    continue;
                }

                _texturePixels[pixelIndex].a = 0;
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        _texture.SetPixels32(_texturePixels);
        _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return true;
    }

    public bool TryRaycast(Ray ray, float maxDistance, out RaycastHit hit)
    {
        hit = default;
        return _meshCollider != null && _meshCollider.Raycast(ray, out hit, maxDistance);
    }

    public Rect GetBoardLocalRect()
    {
        Vector3 center = transform.localPosition;
        Vector3 scale = transform.localScale;
        float rotationRad = transform.localEulerAngles.y * Mathf.Deg2Rad;
        float cos = Mathf.Abs(Mathf.Cos(rotationRad));
        float sin = Mathf.Abs(Mathf.Sin(rotationRad));
        float extentX = ((Mathf.Abs(scale.x) * cos) + (Mathf.Abs(scale.z) * sin)) * 0.5f;
        float extentZ = ((Mathf.Abs(scale.x) * sin) + (Mathf.Abs(scale.z) * cos)) * 0.5f;
        return new Rect(center.x - extentX, center.z - extentZ, extentX * 2f, extentZ * 2f);
    }

    public void Dispose()
    {
        SafeDestroy(_runtimeMaterial);
        _runtimeMaterial = null;
        SafeDestroy(_texture);
        _texture = null;
        _texturePixels = null;
    }

    private void OnDestroy()
    {
        Dispose();
    }

    private void EnsureComponents(Color outlineColor)
    {
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null)
            {
                _meshFilter = gameObject.AddComponent<MeshFilter>();
            }
        }

        _meshFilter.sharedMesh = GetOrCreateSharedStickerMesh();

        if (_meshRenderer == null)
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            if (_meshRenderer == null)
            {
                _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
        }

        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (_meshCollider == null)
        {
            _meshCollider = GetComponent<MeshCollider>();
            if (_meshCollider == null)
            {
                _meshCollider = gameObject.AddComponent<MeshCollider>();
            }
        }

        _meshCollider.sharedMesh = GetOrCreateSharedStickerMesh();

        if (_outlineRenderer == null)
        {
            var outlineObject = new GameObject("StickerOutline");
            outlineObject.transform.SetParent(transform, false);
            outlineObject.hideFlags = RuntimeHideFlags;

            _outlineRenderer = outlineObject.AddComponent<LineRenderer>();
            _outlineRenderer.hideFlags = RuntimeHideFlags;
            _outlineRenderer.useWorldSpace = false;
            _outlineRenderer.loop = true;
            _outlineRenderer.positionCount = 4;
            _outlineRenderer.widthMultiplier = 0.018f;
            _outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outlineRenderer.receiveShadows = false;
            _outlineRenderer.alignment = LineAlignment.View;
            _outlineRenderer.textureMode = LineTextureMode.Stretch;
            _outlineRenderer.SetPosition(0, new Vector3(-0.5f, 0.004f, -0.5f));
            _outlineRenderer.SetPosition(1, new Vector3(-0.5f, 0.004f, 0.5f));
            _outlineRenderer.SetPosition(2, new Vector3(0.5f, 0.004f, 0.5f));
            _outlineRenderer.SetPosition(3, new Vector3(0.5f, 0.004f, -0.5f));

            Shader outlineShader = Shader.Find("Sprites/Default");
            if (outlineShader == null)
            {
                outlineShader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (outlineShader != null)
            {
                _outlineRenderer.sharedMaterial = new Material(outlineShader)
                {
                    name = $"{name}_OutlineMaterial",
                    hideFlags = RuntimeHideFlags
                };
            }
        }

        _outlineRenderer.startColor = outlineColor;
        _outlineRenderer.endColor = outlineColor;
    }

    private void ReplaceTexture(Texture2D sourceTexture)
    {
        SafeDestroy(_runtimeMaterial);
        _runtimeMaterial = null;
        SafeDestroy(_texture);
        _texture = null;

        if (sourceTexture == null)
        {
            _texturePixels = null;
            return;
        }

        _texturePixels = sourceTexture.GetPixels32();

        _texture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false)
        {
            name = $"{name}_StickerTexture",
            filterMode = sourceTexture.filterMode,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = RuntimeHideFlags
        };
        _texture.SetPixels32(_texturePixels);
        _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        _runtimeMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        _runtimeMaterial.name = $"{name}_StickerMaterial";
        _runtimeMaterial.hideFlags = RuntimeHideFlags;
        _runtimeMaterial.mainTexture = _texture;
        _runtimeMaterial.renderQueue = 3000 + SortingOrder;

        if (_runtimeMaterial.HasProperty("_MainTex"))
        {
            _runtimeMaterial.SetTexture("_MainTex", _texture);
        }

        if (_runtimeMaterial.HasProperty("_BaseMap"))
        {
            _runtimeMaterial.SetTexture("_BaseMap", _texture);
        }

        _meshRenderer.sharedMaterial = _runtimeMaterial;
    }

    private void SetSortingOrder(int sortingOrder)
    {
        SortingOrder = Mathf.Max(0, sortingOrder);
        if (_meshRenderer != null)
        {
            _meshRenderer.sortingOrder = SortingOrder;
        }

        if (_runtimeMaterial != null)
        {
            _runtimeMaterial.renderQueue = 3000 + SortingOrder;
        }
    }

    private static Mesh GetOrCreateSharedStickerMesh()
    {
        if (s_SharedStickerMesh != null)
        {
            return s_SharedStickerMesh;
        }

        s_SharedStickerMesh = new Mesh
        {
            name = "DrawingStickerQuad",
            hideFlags = RuntimeHideFlags
        };
        s_SharedStickerMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, -0.5f)
        };
        s_SharedStickerMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };
        s_SharedStickerMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        s_SharedStickerMesh.RecalculateBounds();
        s_SharedStickerMesh.RecalculateNormals();
        return s_SharedStickerMesh;
    }

    private static void SafeDestroy(Object target)
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
