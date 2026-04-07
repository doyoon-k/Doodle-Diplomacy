using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class StableDiffusionCppRuntime
{
    private static readonly object ActiveProcessLock = new object();
    private static Process _activeProcess;
    public static event Action<StableDiffusionCppWorkerProgressResponse> ProgressChanged;

    private sealed class GpuTelemetryStats
    {
        public bool Available;
        public int PeakMemoryMiB;
        public int PeakUtilizationPercent = -1;
        public int Samples;
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private sealed class WindowsGpuProcessMemoryCounters
    {
        public object DedicatedUsageCounter;
        public object SharedUsageCounter;
    }

    private static readonly object WindowsGpuCountersLock = new object();
    private static readonly Dictionary<int, WindowsGpuProcessMemoryCounters> WindowsGpuCountersByPid =
        new Dictionary<int, WindowsGpuProcessMemoryCounters>();
#endif

    public static bool IsBusy
    {
        get
        {
            if (StableDiffusionCppSdServerWorker.IsBusy)
            {
                return true;
            }

            lock (ActiveProcessLock)
            {
                return _activeProcess != null;
            }
        }
    }

    public static void CancelActiveGeneration()
    {
        StableDiffusionCppSdServerWorker.CancelActiveGeneration();

        lock (ActiveProcessLock)
        {
            TryKillProcess(_activeProcess);
        }
    }

    public static void ReleasePersistentWorker()
    {
        StableDiffusionCppSdServerWorker.ReleaseContext();
    }

    internal static void PublishProgress(StableDiffusionCppWorkerProgressResponse progress)
    {
        ProgressChanged?.Invoke(progress);
    }

    public static async Task<bool> PrewarmTxt2ImgAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (settings == null || request == null)
        {
            return false;
        }

        StableDiffusionCppGenerationRequest requestCopy = request.Clone();
        requestCopy.mode = StableDiffusionCppGenerationMode.Txt2Img;

        if (!TryNormalizeRequestForGeneration(settings, requestCopy, out _))
        {
            return false;
        }

        StableDiffusionCppPreparationResult prep = PrepareRuntime(
            settings,
            forceReinstall: false,
            modelPathOverride: requestCopy.modelPathOverride);
        if (!prep.Success)
        {
            return false;
        }

        if (!StableDiffusionCppSdServerWorker.CanUsePersistentServer(settings, prep, requestCopy))
        {
            return false;
        }

        return await StableDiffusionCppSdServerWorker.PrewarmAsync(prep, requestCopy, cancellationToken);
    }

    public static StableDiffusionCppPreparationResult PrepareRuntime(
        StableDiffusionCppSettings settings,
        bool forceReinstall = false,
        string modelPathOverride = null)
    {
        if (settings == null)
        {
            return StableDiffusionCppPreparationResult.Fail("StableDiffusionCppSettings is null.");
        }

        StableDiffusionCppPlatformId platform = StableDiffusionCppPlatformResolver.GetCurrentPlatform();
        if (platform == StableDiffusionCppPlatformId.Unknown)
        {
            return StableDiffusionCppPreparationResult.Fail($"Unsupported platform: {Application.platform}");
        }

        StableDiffusionCppRuntimePackage package = settings.GetPackageForPlatform(platform);
        if (package == null)
        {
            return StableDiffusionCppPreparationResult.Fail(
                $"No runtime package configured for platform {platform}. Add one in StableDiffusionCppSettings.");
        }

        string sourceRuntimeDir = settings.ResolvePathFromStreamingAssetsOrAbsolute(package.streamingAssetsRuntimeFolder);
        if (!Directory.Exists(sourceRuntimeDir))
        {
            return StableDiffusionCppPreparationResult.Fail(
                $"Runtime source directory not found: {sourceRuntimeDir}\n" +
                "Place stable-diffusion.cpp runtime files under StreamingAssets.");
        }

        string sourceExecutablePath = Path.Combine(sourceRuntimeDir, package.executableFileName ?? string.Empty);
        if (!File.Exists(sourceExecutablePath))
        {
            return StableDiffusionCppPreparationResult.Fail(
                $"Runtime executable not found: {sourceExecutablePath}");
        }

        string installRoot = Path.Combine(
            Application.persistentDataPath,
            "sdcpp",
            SanitizePathToken(settings.runtimeVersion),
            platform.ToString());
        string installExecutablePath = Path.Combine(installRoot, package.executableFileName ?? string.Empty);
        string installNativeLibraryPath = string.IsNullOrWhiteSpace(package.nativeLibraryFileName)
            ? string.Empty
            : Path.Combine(installRoot, package.nativeLibraryFileName.Trim());

        try
        {
            if (forceReinstall && Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, true);
            }

            bool requiresCopy = forceReinstall
                                || !Directory.Exists(installRoot)
                                || !File.Exists(installExecutablePath);
            if (requiresCopy)
            {
                CopyDirectory(sourceRuntimeDir, installRoot, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            return StableDiffusionCppPreparationResult.Fail(
                $"Failed to prepare runtime files in persistentDataPath:\n{ex}");
        }

        string modelPath = string.IsNullOrWhiteSpace(modelPathOverride)
            ? settings.ResolveModelPath()
            : settings.ResolvePathFromStreamingAssetsOrAbsolute(modelPathOverride);
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return StableDiffusionCppPreparationResult.Fail(
                string.IsNullOrWhiteSpace(modelPathOverride)
                    ? "Model path is empty in settings."
                    : $"Model path override is empty/invalid: '{modelPathOverride}'.");
        }

        if (!File.Exists(modelPath))
        {
            return StableDiffusionCppPreparationResult.Fail(
                $"Model file not found: {modelPath}\n" +
                "Put the model under StreamingAssets or use an absolute model path.");
        }

        if (settings.copyModelToPersistentDataPath)
        {
            string modelDest = Path.Combine(
                Application.persistentDataPath,
                "sdmodels",
                SanitizePathToken(settings.runtimeVersion),
                Path.GetFileName(modelPath));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(modelDest) ?? Application.persistentDataPath);
                if (!File.Exists(modelDest) || new FileInfo(modelDest).Length != new FileInfo(modelPath).Length)
                {
                    File.Copy(modelPath, modelDest, overwrite: true);
                }
                modelPath = modelDest;
            }
            catch (Exception ex)
            {
                return StableDiffusionCppPreparationResult.Fail($"Failed to copy model to persistentDataPath:\n{ex}");
            }
        }

        string vaePath = settings.ResolveVaePath();
        if (!string.IsNullOrWhiteSpace(vaePath) && !File.Exists(vaePath))
        {
            return StableDiffusionCppPreparationResult.Fail($"Configured VAE file not found: {vaePath}");
        }

        return StableDiffusionCppPreparationResult.Ok(
            platform,
            installRoot,
            installExecutablePath,
            modelPath,
            vaePath,
            File.Exists(installNativeLibraryPath) ? installNativeLibraryPath : string.Empty);
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateTxt2ImgAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        StableDiffusionCppGenerationRequest requestCopy = request != null ? request.Clone() : null;
        if (requestCopy != null)
        {
            requestCopy.mode = StableDiffusionCppGenerationMode.Txt2Img;
        }

        return await GenerateAsync(settings, requestCopy, cancellationToken);
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateImg2ImgAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        StableDiffusionCppGenerationRequest requestCopy = request != null ? request.Clone() : null;
        if (requestCopy != null)
        {
            requestCopy.mode = StableDiffusionCppGenerationMode.Img2Img;
        }

        return await GenerateAsync(settings, requestCopy, cancellationToken);
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateInpaintAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        StableDiffusionCppGenerationRequest requestCopy = request != null ? request.Clone() : null;
        if (requestCopy != null)
        {
            requestCopy.mode = StableDiffusionCppGenerationMode.Inpaint;
        }

        return await GenerateAsync(settings, requestCopy, cancellationToken);
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateFromTexturesAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        Texture2D initImage,
        Texture2D maskImage = null,
        CancellationToken cancellationToken = default)
    {
        return await GenerateFromTexturesAsync(
            settings,
            request,
            initImage,
            maskImage,
            controlImage: null,
            cancellationToken);
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateFromTexturesAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        Texture2D initImage,
        Texture2D maskImage,
        Texture2D controlImage,
        CancellationToken cancellationToken = default)
    {
        DateTime startUtc = DateTime.UtcNow;
        StableDiffusionCppGenerationRequest requestCopy = request != null
            ? request.Clone()
            : new StableDiffusionCppGenerationRequest();

        string tempInputDirectory = string.Empty;
        try
        {
            if (initImage != null || maskImage != null || controlImage != null)
            {
                tempInputDirectory = CreateTempInputDirectory();

                if (initImage != null)
                {
                    if (!StableDiffusionCppImageIO.TryWriteTextureToUniqueTempPng(
                            initImage,
                            tempInputDirectory,
                            "init",
                            out string initImagePath,
                            out string initError))
                    {
                        return StableDiffusionCppGenerationResult.Failed(
                            initError,
                            -1,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            DateTime.UtcNow - startUtc);
                    }

                    requestCopy.initImagePath = initImagePath;
                    if (request == null || requestCopy.mode == StableDiffusionCppGenerationMode.Txt2Img)
                    {
                        requestCopy.mode = maskImage != null
                            ? StableDiffusionCppGenerationMode.Inpaint
                            : StableDiffusionCppGenerationMode.Img2Img;
                    }

                    if (requestCopy.useInitImageDimensions)
                    {
                        requestCopy.width = initImage.width;
                        requestCopy.height = initImage.height;
                    }
                }

                if (maskImage != null)
                {
                    if (!StableDiffusionCppImageIO.TryWriteTextureToUniqueTempPng(
                            maskImage,
                            tempInputDirectory,
                            "mask",
                            out string maskImagePath,
                            out string maskError))
                    {
                        return StableDiffusionCppGenerationResult.Failed(
                            maskError,
                            -1,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            DateTime.UtcNow - startUtc);
                    }

                    requestCopy.maskImagePath = maskImagePath;
                    requestCopy.mode = StableDiffusionCppGenerationMode.Inpaint;
                }

                if (controlImage != null)
                {
                    if (!StableDiffusionCppImageIO.TryWriteTextureToUniqueTempPng(
                            controlImage,
                            tempInputDirectory,
                            "control",
                            out string controlImagePath,
                            out string controlError))
                    {
                        return StableDiffusionCppGenerationResult.Failed(
                            controlError,
                            -1,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            DateTime.UtcNow - startUtc);
                    }

                    requestCopy.controlImagePath = controlImagePath;
                    requestCopy.useControlNet = true;
                    if (request == null || requestCopy.mode == StableDiffusionCppGenerationMode.Txt2Img)
                    {
                        requestCopy.mode = StableDiffusionCppGenerationMode.Sketch;
                    }
                }
            }

            return await GenerateAsync(settings, requestCopy, cancellationToken);
        }
        finally
        {
            CleanupTempInputDirectory(tempInputDirectory);
        }
    }

    public static async Task<StableDiffusionCppGenerationResult> GenerateAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        DateTime startUtc = DateTime.UtcNow;
        if (settings == null)
        {
            return StableDiffusionCppGenerationResult.Failed(
                "StableDiffusionCppSettings is null.",
                -1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TimeSpan.Zero);
        }

        if (request == null)
        {
            return StableDiffusionCppGenerationResult.Failed(
                "Generation request is null.",
                -1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TimeSpan.Zero);
        }

        StableDiffusionCppGenerationRequest requestCopy = request.Clone();
        if (!TryNormalizeRequestForGeneration(settings, requestCopy, out string requestError))
        {
            return StableDiffusionCppGenerationResult.Failed(
                requestError,
                -1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                TimeSpan.Zero);
        }

        StableDiffusionCppPreparationResult prep = PrepareRuntime(
            settings,
            forceReinstall: false,
            modelPathOverride: requestCopy.modelPathOverride);
        if (!prep.Success)
        {
            return StableDiffusionCppGenerationResult.Failed(
                prep.ErrorMessage,
                -1,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                DateTime.UtcNow - startUtc);
        }

        bool persistOutputToRequestedDirectory = requestCopy.persistOutputToRequestedDirectory;
        string requestedOutputDirectory = ResolveOutputDirectory(settings, requestCopy.outputDirectory);
        if (persistOutputToRequestedDirectory)
        {
            Directory.CreateDirectory(requestedOutputDirectory);
        }

        // Always write first into persistent temp output.
        // Some stable-diffusion.cpp Windows builds launched from LocalLow runtime path
        // fail to save directly into project paths (e.g., Assets/...).
        string cliOutputDirectory = GetCliTempOutputDirectory(settings);
        Directory.CreateDirectory(cliOutputDirectory);
        string cliOutputPath = ResolveOutputPath(cliOutputDirectory, requestCopy.outputFileName, requestCopy.outputFormat);

        string args = BuildArguments(settings, prep, requestCopy, cliOutputPath);
        if (StableDiffusionCppSdServerWorker.CanUsePersistentServer(settings, prep, requestCopy))
        {
            string serverCommandLine =
                "[bundled-sd-server] " +
                Path.Combine(prep.RuntimeInstallDirectory ?? string.Empty, "sd-server.exe") +
                " " + args;
            return await StableDiffusionCppSdServerWorker.GenerateAsync(
                settings,
                prep,
                requestCopy,
                serverCommandLine,
                cliOutputDirectory,
                cliOutputPath,
                requestedOutputDirectory,
                persistOutputToRequestedDirectory,
                startUtc,
                cancellationToken);
        }

        if (StableDiffusionCppSdServerWorker.IsBusy)
        {
            return StableDiffusionCppGenerationResult.Failed(
                "Generation is already running. Wait for completion or cancel first.",
                -1,
                prep.ExecutablePath + " " + args,
                string.Empty,
                string.Empty,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc);
        }

        StableDiffusionCppSdServerWorker.ReleaseContext();

        var startInfo = new ProcessStartInfo
        {
            FileName = prep.ExecutablePath,
            Arguments = args,
            WorkingDirectory = prep.RuntimeInstallDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var stdout = new StringBuilder(4096);
        var stderr = new StringBuilder(2048);
        Process process = null;
        GpuTelemetryStats gpuTelemetry = null;
        CancellationTokenSource gpuTelemetryCts = null;
        Task<GpuTelemetryStats> gpuTelemetryTask = null;

        try
        {
            lock (ActiveProcessLock)
            {
                if (_activeProcess != null)
                {
                    return StableDiffusionCppGenerationResult.Failed(
                        "Generation is already running. Wait for completion or cancel first.",
                        -1,
                        startInfo.FileName + " " + startInfo.Arguments,
                        string.Empty,
                        string.Empty,
                        requestedOutputDirectory,
                        DateTime.UtcNow - startUtc);
                }

                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _activeProcess = process;
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Failed to start stable-diffusion.cpp process.",
                    -1,
                    startInfo.FileName + " " + startInfo.Arguments,
                    stdout.ToString(),
                    stderr.ToString(),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (settings.enableGpuTelemetry)
            {
                gpuTelemetryCts = new CancellationTokenSource();
                gpuTelemetryTask = MonitorGpuTelemetryAsync(
                    process.Id,
                    Mathf.Clamp(settings.gpuTelemetryPollIntervalMs, 100, 2000),
                    gpuTelemetryCts.Token);
            }

            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(() => TryKillProcess(process));

            Task waitForExitTask = WaitForExitAsync(process);
            int timeoutSeconds = Mathf.Max(30, settings.processTimeoutSeconds);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);
            Task completed = await Task.WhenAny(waitForExitTask, timeoutTask);

            bool timedOut = completed == timeoutTask;
            if (timedOut)
            {
                TryKillProcess(process);
                await waitForExitTask;
            }
            else
            {
                await waitForExitTask;
            }

            // Ensure async output handlers drain remaining lines before reading buffers.
            process.WaitForExit();

            if (gpuTelemetryCts != null)
            {
                gpuTelemetryCts.Cancel();
                gpuTelemetry = await AwaitGpuTelemetrySafeAsync(gpuTelemetryTask);
                gpuTelemetryCts.Dispose();
                gpuTelemetryCts = null;
            }

            if (process.ExitCode != 0 || timedOut || cancellationToken.IsCancellationRequested)
            {
                string failure = timedOut
                    ? $"Process timed out after {timeoutSeconds} seconds."
                    : cancellationToken.IsCancellationRequested
                        ? "Generation cancelled."
                        : $"Process exited with code {process.ExitCode}.";

                return StableDiffusionCppGenerationResult.Failed(
                    failure,
                    process.ExitCode,
                    startInfo.FileName + " " + startInfo.Arguments,
                    stdout.ToString(),
                    stderr.ToString(),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancellationToken.IsCancellationRequested,
                    timedOut: timedOut,
                    gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                    peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                    peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                    gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
            }

            List<string> outputs = DetectOutputFiles(cliOutputDirectory, cliOutputPath, requestCopy, startUtc);
            if (outputs.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Generation finished but no output image file was found.",
                    process.ExitCode,
                    startInfo.FileName + " " + startInfo.Arguments,
                    stdout.ToString(),
                    stderr.ToString(),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                    peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                    peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                    gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
            }

            if (!persistOutputToRequestedDirectory)
            {
                return StableDiffusionCppGenerationResult.Succeeded(
                    outputs,
                    startInfo.FileName + " " + startInfo.Arguments,
                    stdout.ToString(),
                    stderr.ToString(),
                    cliOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    process.ExitCode,
                    gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                    peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                    peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                    gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
            }

            List<string> finalOutputs = CopyOutputsToRequestedDirectory(
                outputs,
                requestedOutputDirectory);
            if (finalOutputs.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    $"Generation succeeded but failed to copy outputs into target directory: {requestedOutputDirectory}",
                    process.ExitCode,
                    startInfo.FileName + " " + startInfo.Arguments,
                    stdout.ToString(),
                    stderr.ToString(),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                    peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                    peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                    gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
            }

            return StableDiffusionCppGenerationResult.Succeeded(
                finalOutputs,
                startInfo.FileName + " " + startInfo.Arguments,
                stdout.ToString(),
                stderr.ToString(),
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                process.ExitCode,
                gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
        }
        catch (Exception ex)
        {
            return StableDiffusionCppGenerationResult.Failed(
                $"Generation failed due to exception: {ex}",
                process != null && process.HasExited ? process.ExitCode : -1,
                startInfo.FileName + " " + startInfo.Arguments,
                stdout.ToString(),
                stderr.ToString(),
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                cancelled: cancellationToken.IsCancellationRequested,
                gpuTelemetryAvailable: gpuTelemetry != null && gpuTelemetry.Available,
                peakGpuMemoryMiB: gpuTelemetry != null ? gpuTelemetry.PeakMemoryMiB : 0,
                peakGpuUtilizationPercent: gpuTelemetry != null ? gpuTelemetry.PeakUtilizationPercent : 0,
                gpuTelemetrySamples: gpuTelemetry != null ? gpuTelemetry.Samples : 0);
        }
        finally
        {
            if (gpuTelemetryCts != null)
            {
                try
                {
                    gpuTelemetryCts.Cancel();
                }
                catch
                {
                    // Ignore disposal race from final cleanup.
                }
                gpuTelemetryCts.Dispose();
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (process != null)
            {
                ReleaseWindowsGpuProcessMemoryCounters(process.Id);
            }
#endif

            lock (ActiveProcessLock)
            {
                if (ReferenceEquals(_activeProcess, process))
                {
                    _activeProcess = null;
                }
            }

            process?.Dispose();
        }
    }

    private static string BuildArguments(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request,
        string outputPath)
    {
        int width = Mathf.Max(64, request.width);
        int height = Mathf.Max(64, request.height);
        int steps = Mathf.Max(1, request.steps);
        float cfg = Mathf.Max(0.1f, request.cfgScale);
        int batchCount = Mathf.Max(1, request.batchCount);
        int seed = request.seed;

        var parts = new List<string>
        {
            "--mode img_gen",
            "-m " + Quote(NormalizePathForCli(prep.ModelPath)),
            "-p " + Quote(request.prompt),
            "--width " + width.ToString(CultureInfo.InvariantCulture),
            "--height " + height.ToString(CultureInfo.InvariantCulture),
            "--steps " + steps.ToString(CultureInfo.InvariantCulture),
            "--cfg-scale " + cfg.ToString(CultureInfo.InvariantCulture),
            "--sampling-method " + QuoteOrRaw(request.sampler, "euler_a"),
            "--scheduler " + QuoteOrRaw(request.scheduler, "discrete"),
            "--seed " + seed.ToString(CultureInfo.InvariantCulture),
            "--batch-count " + batchCount.ToString(CultureInfo.InvariantCulture),
            "-o " + Quote(NormalizePathForCli(outputPath))
        };

        if (request.RequiresInitImage)
        {
            parts.Add("-i " + Quote(NormalizePathForCli(request.initImagePath)));
            parts.Add("--strength " + request.strength.ToString(CultureInfo.InvariantCulture));
        }

        if (request.RequiresMaskImage)
        {
            parts.Add("--mask " + Quote(NormalizePathForCli(request.maskImagePath)));
        }

        if (request.RequiresInitImage && request.overrideImageCfgScale)
        {
            parts.Add("--img-cfg-scale " + request.imageCfgScale.ToString(CultureInfo.InvariantCulture));
        }

        if (request.RequiresControlImage)
        {
            parts.Add("--control-net " + Quote(NormalizePathForCli(request.controlNetPathOverride)));
            parts.Add("--control-image " + Quote(NormalizePathForCli(request.controlImagePath)));
            parts.Add("--control-strength " + request.controlStrength.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(request.negativePrompt))
        {
            parts.Add("-n " + Quote(request.negativePrompt));
        }

        if (!string.IsNullOrWhiteSpace(prep.VaePath))
        {
            parts.Add("--vae " + Quote(NormalizePathForCli(prep.VaePath)));
        }

        if (request.offloadToCpu)
        {
            parts.Add("--offload-to-cpu");
        }

        if (request.clipOnCpu)
        {
            parts.Add("--clip-on-cpu");
        }

        if (request.vaeTiling)
        {
            parts.Add("--vae-tiling");
        }

        if (request.diffusionFlashAttention)
        {
            parts.Add("--diffusion-fa");
        }

        if (request.useCacheMode)
        {
            string cacheMode = string.IsNullOrWhiteSpace(request.cacheMode) ? "easycache" : request.cacheMode.Trim();
            parts.Add("--cache-mode " + Quote(cacheMode));

            if (!string.IsNullOrWhiteSpace(request.cacheOption))
            {
                parts.Add("--cache-option " + Quote(request.cacheOption.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(request.cachePreset))
            {
                parts.Add("--cache-preset " + Quote(request.cachePreset.Trim()));
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.globalAdditionalArguments))
        {
            parts.Add(settings.globalAdditionalArguments.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.extraArgumentsRaw))
        {
            parts.Add(request.extraArgumentsRaw.Trim());
        }

        return string.Join(" ", parts);
    }

    private static bool TryNormalizeRequestForGeneration(
        StableDiffusionCppSettings settings,
        StableDiffusionCppGenerationRequest request,
        out string error)
    {
        error = null;
        if (request == null)
        {
            error = "Generation request is null.";
            return false;
        }

        request.prompt ??= string.Empty;
        request.negativePrompt ??= string.Empty;
        request.initImagePath ??= string.Empty;
        request.maskImagePath ??= string.Empty;
        request.controlImagePath ??= string.Empty;
        request.controlNetPathOverride ??= string.Empty;
        request.modelPathOverride ??= string.Empty;
        request.outputDirectory ??= string.Empty;
        request.outputFileName ??= string.Empty;
        request.outputFormat = NormalizeFormat(request.outputFormat);
        request.extraArgumentsRaw ??= string.Empty;
        request.sampler = string.IsNullOrWhiteSpace(request.sampler) ? "euler_a" : request.sampler.Trim();
        request.scheduler = string.IsNullOrWhiteSpace(request.scheduler) ? "discrete" : request.scheduler.Trim();
        request.cacheMode = string.IsNullOrWhiteSpace(request.cacheMode) ? "easycache" : request.cacheMode.Trim();
        request.cacheOption ??= string.Empty;
        request.cachePreset ??= string.Empty;
        request.width = Mathf.Max(64, request.width);
        request.height = Mathf.Max(64, request.height);
        request.steps = Mathf.Max(1, request.steps);
        request.cfgScale = Mathf.Max(0.1f, request.cfgScale);
        request.strength = Mathf.Clamp(request.strength, 0.01f, 1f);
        request.imageCfgScale = Mathf.Max(0.1f, request.imageCfgScale);
        request.controlStrength = Mathf.Clamp(request.controlStrength, 0f, 2f);
        request.batchCount = Mathf.Max(1, request.batchCount);

        if (request.mode == StableDiffusionCppGenerationMode.Sketch)
        {
            request.useControlNet = true;
        }

        if (string.IsNullOrWhiteSpace(request.prompt))
        {
            error = "Prompt is required.";
            return false;
        }

        if (request.RequiresInitImage)
        {
            request.initImagePath = ResolveInputPath(request.initImagePath);
            if (string.IsNullOrWhiteSpace(request.initImagePath) || !File.Exists(request.initImagePath))
            {
                error = "Init image is required and must point to an existing file.";
                return false;
            }

            if (request.useInitImageDimensions)
            {
                if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(
                        request.initImagePath,
                        out Vector2Int initSize,
                        out string sizeError))
                {
                    error = $"Failed to read init image dimensions: {sizeError}";
                    return false;
                }

                request.width = Mathf.Max(64, initSize.x);
                request.height = Mathf.Max(64, initSize.y);
            }
        }

        if (request.RequiresMaskImage)
        {
            request.maskImagePath = ResolveInputPath(request.maskImagePath);
            if (string.IsNullOrWhiteSpace(request.maskImagePath) || !File.Exists(request.maskImagePath))
            {
                error = "Mask image is required for inpainting and must point to an existing file.";
                return false;
            }

            if (!TryValidateMaskDimensions(request.initImagePath, request.maskImagePath, out error))
            {
                return false;
            }
        }

        if (request.RequiresControlImage)
        {
            request.controlImagePath = ResolveInputPath(request.controlImagePath);
            if (string.IsNullOrWhiteSpace(request.controlImagePath) || !File.Exists(request.controlImagePath))
            {
                error = "Control image is required for Sketch/ControlNet generation and must point to an existing file.";
                return false;
            }

            request.controlNetPathOverride = settings.ResolveControlNetPath(request.controlNetPathOverride);
            if (string.IsNullOrWhiteSpace(request.controlNetPathOverride) || !File.Exists(request.controlNetPathOverride))
            {
                error =
                    "ControlNet model is required for Sketch/ControlNet generation and must point to an existing .gguf/.safetensors/.ckpt file.";
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateMaskDimensions(string initImagePath, string maskImagePath, out string error)
    {
        error = null;
        if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(initImagePath, out Vector2Int initSize, out string initError))
        {
            error = $"Failed to read init image dimensions: {initError}";
            return false;
        }

        if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(maskImagePath, out Vector2Int maskSize, out string maskError))
        {
            error = $"Failed to read mask image dimensions: {maskError}";
            return false;
        }

        if (initSize != maskSize)
        {
            error =
                $"Init image and mask image must have matching dimensions. Init={initSize.x}x{initSize.y}, Mask={maskSize.x}x{maskSize.y}.";
            return false;
        }

        return true;
    }

    private static string ResolveInputPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        string normalized = trimmed
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        string streamingAssetsCandidate = Path.Combine(Application.streamingAssetsPath, normalized);
        if (File.Exists(streamingAssetsCandidate))
        {
            return streamingAssetsCandidate;
        }

        string projectRoot = GetProjectRoot();
        string projectCandidate = Path.Combine(projectRoot, normalized);
        if (File.Exists(projectCandidate))
        {
            return projectCandidate;
        }

        return projectCandidate;
    }

    private static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string CreateTempInputDirectory()
    {
        string directory = Path.Combine(
            Application.temporaryCachePath,
            "sdcpp_input",
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CleanupTempInputDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppRuntime] Failed to delete temporary input directory '{directory}': {ex.Message}");
        }
    }

    private static string ResolveOutputDirectory(StableDiffusionCppSettings settings, string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            if (Path.IsPathRooted(outputDirectory))
            {
                return outputDirectory;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, outputDirectory.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        }

        return settings.ResolvePersistentOutputDirectory();
    }

    private static string GetCliTempOutputDirectory(StableDiffusionCppSettings settings)
    {
        return Path.Combine(
            Application.persistentDataPath,
            "sdcpp",
            "tmp_output",
            SanitizePathToken(settings.runtimeVersion));
    }

    private static string ResolveOutputPath(string outputDirectory, string outputFileName, string outputFormat)
    {
        string extension = NormalizeFormat(outputFormat);
        string fileNameWithoutExt = string.IsNullOrWhiteSpace(outputFileName)
            ? "sd_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            : Path.GetFileNameWithoutExtension(outputFileName.Trim());

        string finalName = fileNameWithoutExt + "." + extension;
        return Path.Combine(outputDirectory, finalName);
    }

    private static string NormalizeFormat(string outputFormat)
    {
        if (string.Equals(outputFormat, "jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outputFormat, "jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "jpg";
        }

        return "png";
    }

    private static List<string> DetectOutputFiles(
        string outputDirectory,
        string requestedOutputPath,
        StableDiffusionCppGenerationRequest request,
        DateTime generationStartUtc)
    {
        var outputs = new List<string>();
        if (File.Exists(requestedOutputPath))
        {
            outputs.Add(requestedOutputPath);
        }

        string requestedExt = Path.GetExtension(requestedOutputPath);
        string requestedPrefix = Path.GetFileNameWithoutExtension(requestedOutputPath);
        DateTime searchThresholdUtc = generationStartUtc.AddSeconds(-1);
        if (Directory.Exists(outputDirectory))
        {
            foreach (string file in Directory.GetFiles(outputDirectory))
            {
                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, requestedExt, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string baseName = Path.GetFileNameWithoutExtension(file);
                if (!baseName.StartsWith(requestedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(file);
                if (writeUtc < searchThresholdUtc)
                {
                    continue;
                }

                if (!outputs.Contains(file))
                {
                    outputs.Add(file);
                }
            }
        }

        outputs.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
        return outputs;
    }

    private static List<string> CopyOutputsToRequestedDirectory(IReadOnlyList<string> cliOutputs, string requestedOutputDirectory)
    {
        var result = new List<string>();
        if (cliOutputs == null || cliOutputs.Count == 0)
        {
            return result;
        }

        Directory.CreateDirectory(requestedOutputDirectory);
        for (int i = 0; i < cliOutputs.Count; i++)
        {
            string source = cliOutputs[i];
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                continue;
            }

            try
            {
                string fileName = Path.GetFileName(source);
                string destination = Path.Combine(requestedOutputDirectory, fileName);
                File.Copy(source, destination, overwrite: true);
                result.Add(destination);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[StableDiffusionCppRuntime] Failed to copy '{source}' to '{requestedOutputDirectory}': {ex.Message}");
            }
        }

        return result;
    }

    private static async Task<GpuTelemetryStats> MonitorGpuTelemetryAsync(
        int targetProcessId,
        int pollIntervalMs,
        CancellationToken cancellationToken)
    {
        var stats = new GpuTelemetryStats();
        int delayMs = Mathf.Clamp(pollIntervalMs, 100, 2000);

        while (!cancellationToken.IsCancellationRequested)
        {
            TrySampleGpuMemoryForProcess(stats, targetProcessId);
            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Try one final sample at end of run.
        TrySampleGpuMemoryForProcess(stats, targetProcessId);
        return stats;
    }

    private static async Task<GpuTelemetryStats> AwaitGpuTelemetrySafeAsync(Task<GpuTelemetryStats> task)
    {
        if (task == null)
        {
            return null;
        }

        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppRuntime] GPU telemetry monitor failed: {ex.Message}");
            return null;
        }
    }

    private static void TrySampleGpuMemoryForProcess(GpuTelemetryStats stats, int targetProcessId)
    {
        if (stats == null)
        {
            return;
        }

        bool sampled = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        sampled = TrySampleWindowsGpuProcessMemoryForProcess(stats, targetProcessId);
        if (!sampled)
        {
            sampled = TrySampleWindowsGpuProcessMemoryViaPowerShell(stats, targetProcessId);
        }
#endif
        if (!sampled)
        {
            TrySampleNvidiaSmiForProcess(stats, targetProcessId);
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static bool TrySampleWindowsGpuProcessMemoryForProcess(GpuTelemetryStats stats, int targetProcessId)
    {
        if (stats == null || targetProcessId <= 0)
        {
            return false;
        }

        WindowsGpuProcessMemoryCounters counters = GetOrCreateWindowsGpuProcessMemoryCounters(targetProcessId);
        if (counters == null)
        {
            return false;
        }

        try
        {
            if (!TryReadWindowsPerfCounterValue(counters.DedicatedUsageCounter, out long dedicatedBytes) ||
                !TryReadWindowsPerfCounterValue(counters.SharedUsageCounter, out long sharedBytes))
            {
                return false;
            }

            long totalBytes = 0L;
            if (dedicatedBytes > 0L)
            {
                totalBytes += dedicatedBytes;
            }

            if (sharedBytes > 0L)
            {
                totalBytes += sharedBytes;
            }

            int totalMiB = (int)(totalBytes / (1024L * 1024L));
            stats.PeakMemoryMiB = Mathf.Max(stats.PeakMemoryMiB, totalMiB);
            stats.Available = true;
            stats.Samples++;
            return true;
        }
        catch
        {
            ReleaseWindowsGpuProcessMemoryCounters(targetProcessId);
            return false;
        }
    }

    private static bool TrySampleWindowsGpuProcessMemoryViaPowerShell(GpuTelemetryStats stats, int targetProcessId)
    {
        if (stats == null || targetProcessId <= 0)
        {
            return false;
        }

        string script =
            "$targetPid=" + targetProcessId.ToString(CultureInfo.InvariantCulture) + "; " +
            "$samples=(Get-Counter '\\GPU Process Memory(*)\\Dedicated Usage','\\GPU Process Memory(*)\\Shared Usage').CounterSamples; " +
            "$total=0; foreach($s in $samples){ if($s.InstanceName -like ('pid_'+$targetPid+'_*')){ $total += [int64]$s.CookedValue } }; " +
            "Write-Output $total";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " + Quote(script),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using (var proc = new Process { StartInfo = psi })
            {
                if (!proc.Start())
                {
                    return false;
                }

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2500);
                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return false;
                }

                string trimmed = output.Trim();
                if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long totalBytes))
                {
                    return false;
                }

                if (totalBytes <= 0L)
                {
                    return false;
                }

                int totalMiB = (int)(totalBytes / (1024L * 1024L));
                stats.PeakMemoryMiB = Mathf.Max(stats.PeakMemoryMiB, totalMiB);
                stats.Available = true;
                stats.Samples++;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static WindowsGpuProcessMemoryCounters GetOrCreateWindowsGpuProcessMemoryCounters(int targetProcessId)
    {
        lock (WindowsGpuCountersLock)
        {
            if (WindowsGpuCountersByPid.TryGetValue(targetProcessId, out WindowsGpuProcessMemoryCounters existing))
            {
                return existing;
            }

            try
            {
                Type categoryType = ResolveType(
                    "System.Diagnostics.PerformanceCounterCategory, System.Diagnostics.PerformanceCounter",
                    "System.Diagnostics.PerformanceCounterCategory, System",
                    "System.Diagnostics.PerformanceCounterCategory");
                if (categoryType == null)
                {
                    return null;
                }

                object category = Activator.CreateInstance(categoryType, "GPU Process Memory");
                if (category == null)
                {
                    return null;
                }

                var getInstanceNamesMethod = categoryType.GetMethod("GetInstanceNames", Type.EmptyTypes);
                if (getInstanceNamesMethod == null)
                {
                    return null;
                }

                string[] instances = getInstanceNamesMethod.Invoke(category, null) as string[];
                if (instances == null || instances.Length == 0)
                {
                    return null;
                }

                string prefix = "pid_" + targetProcessId.ToString(CultureInfo.InvariantCulture) + "_";
                for (int i = 0; i < instances.Length; i++)
                {
                    string instance = instances[i];
                    if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var counters = new WindowsGpuProcessMemoryCounters
                    {
                        DedicatedUsageCounter = CreateWindowsPerfCounter("GPU Process Memory", "Dedicated Usage", instance),
                        SharedUsageCounter = CreateWindowsPerfCounter("GPU Process Memory", "Shared Usage", instance)
                    };
                    if (counters.DedicatedUsageCounter == null || counters.SharedUsageCounter == null)
                    {
                        SafeDisposeWindowsPerfCounter(counters.DedicatedUsageCounter);
                        SafeDisposeWindowsPerfCounter(counters.SharedUsageCounter);
                        continue;
                    }

                    WindowsGpuCountersByPid[targetProcessId] = counters;
                    return counters;
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static void ReleaseWindowsGpuProcessMemoryCounters(int targetProcessId)
    {
        lock (WindowsGpuCountersLock)
        {
            if (!WindowsGpuCountersByPid.TryGetValue(targetProcessId, out WindowsGpuProcessMemoryCounters counters))
            {
                return;
            }

            WindowsGpuCountersByPid.Remove(targetProcessId);
            SafeDisposeWindowsPerfCounter(counters.DedicatedUsageCounter);
            SafeDisposeWindowsPerfCounter(counters.SharedUsageCounter);
        }
    }

    private static object CreateWindowsPerfCounter(string categoryName, string counterName, string instanceName)
    {
        Type counterType = ResolveType(
            "System.Diagnostics.PerformanceCounter, System.Diagnostics.PerformanceCounter",
            "System.Diagnostics.PerformanceCounter, System",
            "System.Diagnostics.PerformanceCounter");
        if (counterType == null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(counterType, categoryName, counterName, instanceName, true);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadWindowsPerfCounterValue(object counter, out long value)
    {
        value = 0L;
        if (counter == null)
        {
            return false;
        }

        try
        {
            var method = counter.GetType().GetMethod("NextValue", Type.EmptyTypes);
            if (method == null)
            {
                return false;
            }

            object raw = method.Invoke(counter, null);
            if (raw == null)
            {
                return false;
            }

            float valueFloat = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            if (float.IsNaN(valueFloat) || float.IsInfinity(valueFloat))
            {
                return false;
            }

            value = (long)valueFloat;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SafeDisposeWindowsPerfCounter(object counter)
    {
        if (counter == null)
        {
            return;
        }

        try
        {
            if (counter is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }

            var disposeMethod = counter.GetType().GetMethod("Dispose", Type.EmptyTypes);
            disposeMethod?.Invoke(counter, null);
        }
        catch
        {
            // ignore dispose failures
        }
    }

    private static Type ResolveType(params string[] typeNames)
    {
        for (int i = 0; i < typeNames.Length; i++)
        {
            string typeName = typeNames[i];
            if (string.IsNullOrWhiteSpace(typeName))
            {
                continue;
            }

            Type type = Type.GetType(typeName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
#endif

    private static void TrySampleNvidiaSmiForProcess(GpuTelemetryStats stats, int targetProcessId)
    {
        if (stats == null)
        {
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = "--query-compute-apps=pid,used_gpu_memory --format=csv,noheader,nounits",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using (var proc = new Process { StartInfo = psi })
            {
                if (!proc.Start())
                {
                    return;
                }

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                {
                    return;
                }

                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool parsedAny = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] cols = lines[i].Split(',');
                    if (cols.Length < 2)
                    {
                        continue;
                    }

                    if (!int.TryParse(cols[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
                        pid != targetProcessId)
                    {
                        continue;
                    }

                    if (!int.TryParse(cols[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int usedMiB))
                    {
                        continue;
                    }

                    stats.PeakMemoryMiB = Mathf.Max(stats.PeakMemoryMiB, usedMiB);
                    parsedAny = true;
                }

                if (parsedAny)
                {
                    stats.Available = true;
                    stats.Samples++;
                }
            }
        }
        catch
        {
            // Ignore optional telemetry failures.
        }
    }

    private static string Quote(string value)
    {
        if (value == null)
        {
            return "\"\"";
        }

        string escaped = value.Replace("\"", "\\\"");
        return "\"" + escaped + "\"";
    }

    private static string NormalizePathForCli(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/');
    }

    private static string QuoteOrRaw(string value, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (normalized.IndexOf(' ') >= 0)
        {
            return Quote(normalized);
        }

        return normalized;
    }

    private static Task WaitForExitAsync(Process process)
    {
        var tcs = new TaskCompletionSource<object>();
        void Handler(object _, EventArgs __)
        {
            process.Exited -= Handler;
            tcs.TrySetResult(null);
        }

        process.Exited += Handler;
        if (process.HasExited)
        {
            process.Exited -= Handler;
            tcs.TrySetResult(null);
        }

        return tcs.Task;
    }

    private static void TryKillProcess(Process process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppRuntime] Failed to kill process: {ex.Message}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = directory.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
            string destFile = Path.Combine(destinationDir, relative);
            string parent = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, destFile, overwrite);
        }
    }

    private static string SanitizePathToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "default";
        }

        string value = token.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
