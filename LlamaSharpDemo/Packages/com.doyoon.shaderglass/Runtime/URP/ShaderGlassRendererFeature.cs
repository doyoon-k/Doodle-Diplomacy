using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class ShaderGlassRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public ShaderGlassPreset preset;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRendering;
        public Vector2Int targetInputResolution = new Vector2Int(320, 240);
        public float outputScale = 1.0f;
        public bool freeScale = false;
        public bool flipHorizontal = false;
        public bool flipVertical = false;
        public bool clearBlackBars = false;
        public bool matchShaderGlassGamma = true;

        [Header("Single Camera Tablet Re-Render")]
        public bool renderExcludedLayersAfterPostProcess = true;
        public LayerMask excludedLayerMask = 1 << 8;
        public RenderPassEvent excludedLayerRenderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    class ExcludedLayerAfterPostProcessPass : ScriptableRenderPass
    {
        static readonly ShaderTagId[] ShaderTagIds =
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("LightweightForward")
        };

        readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler("RenderExcludedLayersAfterPostProcess");
        LayerMask m_LayerMask;
        RTHandle m_CameraColor;
        RTHandle m_CameraDepth;

        public void Setup(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
            m_CameraColor = null;
            m_CameraDepth = null;
        }

        [Obsolete("Compatibility Mode API (RenderGraph disabled).", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
#pragma warning disable CS0618
            m_CameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            m_CameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
#pragma warning restore CS0618
        }

        [Obsolete("Compatibility Mode API (RenderGraph disabled).", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_LayerMask.value == 0 || m_CameraColor == null || m_CameraDepth == null)
                return;

            var camera = renderingData.cameraData.camera;
            if (camera == null)
                return;

            if (!camera.TryGetCullingParameters(out ScriptableCullingParameters cullingParameters))
                return;

            cullingParameters.cullingMask = (uint)m_LayerMask.value;
            CullingResults cullingResults = context.Cull(ref cullingParameters);

            CommandBuffer cmd = CommandBufferPool.Get("RenderExcludedLayersAfterPostProcess");
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                CoreUtils.SetRenderTarget(cmd, m_CameraColor, m_CameraDepth);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                DrawingSettings opaqueDrawing = CreateDrawingSettings(
                    ShaderTagIds[0],
                    ref renderingData,
                    renderingData.cameraData.defaultOpaqueSortFlags);
                for (int i = 1; i < ShaderTagIds.Length; i++)
                    opaqueDrawing.SetShaderPassName(i, ShaderTagIds[i]);

                var opaqueFilter = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);
                context.DrawRenderers(cullingResults, ref opaqueDrawing, ref opaqueFilter);

                DrawingSettings transparentDrawing = CreateDrawingSettings(
                    ShaderTagIds[0],
                    ref renderingData,
                    SortingCriteria.CommonTransparent);
                for (int i = 1; i < ShaderTagIds.Length; i++)
                    transparentDrawing.SetShaderPassName(i, ShaderTagIds[i]);

                var transparentFilter = new FilteringSettings(RenderQueueRange.transparent, m_LayerMask);
                context.DrawRenderers(cullingResults, ref transparentDrawing, ref transparentFilter);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    class ShaderGlassRenderPass : ScriptableRenderPass
    {
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

        readonly Dictionary<string, int> m_PropertyIds = new Dictionary<string, int>(64);
        readonly List<RTHandle> m_PassOutputs = new List<RTHandle>();
        readonly List<RTHandle> m_PassFeedbacks = new List<RTHandle>();
        readonly List<RTHandle> m_History = new List<RTHandle>();
        readonly MaterialPropertyBlock m_PropertyBlock = new MaterialPropertyBlock();
        readonly Settings m_Settings;

        RTHandle m_CameraColor;
        RTHandle m_Original;
        RTHandle m_FinalOutput;
        Material m_BlitMaterial;
        ShaderGlassPreset m_ActivePreset;
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
        bool m_RenderGraphWarned;
        bool m_InvalidPresetWarned;
        bool m_MissingBlitShaderWarned;

        public ShaderGlassRenderPass(Settings settings)
        {
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("ShaderGlass");
        }

        public void Setup()
        {
            m_CameraColor = null;
            m_ApplyGammaConversion = false;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!m_RenderGraphWarned)
            {
                Debug.LogWarning("ShaderGlassRendererFeature runs in Compatibility Mode. Disable RenderGraph in the URP Renderer asset.");
                m_RenderGraphWarned = true;
            }
        }

        [Obsolete("Compatibility Mode API (RenderGraph disabled).", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!IsPresetValid())
                return;

            EnsureBlitMaterial();
            if (m_BlitMaterial == null)
                return;

            var cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            if (cameraDesc.width <= 0 || cameraDesc.height <= 0)
                return;

            m_ApplyGammaConversion = m_Settings.matchShaderGlassGamma && QualitySettings.activeColorSpace == ColorSpace.Linear;
            EnsurePassLists();
            CalculateSizes(cameraDesc.width, cameraDesc.height);
            EnsureTargets(cameraDesc);
#pragma warning disable CS0618
            m_CameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
#pragma warning restore CS0618
        }

        [Obsolete("Compatibility Mode API (RenderGraph disabled).", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!IsPresetValid() || m_BlitMaterial == null || m_CameraColor == null)
                return;

            var cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            if (cameraDesc.width <= 0 || cameraDesc.height <= 0)
                return;

            if (m_CameraColor == null || m_CameraColor.rt == null)
            {
#pragma warning disable CS0618
                m_CameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
#pragma warning restore CS0618
            }

            if (m_CameraColor == null || m_CameraColor.rt == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("ShaderGlass");
            using (new ProfilingScope(cmd, profilingSampler))
            {
                var cameraData = renderingData.cameraData;
                var viewMatrix = cameraData.GetViewMatrix();
                var projMatrix = cameraData.GetGPUProjectionMatrix();

                RenderingUtils.SetViewAndProjectionMatrices(cmd, Matrix4x4.identity, Matrix4x4.identity, true);

                SetupGlobalResources(cmd);
                RunPrepass(cmd);
                RunShaderPasses(cmd);
                UpdateFeedback(cmd);
                UpdateHistory(cmd);

                RenderingUtils.SetViewAndProjectionMatrices(cmd, viewMatrix, projMatrix, true);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            ReleaseRTHandle(ref m_Original);
            ReleaseRTHandle(ref m_FinalOutput);
            ReleaseRTHandles(m_PassOutputs);
            ReleaseRTHandles(m_PassFeedbacks);
            ReleaseRTHandles(m_History);

            if (m_BlitMaterial != null)
            {
                CoreUtils.Destroy(m_BlitMaterial);
                m_BlitMaterial = null;
            }
        }

        bool IsPresetValid()
        {
            if (m_Settings == null || m_Settings.preset == null || m_Settings.preset.passes.Count == 0)
                return false;

            if (!ReferenceEquals(m_Settings.preset, m_ActivePreset))
            {
                m_ActivePreset = m_Settings.preset;
                m_InvalidPresetWarned = false;
            }

            for (int i = 0; i < m_Settings.preset.passes.Count; i++)
            {
                var pass = m_Settings.preset.passes[i];
                if (pass.material == null)
                {
                    if (!m_InvalidPresetWarned)
                    {
                        Debug.LogWarning("ShaderGlassRendererFeature: pass material is missing.");
                        m_InvalidPresetWarned = true;
                    }
                    return false;
                }
                if (pass.materialPassIndex < 0 || pass.materialPassIndex >= pass.material.passCount)
                {
                    if (!m_InvalidPresetWarned)
                    {
                        Debug.LogWarning("ShaderGlassRendererFeature: pass index is out of range.");
                        m_InvalidPresetWarned = true;
                    }
                    return false;
                }
            }

            return true;
        }

        void EnsureBlitMaterial()
        {
            if (m_BlitMaterial != null)
                return;

            Shader shader = Shader.Find("Hidden/ShaderGlass/Preprocess");
            if (shader == null)
            {
                if (!m_MissingBlitShaderWarned)
                {
                    Debug.LogWarning("ShaderGlassRendererFeature: missing shader 'Hidden/ShaderGlass/Preprocess'. Add it to Always Included Shaders for player builds.");
                    m_MissingBlitShaderWarned = true;
                }
                return;
            }

            m_BlitMaterial = CoreUtils.CreateEngineMaterial(shader);
            m_MissingBlitShaderWarned = false;
        }

        void EnsurePassLists()
        {
            int passCount = m_Settings.preset.passes.Count;
            int historyCount = Math.Max(0, m_Settings.preset.historyCount);
            bool needsFeedback = m_Settings.preset.enableFeedback;
            int expectedFeedbackCount = needsFeedback ? passCount : 0;

            if (passCount != m_LastPassCount)
            {
                m_LastPassCount = passCount;
                ReleaseRTHandles(m_PassOutputs);
                ReleaseRTHandles(m_PassFeedbacks);
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
                ReleaseRTHandles(m_PassFeedbacks);
                m_PassFeedbacks.Clear();
                for (int i = 0; i < expectedFeedbackCount; i++)
                    m_PassFeedbacks.Add(null);
            }

            if (historyCount != m_LastHistoryCount)
            {
                m_LastHistoryCount = historyCount;
                ReleaseRTHandles(m_History);
                m_History.Clear();
                for (int i = 0; i < historyCount; i++)
                    m_History.Add(null);
                m_HistoryWriteIndex = 0;
            }
        }

        void EnsureTargets(RenderTextureDescriptor cameraDesc)
        {
            cameraDesc.msaaSamples = 1;
            cameraDesc.depthBufferBits = 0;
            cameraDesc.depthStencilFormat = GraphicsFormat.None;

            var originalDesc = cameraDesc;
            originalDesc.width = m_OriginalSize.x;
            originalDesc.height = m_OriginalSize.y;
            originalDesc.graphicsFormat = GetPassFormat(originalDesc.graphicsFormat, ShaderGlassPassFormat.Default);
            RenderingUtils.ReAllocateHandleIfNeeded(ref m_Original, originalDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SG_Original");

            for (int i = 0; i < m_PassOutputs.Count; i++)
            {
                var pass = m_Settings.preset.passes[i];
                var desc = cameraDesc;
                desc.width = m_PassOutputSizes[i].x;
                desc.height = m_PassOutputSizes[i].y;
                desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
                var output = m_PassOutputs[i];
                RenderingUtils.ReAllocateHandleIfNeeded(ref output, desc, pass.filterMode, pass.wrapMode, name: "_SG_PassOutput" + i);
                m_PassOutputs[i] = output;
            }

            if (m_Settings.preset.enableFeedback)
            {
                for (int i = 0; i < m_Settings.preset.passes.Count; i++)
                {
                    var pass = m_Settings.preset.passes[i];
                    var desc = cameraDesc;
                    desc.width = m_PassOutputSizes[i].x;
                    desc.height = m_PassOutputSizes[i].y;
                    desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
                    var feedback = m_PassFeedbacks[i];
                    RenderingUtils.ReAllocateHandleIfNeeded(ref feedback, desc, pass.filterMode, pass.wrapMode, name: "_SG_PassFeedback" + i);
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
                RenderingUtils.ReAllocateHandleIfNeeded(ref history, historyDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SG_History" + i);
                m_History[i] = history;
            }

            if (m_ApplyGammaConversion)
            {
                int lastIndex = m_Settings.preset.passes.Count - 1;
                if (lastIndex >= 0)
                {
                    var pass = m_Settings.preset.passes[lastIndex];
                    var desc = cameraDesc;
                    desc.width = m_ViewportSize.x;
                    desc.height = m_ViewportSize.y;
                    desc.graphicsFormat = GetPassFormat(desc.graphicsFormat, pass.format);
                    RenderingUtils.ReAllocateHandleIfNeeded(ref m_FinalOutput, desc, pass.filterMode, pass.wrapMode, name: "_SG_FinalOutput");
                }
            }
            else
            {
                ReleaseRTHandle(ref m_FinalOutput);
            }
        }

        void CalculateSizes(int cameraWidth, int cameraHeight)
        {
            m_CameraWidth = cameraWidth;
            m_CameraHeight = cameraHeight;
            int targetW = Mathf.Max(1, m_Settings.targetInputResolution.x);
            int targetH = Mathf.Max(1, m_Settings.targetInputResolution.y);
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

            float outputScale = Mathf.Max(0.0001f, m_Settings.outputScale);
            float outputScaleW = aspectRatio / outputScale;
            float outputScaleH = 1.0f / outputScale;

            float clientW = cameraWidth;
            float clientH = cameraHeight;

            if (!m_Settings.freeScale)
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
            for (int i = 0; i < m_Settings.preset.passes.Count; i++)
            {
                m_PassSourceSizes[i] = new Vector2Int(sourceW, sourceH);
                int outputW;
                int outputH;
                if (i == m_Settings.preset.passes.Count - 1)
                {
                    outputW = m_ViewportSize.x;
                    outputH = m_ViewportSize.y;
                }
                else
                {
                    var pass = m_Settings.preset.passes[i];
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

        void SetupGlobalResources(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(OriginalId, m_Original);
            cmd.SetGlobalVector(OriginalSizeId, SizeVec(m_OriginalSize));
            cmd.SetGlobalVector(FinalViewportSizeId, SizeVec(m_ViewportSize));

            for (int i = 0; i < m_Settings.preset.passes.Count; i++)
            {
                var outputSize = SizeVec(m_PassOutputSizes[i]);
                cmd.SetGlobalVector(GetId("PassOutputSize" + i), outputSize);

                if (i < m_PassOutputs.Count)
                    cmd.SetGlobalTexture(GetId("PassOutput" + i), m_PassOutputs[i]);

                if (m_Settings.preset.enableFeedback && i < m_PassFeedbacks.Count)
                    cmd.SetGlobalTexture(GetId("PassFeedback" + i), m_PassFeedbacks[i]);

                var alias = m_Settings.preset.passes[i].alias;
                if (!string.IsNullOrWhiteSpace(alias) && i < m_PassOutputs.Count)
                {
                    cmd.SetGlobalTexture(GetId(alias), m_PassOutputs[i]);
                    cmd.SetGlobalVector(GetId(alias + "Size"), outputSize);
                    if (m_Settings.preset.enableFeedback && i < m_PassFeedbacks.Count)
                        cmd.SetGlobalTexture(GetId(alias + "Feedback"), m_PassFeedbacks[i]);
                }
            }

            for (int h = 0; h < m_Settings.preset.historyCount && h < m_History.Count; h++)
            {
                int historyIndex = GetHistoryIndex(h + 1);
                cmd.SetGlobalTexture(GetId("OriginalHistory" + (h + 1)), m_History[historyIndex]);
                cmd.SetGlobalVector(GetId("OriginalHistory" + (h + 1) + "Size"), SizeVec(m_OriginalSize));
            }

            foreach (var binding in m_Settings.preset.textures)
            {
                if (binding == null || binding.texture == null || string.IsNullOrWhiteSpace(binding.name))
                    continue;

                cmd.SetGlobalTexture(GetId(binding.name), binding.texture);
                cmd.SetGlobalVector(GetId(binding.name + "Size"), SizeVec(binding.texture.width, binding.texture.height));
            }
        }

        void RunPrepass(CommandBuffer cmd)
        {
            var uvScale = new Vector2(m_Settings.flipHorizontal ? -1.0f : 1.0f, m_Settings.flipVertical ? -1.0f : 1.0f);
            var uvOffset = new Vector2(m_Settings.flipHorizontal ? 1.0f : 0.0f, m_Settings.flipVertical ? 1.0f : 0.0f);
            uvOffset += new Vector2(0.0001f, 0.0001f);
            var uvScaleOffset = new Vector4(uvScale.x, uvScale.y, uvOffset.x, uvOffset.y);

            m_PropertyBlock.Clear();
            m_PropertyBlock.SetTexture(MainTexId, m_CameraColor);
            m_PropertyBlock.SetVector(UVScaleOffsetId, uvScaleOffset);
            m_PropertyBlock.SetFloat(ColorConversionId, m_ApplyGammaConversion ? 1.0f : 0.0f);

            CoreUtils.SetRenderTarget(cmd, m_Original);
            SetViewport(cmd, 0, 0, m_OriginalSize.x, m_OriginalSize.y);
            DrawFullscreen(cmd, m_BlitMaterial, 0, m_PropertyBlock);
        }

        void RunShaderPasses(CommandBuffer cmd)
        {
            int frameCount = Time.frameCount;
            bool needsFinalBlit = m_ApplyGammaConversion && m_FinalOutput != null;

            if (m_Settings.clearBlackBars && (m_BoxX != 0 || m_BoxY != 0))
            {
                CoreUtils.SetRenderTarget(cmd, m_CameraColor);
                cmd.ClearRenderTarget(false, true, Color.black);
            }

            RTHandle source = m_Original;
            for (int i = 0; i < m_Settings.preset.passes.Count; i++)
            {
                var pass = m_Settings.preset.passes[i];
                bool isLast = (i == m_Settings.preset.passes.Count - 1);
                RTHandle dest = isLast ? (needsFinalBlit ? m_FinalOutput : m_CameraColor) : m_PassOutputs[i];

                var sourceSize = m_PassSourceSizes[i];
                var outputSize = m_PassOutputSizes[i];

                m_PropertyBlock.Clear();
                m_PropertyBlock.SetTexture(MainTexId, source);
                m_PropertyBlock.SetTexture(SourceId, source);
                m_PropertyBlock.SetVector(SourceSizeId, SizeVec(sourceSize));
                m_PropertyBlock.SetVector(OutputSizeId, SizeVec(outputSize));
                m_PropertyBlock.SetFloat(FrameCountId, ApplyFrameCountMod(frameCount, pass.frameCountMod));
                m_PropertyBlock.SetVector(MainTexTexelSizeId, TexelVec(sourceSize));

                ApplyOverrides(m_PropertyBlock, m_Settings.preset.floatOverrides, m_Settings.preset.vectorOverrides);
                ApplyOverrides(m_PropertyBlock, pass.floatOverrides, pass.vectorOverrides);

                CoreUtils.SetRenderTarget(cmd, dest);
                int viewportX = 0;
                int viewportY = 0;
                int viewportW = outputSize.x;
                int viewportH = outputSize.y;
                if (isLast)
                {
                    viewportW = m_ViewportSize.x;
                    viewportH = m_ViewportSize.y;
                    if (!needsFinalBlit && (m_BoxX != 0 || m_BoxY != 0))
                    {
                        viewportX = m_BoxX;
                        viewportY = m_BoxY;
                    }
                }
                SetViewport(cmd, viewportX, viewportY, viewportW, viewportH);

                DrawFullscreen(cmd, pass.material, pass.materialPassIndex, m_PropertyBlock);
                source = dest;
            }

            if (needsFinalBlit)
                BlitFinal(cmd);

            SetViewport(cmd, 0, 0, m_CameraWidth, m_CameraHeight);
        }

        void UpdateFeedback(CommandBuffer cmd)
        {
            if (!m_Settings.preset.enableFeedback || m_PassFeedbacks.Count == 0)
                return;

            for (int i = 0; i < m_PassOutputs.Count; i++)
                CopyTexture(cmd, m_PassOutputs[i], m_PassFeedbacks[i]);

            int lastIndex = m_Settings.preset.passes.Count - 1;
            if (lastIndex >= 0 && lastIndex < m_PassFeedbacks.Count)
            {
                var lastSource = m_ApplyGammaConversion && m_FinalOutput != null ? m_FinalOutput : m_CameraColor;
                CopyTexture(cmd, lastSource, m_PassFeedbacks[lastIndex]);
            }
        }

        void UpdateHistory(CommandBuffer cmd)
        {
            if (m_History.Count == 0)
                return;

            CopyTexture(cmd, m_Original, m_History[m_HistoryWriteIndex]);
            m_HistoryWriteIndex = (m_HistoryWriteIndex + 1) % m_History.Count;
        }

        bool TryCopyTexture(CommandBuffer cmd, RTHandle source, RTHandle dest)
        {
            if (source == null || dest == null)
                return false;

            var sourceRt = source.rt;
            var destRt = dest.rt;
            if (sourceRt == null || destRt == null)
                return false;

            if (sourceRt.width != destRt.width || sourceRt.height != destRt.height)
                return false;
            if (sourceRt.graphicsFormat != destRt.graphicsFormat)
                return false;
            if (sourceRt.dimension != destRt.dimension)
                return false;
            if (sourceRt.volumeDepth != destRt.volumeDepth)
                return false;
            if (sourceRt.antiAliasing != destRt.antiAliasing)
                return false;

            cmd.CopyTexture(source, dest);
            return true;
        }

        void CopyTexture(CommandBuffer cmd, RTHandle source, RTHandle dest)
        {
            // Prefer direct copies to avoid UV orientation differences across targets.
            if (TryCopyTexture(cmd, source, dest))
                return;

            m_PropertyBlock.Clear();
            m_PropertyBlock.SetTexture(MainTexId, source);
            m_PropertyBlock.SetVector(UVScaleOffsetId, new Vector4(1, 1, 0, 0));
            m_PropertyBlock.SetFloat(ColorConversionId, 0.0f);

            CoreUtils.SetRenderTarget(cmd, dest);
            int width = dest?.rt != null ? dest.rt.width : m_CameraWidth;
            int height = dest?.rt != null ? dest.rt.height : m_CameraHeight;
            SetViewport(cmd, 0, 0, width, height);
            DrawFullscreen(cmd, m_BlitMaterial, 0, m_PropertyBlock);
        }

        void BlitFinal(CommandBuffer cmd)
        {
            if (m_FinalOutput == null)
                return;

            m_PropertyBlock.Clear();
            m_PropertyBlock.SetTexture(MainTexId, m_FinalOutput);
            m_PropertyBlock.SetVector(UVScaleOffsetId, new Vector4(1, 1, 0, 0));
            m_PropertyBlock.SetFloat(ColorConversionId, 2.0f);

            CoreUtils.SetRenderTarget(cmd, m_CameraColor);
            if (m_BoxX != 0 || m_BoxY != 0)
            {
                SetViewport(cmd, m_BoxX, m_BoxY, m_ViewportSize.x, m_ViewportSize.y);
            }
            else
            {
                SetViewport(cmd, 0, 0, m_CameraWidth, m_CameraHeight);
            }

            DrawFullscreen(cmd, m_BlitMaterial, 0, m_PropertyBlock);
        }

        void SetViewport(CommandBuffer cmd, int x, int y, int width, int height)
        {
            cmd.SetViewport(new Rect(x, y, width, height));
        }

        float ApplyFrameCountMod(int frameCount, int mod)
        {
            if (mod <= 0)
                return frameCount;
            return frameCount % mod;
        }

        void ApplyOverrides(MaterialPropertyBlock block, List<ShaderGlassFloatOverride> floats, List<ShaderGlassVectorOverride> vectors)
        {
            if (floats != null)
            {
                foreach (var ov in floats)
                {
                    if (ov == null || string.IsNullOrWhiteSpace(ov.name))
                        continue;
                    block.SetFloat(GetId(ov.name), ov.value);
                }
            }

            if (vectors != null)
            {
                foreach (var ov in vectors)
                {
                    if (ov == null || string.IsNullOrWhiteSpace(ov.name))
                        continue;
                    block.SetVector(GetId(ov.name), ov.value);
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

        void ReleaseRTHandle(ref RTHandle handle)
        {
            if (handle == null)
                return;

            handle.Release();
            handle = null;
        }

        void ReleaseRTHandles(List<RTHandle> handles)
        {
            for (int i = 0; i < handles.Count; i++)
            {
                if (handles[i] == null)
                    continue;
                handles[i].Release();
                handles[i] = null;
            }
        }

        void DrawFullscreen(CommandBuffer cmd, Material material, int passIndex, MaterialPropertyBlock block)
        {
#pragma warning disable CS0618
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, passIndex, block);
#pragma warning restore CS0618
        }
    }

    public Settings settings = new Settings();
    ExcludedLayerAfterPostProcessPass m_ExcludedLayerPass;
    ShaderGlassRenderPass m_Pass;

    public override void Create()
    {
        m_ExcludedLayerPass = new ExcludedLayerAfterPostProcessPass();
        m_Pass = new ShaderGlassRenderPass(settings);
        m_Pass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
            return;

        // Skip off-screen capture cameras (for monitor/terminal RenderTexture pipelines).
        var camera = renderingData.cameraData.camera;
        if (camera != null && camera.targetTexture != null)
            return;

        if (m_ExcludedLayerPass != null &&
            settings != null &&
            settings.renderExcludedLayersAfterPostProcess &&
            settings.excludedLayerMask.value != 0)
        {
            m_ExcludedLayerPass.renderPassEvent = settings.excludedLayerRenderPassEvent;
            m_ExcludedLayerPass.Setup(settings.excludedLayerMask);
            renderer.EnqueuePass(m_ExcludedLayerPass);
        }

        if (settings == null || settings.preset == null || settings.preset.passes.Count == 0)
            return;

        m_Pass.renderPassEvent = settings.renderPassEvent;
        m_Pass.Setup();
        m_Pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass?.Dispose();
    }
}
