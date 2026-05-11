using UnityEngine;

internal static class DrawingSurfaceMapper
{
    public static bool TryGetSurfaceUvFromHit(RaycastHit hit, Collider configuredCollider, out Vector2 surfaceUv)
    {
        surfaceUv = default;

        if (hit.collider is MeshCollider)
        {
            surfaceUv = hit.textureCoord;
            return true;
        }

        if (hit.collider is BoxCollider hitBoxCollider &&
            TryGetSurfaceUvFromBoxColliderHit(hit.point, hitBoxCollider, out surfaceUv))
        {
            return true;
        }

        if (configuredCollider is BoxCollider configuredBoxCollider &&
            TryGetSurfaceUvFromBoxColliderHit(hit.point, configuredBoxCollider, out surfaceUv))
        {
            return true;
        }

        return false;
    }

    public static bool TryGetSurfaceUvFromBoxColliderHit(
        Vector3 worldPoint,
        BoxCollider boxCollider,
        out Vector2 surfaceUv)
    {
        surfaceUv = default;
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 size = boxCollider.size;
        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        Vector3 localPoint = boxCollider.transform.InverseTransformPoint(worldPoint) - boxCollider.center;
        float halfU = Mathf.Abs(GetAxis(size, uAxis)) * 0.5f;
        float halfV = Mathf.Abs(GetAxis(size, vAxis)) * 0.5f;
        if (halfU <= 0.0001f || halfV <= 0.0001f)
        {
            return false;
        }

        float u = Mathf.InverseLerp(-halfU, halfU, GetAxis(localPoint, uAxis));
        float v = Mathf.InverseLerp(-halfV, halfV, GetAxis(localPoint, vAxis));
        u = 1f - u;
        v = 1f - v;

        surfaceUv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return true;
    }

    public static bool TryResolveBoxPaintAxes(
        BoxCollider boxCollider,
        Vector3 axisWorldSizes,
        out int uAxis,
        out int vAxis)
    {
        uAxis = 0;
        vAxis = 2;
        if (boxCollider == null)
        {
            return false;
        }

        float x = Mathf.Abs(GetAxis(axisWorldSizes, 0));
        float y = Mathf.Abs(GetAxis(axisWorldSizes, 1));
        float z = Mathf.Abs(GetAxis(axisWorldSizes, 2));
        int normalAxis;

        if (x <= y && x <= z)
        {
            normalAxis = 0;
        }
        else if (y <= x && y <= z)
        {
            normalAxis = 1;
        }
        else
        {
            normalAxis = 2;
        }

        switch (normalAxis)
        {
            case 0:
                uAxis = 2;
                vAxis = 1;
                break;
            case 1:
                uAxis = 0;
                vAxis = 2;
                break;
            default:
                uAxis = 0;
                vAxis = 1;
                break;
        }

        return Mathf.Abs(GetAxis(axisWorldSizes, uAxis)) > 0.0001f &&
               Mathf.Abs(GetAxis(axisWorldSizes, vAxis)) > 0.0001f;
    }

    public static Vector3 GetBoxAxisWorldSizes(BoxCollider boxCollider)
    {
        if (boxCollider == null)
        {
            return Vector3.zero;
        }

        Vector3 size = boxCollider.size;
        return new Vector3(
            boxCollider.transform.TransformVector(new Vector3(size.x, 0f, 0f)).magnitude,
            boxCollider.transform.TransformVector(new Vector3(0f, size.y, 0f)).magnitude,
            boxCollider.transform.TransformVector(new Vector3(0f, 0f, size.z)).magnitude);
    }

    public static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    public static Vector3 GetAxisDirection(Transform targetTransform, int axis)
    {
        if (targetTransform == null)
        {
            return Vector3.zero;
        }

        Vector3 axisVector;
        switch (axis)
        {
            case 0:
                axisVector = new Vector3(1f, 0f, 0f);
                break;
            case 1:
                axisVector = new Vector3(0f, 1f, 0f);
                break;
            default:
                axisVector = new Vector3(0f, 0f, 1f);
                break;
        }

        return targetTransform.TransformVector(axisVector).normalized;
    }

    public static bool TryGetBoxColliderWorldSurfaceSize(BoxCollider boxCollider, out float worldWidth, out float worldHeight)
    {
        worldWidth = 1f;
        worldHeight = 1f;
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        worldWidth = GetAxis(axisWorldSizes, uAxis);
        worldHeight = GetAxis(axisWorldSizes, vAxis);
        return worldWidth > 0.0001f && worldHeight > 0.0001f;
    }
}
