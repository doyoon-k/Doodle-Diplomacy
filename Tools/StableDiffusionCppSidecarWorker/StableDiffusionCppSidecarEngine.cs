using System.Runtime.InteropServices;
using System.Text;

internal sealed class StableDiffusionCppSidecarEngine
{
    private const long MaxLeakedOutputBytesBeforeRecycle = 192L * 1024L * 1024L;
    private const string LoadingPhase = "Loading Model";
    private const string SamplingPhase = "Sampling";
    private const string DecodingPhase = "Decoding";
    private const string IdlePhase = "Idle";

    private static readonly StableDiffusionCppNativeBridge.ProgressCallback NativeProgressCallback =
        HandleNativeProgress;
    private static readonly StableDiffusionCppNativeBridge.PreviewCallback NativePreviewCallback =
        HandleNativePreview;

    private readonly object _engineLock = new object();
    private readonly object _progressLock = new object();
    private readonly IntPtr _callbackUserData;

    private IntPtr _sdContext = IntPtr.Zero;
    private ContextSignature _loadedSignature;
    private long _leakedOutputBytes;
    private bool _isBusy;
    private bool _shouldRecycleAfterResponse;
    private long _progressSessionId;
    private bool _hasProgressSnapshot;
    private int _progressStep;
    private int _progressTotalSteps;
    private string _progressPhase = IdlePhase;
    private string _progressMessage = string.Empty;
    private long _previewUpdateIndex;
    private int _previewWidth;
    private int _previewHeight;
    private int _previewChannelCount = 3;
    private byte[] _previewBytes;

    public StableDiffusionCppSidecarEngine()
    {
        _callbackUserData = GCHandle.ToIntPtr(GCHandle.Alloc(this));
    }

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

    public StableDiffusionCppWorkerProgressResponse GetProgressSnapshot()
    {
        lock (_progressLock)
        {
            StableDiffusionCppWorkerImagePayload previewImage = null;
            if (_previewBytes != null &&
                _previewBytes.Length > 0 &&
                _previewWidth > 0 &&
                _previewHeight > 0 &&
                _previewChannelCount > 0)
            {
                previewImage = new StableDiffusionCppWorkerImagePayload
                {
                    width = _previewWidth,
                    height = _previewHeight,
                    channelCount = _previewChannelCount,
                    base64Data = Convert.ToBase64String(_previewBytes)
                };
            }

            int totalSteps = Math.Max(0, _progressTotalSteps);
            int step = MathfClamp(_progressStep, 0, Math.Max(1, totalSteps));
            return new StableDiffusionCppWorkerProgressResponse
            {
                isBusy = _isBusy,
                hasProgress = _hasProgressSnapshot,
                progressSessionId = _progressSessionId,
                step = step,
                totalSteps = totalSteps,
                progress01 = totalSteps > 0 ? (float)step / totalSteps : (_isBusy ? 0f : 1f),
                phase = _progressPhase ?? string.Empty,
                message = _progressMessage ?? string.Empty,
                previewUpdateIndex = _previewUpdateIndex,
                previewImage = previewImage
            };
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
        ResetProgressSnapshot(Math.Max(1, request.steps));
        UpdateProgressSnapshot(LoadingPhase, "Loading Stable Diffusion model...", 0, Math.Max(1, request.steps));

        ContextSignature signature = ContextSignature.From(request);
        if (!EnsureContext(signature, stdOut, stdErr, out string contextError))
        {
            return Failure(contextError ?? "Failed to initialize sd_ctx.", stdOut.ToString(), stdErr.ToString());
        }

        ConfigureProgressCallbacks(request);
        UpdateProgressSnapshot(SamplingPhase, "Starting denoise steps...", 0, Math.Max(1, request.steps));

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

            UpdateProgressSnapshot(DecodingPhase, "Decoding final image...", Math.Max(1, request.steps), Math.Max(1, request.steps));

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

            StableDiffusionCppNativeBridge.SdSetPreviewCallback(
                null,
                StableDiffusionCppNativeBridge.PreviewMode.None,
                0,
                false,
                false,
                IntPtr.Zero);
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

    private void ConfigureProgressCallbacks(StableDiffusionCppWorkerGenerateRequest request)
    {
        StableDiffusionCppNativeBridge.SdSetProgressCallback(
            NativeProgressCallback,
            _callbackUserData);

        bool enablePreview = request != null && request.enableProgressPreview;
        StableDiffusionCppNativeBridge.PreviewMode previewMode = enablePreview
            ? ParsePreviewMode(request.previewMode)
            : StableDiffusionCppNativeBridge.PreviewMode.None;
        int previewInterval = request != null
            ? Math.Max(1, request.previewIntervalSteps)
            : 2;

        StableDiffusionCppNativeBridge.SdSetPreviewCallback(
            enablePreview ? NativePreviewCallback : null,
            previewMode,
            previewInterval,
            enablePreview,
            false,
            enablePreview ? _callbackUserData : IntPtr.Zero);
    }

    private void ResetProgressSnapshot(int totalSteps)
    {
        lock (_progressLock)
        {
            _progressSessionId++;
            _hasProgressSnapshot = true;
            _progressStep = 0;
            _progressTotalSteps = Math.Max(1, totalSteps);
            _progressPhase = LoadingPhase;
            _progressMessage = "Loading Stable Diffusion model...";
            _previewUpdateIndex = 0;
            _previewWidth = 0;
            _previewHeight = 0;
            _previewChannelCount = 3;
            _previewBytes = null;
        }
    }

    private void UpdateProgressSnapshot(string phase, string message, int step, int totalSteps)
    {
        lock (_progressLock)
        {
            _hasProgressSnapshot = true;
            _progressPhase = string.IsNullOrWhiteSpace(phase) ? SamplingPhase : phase;
            _progressMessage = string.IsNullOrWhiteSpace(message)
                ? _progressPhase
                : message;
            _progressTotalSteps = Math.Max(1, totalSteps);
            _progressStep = MathfClamp(step, 0, _progressTotalSteps);
        }
    }

    private void UpdatePreviewSnapshot(StableDiffusionCppNativeBridge.SdImage nativeImage)
    {
        int width = checked((int)nativeImage.width);
        int height = checked((int)nativeImage.height);
        int channel = checked((int)nativeImage.channel);
        int byteLength = checked(width * height * channel);
        if (width <= 0 || height <= 0 || channel <= 0 || nativeImage.data == IntPtr.Zero || byteLength <= 0)
        {
            return;
        }

        var bytes = new byte[byteLength];
        Marshal.Copy(nativeImage.data, bytes, 0, byteLength);

        lock (_progressLock)
        {
            _previewWidth = width;
            _previewHeight = height;
            _previewChannelCount = channel;
            _previewBytes = bytes;
            _previewUpdateIndex++;
        }
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

    private static StableDiffusionCppNativeBridge.PreviewMode ParsePreviewMode(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "vae"
            : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "proj" => StableDiffusionCppNativeBridge.PreviewMode.Proj,
            "tae" => StableDiffusionCppNativeBridge.PreviewMode.Tae,
            "vae" => StableDiffusionCppNativeBridge.PreviewMode.Vae,
            _ => StableDiffusionCppNativeBridge.PreviewMode.Vae
        };
    }

    private static void HandleNativeProgress(int step, int steps, float time, IntPtr data)
    {
        StableDiffusionCppSidecarEngine target = ResolveCallbackTarget(data);
        if (target == null)
        {
            return;
        }

        int totalSteps = Math.Max(1, steps);
        int clampedStep = MathfClamp(step, 0, totalSteps);
        string elapsedText = time > 0f
            ? $"Sampling step {clampedStep}/{totalSteps} ({time:0.0}s)"
            : $"Sampling step {clampedStep}/{totalSteps}";
        target.UpdateProgressSnapshot(SamplingPhase, elapsedText, clampedStep, totalSteps);
    }

    private static void HandleNativePreview(
        int step,
        int frameCount,
        IntPtr frames,
        bool isNoisy,
        IntPtr data)
    {
        if (isNoisy || frameCount <= 0 || frames == IntPtr.Zero)
        {
            return;
        }

        StableDiffusionCppSidecarEngine target = ResolveCallbackTarget(data);
        if (target == null)
        {
            return;
        }

        StableDiffusionCppNativeBridge.SdImage previewImage =
            Marshal.PtrToStructure<StableDiffusionCppNativeBridge.SdImage>(frames);
        target.UpdatePreviewSnapshot(previewImage);

        lock (target._progressLock)
        {
            int totalSteps = Math.Max(1, target._progressTotalSteps);
            int clampedStep = MathfClamp(step, 0, totalSteps);
            target._hasProgressSnapshot = true;
            target._progressPhase = SamplingPhase;
            target._progressStep = clampedStep;
            target._progressMessage = $"Sampling step {clampedStep}/{totalSteps} · preview {target._previewUpdateIndex}";
        }
    }

    private static StableDiffusionCppSidecarEngine ResolveCallbackTarget(IntPtr data)
    {
        if (data == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GCHandle.FromIntPtr(data).Target as StableDiffusionCppSidecarEngine;
        }
        catch
        {
            return null;
        }
    }

    private static int MathfClamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
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
