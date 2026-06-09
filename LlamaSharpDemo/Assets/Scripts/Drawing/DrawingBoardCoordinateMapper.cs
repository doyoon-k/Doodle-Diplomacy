using UnityEngine;

internal readonly struct DrawingBoardCoordinateMapper
{
    private readonly Vector2 _textureScale;
    private readonly Vector2 _textureOffset;
    private readonly bool _flipExportHorizontally;
    private readonly bool _flipExportVertically;

    public DrawingBoardCoordinateMapper(
        Vector2 textureScale,
        Vector2 textureOffset,
        bool flipExportHorizontally,
        bool flipExportVertically)
    {
        _textureScale = SanitizeScale(textureScale);
        _textureOffset = textureOffset;
        _flipExportHorizontally = flipExportHorizontally;
        _flipExportVertically = flipExportVertically;
    }

    public float TextureScaleX => Mathf.Max(0.0001f, Mathf.Abs(_textureScale.x));
    public float TextureScaleY => Mathf.Max(0.0001f, Mathf.Abs(_textureScale.y));

    public Vector2 SurfaceUvToCanvasUv(Vector2 surfaceUv)
    {
        return new Vector2(
            (surfaceUv.x * _textureScale.x) + _textureOffset.x,
            (surfaceUv.y * _textureScale.y) + _textureOffset.y);
    }

    public Vector2 CanvasUvToSurfaceUv(Vector2 canvasUv)
    {
        return new Vector2(
            (canvasUv.x - _textureOffset.x) / _textureScale.x,
            (canvasUv.y - _textureOffset.y) / _textureScale.y);
    }

    public bool TryGetCanvasUvFromHit(
        RaycastHit hit,
        Collider configuredCollider,
        out Vector2 canvasUv)
    {
        canvasUv = default;
        if (!DrawingSurfaceMapper.TryGetSurfaceUvFromHit(hit, configuredCollider, out Vector2 surfaceUv))
        {
            return false;
        }

        canvasUv = SurfaceUvToCanvasUv(surfaceUv);
        return true;
    }

    public Vector2Int CanvasUvToPixel(Vector2 canvasUv, int width, int height)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(canvasUv.x * width), 0, Mathf.Max(0, width - 1));
        int y = Mathf.Clamp(Mathf.FloorToInt(canvasUv.y * height), 0, Mathf.Max(0, height - 1));
        return new Vector2Int(x, y);
    }

    public float GetCanvasWorldWidth(Vector3 axisWorldSizes, int uAxis)
    {
        return Mathf.Abs(DrawingSurfaceMapper.GetAxis(axisWorldSizes, uAxis)) / TextureScaleX;
    }

    public float GetCanvasWorldHeight(Vector3 axisWorldSizes, int vAxis)
    {
        return Mathf.Abs(DrawingSurfaceMapper.GetAxis(axisWorldSizes, vAxis)) / TextureScaleY;
    }

    public Vector3 GetCanvasUAxisWorldDirection(Transform targetTransform, int uAxis)
    {
        return GetCanvasAxisWorldDirection(targetTransform, uAxis, _textureScale.x);
    }

    public Vector3 GetCanvasVAxisWorldDirection(Transform targetTransform, int vAxis)
    {
        return GetCanvasAxisWorldDirection(targetTransform, vAxis, _textureScale.y);
    }

    public void ApplyExportOrientation(Color32[] pixels, int width, int height)
    {
        if (_flipExportHorizontally)
        {
            FlipPixelsHorizontally(pixels, width, height);
        }

        if (_flipExportVertically)
        {
            FlipPixelsVertically(pixels, width, height);
        }
    }

    public bool TryGetSurfaceLabelPlacement(
        BoxCollider boxCollider,
        Camera viewCamera,
        Transform fallbackViewTransform,
        Vector2 canvasAnchor,
        float widthNormalized,
        float heightNormalized,
        float surfaceOffset,
        out Vector3 position,
        out Quaternion rotation,
        out Vector2 rectSize,
        out float fontSize)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        rectSize = Vector2.zero;
        fontSize = 0f;

        if (boxCollider == null)
        {
            return false;
        }

        Vector3 axisWorldSizes = DrawingSurfaceMapper.GetBoxAxisWorldSizes(boxCollider);
        if (!DrawingSurfaceMapper.TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        int normalAxis = GetRemainingAxis(uAxis, vAxis);
        Vector3 boxSize = boxCollider.size;
        float halfU = Mathf.Abs(DrawingSurfaceMapper.GetAxis(boxSize, uAxis)) * 0.5f;
        float halfV = Mathf.Abs(DrawingSurfaceMapper.GetAxis(boxSize, vAxis)) * 0.5f;
        float halfNormal = Mathf.Abs(DrawingSurfaceMapper.GetAxis(boxSize, normalAxis)) * 0.5f;
        if (halfU <= 0.0001f || halfV <= 0.0001f)
        {
            return false;
        }

        Vector2 surfaceUv = CanvasUvToSurfaceUv(new Vector2(
            Mathf.Clamp01(canvasAnchor.x),
            Mathf.Clamp01(canvasAnchor.y)));

        Vector3 localPoint = boxCollider.center;
        SetAxis(ref localPoint, uAxis, Mathf.Lerp(-halfU, halfU, 1f - surfaceUv.x));
        SetAxis(ref localPoint, vAxis, Mathf.Lerp(-halfV, halfV, 1f - surfaceUv.y));

        Vector3 positiveNormal = DrawingSurfaceMapper.GetAxisDirection(boxCollider.transform, normalAxis);
        Vector3 boxCenterWorld = boxCollider.transform.TransformPoint(boxCollider.center);
        Vector3 viewDirection = GetViewDirection(viewCamera, fallbackViewTransform, boxCenterWorld);
        float normalSign = Vector3.Dot(positiveNormal, viewDirection) >= 0f ? 1f : -1f;
        SetAxis(ref localPoint, normalAxis, normalSign * halfNormal);

        Vector3 normal = positiveNormal * normalSign;
        Vector3 canvasUp = GetCanvasVAxisWorldDirection(boxCollider.transform, vAxis);
        canvasUp = Vector3.ProjectOnPlane(canvasUp, normal);
        if (canvasUp.sqrMagnitude <= 0.0001f && fallbackViewTransform != null)
        {
            canvasUp = Vector3.ProjectOnPlane(fallbackViewTransform.up, normal);
        }

        if (canvasUp.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        canvasUp.Normalize();
        float worldWidth = GetCanvasWorldWidth(axisWorldSizes, uAxis);
        float worldHeight = GetCanvasWorldHeight(axisWorldSizes, vAxis);
        fontSize = Mathf.Max(0.01f, worldHeight * Mathf.Clamp(heightNormalized, 0.01f, 0.15f));
        rectSize = new Vector2(
            Mathf.Max(fontSize * 2f, worldWidth * Mathf.Clamp(widthNormalized, 0.05f, 0.8f)),
            Mathf.Max(fontSize * 1.4f, fontSize));

        position = boxCollider.transform.TransformPoint(localPoint) +
                   normal * Mathf.Max(0.0005f, surfaceOffset);
        rotation = Quaternion.LookRotation(-normal, canvasUp);
        return true;
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

    private static Vector3 GetCanvasAxisWorldDirection(
        Transform targetTransform,
        int axis,
        float textureAxisScale)
    {
        float scaleSign = textureAxisScale < 0f ? -1f : 1f;
        return -DrawingSurfaceMapper.GetAxisDirection(targetTransform, axis) * scaleSign;
    }

    private static Vector3 GetViewDirection(
        Camera viewCamera,
        Transform fallbackViewTransform,
        Vector3 surfaceCenterWorld)
    {
        if (viewCamera != null)
        {
            return viewCamera.transform.position - surfaceCenterWorld;
        }

        if (Camera.main != null)
        {
            return Camera.main.transform.position - surfaceCenterWorld;
        }

        return fallbackViewTransform != null ? fallbackViewTransform.forward : Vector3.forward;
    }

    private static int GetRemainingAxis(int firstAxis, int secondAxis)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis != firstAxis && axis != secondAxis)
            {
                return axis;
            }
        }

        return 1;
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 0:
                value.x = axisValue;
                break;
            case 1:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }

    private static void FlipPixelsHorizontally(Color32[] pixels, int width, int height)
    {
        if (pixels == null || width <= 1 || height <= 0)
        {
            return;
        }

        int halfColumns = width / 2;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < halfColumns; x++)
            {
                int leftIndex = rowOffset + x;
                int rightIndex = rowOffset + (width - 1 - x);
                (pixels[leftIndex], pixels[rightIndex]) = (pixels[rightIndex], pixels[leftIndex]);
            }
        }
    }

    private static void FlipPixelsVertically(Color32[] pixels, int width, int height)
    {
        if (pixels == null || width <= 0 || height <= 1)
        {
            return;
        }

        int halfRows = height / 2;
        for (int y = 0; y < halfRows; y++)
        {
            int oppositeY = height - 1 - y;
            int topOffset = y * width;
            int bottomOffset = oppositeY * width;
            for (int x = 0; x < width; x++)
            {
                int topIndex = topOffset + x;
                int bottomIndex = bottomOffset + x;
                (pixels[topIndex], pixels[bottomIndex]) = (pixels[bottomIndex], pixels[topIndex]);
            }
        }
    }
}
