using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class ShaderGlassBuiltinEffect : MonoBehaviour
{
    [Serializable]
    public class Settings
    {
        public ShaderGlassPreset preset;
        public Vector2Int targetInputResolution = new Vector2Int(320, 240);
        public float outputScale = 1.0f;
        public bool freeScale = false;
        public bool flipHorizontal = false;
        public bool flipVertical = false;
        public bool clearBlackBars = false;
        public bool matchShaderGlassGamma = true;
    }

    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    static readonly int MainTexTexelSizeId = Shader.PropertyToID("_MainTex_TexelSize");
    static readonly int SourceId = Shader.PropertyToID("Source");
    static readonly int SourceSizeId = Shader.PropertyToID("SourceSize");
    static readonly int OutputSizeId = Shader.PropertyToID("OutputSize");
    static readonly int FrameCountId = Shader.PropertyToID("FrameCount");
    static readonly int OriginalId = Shader.PropertyToID("Original");
    static readonly int OriginalSizeId = Shader.PropertyToID("OriginalSize");
    static readonly int FinalViewportSizeId = Shader.PropertyToID("FinalViewportSize");
    static readonly int UVScaleOffsetId = Shader.PropertyToID("_UVScaleOffset");
    static readonly int ColorConversionId = Shader.PropertyToID("_ColorConversion");

    public Settings settings = new Settings();

    readonly Dictionary<string, int> m_PropertyIds = new Dictionary<string, int>(64);
    readonly List<RenderTexture> m_PassOutputs = new List<RenderTexture>();
    readonly List<RenderTexture> m_PassFeedbacks = new List<RenderTexture>();
    readonly List<RenderTexture> m_History = new List<RenderTexture>();

    ShaderGlassPreset m_ActivePreset;
    RenderTexture m_Original;
    RenderTexture m_FinalOutput;
    Material m_PreprocessMaterial;
    Vector2Int m_OriginalSize;
    Vector2Int m_ViewportSize;
    Vector2Int[] m_PassOutputSizes = Array.Empty<Vector2Int>();
    Vector2Int[] m_PassSourceSizes = Array.Empty<Vector2Int>();
    int m_CameraWidth;
    int m_CameraHeight;
    int m_HistoryWriteIndex;
    int m_LastPassCount = -1;
    int m_LastHistoryCount = -1;
    int m_BoxX;
    int m_BoxY;
    bool m_ApplyGammaConversion;
    bool m_InvalidPresetWarned;

    void OnDisable()
    {
        ReleaseRenderTexture(ref m_Original);
        ReleaseRenderTexture(ref m_FinalOutput);
        ReleaseRenderTextures(m_PassOutputs);
        ReleaseRenderTextures(m_PassFeedbacks);
        ReleaseRenderTextures(m_History);

        if (m_PreprocessMaterial != null)
        {
            DestroyObject(m_PreprocessMaterial);
            m_PreprocessMaterial = null;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (source == null || destination == null)
            return;

        if (!IsPresetValid())
        {
            Graphics.Blit(source, destination);
            return;
        }

        EnsurePreprocessMaterial();
        if (m_PreprocessMaterial == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        m_ApplyGammaConversion = settings.matchShaderGlassGamma && QualitySettings.activeColorSpace == ColorSpace.Linear;

        EnsurePassLists();
        CalculateSizes(source.width, source.height);
        EnsureTargets(source);
        SetupGlobalResources();

        RunPrepass(source);
        RunShaderPasses();
        UpdateFeedback();
        UpdateHistory();

        if (settings.clearBlackBars && (m_BoxX != 0 || m_BoxY != 0))
            ClearBlackBars(destination);

        BlitFinal(destination);
    }

    bool IsPresetValid()
    {
        if (settings == null || settings.preset == null || settings.preset.passes.Count == 0)
            return false;

        if (!ReferenceEquals(settings.preset, m_ActivePreset))
        {
            m_ActivePreset = settings.preset;
            m_InvalidPresetWarned = false;
        }

        for (int i = 0; i < settings.preset.passes.Count; i++)
        {
            var pass = settings.preset.passes[i];
            if (pass.material == null)
            {
                if (!m_InvalidPresetWarned)
                {
                    Debug.LogWarning("ShaderGlassBuiltinEffect: pass material is missing.");
                    m_InvalidPresetWarned = true;
                }
                return false;
            }
            if (pass.materialPassIndex < 0 || pass.materialPassIndex >= pass.material.passCount)
            {
                if (!m_InvalidPresetWarned)
                {
                    Debug.LogWarning("ShaderGlassBuiltinEffect: pass index is out of range.");
                    m_InvalidPresetWarned = true;
                }
                return false;
            }
        }

        return true;
    }

    void EnsurePreprocessMaterial()
    {
        if (m_PreprocessMaterial != null)
            return;

        Shader shader = Shader.Find("Hidden/ShaderGlass/BuiltIn/Preprocess");
        if (shader == null)
            return;

        m_PreprocessMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void EnsurePassLists()
    {
        int passCount = settings.preset.passes.Count;
        int historyCount = Math.Max(0, settings.preset.historyCount);
        bool needsFeedback = settings.preset.enableFeedback;
        int expectedFeedbackCount = needsFeedback ? passCount : 0;

        if (passCount != m_LastPassCount)
        {
            m_LastPassCount = passCount;
            ReleaseRenderTextures(m_PassOutputs);
            ReleaseRenderTextures(m_PassFeedbacks);
            m_PassOutputs.Clear();
            m_PassFeedbacks.Clear();
            int outputCount = Math.Max(0, passCount - 1);
            for (int i = 0; i < outputCount; i++)
                m_PassOutputs.Add(null);

            m_PassOutputSizes = new Vector2Int[passCount];
            m_PassSourceSizes = new Vector2Int[passCount];
            m_HistoryWriteIndex = 0;
        }

        if (m_PassFeedbacks.Count != expectedFeedbackCount)
        {
            ReleaseRenderTextures(m_PassFeedbacks);
            m_PassFeedbacks.Clear();
            for (int i = 0; i < expectedFeedbackCount; i++)
                m_PassFeedbacks.Add(null);
        }

        if (historyCount != m_LastHistoryCount)
        {
            m_LastHistoryCount = historyCount;
            ReleaseRenderTextures(m_History);
            m_History.Clear();
            for (int i = 0; i < historyCount; i++)
                m_History.Add(null);
            m_HistoryWriteIndex = 0;
        }
    }

    void EnsureTargets(RenderTexture source)
    {
        var cameraDesc = source.descriptor;
        cameraDesc.msaaSamples = 1;
        cameraDesc.depthBufferBits = 0;
        cameraDesc.volumeDepth = 1;
        cameraDesc.useMipMap = false;
        cameraDesc.autoGenerateMips = false;

        var originalDesc = cameraDesc;
        originalDesc.width = m_OriginalSize.x;
        originalDesc.height = m_OriginalSize.y;
        originalDesc.graphicsFormat = GetPassFormat(originalDesc.graphicsFormat, ShaderGlassPassFormat.Default);
        EnsureRenderTexture(ref m_Original, originalDesc, FilterMode.Point, TextureWrapMode.Clamp, "_SG_Original");

        for (int i = 0; i < m_PassOutputs.Count; i++)
        {
            var pass = settings.preset.passes[i];
            var desc = cameraDesc;
            desc.width = m_PassOutputSizes[i].x;
            desc.height = m_PassOutputSizes[i].y;
            desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
            var output = m_PassOutputs[i];
            EnsureRenderTexture(ref output, desc, pass.filterMode, pass.wrapMode, "_SG_PassOutput" + i);
            m_PassOutputs[i] = output;
        }

        if (settings.preset.enableFeedback)
        {
            for (int i = 0; i < settings.preset.passes.Count; i++)
            {
                var pass = settings.preset.passes[i];
                var desc = cameraDesc;
                desc.width = m_PassOutputSizes[i].x;
                desc.height = m_PassOutputSizes[i].y;
                desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
                var feedback = m_PassFeedbacks[i];
                EnsureRenderTexture(ref feedback, desc, pass.filterMode, pass.wrapMode, "_SG_PassFeedback" + i);
                m_PassFeedbacks[i] = feedback;
            }
        }

        for (int i = 0; i < m_History.Count; i++)
        {
            var historyDesc = cameraDesc;
            historyDesc.width = m_OriginalSize.x;
            historyDesc.height = m_OriginalSize.y;
            historyDesc.graphicsFormat = GetPassFormat(historyDesc.graphicsFormat, ShaderGlassPassFormat.Default);
            var history = m_History[i];
            EnsureRenderTexture(ref history, historyDesc, FilterMode.Point, TextureWrapMode.Clamp, "_SG_History" + i);
            m_History[i] = history;
        }

        int lastIndex = settings.preset.passes.Count - 1;
        if (lastIndex >= 0)
        {
            var pass = settings.preset.passes[lastIndex];
            var desc = cameraDesc;
            desc.width = m_ViewportSize.x;
            desc.height = m_ViewportSize.y;
            desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
            EnsureRenderTexture(ref m_FinalOutput, desc, pass.filterMode, pass.wrapMode, "_SG_FinalOutput");
        }
    }

    void CalculateSizes(int cameraWidth, int cameraHeight)
    {
        m_CameraWidth = cameraWidth;
        m_CameraHeight = cameraHeight;

        int targetW = Mathf.Max(1, settings.targetInputResolution.x);
        int targetH = Mathf.Max(1, settings.targetInputResolution.y);
        float inputW = cameraWidth / (float)targetW;
        float inputH = cameraHeight / (float)targetH;
        float targetAspect = targetW / (float)targetH;
        float cameraAspect = cameraHeight > 0 ? cameraWidth / (float)cameraHeight : 1.0f;
        float aspectRatio = cameraAspect / targetAspect;

        inputW = Mathf.Max(0.0001f, inputW);
        inputH = Mathf.Max(0.0001f, inputH);
        aspectRatio = Mathf.Max(0.0001f, aspectRatio);

        m_OriginalSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(cameraWidth / inputW)),
            Mathf.Max(1, Mathf.RoundToInt(cameraHeight / inputH)));

        float outputScale = Mathf.Max(0.0001f, settings.outputScale);
        float outputScaleW = aspectRatio / outputScale;
        float outputScaleH = 1.0f / outputScale;

        float clientW = cameraWidth;
        float clientH = cameraHeight;

        if (!settings.freeScale)
        {
            clientW = Mathf.Round(cameraWidth / outputScaleW);
            clientH = Mathf.Round(cameraHeight / outputScaleH);
        }

        float boxX = 0.0f;
        float boxY = 0.0f;
        float inputAspect = cameraHeight > 0 ? (cameraWidth / (float)cameraHeight) : 1.0f;
        float outputAspect = (clientW * outputScaleW) / (clientH * outputScaleH);

        if (outputAspect > inputAspect)
        {
            float newWidth = Mathf.Round(clientH * (outputScaleH / outputScaleW) * inputAspect);
            boxX = (clientW - newWidth) * 0.5f;
            clientW = newWidth;
        }
        else if (outputAspect < inputAspect)
        {
            float newHeight = Mathf.Round(clientW * (outputScaleW / outputScaleH) / inputAspect);
            boxY = (clientH - newHeight) * 0.5f;
            clientH = newHeight;
        }

        m_ViewportSize = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(clientW)),
            Mathf.Max(1, Mathf.RoundToInt(clientH)));

        m_BoxX = Mathf.RoundToInt(boxX);
        m_BoxY = Mathf.RoundToInt(boxY);

        int sourceW = m_OriginalSize.x;
        int sourceH = m_OriginalSize.y;
        for (int i = 0; i < settings.preset.passes.Count; i++)
        {
            m_PassSourceSizes[i] = new Vector2Int(sourceW, sourceH);
            int outputW;
            int outputH;
            if (i == settings.preset.passes.Count - 1)
            {
                outputW = m_ViewportSize.x;
                outputH = m_ViewportSize.y;
            }
            else
            {
                var pass = settings.preset.passes[i];
                outputW = ScaleDimension(sourceW, m_ViewportSize.x, pass.scaleTypeX, pass.scaleX);
                outputH = ScaleDimension(sourceH, m_ViewportSize.y, pass.scaleTypeY, pass.scaleY);
            }

            outputW = Mathf.Max(1, outputW);
            outputH = Mathf.Max(1, outputH);
            m_PassOutputSizes[i] = new Vector2Int(outputW, outputH);
            sourceW = outputW;
            sourceH = outputH;
        }
    }

    int ScaleDimension(int source, int viewport, ShaderGlassScaleType scaleType, float scale)
    {
        switch (scaleType)
        {
            case ShaderGlassScaleType.Viewport:
                return Mathf.RoundToInt(viewport * scale);
            case ShaderGlassScaleType.Absolute:
                return Mathf.RoundToInt(scale);
            default:
                return Mathf.RoundToInt(source * scale);
        }
    }

    GraphicsFormat GetPassFormat(GraphicsFormat fallback, ShaderGlassPassFormat format)
    {
        GraphicsFormat desired = fallback;
        if (format == ShaderGlassPassFormat.SRGB)
            desired = GraphicsFormat.R8G8B8A8_SRGB;
        else if (format == ShaderGlassPassFormat.Float16)
            desired = GraphicsFormat.R16G16B16A16_SFloat;
        else if (m_ApplyGammaConversion && GraphicsFormatUtility.IsSRGBFormat(desired))
            desired = GraphicsFormatUtility.GetLinearFormat(desired);

        if (!SystemInfo.IsFormatSupported(desired, GraphicsFormatUsage.Render))
            return fallback;

        return desired;
    }

    void SetupGlobalResources()
    {
        Shader.SetGlobalTexture(OriginalId, m_Original);
        Shader.SetGlobalVector(OriginalSizeId, SizeVec(m_OriginalSize));
        Shader.SetGlobalVector(FinalViewportSizeId, SizeVec(m_ViewportSize));

        for (int i = 0; i < settings.preset.passes.Count; i++)
        {
            var outputSize = SizeVec(m_PassOutputSizes[i]);
            Shader.SetGlobalVector(GetId("PassOutputSize" + i), outputSize);

            if (i < m_PassOutputs.Count)
                Shader.SetGlobalTexture(GetId("PassOutput" + i), m_PassOutputs[i]);

            if (settings.preset.enableFeedback && i < m_PassFeedbacks.Count)
                Shader.SetGlobalTexture(GetId("PassFeedback" + i), m_PassFeedbacks[i]);

            var alias = settings.preset.passes[i].alias;
            if (!string.IsNullOrWhiteSpace(alias) && i < m_PassOutputs.Count)
            {
                Shader.SetGlobalTexture(GetId(alias), m_PassOutputs[i]);
                Shader.SetGlobalVector(GetId(alias + "Size"), outputSize);
                if (settings.preset.enableFeedback && i < m_PassFeedbacks.Count)
                    Shader.SetGlobalTexture(GetId(alias + "Feedback"), m_PassFeedbacks[i]);
            }
        }

        for (int h = 0; h < settings.preset.historyCount && h < m_History.Count; h++)
        {
            int historyIndex = GetHistoryIndex(h + 1);
            Shader.SetGlobalTexture(GetId("OriginalHistory" + (h + 1)), m_History[historyIndex]);
            Shader.SetGlobalVector(GetId("OriginalHistory" + (h + 1) + "Size"), SizeVec(m_OriginalSize));
        }

        foreach (var binding in settings.preset.textures)
        {
            if (binding == null || binding.texture == null || string.IsNullOrWhiteSpace(binding.name))
                continue;

            Shader.SetGlobalTexture(GetId(binding.name), binding.texture);
            Shader.SetGlobalVector(GetId(binding.name + "Size"), SizeVec(binding.texture.width, binding.texture.height));
        }
    }

    void RunPrepass(RenderTexture source)
    {
        var uvScale = new Vector2(settings.flipHorizontal ? -1.0f : 1.0f, settings.flipVertical ? -1.0f : 1.0f);
        var uvOffset = new Vector2(settings.flipHorizontal ? 1.0f : 0.0f, settings.flipVertical ? 1.0f : 0.0f);
        uvOffset += new Vector2(0.0001f, 0.0001f);
        var uvScaleOffset = new Vector4(uvScale.x, uvScale.y, uvOffset.x, uvOffset.y);

        m_PreprocessMaterial.SetTexture(MainTexId, source);
        m_PreprocessMaterial.SetVector(UVScaleOffsetId, uvScaleOffset);
        m_PreprocessMaterial.SetFloat(ColorConversionId, m_ApplyGammaConversion ? 1.0f : 0.0f);

        Graphics.Blit(source, m_Original, m_PreprocessMaterial, 0);
    }

    void RunShaderPasses()
    {
        int frameCount = Time.frameCount;
        RenderTexture source = m_Original;

        for (int i = 0; i < settings.preset.passes.Count; i++)
        {
            var pass = settings.preset.passes[i];
            bool isLast = (i == settings.preset.passes.Count - 1);
            RenderTexture dest = isLast ? m_FinalOutput : m_PassOutputs[i];

            var sourceSize = m_PassSourceSizes[i];
            var outputSize = m_PassOutputSizes[i];

            pass.material.SetTexture(SourceId, source);
            pass.material.SetVector(SourceSizeId, SizeVec(sourceSize));
            pass.material.SetVector(OutputSizeId, SizeVec(outputSize));
            pass.material.SetFloat(FrameCountId, ApplyFrameCountMod(frameCount, pass.frameCountMod));
            pass.material.SetVector(MainTexTexelSizeId, TexelVec(sourceSize));

            ApplyOverrides(pass.material, settings.preset.floatOverrides, settings.preset.vectorOverrides);
            ApplyOverrides(pass.material, pass.floatOverrides, pass.vectorOverrides);

            Graphics.Blit(source, dest, pass.material, pass.materialPassIndex);
            source = dest;
        }
    }

    void UpdateFeedback()
    {
        if (!settings.preset.enableFeedback || m_PassFeedbacks.Count == 0)
            return;

        for (int i = 0; i < m_PassOutputs.Count; i++)
            CopyTexture(m_PassOutputs[i], m_PassFeedbacks[i]);

        int lastIndex = settings.preset.passes.Count - 1;
        if (lastIndex >= 0 && lastIndex < m_PassFeedbacks.Count)
            CopyTexture(m_FinalOutput, m_PassFeedbacks[lastIndex]);
    }

    void UpdateHistory()
    {
        if (m_History.Count == 0)
            return;

        CopyTexture(m_Original, m_History[m_HistoryWriteIndex]);
        m_HistoryWriteIndex = (m_HistoryWriteIndex + 1) % m_History.Count;
    }

    void CopyTexture(RenderTexture source, RenderTexture dest)
    {
        if (TryCopyTexture(source, dest))
            return;

        Graphics.Blit(source, dest);
    }

    bool TryCopyTexture(RenderTexture source, RenderTexture dest)
    {
        if (source == null || dest == null)
            return false;
        if (source.width != dest.width || source.height != dest.height)
            return false;
        if (source.graphicsFormat != dest.graphicsFormat)
            return false;
        if (source.antiAliasing != dest.antiAliasing)
            return false;
        if (source.volumeDepth != dest.volumeDepth)
            return false;

        Graphics.CopyTexture(source, dest);
        return true;
    }

    void BlitFinal(RenderTexture destination)
    {
        if (m_FinalOutput == null)
            return;

        float colorConversion = m_ApplyGammaConversion ? 2.0f : 0.0f;
        m_PreprocessMaterial.SetTexture(MainTexId, m_FinalOutput);
        m_PreprocessMaterial.SetVector(UVScaleOffsetId, new Vector4(1, 1, 0, 0));
        m_PreprocessMaterial.SetFloat(ColorConversionId, colorConversion);

        var rect = new Rect(m_BoxX, m_BoxY, m_ViewportSize.x, m_ViewportSize.y);
        BlitToViewport(m_PreprocessMaterial, rect, destination);
    }

    void BlitToViewport(Material material, Rect viewport, RenderTexture destination)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = destination;
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Viewport(viewport);
        material.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.TexCoord2(0, 0);
        GL.Vertex3(0, 0, 0);
        GL.TexCoord2(1, 0);
        GL.Vertex3(1, 0, 0);
        GL.TexCoord2(1, 1);
        GL.Vertex3(1, 1, 0);
        GL.TexCoord2(0, 1);
        GL.Vertex3(0, 1, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = previous;
    }

    void ClearBlackBars(RenderTexture destination)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = destination;
        GL.Clear(false, true, Color.black);
        RenderTexture.active = previous;
    }

    float ApplyFrameCountMod(int frameCount, int mod)
    {
        if (mod <= 0)
            return frameCount;
        return frameCount % mod;
    }

    void ApplyOverrides(Material material, List<ShaderGlassFloatOverride> floats, List<ShaderGlassVectorOverride> vectors)
    {
        if (floats != null)
        {
            foreach (var ov in floats)
            {
                if (ov == null || string.IsNullOrWhiteSpace(ov.name))
                    continue;
                material.SetFloat(GetId(ov.name), ov.value);
            }
        }

        if (vectors != null)
        {
            foreach (var ov in vectors)
            {
                if (ov == null || string.IsNullOrWhiteSpace(ov.name))
                    continue;
                material.SetVector(GetId(ov.name), ov.value);
            }
        }
    }

    Vector4 SizeVec(Vector2Int size)
    {
        return SizeVec(size.x, size.y);
    }

    Vector4 SizeVec(int width, int height)
    {
        float w = Mathf.Max(1.0f, width);
        float h = Mathf.Max(1.0f, height);
        return new Vector4(w, h, 1.0f / w, 1.0f / h);
    }

    Vector4 TexelVec(Vector2Int size)
    {
        float w = Mathf.Max(1.0f, size.x);
        float h = Mathf.Max(1.0f, size.y);
        return new Vector4(1.0f / w, 1.0f / h, w, h);
    }

    int GetHistoryIndex(int historySlot)
    {
        if (m_History.Count == 0)
            return 0;

        int index = m_HistoryWriteIndex - historySlot;
        while (index < 0)
            index += m_History.Count;
        return index % m_History.Count;
    }

    int GetId(string name)
    {
        if (m_PropertyIds.TryGetValue(name, out int id))
            return id;

        id = Shader.PropertyToID(name);
        m_PropertyIds[name] = id;
        return id;
    }

    void EnsureRenderTexture(ref RenderTexture texture, RenderTextureDescriptor desc, FilterMode filter, TextureWrapMode wrap, string name)
    {
        if (texture != null &&
            texture.width == desc.width &&
            texture.height == desc.height &&
            texture.graphicsFormat == desc.graphicsFormat &&
            texture.antiAliasing == desc.msaaSamples)
        {
            return;
        }

        ReleaseRenderTexture(ref texture);
        texture = new RenderTexture(desc)
        {
            name = name,
            filterMode = filter,
            wrapMode = wrap
        };
        texture.Create();
    }

    void ReleaseRenderTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        DestroyObject(texture);
        texture = null;
    }

    void ReleaseRenderTextures(List<RenderTexture> textures)
    {
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] == null)
                continue;
            textures[i].Release();
            DestroyObject(textures[i]);
            textures[i] = null;
        }
    }

    void DestroyObject(UnityEngine.Object obj)
    {
        if (obj == null)
            return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
