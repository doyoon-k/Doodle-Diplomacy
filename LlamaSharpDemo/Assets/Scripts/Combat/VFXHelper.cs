using UnityEngine;

/// <summary>
/// Utility functions for creating procedural visual effects
/// </summary>
public static class VFXHelper
{
    /// <summary>
    /// Create a standard unlit material with specified color
    /// </summary>
    public static Material CreateUnlitMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        return mat;
    }

    /// <summary>
    /// Setup a LineRenderer as a circle
    /// </summary>
    public static void SetupCircle(LineRenderer line, float radius, int segments = 32)
    {
        line.positionCount = segments + 1;
        line.useWorldSpace = false;
        line.loop = true;

        for (int i = 0; i <= segments; i++)
        {
            float angle = i * 360f / segments * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            line.SetPosition(i, new Vector3(x, y, 0));
        }
    }

    /// <summary>
    /// Setup a LineRenderer as an arc
    /// </summary>
    public static void SetupArc(LineRenderer line, float radius, float startAngle, float endAngle, int segments = 16)
    {
        line.positionCount = segments + 1;
        line.useWorldSpace = false;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startAngle, endAngle, t) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            line.SetPosition(i, new Vector3(x, y, 0));
        }
    }

    /// <summary>
    /// Setup a LineRenderer as a star shape
    /// </summary>
    public static void SetupStar(LineRenderer line, float outerRadius, float innerRadius, int points = 5)
    {
        line.positionCount = points * 2 + 1;
        line.useWorldSpace = false;
        line.loop = true;

        for (int i = 0; i < points * 2; i++)
        {
            float angle = i * 180f / points * Mathf.Deg2Rad;
            float radius = (i % 2 == 0) ? outerRadius : innerRadius;
            float x = Mathf.Cos(angle - Mathf.PI / 2f) * radius;
            float y = Mathf.Sin(angle - Mathf.PI / 2f) * radius;
            line.SetPosition(i, new Vector3(x, y, 0));
        }
        line.SetPosition(points * 2, line.GetPosition(0)); // Close the loop
    }

    /// <summary>
    /// Setup a LineRenderer as a hexagon
    /// </summary>
    public static void SetupHexagon(LineRenderer line, float radius)
    {
        SetupCircle(line, radius, 6);
    }

    /// <summary>
    /// Create a basic particle system with common settings
    /// </summary>
    public static ParticleSystem CreateBasicParticles(GameObject parent, Color color, float lifetime = 1f, float size = 0.2f)
    {
        ParticleSystem ps = parent.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.startColor = color;
        main.startSize = size;
        main.startLifetime = lifetime;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = CreateUnlitMaterial(Color.white);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        return ps;
    }

    /// <summary>
    /// Create a gradient from colors
    /// </summary>
    public static Gradient CreateGradient(Color start, Color end)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(start, 0.0f), new GradientColorKey(end, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        return gradient;
    }

    /// <summary>
    /// Create a fade-out gradient
    /// </summary>
    public static Gradient CreateFadeGradient(Color color)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        return gradient;
    }
}
