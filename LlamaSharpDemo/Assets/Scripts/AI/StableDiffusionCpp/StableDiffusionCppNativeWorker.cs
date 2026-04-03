using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

internal static class StableDiffusionCppNativeWorker
{
    private static readonly object WorkerLock = new object();
    private static readonly object DependencyWarningLock = new object();
    private static readonly HashSet<string> SharedCudaDependencyNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cublas64_12.dll",
            "cublasLt64_12.dll",
            "cudart64_12.dll"
        };

    private static IntPtr _sdContext = IntPtr.Zero;
    private static ContextSignature _loadedSignature;
    private static bool _isBusy;
    private static bool _cancelRequested;
    private static bool _loggedDependencyConflictWarning;

    internal static bool IsBusy
    {
        get
        {
            lock (WorkerLock)
            {
                return _isBusy;
            }
        }
    }

    internal static bool CanUsePersistentWorker(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request)
    {
        if (settings == null || prep == null || request == null)
        {
            return false;
        }

        if (!settings.preferPersistentNativeWorker)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(prep.NativeLibraryPath) || !File.Exists(prep.NativeLibraryPath))
        {
            return false;
        }

        if (!StableDiffusionCppNativeBridge.CanUseLibraryPath(prep.NativeLibraryPath))
        {
            return false;
        }

        if (HasLoadedSharedDependencyConflict(
                prep.RuntimeInstallDirectory,
                out string dependencyConflictWarning))
        {
            LogDependencyConflictWarningOnce(dependencyConflictWarning);
            return false;
        }

        // Raw CLI escape hatches and advanced cache presets are still delegated to the one-shot sd-cli backend.
        return string.IsNullOrWhiteSpace(settings.globalAdditionalArguments)
               && string.IsNullOrWhiteSpace(request.extraArgumentsRaw)
               && string.IsNullOrWhiteSpace(request.cacheOption)
               && string.IsNullOrWhiteSpace(request.cachePreset);
    }

    internal static void CancelActiveGeneration()
    {
        lock (WorkerLock)
        {
            if (_isBusy)
            {
                _cancelRequested = true;
            }
        }
    }

    internal static void ReleaseContext()
    {
        lock (WorkerLock)
        {
            if (_isBusy)
            {
                _cancelRequested = true;
                return;
            }

            DisposeContextUnlocked();
        }
    }

    internal static async Task<StableDiffusionCppGenerationResult> GenerateAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request,
        string commandLine,
        string workerOutputDirectory,
        string workerOutputPath,
        string requestedOutputDirectory,
        bool persistOutputToRequestedDirectory,
        DateTime startUtc,
        CancellationToken cancellationToken)
    {
        if (!TryAcquireWorker())
        {
            return StableDiffusionCppGenerationResult.Failed(
                "Generation is already running. Wait for completion or cancel first.",
                -1,
                commandLine,
                string.Empty,
                string.Empty,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc);
        }

        try
        {
            if (!TryBuildInputImage(request, out NativeInputImage initImage, out string initError))
            {
                return CreateFailure(commandLine, requestedOutputDirectory, startUtc, initError, cancellationToken.IsCancellationRequested);
            }

            if (!TryBuildMaskImage(request, out NativeInputImage maskImage, out string maskError))
            {
                return CreateFailure(commandLine, requestedOutputDirectory, startUtc, maskError, cancellationToken.IsCancellationRequested);
            }

            if (!TryBuildControlImage(request, out NativeInputImage controlImage, out string controlError))
            {
                return CreateFailure(commandLine, requestedOutputDirectory, startUtc, controlError, cancellationToken.IsCancellationRequested);
            }

            ContextSignature signature = ContextSignature.From(prep, request);

            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(CancelActiveGeneration);

            NativeInvocationResult invocationResult = await Task.Run(() =>
                ExecuteNativeGeneration(signature, request, initImage, maskImage, controlImage, cancellationToken));

            if (!invocationResult.Success)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    invocationResult.ErrorMessage,
                    -1,
                    commandLine,
                    invocationResult.StdOut,
                    invocationResult.StdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: invocationResult.Cancelled || cancellationToken.IsCancellationRequested);
            }

            if (invocationResult.Cancelled || cancellationToken.IsCancellationRequested)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Generation cancelled. Native persistent worker cannot interrupt an in-flight denoise call immediately, so the result was discarded after completion.",
                    -1,
                    commandLine,
                    invocationResult.StdOut,
                    invocationResult.StdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: true);
            }

            List<string> outputFiles = WriteOutputImages(
                invocationResult.Images,
                workerOutputPath,
                request.outputFormat,
                out string writeError);
            if (outputFiles.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    writeError ?? "Native worker finished but no output image file was written.",
                    -1,
                    commandLine,
                    invocationResult.StdOut,
                    invocationResult.StdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            if (!persistOutputToRequestedDirectory)
            {
                return StableDiffusionCppGenerationResult.Succeeded(
                    outputFiles,
                    commandLine,
                    invocationResult.StdOut,
                    invocationResult.StdErr,
                    workerOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            List<string> finalOutputs = CopyOutputsToRequestedDirectory(outputFiles, requestedOutputDirectory, out string copyError);
            if (finalOutputs.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    copyError ?? $"Generation succeeded but failed to copy outputs into target directory: {requestedOutputDirectory}",
                    -1,
                    commandLine,
                    invocationResult.StdOut,
                    invocationResult.StdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            return StableDiffusionCppGenerationResult.Succeeded(
                finalOutputs,
                commandLine,
                invocationResult.StdOut,
                invocationResult.StdErr,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc);
        }
        catch (Exception ex)
        {
            return StableDiffusionCppGenerationResult.Failed(
                $"Native persistent worker generation failed due to exception: {ex}",
                -1,
                commandLine,
                string.Empty,
                string.Empty,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                cancelled: cancellationToken.IsCancellationRequested || IsCancellationRequested());
        }
        finally
        {
            ReleaseWorker();
        }
    }

    private static NativeInvocationResult ExecuteNativeGeneration(
        ContextSignature signature,
        StableDiffusionCppGenerationRequest request,
        NativeInputImage initImage,
        NativeInputImage maskImage,
        NativeInputImage controlImage,
        CancellationToken cancellationToken)
    {
        var result = new NativeInvocationResult();
        var stdOut = new StringBuilder(512);
        var stdErr = new StringBuilder(512);
        IntPtr nativeResults = IntPtr.Zero;
        IntPtr promptPtr = IntPtr.Zero;
        IntPtr negativePromptPtr = IntPtr.Zero;
        IntPtr initImagePtr = IntPtr.Zero;
        IntPtr maskImagePtr = IntPtr.Zero;
        IntPtr controlImagePtr = IntPtr.Zero;
        int freedOutputImages = 0;

        try
        {
            if (!StableDiffusionCppNativeBridge.TryEnsureLoaded(
                    signature.RuntimeInstallDirectory,
                    signature.NativeLibraryPath,
                    out string loadError))
            {
                result.ErrorMessage = loadError;
                return result;
            }

            if (cancellationToken.IsCancellationRequested || IsCancellationRequested())
            {
                result.Cancelled = true;
                result.ErrorMessage = "Generation cancelled.";
                return result;
            }

            if (!EnsureContext(signature, stdOut, stdErr, out string contextError))
            {
                result.ErrorMessage = contextError;
                result.StdOut = stdOut.ToString();
                result.StdErr = stdErr.ToString();
                return result;
            }

            if (cancellationToken.IsCancellationRequested || IsCancellationRequested())
            {
                result.Cancelled = true;
                result.ErrorMessage = "Generation cancelled.";
                result.StdOut = stdOut.ToString();
                result.StdErr = stdErr.ToString();
                return result;
            }

            StableDiffusionCppNativeBridge.SdImgGenParams genParams =
                new StableDiffusionCppNativeBridge.SdImgGenParams();
            StableDiffusionCppNativeBridge.SdImgGenParamsInit(ref genParams);

            StableDiffusionCppNativeBridge.SdSampleParams sampleParams =
                new StableDiffusionCppNativeBridge.SdSampleParams();
            StableDiffusionCppNativeBridge.SdSampleParamsInit(ref sampleParams);

            StableDiffusionCppNativeBridge.SampleMethod sampleMethod =
                StableDiffusionCppNativeBridge.StrToSampleMethod(request.sampler);
            if (sampleMethod == StableDiffusionCppNativeBridge.SampleMethod.Count)
            {
                sampleMethod = StableDiffusionCppNativeBridge.GetDefaultSampleMethod(_sdContext);
                stdErr.AppendLine($"[NativeWorker] Unknown sampler '{request.sampler}', falling back to {sampleMethod}.");
            }

            StableDiffusionCppNativeBridge.Scheduler scheduler =
                StableDiffusionCppNativeBridge.StrToScheduler(request.scheduler);
            if (scheduler == StableDiffusionCppNativeBridge.Scheduler.Count)
            {
                scheduler = StableDiffusionCppNativeBridge.GetDefaultScheduler(_sdContext, sampleMethod);
                stdErr.AppendLine($"[NativeWorker] Unknown scheduler '{request.scheduler}', falling back to {scheduler}.");
            }

            sampleParams.sample_method = sampleMethod;
            sampleParams.scheduler = scheduler;
            sampleParams.sample_steps = Math.Max(1, request.steps);
            sampleParams.guidance.txt_cfg = Math.Max(0.1f, request.cfgScale);
            sampleParams.guidance.img_cfg = request.overrideImageCfgScale
                ? Math.Max(0.1f, request.imageCfgScale)
                : Math.Max(0.1f, request.cfgScale);

            StableDiffusionCppNativeBridge.SdCacheParams cacheParams =
                new StableDiffusionCppNativeBridge.SdCacheParams();
            StableDiffusionCppNativeBridge.SdCacheParamsInit(ref cacheParams);
            cacheParams.mode = request.useCacheMode
                ? ParseCacheMode(request.cacheMode, stdErr)
                : StableDiffusionCppNativeBridge.CacheMode.Disabled;

            promptPtr = StableDiffusionCppNativeBridge.StringToNative(request.prompt);
            negativePromptPtr = StableDiffusionCppNativeBridge.StringToNative(request.negativePrompt);
            initImagePtr = initImage.CopyToNative();
            maskImagePtr = maskImage.CopyToNative();
            controlImagePtr = controlImage.CopyToNative();

            genParams.prompt = promptPtr;
            genParams.negative_prompt = negativePromptPtr;
            genParams.clip_skip = -1;
            genParams.init_image = initImage.ToSdImage(initImagePtr);
            genParams.mask_image = maskImage.ToSdImage(maskImagePtr);
            genParams.control_image = controlImage.ToSdImage(controlImagePtr);
            genParams.width = Math.Max(64, request.width);
            genParams.height = Math.Max(64, request.height);
            genParams.sample_params = sampleParams;
            genParams.strength = Math.Max(0.01f, Math.Min(1f, request.strength));
            genParams.seed = request.seed;
            genParams.batch_count = Math.Max(1, request.batchCount);
            genParams.control_strength = Math.Max(0f, Math.Min(2f, request.controlStrength));
            genParams.vae_tiling_params.enabled = request.vaeTiling ? (byte)1 : (byte)0;
            genParams.cache = cacheParams;

            stdOut.AppendLine("[NativeWorker] Generating with persistent sd_ctx.");
            nativeResults = StableDiffusionCppNativeBridge.GenerateImage(_sdContext, ref genParams);
            if (nativeResults == IntPtr.Zero)
            {
                result.Cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
                result.ErrorMessage = result.Cancelled
                    ? "Generation cancelled."
                    : "Native generate_image returned null.";
                result.StdOut = stdOut.ToString();
                result.StdErr = stdErr.ToString();
                return result;
            }

            int imageCount = Math.Max(1, request.batchCount);
            int imageSize = Marshal.SizeOf(typeof(StableDiffusionCppNativeBridge.SdImage));
            for (int i = 0; i < imageCount; i++)
            {
                IntPtr imagePtr = IntPtr.Add(nativeResults, i * imageSize);
                StableDiffusionCppNativeBridge.SdImage nativeImage =
                    Marshal.PtrToStructure<StableDiffusionCppNativeBridge.SdImage>(imagePtr);
                result.Images.Add(CopyOutputImage(nativeImage));
                StableDiffusionCppNativeBridge.FreeNativeMemory(nativeImage.data);
                freedOutputImages++;
            }

            StableDiffusionCppNativeBridge.FreeNativeMemory(nativeResults);
            nativeResults = IntPtr.Zero;

            result.Success = true;
            result.Cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
            result.StdOut = stdOut.ToString();
            result.StdErr = stdErr.ToString();
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
            result.ErrorMessage = $"Native worker invocation failed: {ex.Message}";
            stdErr.AppendLine(ex.ToString());
            result.StdOut = stdOut.ToString();
            result.StdErr = stdErr.ToString();
            return result;
        }
        finally
        {
            StableDiffusionCppNativeBridge.FreeHGlobal(promptPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(negativePromptPtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(initImagePtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(maskImagePtr);
            StableDiffusionCppNativeBridge.FreeHGlobal(controlImagePtr);

            if (nativeResults != IntPtr.Zero)
            {
                FreePartialResults(nativeResults, Math.Max(1, request.batchCount), freedOutputImages);
            }
        }
    }

    private static bool EnsureContext(
        ContextSignature signature,
        StringBuilder stdOut,
        StringBuilder stdErr,
        out string error)
    {
        error = null;
        if (_sdContext != IntPtr.Zero && _loadedSignature != null && _loadedSignature.Matches(signature))
        {
            stdOut.AppendLine("[NativeWorker] Reusing cached sd_ctx.");
            return true;
        }

        DisposeContextUnlocked();

        StableDiffusionCppNativeBridge.SdCtxParams ctxParams =
            new StableDiffusionCppNativeBridge.SdCtxParams();
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
            ctxParams.free_params_immediately = 1;
            ctxParams.n_threads = ResolveWorkerThreadCount();
            ctxParams.offload_params_to_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.enable_mmap = 1;
            ctxParams.keep_clip_on_cpu = signature.ClipOnCpu ? (byte)1 : (byte)0;
            ctxParams.keep_control_net_on_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.keep_vae_on_cpu = signature.OffloadToCpu ? (byte)1 : (byte)0;
            ctxParams.diffusion_flash_attn = signature.DiffusionFlashAttention ? (byte)1 : (byte)0;

            stdOut.AppendLine("[NativeWorker] Loading new sd_ctx.");
            stdOut.AppendLine($"[NativeWorker] Model={signature.ModelPath}");
            if (!string.IsNullOrWhiteSpace(signature.VaePath))
            {
                stdOut.AppendLine($"[NativeWorker] VAE={signature.VaePath}");
            }

            if (!string.IsNullOrWhiteSpace(signature.ControlNetPath))
            {
                stdOut.AppendLine($"[NativeWorker] ControlNet={signature.ControlNetPath}");
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

    private static bool TryAcquireWorker()
    {
        lock (WorkerLock)
        {
            if (_isBusy)
            {
                return false;
            }

            _isBusy = true;
            _cancelRequested = false;
            return true;
        }
    }

    private static void ReleaseWorker()
    {
        lock (WorkerLock)
        {
            _isBusy = false;
            _cancelRequested = false;
        }
    }

    private static bool IsCancellationRequested()
    {
        lock (WorkerLock)
        {
            return _cancelRequested;
        }
    }

    private static void DisposeContextUnlocked()
    {
        if (_sdContext != IntPtr.Zero)
        {
            StableDiffusionCppNativeBridge.FreeSdCtx(_sdContext);
            _sdContext = IntPtr.Zero;
        }

        _loadedSignature = null;
    }

    private static int ResolveWorkerThreadCount()
    {
        return Math.Max(1, Environment.ProcessorCount);
    }

    private static bool HasLoadedSharedDependencyConflict(
        string runtimeInstallDirectory,
        out string warningMessage)
    {
        warningMessage = null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (string.IsNullOrWhiteSpace(runtimeInstallDirectory))
        {
            return false;
        }

        string normalizedRuntimeDirectory;
        try
        {
            normalizedRuntimeDirectory = Path.GetFullPath(runtimeInstallDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            foreach (ProcessModule module in currentProcess.Modules)
            {
                string modulePath = module?.FileName;
                if (string.IsNullOrWhiteSpace(modulePath))
                {
                    continue;
                }

                string moduleName = Path.GetFileName(modulePath);
                if (!SharedCudaDependencyNames.Contains(moduleName))
                {
                    continue;
                }

                string normalizedModuleDirectory = Path.GetDirectoryName(Path.GetFullPath(modulePath)) ?? string.Empty;
                normalizedModuleDirectory = normalizedModuleDirectory
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(
                        normalizedModuleDirectory,
                        normalizedRuntimeDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                warningMessage =
                    "[StableDiffusionCppNativeWorker] Persistent in-process SD worker is disabled because " +
                    $"'{moduleName}' is already loaded from '{normalizedModuleDirectory}', while this SD runtime is in " +
                    $"'{normalizedRuntimeDirectory}'. Windows resolves same-name DLL imports process-wide, so mixing " +
                    "different CUDA dependency builds can crash Unity. Falling back to one-shot sd-cli for this request.";
                return true;
            }
        }
        catch (Exception ex)
        {
            warningMessage =
                "[StableDiffusionCppNativeWorker] Could not inspect loaded native modules safely, so persistent " +
                $"in-process SD worker is disabled for this request. Falling back to one-shot sd-cli. Reason: {ex.Message}";
            return true;
        }
#endif

        return false;
    }

    private static void LogDependencyConflictWarningOnce(string warningMessage)
    {
        if (string.IsNullOrWhiteSpace(warningMessage))
        {
            return;
        }

        lock (DependencyWarningLock)
        {
            if (_loggedDependencyConflictWarning)
            {
                return;
            }

            _loggedDependencyConflictWarning = true;
            Debug.LogWarning(warningMessage);
        }
    }

    private static bool TryBuildInputImage(
        StableDiffusionCppGenerationRequest request,
        out NativeInputImage image,
        out string error)
    {
        image = NativeInputImage.EmptyRgb();
        error = null;

        if (!request.RequiresInitImage)
        {
            return true;
        }

        if (!StableDiffusionCppImageIO.TryLoadImageFileAsRawBytes(
                request.initImagePath,
                request.width,
                request.height,
                3,
                out byte[] bytes,
                out int width,
                out int height,
                out error))
        {
            return false;
        }

        image = new NativeInputImage(bytes, width, height, 3);
        return true;
    }

    private static bool TryBuildMaskImage(
        StableDiffusionCppGenerationRequest request,
        out NativeInputImage image,
        out string error)
    {
        error = null;

        if (request.RequiresMaskImage)
        {
            if (!StableDiffusionCppImageIO.TryLoadImageFileAsRawBytes(
                    request.maskImagePath,
                    request.width,
                    request.height,
                    1,
                    out byte[] maskBytes,
                    out int maskWidth,
                    out int maskHeight,
                    out error))
            {
                image = default;
                return false;
            }

            image = new NativeInputImage(maskBytes, maskWidth, maskHeight, 1);
            return true;
        }

        int width = Math.Max(64, request.width);
        int height = Math.Max(64, request.height);
        var bytes = new byte[width * height];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = 255;
        }

        image = new NativeInputImage(bytes, width, height, 1);
        return true;
    }

    private static bool TryBuildControlImage(
        StableDiffusionCppGenerationRequest request,
        out NativeInputImage image,
        out string error)
    {
        image = NativeInputImage.EmptyRgb();
        error = null;

        if (!request.RequiresControlImage)
        {
            return true;
        }

        if (!StableDiffusionCppImageIO.TryLoadImageFileAsRawBytes(
                request.controlImagePath,
                request.width,
                request.height,
                3,
                out byte[] bytes,
                out int width,
                out int height,
                out error))
        {
            return false;
        }

        image = new NativeInputImage(bytes, width, height, 3);
        return true;
    }

    private static GeneratedImageData CopyOutputImage(StableDiffusionCppNativeBridge.SdImage nativeImage)
    {
        int width = checked((int)nativeImage.width);
        int height = checked((int)nativeImage.height);
        int channel = checked((int)nativeImage.channel);
        int byteLength = checked(width * height * channel);

        if (width <= 0 || height <= 0 || channel <= 0 || nativeImage.data == IntPtr.Zero || byteLength <= 0)
        {
            throw new InvalidOperationException(
                $"Native output image is invalid. width={width}, height={height}, channel={channel}, data={nativeImage.data}");
        }

        var bytes = new byte[byteLength];
        Marshal.Copy(nativeImage.data, bytes, 0, byteLength);
        return new GeneratedImageData(bytes, width, height, channel);
    }

    private static void FreePartialResults(IntPtr nativeResults, int imageCount, int alreadyFreedCount)
    {
        int imageSize = Marshal.SizeOf(typeof(StableDiffusionCppNativeBridge.SdImage));
        for (int i = Math.Max(0, alreadyFreedCount); i < imageCount; i++)
        {
            IntPtr imagePtr = IntPtr.Add(nativeResults, i * imageSize);
            StableDiffusionCppNativeBridge.SdImage nativeImage =
                Marshal.PtrToStructure<StableDiffusionCppNativeBridge.SdImage>(imagePtr);
            StableDiffusionCppNativeBridge.FreeNativeMemory(nativeImage.data);
        }

        StableDiffusionCppNativeBridge.FreeNativeMemory(nativeResults);
    }

    private static List<string> WriteOutputImages(
        IReadOnlyList<GeneratedImageData> images,
        string baseOutputPath,
        string outputFormat,
        out string error)
    {
        error = null;
        var outputFiles = new List<string>();
        if (images == null || images.Count == 0)
        {
            error = "Native worker produced no image data.";
            return outputFiles;
        }

        string normalizedFormat = string.Equals(outputFormat, "jpg", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(outputFormat, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";

        for (int i = 0; i < images.Count; i++)
        {
            GeneratedImageData image = images[i];
            string outputPath = ResolveIndexedOutputPath(baseOutputPath, i, images.Count, normalizedFormat);
            if (!StableDiffusionCppImageIO.TryWriteRawBytesToImageFile(
                    image.Bytes,
                    image.Width,
                    image.Height,
                    image.ChannelCount,
                    outputPath,
                    normalizedFormat,
                    out string writeError))
            {
                error = writeError;
                return new List<string>();
            }

            outputFiles.Add(outputPath);
        }

        return outputFiles;
    }

    private static string ResolveIndexedOutputPath(
        string baseOutputPath,
        int imageIndex,
        int imageCount,
        string extension)
    {
        string parent = Path.GetDirectoryName(baseOutputPath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseOutputPath);
        string fileName = imageCount > 1
            ? fileNameWithoutExtension + "_" + imageIndex.ToString(CultureInfo.InvariantCulture) + "." + extension
            : fileNameWithoutExtension + "." + extension;

        return string.IsNullOrWhiteSpace(parent)
            ? fileName
            : Path.Combine(parent, fileName);
    }

    private static List<string> CopyOutputsToRequestedDirectory(
        IReadOnlyList<string> sourceOutputs,
        string requestedOutputDirectory,
        out string error)
    {
        error = null;
        var result = new List<string>();
        if (sourceOutputs == null || sourceOutputs.Count == 0)
        {
            error = "No source outputs to copy.";
            return result;
        }

        try
        {
            Directory.CreateDirectory(requestedOutputDirectory);
        }
        catch (Exception ex)
        {
            error = $"Failed to create output directory '{requestedOutputDirectory}': {ex.Message}";
            return result;
        }

        for (int i = 0; i < sourceOutputs.Count; i++)
        {
            string source = sourceOutputs[i];
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                continue;
            }

            try
            {
                string destination = Path.Combine(requestedOutputDirectory, Path.GetFileName(source));
                File.Copy(source, destination, overwrite: true);
                result.Add(destination);
            }
            catch (Exception ex)
            {
                error = $"Failed to copy '{source}' to '{requestedOutputDirectory}': {ex.Message}";
                return new List<string>();
            }
        }

        if (result.Count == 0)
        {
            error = "No output files were copied.";
        }

        return result;
    }

    private static StableDiffusionCppNativeBridge.CacheMode ParseCacheMode(
        string value,
        StringBuilder stdErr)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "easycache"
            : value.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "disabled":
            case "none":
                return StableDiffusionCppNativeBridge.CacheMode.Disabled;
            case "easycache":
                return StableDiffusionCppNativeBridge.CacheMode.EasyCache;
            case "ucache":
                return StableDiffusionCppNativeBridge.CacheMode.UCache;
            case "dbcache":
                return StableDiffusionCppNativeBridge.CacheMode.DBCache;
            case "taylorseer":
                return StableDiffusionCppNativeBridge.CacheMode.TaylorSeer;
            case "cache-dit":
            case "cache_dit":
                return StableDiffusionCppNativeBridge.CacheMode.CacheDit;
            case "spectrum":
                return StableDiffusionCppNativeBridge.CacheMode.Spectrum;
            default:
                stdErr?.AppendLine($"[NativeWorker] Unsupported cache mode '{value}', falling back to easycache.");
                return StableDiffusionCppNativeBridge.CacheMode.EasyCache;
        }
    }

    private static StableDiffusionCppGenerationResult CreateFailure(
        string commandLine,
        string requestedOutputDirectory,
        DateTime startUtc,
        string errorMessage,
        bool cancelled)
    {
        return StableDiffusionCppGenerationResult.Failed(
            errorMessage,
            -1,
            commandLine,
            string.Empty,
            string.Empty,
            requestedOutputDirectory,
            DateTime.UtcNow - startUtc,
            cancelled: cancelled);
    }

    private sealed class NativeInvocationResult
    {
        public bool Success;
        public bool Cancelled;
        public string ErrorMessage;
        public string StdOut = string.Empty;
        public string StdErr = string.Empty;
        public readonly List<GeneratedImageData> Images = new List<GeneratedImageData>();
    }

    private sealed class ContextSignature
    {
        public string RuntimeInstallDirectory;
        public string NativeLibraryPath;
        public string ModelPath;
        public string VaePath;
        public string ControlNetPath;
        public bool OffloadToCpu;
        public bool ClipOnCpu;
        public bool DiffusionFlashAttention;

        public static ContextSignature From(
            StableDiffusionCppPreparationResult prep,
            StableDiffusionCppGenerationRequest request)
        {
            return new ContextSignature
            {
                RuntimeInstallDirectory = prep.RuntimeInstallDirectory ?? string.Empty,
                NativeLibraryPath = prep.NativeLibraryPath ?? string.Empty,
                ModelPath = prep.ModelPath ?? string.Empty,
                VaePath = prep.VaePath ?? string.Empty,
                ControlNetPath = request.RequiresControlNetModel
                    ? request.controlNetPathOverride ?? string.Empty
                    : string.Empty,
                OffloadToCpu = request.offloadToCpu,
                ClipOnCpu = request.clipOnCpu,
                DiffusionFlashAttention = request.diffusionFlashAttention
            };
        }

        public bool Matches(ContextSignature other)
        {
            if (other == null)
            {
                return false;
            }

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

    private readonly struct NativeInputImage
    {
        public readonly byte[] Bytes;
        public readonly int Width;
        public readonly int Height;
        public readonly int ChannelCount;

        public NativeInputImage(byte[] bytes, int width, int height, int channelCount)
        {
            Bytes = bytes;
            Width = width;
            Height = height;
            ChannelCount = channelCount;
        }

        public static NativeInputImage EmptyRgb()
        {
            return new NativeInputImage(null, 0, 0, 3);
        }

        public IntPtr CopyToNative()
        {
            return StableDiffusionCppNativeBridge.BytesToNative(Bytes);
        }

        public StableDiffusionCppNativeBridge.SdImage ToSdImage(IntPtr dataPointer)
        {
            return new StableDiffusionCppNativeBridge.SdImage
            {
                width = (uint)Math.Max(0, Width),
                height = (uint)Math.Max(0, Height),
                channel = (uint)Math.Max(1, ChannelCount),
                data = dataPointer
            };
        }
    }

    private sealed class GeneratedImageData
    {
        public readonly byte[] Bytes;
        public readonly int Width;
        public readonly int Height;
        public readonly int ChannelCount;

        public GeneratedImageData(byte[] bytes, int width, int height, int channelCount)
        {
            Bytes = bytes;
            Width = width;
            Height = height;
            ChannelCount = channelCount;
        }
    }
}
