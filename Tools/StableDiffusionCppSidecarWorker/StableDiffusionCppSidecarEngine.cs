using System.Runtime.InteropServices;
using System.Text;

internal sealed class StableDiffusionCppSidecarEngine
{
    private const long MaxLeakedOutputBytesBeforeRecycle = 192L * 1024L * 1024L;

    private readonly object _engineLock = new object();

    private IntPtr _sdContext = IntPtr.Zero;
    private ContextSignature _loadedSignature;
    private long _leakedOutputBytes;
    private bool _isBusy;
    private bool _shouldRecycleAfterResponse;

    public bool HasLoadedContext
    {
        get
        {
            lock (_engineLock)
            {
                return _sdContext != IntPtr.Zero;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_engineLock)
            {
                return _isBusy;
            }
        }
    }

    public bool ShouldRecycleAfterResponse
    {
        get
        {
            lock (_engineLock)
            {
                return _shouldRecycleAfterResponse;
            }
        }
    }

    public StableDiffusionCppWorkerGenerateResponse Generate(StableDiffusionCppWorkerGenerateRequest request)
    {
        lock (_engineLock)
        {
            if (_isBusy)
            {
                return new StableDiffusionCppWorkerGenerateResponse
                {
                    success = false,
                    errorMessage = "Generation is already running."
                };
            }

            _isBusy = true;

            try
            {
                return ExecuteLocked(request);
            }
            catch (Exception ex)
            {
                return new StableDiffusionCppWorkerGenerateResponse
                {
                    success = false,
                    errorMessage = $"Sidecar worker generation failed: {ex}"
                };
            }
            finally
            {
                _isBusy = false;
            }
        }
    }

    public void ReleaseContext()
    {
        lock (_engineLock)
        {
            DisposeContextUnlocked();
        }
    }

    private StableDiffusionCppWorkerGenerateResponse ExecuteLocked(
        StableDiffusionCppWorkerGenerateRequest request)
    {
        if (request == null)
        {
            return Failure("Request is null.", string.Empty, string.Empty);
        }

        var stdOut = new StringBuilder(512);
        var stdErr = new StringBuilder(512);
        ContextSignature signature = ContextSignature.From(request);
        if (!EnsureContext(signature, stdOut, stdErr, out string contextError))
        {
            return Failure(contextError ?? "Failed to initialize sd_ctx.", stdOut.ToString(), stdErr.ToString());
        }

        IntPtr nativeResults = IntPtr.Zero;
        IntPtr promptPtr = IntPtr.Zero;
        IntPtr negativePromptPtr = IntPtr.Zero;
        IntPtr initImagePtr = IntPtr.Zero;
        IntPtr maskImagePtr = IntPtr.Zero;
        IntPtr controlImagePtr = IntPtr.Zero;
        bool nativeOutputsNeedRecycle = false;
        long leakedOutputBytes = 0;

        try
        {
            var genParams = new StableDiffusionCppNativeBridge.SdImgGenParams();
            StableDiffusionCppNativeBridge.SdImgGenParamsInit(ref genParams);

            var sampleParams = new StableDiffusionCppNativeBridge.SdSampleParams();
            StableDiffusionCppNativeBridge.SdSampleParamsInit(ref sampleParams);

            StableDiffusionCppNativeBridge.SampleMethod sampleMethod =
                StableDiffusionCppNativeBridge.StrToSampleMethod(request.sampler ?? string.Empty);
            if (sampleMethod == StableDiffusionCppNativeBridge.SampleMethod.Count)
            {
                sampleMethod = StableDiffusionCppNativeBridge.GetDefaultSampleMethod(_sdContext);
                stdErr.AppendLine(
                    $"[SidecarWorker] Unknown sampler '{request.sampler}', falling back to {sampleMethod}.");
            }

            StableDiffusionCppNativeBridge.Scheduler scheduler =
                StableDiffusionCppNativeBridge.StrToScheduler(request.scheduler ?? string.Empty);
            if (scheduler == StableDiffusionCppNativeBridge.Scheduler.Count)
            {
                scheduler = StableDiffusionCppNativeBridge.GetDefaultScheduler(_sdContext, sampleMethod);
                stdErr.AppendLine(
                    $"[SidecarWorker] Unknown scheduler '{request.scheduler}', falling back to {scheduler}.");
            }

            sampleParams.sample_method = sampleMethod;
            sampleParams.scheduler = scheduler;
            sampleParams.sample_steps = Math.Max(1, request.steps);
            sampleParams.guidance.txt_cfg = Math.Max(0.1f, request.cfgScale);
            sampleParams.guidance.img_cfg = request.overrideImageCfgScale
                ? Math.Max(0.1f, request.imageCfgScale)
                : Math.Max(0.1f, request.cfgScale);

            var cacheParams = new StableDiffusionCppNativeBridge.SdCacheParams();
            StableDiffusionCppNativeBridge.SdCacheParamsInit(ref cacheParams);
            cacheParams.mode = request.useCacheMode
                ? ParseCacheMode(request.cacheMode, stdErr)
                : StableDiffusionCppNativeBridge.CacheMode.Disabled;

            promptPtr = StableDiffusionCppNativeBridge.StringToNative(request.prompt);
            negativePromptPtr = StableDiffusionCppNativeBridge.StringToNative(request.negativePrompt);
            initImagePtr = CopyImageToNative(request.initImage);
            maskImagePtr = CopyImageToNative(request.maskImage);
            controlImagePtr = CopyImageToNative(request.controlImage);

            genParams.prompt = promptPtr;
            genParams.negative_prompt = negativePromptPtr;
            genParams.clip_skip = -1;
            genParams.init_image = ToNativeImage(request.initImage, initImagePtr, fallbackChannels: 3);
            genParams.mask_image = ToNativeImage(request.maskImage, maskImagePtr, fallbackChannels: 1);
            genParams.control_image = ToNativeImage(request.controlImage, controlImagePtr, fallbackChannels: 3);
            genParams.width = Math.Max(64, request.width);
            genParams.height = Math.Max(64, request.height);
            genParams.sample_params = sampleParams;
            genParams.strength = Math.Clamp(request.strength, 0.01f, 1f);
            genParams.seed = request.seed;
            genParams.batch_count = Math.Max(1, request.batchCount);
            genParams.control_strength = Math.Clamp(request.controlStrength, 0f, 2f);
            genParams.vae_tiling_params.enabled = request.vaeTiling ? (byte)1 : (byte)0;
            genParams.cache = cacheParams;

            stdOut.AppendLine("[SidecarWorker] Generating with cached sd_ctx.");
            nativeResults = StableDiffusionCppNativeBridge.GenerateImage(_sdContext, ref genParams);
            if (nativeResults == IntPtr.Zero)
            {
                return Failure("Native generate_image returned null.", stdOut.ToString(), stdErr.ToString());
            }

            nativeOutputsNeedRecycle = true;

            int imageCount = Math.Max(1, request.batchCount);
            int imageSize = Marshal.SizeOf(typeof(StableDiffusionCppNativeBridge.SdImage));
            var outputs = new List<StableDiffusionCppWorkerImagePayload>(imageCount);
            for (int i = 0; i < imageCount; i++)
            {
                IntPtr imagePtr = IntPtr.Add(nativeResults, i * imageSize);
                StableDiffusionCppNativeBridge.SdImage nativeImage =
                    Marshal.PtrToStructure<StableDiffusionCppNativeBridge.SdImage>(imagePtr);
                outputs.Add(CopyOutputImage(nativeImage, out long imageByteLength));
                leakedOutputBytes = checked(leakedOutputBytes + imageByteLength);
            }

            TrackLeakedOutputBytes(leakedOutputBytes, stdErr);
            nativeResults = IntPtr.Zero;
            nativeOutputsNeedRecycle = false;

            return new StableDiffusionCppWorkerGenerateResponse
            {
                success = true,
                stdOut = stdOut.ToString(),
                stdErr = stdErr.ToString(),
                images = outputs.ToArray()
            };
        }
        catch (Exception ex)
        {
            stdErr.AppendLine(ex.ToString());
            return Failure($"Native worker invocation failed: {ex.Message}", stdOut.ToString(), stdErr.ToString());
        }
        finally
        {
            StableDiffusionCppNativeBridge.FreeHGlobal(promptPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(negativePromptPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(initImagePtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(maskImagePtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(controlImagePtr);

            if (nativeResults != IntPtr.Zero || nativeOutputsNeedRecycle)
            {
                long estimatedBytes = leakedOutputBytes > 0
                    ? leakedOutputBytes
                    : EstimateOutputBytes(request);
                TrackLeakedOutputBytes(estimatedBytes, stdErr, forceRecycle: true);
            }
        }
    }

    private bool EnsureContext(
        ContextSignature signature,
        StringBuilder stdOut,
        StringBuilder stdErr,
        out string error)
    {
        error = null;
        if (_sdContext != IntPtr.Zero && _loadedSignature != null && _loadedSignature.Matches(signature))
        {
            stdOut.AppendLine("[SidecarWorker] Reusing cached sd_ctx.");
            return true;
        }

        DisposeContextUnlocked();

        if (!StableDiffusionCppNativeBridge.TryEnsureLoaded(
                signature.RuntimeInstallDirectory,
                signature.NativeLibraryPath,
                out string loadError))
        {
            error = loadError;
            return false;
        }

        var ctxParams = new StableDiffusionCppNativeBridge.SdCtxParams();
        StableDiffusionCppNativeBridge.SdCtxParamsInit(ref ctxParams);

        IntPtr modelPathPtr = IntPtr.Zero;
        IntPtr vaePathPtr = IntPtr.Zero;
        IntPtr controlNetPathPtr = IntPtr.Zero;

        try
        {
            modelPathPtr = StableDiffusionCppNativeBridge.StringToNative(signature.ModelPath);
            vaePathPtr = StableDiffusionCppNativeBridge.StringToNative(signature.VaePath);
            controlNetPathPtr = StableDiffusionCppNativeBridge.StringToNative(signature.ControlNetPath);

            ctxParams.model_path = modelPathPtr;
            ctxParams.vae_path = vaePathPtr;
            ctxParams.control_net_path = controlNetPathPtr;
            ctxParams.vae_decode_only = 0;
            ctxParams.free_params_immediately = 0;
            ctxParams.n_threads = Math.Max(1, Environment.ProcessorCount);
            ctxParams.offload_params_to_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.enable_mmap = 1;
            ctxParams.keep_clip_on_cpu = signature.ClipOnCpu ? (byte)1 : (byte)0;
            ctxParams.keep_control_net_on_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.keep_vae_on_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.diffusion_flash_attn = signature.DiffusionFlashAttention ? (byte)1 : (byte)0;

            stdOut.AppendLine("[SidecarWorker] Loading new sd_ctx.");
            stdOut.AppendLine($"[SidecarWorker] Model={signature.ModelPath}");
            if (!string.IsNullOrWhiteSpace(signature.VaePath))
            {
                stdOut.AppendLine($"[SidecarWorker] VAE={signature.VaePath}");
            }

            if (!string.IsNullOrWhiteSpace(signature.ControlNetPath))
            {
                stdOut.AppendLine($"[SidecarWorker] ControlNet={signature.ControlNetPath}");
            }

            string systemInfo = StableDiffusionCppNativeBridge.SdGetSystemInfo();
            if (!string.IsNullOrWhiteSpace(systemInfo))
            {
                stdOut.AppendLine(systemInfo.Trim());
            }

            _sdContext = StableDiffusionCppNativeBridge.NewSdCtx(ref ctxParams);
            if (_sdContext == IntPtr.Zero)
            {
                error = "Failed to create native sd_ctx.";
                DisposeContextUnlocked();
                return false;
            }

            _loadedSignature = signature;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to initialize native sd_ctx: {ex.Message}";
            stdErr.AppendLine(ex.ToString());
            DisposeContextUnlocked();
            return false;
        }
        finally
        {
            StableDiffusionCppNativeBridge.FreeHGlobal(modelPathPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(vaePathPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(controlNetPathPtr);
        }
    }

    private void DisposeContextUnlocked()
    {
        if (_sdContext != IntPtr.Zero)
        {
            StableDiffusionCppNativeBridge.FreeSdCtx(_sdContext);
            _sdContext = IntPtr.Zero;
        }

        _loadedSignature = null;
    }

    private static IntPtr CopyImageToNative(StableDiffusionCppWorkerImagePayload image)
    {
        if (image == null || !image.HasData)
        {
            return IntPtr.Zero;
        }

        byte[] bytes = Convert.FromBase64String(image.base64Data);
        return StableDiffusionCppNativeBridge.BytesToNative(bytes);
    }

    private static StableDiffusionCppNativeBridge.SdImage ToNativeImage(
        StableDiffusionCppWorkerImagePayload image,
        IntPtr dataPointer,
        int fallbackChannels)
    {
        if (image == null)
        {
            return new StableDiffusionCppNativeBridge.SdImage
            {
                width = 0,
                height = 0,
                channel = (uint)Math.Max(1, fallbackChannels),
                data = IntPtr.Zero
            };
        }

        return new StableDiffusionCppNativeBridge.SdImage
        {
            width = (uint)Math.Max(0, image.width),
            height = (uint)Math.Max(0, image.height),
            channel = (uint)Math.Max(1, image.channelCount > 0 ? image.channelCount : fallbackChannels),
            data = dataPointer
        };
    }

    private static StableDiffusionCppWorkerImagePayload CopyOutputImage(
        StableDiffusionCppNativeBridge.SdImage nativeImage,
        out long byteLength)
    {
        int width = checked((int)nativeImage.width);
        int height = checked((int)nativeImage.height);
        int channel = checked((int)nativeImage.channel);
        int imageByteLength = checked(width * height * channel);

        if (width <= 0 || height <= 0 || channel <= 0 || nativeImage.data == IntPtr.Zero || imageByteLength <= 0)
        {
            throw new InvalidOperationException(
                $"Native output image is invalid. width={width}, height={height}, channel={channel}, data={nativeImage.data}");
        }

        var bytes = new byte[imageByteLength];
        Marshal.Copy(nativeImage.data, bytes, 0, imageByteLength);
        byteLength = imageByteLength;
        return new StableDiffusionCppWorkerImagePayload
        {
            width = width,
            height = height,
            channelCount = channel,
            base64Data = Convert.ToBase64String(bytes)
        };
    }

    private void TrackLeakedOutputBytes(
        long leakedBytes,
        StringBuilder stdErr,
        bool forceRecycle = false)
    {
        if (leakedBytes > 0)
        {
            _leakedOutputBytes += leakedBytes;
        }

        if (!forceRecycle && _leakedOutputBytes < MaxLeakedOutputBytesBeforeRecycle)
        {
            return;
        }

        _shouldRecycleAfterResponse = true;
        stdErr.AppendLine(
            "[SidecarWorker] Scheduling process recycle after this response to reclaim native output buffers. " +
            $"Estimated unreleased bytes={_leakedOutputBytes}.");
    }

    private static long EstimateOutputBytes(StableDiffusionCppWorkerGenerateRequest request)
    {
        if (request == null)
        {
            return 0;
        }

        long width = Math.Max(1, request.width);
        long height = Math.Max(1, request.height);
        long batchCount = Math.Max(1, request.batchCount);
        return width * height * 3L * batchCount;
    }

    private static StableDiffusionCppNativeBridge.CacheMode ParseCacheMode(
        string value,
        StringBuilder stdErr)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "easycache"
            : value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "disabled" or "none" => StableDiffusionCppNativeBridge.CacheMode.Disabled,
            "easycache" => StableDiffusionCppNativeBridge.CacheMode.EasyCache,
            "ucache" => StableDiffusionCppNativeBridge.CacheMode.UCache,
            "dbcache" => StableDiffusionCppNativeBridge.CacheMode.DBCache,
            "taylorseer" => StableDiffusionCppNativeBridge.CacheMode.TaylorSeer,
            "cache-dit" or "cache_dit" => StableDiffusionCppNativeBridge.CacheMode.CacheDit,
            "spectrum" => StableDiffusionCppNativeBridge.CacheMode.Spectrum,
            _ => LogUnknownCacheMode(value, stdErr)
        };
    }

    private static StableDiffusionCppNativeBridge.CacheMode LogUnknownCacheMode(
        string value,
        StringBuilder stdErr)
    {
        stdErr.AppendLine($"[SidecarWorker] Unsupported cache mode '{value}', falling back to easycache.");
        return StableDiffusionCppNativeBridge.CacheMode.EasyCache;
    }

    private static StableDiffusionCppWorkerGenerateResponse Failure(
        string errorMessage,
        string stdOut,
        string stdErr)
    {
        return new StableDiffusionCppWorkerGenerateResponse
        {
            success = false,
            errorMessage = errorMessage,
            stdOut = stdOut,
            stdErr = stdErr
        };
    }

    private sealed class ContextSignature
    {
        public string RuntimeInstallDirectory = string.Empty;
        public string NativeLibraryPath = string.Empty;
        public string ModelPath = string.Empty;
        public string VaePath = string.Empty;
        public string ControlNetPath = string.Empty;
        public bool OffloadToCpu;
        public bool ClipOnCpu;
        public bool DiffusionFlashAttention;

        public static ContextSignature From(StableDiffusionCppWorkerGenerateRequest request)
        {
            return new ContextSignature
            {
                RuntimeInstallDirectory = request.runtimeInstallDirectory ?? string.Empty,
                NativeLibraryPath = request.nativeLibraryPath ?? string.Empty,
                ModelPath = request.modelPath ?? string.Empty,
                VaePath = request.vaePath ?? string.Empty,
                ControlNetPath = request.controlNetPath ?? string.Empty,
                OffloadToCpu = request.offloadToCpu,
                ClipOnCpu = request.clipOnCpu,
                DiffusionFlashAttention = request.diffusionFlashAttention
            };
        }

        public bool Matches(ContextSignature other)
        {
            return string.Equals(RuntimeInstallDirectory, other.RuntimeInstallDirectory, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(NativeLibraryPath, other.NativeLibraryPath, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ModelPath, other.ModelPath, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(VaePath, other.VaePath, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ControlNetPath, other.ControlNetPath, StringComparison.OrdinalIgnoreCase)
                   && OffloadToCpu == other.OffloadToCpu
                   && ClipOnCpu == other.ClipOnCpu
                   && DiffusionFlashAttention == other.DiffusionFlashAttention;
        }
    }
}
