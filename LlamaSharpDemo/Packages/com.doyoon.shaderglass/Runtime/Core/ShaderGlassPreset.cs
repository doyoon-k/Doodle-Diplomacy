using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ShaderGlass/ShaderGlass Preset", fileName = "ShaderGlassPreset")]
public class ShaderGlassPreset : ScriptableObject
{
    public List<ShaderGlassPassSettings> passes = new List<ShaderGlassPassSettings>();
    public List<ShaderGlassTextureBinding> textures = new List<ShaderGlassTextureBinding>();
    public List<ShaderGlassFloatOverride> floatOverrides = new List<ShaderGlassFloatOverride>();
    public List<ShaderGlassVectorOverride> vectorOverrides = new List<ShaderGlassVectorOverride>();
    public bool enableFeedback = false;
    [Min(0)] public int historyCount = 0;
}

public enum ShaderGlassScaleType
{
    Relative,
    Viewport,
    Absolute
}

public enum ShaderGlassPassFormat
{
    Default,
    SRGB,
    Float16
}

[Serializable]
public class ShaderGlassPassSettings
{
    public Material material;
    public int materialPassIndex = 0;
    public string alias;
    public ShaderGlassScaleType scaleTypeX = ShaderGlassScaleType.Relative;
    public ShaderGlassScaleType scaleTypeY = ShaderGlassScaleType.Relative;
    public float scaleX = 1.0f;
    public float scaleY = 1.0f;
    public ShaderGlassPassFormat format = ShaderGlassPassFormat.Default;
    public FilterMode filterMode = FilterMode.Point;
    public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
    public int frameCountMod = 0;
    public List<ShaderGlassFloatOverride> floatOverrides = new List<ShaderGlassFloatOverride>();
    public List<ShaderGlassVectorOverride> vectorOverrides = new List<ShaderGlassVectorOverride>();
}

[Serializable]
public class ShaderGlassTextureBinding
{
    public string name;
    public Texture texture;
}

[Serializable]
public class ShaderGlassFloatOverride
{
    public string name;
    public float value;
}

[Serializable]
public class ShaderGlassVectorOverride
{
    public string name;
    public Vector4 value;
}
