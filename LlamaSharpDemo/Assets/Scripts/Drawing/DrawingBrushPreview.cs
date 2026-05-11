using UnityEngine;
using UnityEngine.Rendering;

public sealed class DrawingBrushPreview : MonoBehaviour
{
    private const int MinCircleSegments = 24;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Scene References")]
    [SerializeField] private MeshFilter outlineMeshFilter;
    [SerializeField] private MeshRenderer outlineRenderer;
    [SerializeField] private MeshFilter fillMeshFilter;
    [SerializeField] private MeshRenderer fillRenderer;

    private MaterialPropertyBlock _propertyBlock;
    private Mesh _runtimeFillMesh;
    private Mesh _runtimeOutlineMesh;
    private int _outlineSegmentCount;
    private float _outlineNormalizedWidth = -1f;

    public bool HasRequiredReferences =>
        outlineMeshFilter != null &&
        outlineRenderer != null &&
        fillMeshFilter != null &&
        fillRenderer != null;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        InitializeAssignedRenderers();
        Hide();
    }

    private void OnDestroy()
    {
        ReleaseRuntimeMesh();
    }

    public void ConfigureFromBoardRenderer(Renderer boardRenderer, int requestedSegments)
    {
        if (!HasRequiredReferences)
        {
            Debug.LogError("[DrawingBrushPreview] Renderer references must be assigned in the Inspector.", this);
            return;
        }

        InitializeAssignedRenderers();
        EnsureFillMesh(requestedSegments);
        EnsureOutlineMesh(requestedSegments, 0.15f);

        int targetLayer = boardRenderer != null ? boardRenderer.gameObject.layer : gameObject.layer;
        outlineRenderer.gameObject.layer = targetLayer;
        fillRenderer.gameObject.layer = targetLayer;

        if (boardRenderer == null)
        {
            return;
        }

        outlineRenderer.sortingLayerID = boardRenderer.sortingLayerID;
        outlineRenderer.sortingOrder = boardRenderer.sortingOrder + 1000;
        fillRenderer.sortingLayerID = boardRenderer.sortingLayerID;
        fillRenderer.sortingOrder = boardRenderer.sortingOrder + 1000;
    }

    public void ShowOutline(
        Vector3 center,
        Vector3 axisU,
        Vector3 axisV,
        float radiusU,
        float radiusV,
        float width,
        int requestedSegments,
        Color color)
    {
        if (!HasRequiredReferences)
        {
            return;
        }

        int segmentCount = Mathf.Max(MinCircleSegments, requestedSegments);
        float normalizedWidth = Mathf.Clamp01(width / Mathf.Max(0.0001f, Mathf.Min(radiusU, radiusV)));
        EnsureOutlineMesh(segmentCount, normalizedWidth);

        Transform outlineTransform = outlineRenderer.transform;
        outlineTransform.position = center;
        outlineTransform.rotation = Quaternion.LookRotation(Vector3.Cross(axisU, axisV).normalized, axisV);
        outlineTransform.localScale = new Vector3(radiusU, radiusV, 1f);
        ApplyColor(outlineRenderer, color);
        outlineRenderer.enabled = true;
        fillRenderer.enabled = false;
    }

    public void ShowFill(Vector3 center, Vector3 normal, Vector3 axisV, float radiusU, float radiusV, Color color)
    {
        if (!HasRequiredReferences)
        {
            return;
        }

        Transform fillTransform = fillRenderer.transform;
        fillTransform.position = center;
        fillTransform.rotation = Quaternion.LookRotation(normal, axisV);
        fillTransform.localScale = new Vector3(radiusU, radiusV, 1f);
        ApplyColor(fillRenderer, color);
        outlineRenderer.enabled = false;
        fillRenderer.enabled = true;
    }

    public void Hide()
    {
        if (outlineRenderer != null)
        {
            outlineRenderer.enabled = false;
        }

        if (fillRenderer != null)
        {
            fillRenderer.enabled = false;
        }
    }

    private void InitializeAssignedRenderers()
    {
        if (outlineRenderer != null)
        {
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            outlineRenderer.allowOcclusionWhenDynamic = false;
        }

        if (fillRenderer != null)
        {
            fillRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;
            fillRenderer.allowOcclusionWhenDynamic = false;
        }
    }

    private void EnsureFillMesh(int requestedSegmentCount)
    {
        int segmentCount = Mathf.Max(MinCircleSegments, requestedSegmentCount);
        if (_runtimeFillMesh != null && fillMeshFilter.sharedMesh == _runtimeFillMesh)
        {
            return;
        }

        ReleaseRuntimeMesh();
        _runtimeFillMesh = BuildUnitDiscMesh(segmentCount);
        _runtimeFillMesh.name = $"{name}_RuntimeBrushPreviewFillMesh";
        fillMeshFilter.sharedMesh = _runtimeFillMesh;
    }

    private void EnsureOutlineMesh(int requestedSegmentCount, float normalizedWidth)
    {
        int segmentCount = Mathf.Max(MinCircleSegments, requestedSegmentCount);
        if (_runtimeOutlineMesh != null &&
            outlineMeshFilter.sharedMesh == _runtimeOutlineMesh &&
            _outlineSegmentCount == segmentCount &&
            Mathf.Approximately(_outlineNormalizedWidth, normalizedWidth))
        {
            return;
        }

        ReleaseRuntimeOutlineMesh();
        _runtimeOutlineMesh = BuildUnitRingMesh(segmentCount, normalizedWidth);
        _runtimeOutlineMesh.name = $"{name}_RuntimeBrushPreviewOutlineMesh";
        outlineMeshFilter.sharedMesh = _runtimeOutlineMesh;
        _outlineSegmentCount = segmentCount;
        _outlineNormalizedWidth = normalizedWidth;
    }

    private void ReleaseRuntimeMesh()
    {
        if (_runtimeFillMesh == null)
        {
            ReleaseRuntimeOutlineMesh();
            return;
        }

        if (fillMeshFilter != null && fillMeshFilter.sharedMesh == _runtimeFillMesh)
        {
            fillMeshFilter.sharedMesh = null;
        }

        if (Application.isPlaying)
        {
            Destroy(_runtimeFillMesh);
        }
        else
        {
            DestroyImmediate(_runtimeFillMesh);
        }

        _runtimeFillMesh = null;
        ReleaseRuntimeOutlineMesh();
    }

    private void ReleaseRuntimeOutlineMesh()
    {
        if (_runtimeOutlineMesh == null)
        {
            return;
        }

        if (outlineMeshFilter != null && outlineMeshFilter.sharedMesh == _runtimeOutlineMesh)
        {
            outlineMeshFilter.sharedMesh = null;
        }

        if (Application.isPlaying)
        {
            Destroy(_runtimeOutlineMesh);
        }
        else
        {
            DestroyImmediate(_runtimeOutlineMesh);
        }

        _runtimeOutlineMesh = null;
        _outlineSegmentCount = 0;
        _outlineNormalizedWidth = -1f;
    }

    private void ApplyColor(Renderer targetRenderer, Color color)
    {
        if (targetRenderer == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(BaseColorId, color);
        _propertyBlock.SetColor(ColorId, color);
        targetRenderer.SetPropertyBlock(_propertyBlock);
    }

    private static Mesh BuildUnitDiscMesh(int segmentCount)
    {
        var mesh = new Mesh();
        var vertices = new Vector3[segmentCount + 1];
        var triangles = new int[segmentCount * 3];
        var uv = new Vector2[vertices.Length];

        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);
            int vertexIndex = i + 1;
            vertices[vertexIndex] = new Vector3(x, y, 0f);
            uv[vertexIndex] = new Vector2((x + 1f) * 0.5f, (y + 1f) * 0.5f);

            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = vertexIndex;
            triangles[triangleIndex + 2] = (i == segmentCount - 1) ? 1 : vertexIndex + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildUnitRingMesh(int segmentCount, float normalizedWidth)
    {
        float innerRadius = Mathf.Clamp01(1f - Mathf.Max(0.02f, normalizedWidth));
        var mesh = new Mesh();
        var vertices = new Vector3[segmentCount * 2];
        var triangles = new int[segmentCount * 6];
        var uv = new Vector2[vertices.Length];

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);
            int outerIndex = i * 2;
            int innerIndex = outerIndex + 1;
            vertices[outerIndex] = new Vector3(x, y, 0f);
            vertices[innerIndex] = new Vector3(x * innerRadius, y * innerRadius, 0f);
            uv[outerIndex] = new Vector2((x + 1f) * 0.5f, (y + 1f) * 0.5f);
            uv[innerIndex] = new Vector2(((x * innerRadius) + 1f) * 0.5f, ((y * innerRadius) + 1f) * 0.5f);

            int nextOuter = ((i + 1) % segmentCount) * 2;
            int nextInner = nextOuter + 1;
            int triangleIndex = i * 6;
            triangles[triangleIndex] = outerIndex;
            triangles[triangleIndex + 1] = nextOuter;
            triangles[triangleIndex + 2] = innerIndex;
            triangles[triangleIndex + 3] = innerIndex;
            triangles[triangleIndex + 4] = nextOuter;
            triangles[triangleIndex + 5] = nextInner;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
