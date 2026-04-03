using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class StableDiffusionCppSidecarWorker
{
    private const string WorkerProjectRelativePath =
        "Tools/StableDiffusionCppSidecarWorker/StableDiffusionCppSidecarWorker.csproj";
    private const string WorkerDllFileName = "StableDiffusionCppSidecarWorker.dll";
    private const string WorkerStateFileName = "worker_state.json";
    private const string ListenIp = "127.0.0.1";

    private static readonly object WorkerLock = new object();
    private static readonly HttpClient HttpClient = new HttpClient();

    private static Process _workerProcess;
    private static Uri _baseUri;
    private static string _workerStatePath = string.Empty;
    private static bool _isBusy;
    private static bool _cancelRequested;

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

        // Raw CLI escape hatches and advanced cache presets still use the one-shot sd-cli backend.
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

        StopWorkerProcess();
    }

    internal static void ReleaseContext()
    {
        StopWorkerProcess();
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
            StableDiffusionCppWorkerGenerateRequest payload = BuildPayload(
                settings,
                prep,
                request,
                out string payloadError);
            if (payload == null)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    payloadError,
                    -1,
                    commandLine,
                    string.Empty,
                    string.Empty,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancellationToken.IsCancellationRequested);
            }

            if (!await EnsureWorkerProcessAsync(settings, prep, cancellationToken))
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Failed to start Stable Diffusion sidecar worker process. Disable Prefer Persistent Native Worker to fall back to one-shot sd-cli.",
                    -1,
                    commandLine,
                    string.Empty,
                    string.Empty,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancellationToken.IsCancellationRequested);
            }

            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(CancelActiveGeneration);

            StableDiffusionCppWorkerGenerateResponse workerResponse =
                await SendGenerateRequestAsync(settings, payload, cancellationToken);

            if (workerResponse == null)
            {
                StopWorkerProcess();
                return StableDiffusionCppGenerationResult.Failed(
                    "Stable Diffusion sidecar worker returned an empty response.",
                    -1,
                    commandLine,
                    string.Empty,
                    string.Empty,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancellationToken.IsCancellationRequested);
            }

            if (!workerResponse.success || workerResponse.cancelled || cancellationToken.IsCancellationRequested)
            {
                if (workerResponse.cancelled || cancellationToken.IsCancellationRequested)
                {
                    StopWorkerProcess();
                }

                return StableDiffusionCppGenerationResult.Failed(
                    string.IsNullOrWhiteSpace(workerResponse.errorMessage)
                        ? "Stable Diffusion sidecar worker generation failed."
                        : workerResponse.errorMessage,
                    -1,
                    commandLine,
                    workerResponse.stdOut,
                    workerResponse.stdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: workerResponse.cancelled ||
                               cancellationToken.IsCancellationRequested ||
                               IsCancellationRequested());
            }

            List<string> outputFiles = WriteOutputImages(
                workerResponse.images,
                workerOutputPath,
                request.outputFormat,
                out string writeError);
            if (outputFiles.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    writeError ?? "Stable Diffusion sidecar worker finished but no output image file was written.",
                    -1,
                    commandLine,
                    workerResponse.stdOut,
                    workerResponse.stdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            if (!persistOutputToRequestedDirectory)
            {
                return StableDiffusionCppGenerationResult.Succeeded(
                    outputFiles,
                    commandLine,
                    workerResponse.stdOut,
                    workerResponse.stdErr,
                    workerOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            List<string> finalOutputs = CopyOutputsToRequestedDirectory(
                outputFiles,
                requestedOutputDirectory,
                out string copyError);
            if (finalOutputs.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    copyError ?? $"Generation succeeded but failed to copy outputs into target directory: {requestedOutputDirectory}",
                    -1,
                    commandLine,
                    workerResponse.stdOut,
                    workerResponse.stdErr,
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            return StableDiffusionCppGenerationResult.Succeeded(
                finalOutputs,
                commandLine,
                workerResponse.stdOut,
                workerResponse.stdErr,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc);
        }
        catch (OperationCanceledException)
        {
            StopWorkerProcess();
            return StableDiffusionCppGenerationResult.Failed(
                "Generation cancelled.",
                -1,
                commandLine,
                string.Empty,
                string.Empty,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                cancelled: true);
        }
        catch (Exception ex)
        {
            bool cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
            StopWorkerProcess();
            return StableDiffusionCppGenerationResult.Failed(
                cancelled
                    ? "Generation cancelled."
                    : $"Stable Diffusion sidecar worker generation failed due to exception: {ex}",
                -1,
                commandLine,
                string.Empty,
                string.Empty,
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                cancelled: cancelled);
        }
        finally
        {
            ReleaseWorker();
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

    private static StableDiffusionCppWorkerGenerateRequest BuildPayload(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request,
        out string error)
    {
        error = null;

        StableDiffusionCppWorkerImagePayload initImage = null;
        StableDiffusionCppWorkerImagePayload maskImage = null;
        StableDiffusionCppWorkerImagePayload controlImage = null;

        if (request.RequiresInitImage &&
            !TryBuildImagePayload(request.initImagePath, request.width, request.height, 3, out initImage, out error))
        {
            return null;
        }

        if (request.RequiresMaskImage)
        {
            if (!TryBuildImagePayload(request.maskImagePath, request.width, request.height, 1, out maskImage, out error))
            {
                return null;
            }
        }
        else
        {
            maskImage = BuildSolidMaskPayload(request.width, request.height);
        }

        if (request.RequiresControlImage &&
            !TryBuildImagePayload(request.controlImagePath, request.width, request.height, 3, out controlImage, out error))
        {
            return null;
        }

        return new StableDiffusionCppWorkerGenerateRequest
        {
            runtimeInstallDirectory = prep.RuntimeInstallDirectory ?? string.Empty,
            nativeLibraryPath = prep.NativeLibraryPath ?? string.Empty,
            modelPath = prep.ModelPath ?? string.Empty,
            vaePath = prep.VaePath ?? string.Empty,
            controlNetPath = request.RequiresControlNetModel
                ? settings.ResolveControlNetPath(request.controlNetPathOverride)
                : string.Empty,
            prompt = request.prompt ?? string.Empty,
            negativePrompt = request.negativePrompt ?? string.Empty,
            width = Math.Max(64, request.width),
            height = Math.Max(64, request.height),
            steps = Math.Max(1, request.steps),
            cfgScale = Math.Max(0.1f, request.cfgScale),
            imageCfgScale = Math.Max(0.1f, request.imageCfgScale),
            overrideImageCfgScale = request.overrideImageCfgScale,
            strength = Mathf.Clamp(request.strength, 0.01f, 1f),
            seed = request.seed,
            batchCount = Math.Max(1, request.batchCount),
            sampler = request.sampler ?? string.Empty,
            scheduler = request.scheduler ?? string.Empty,
            controlStrength = Mathf.Clamp(request.controlStrength, 0f, 2f),
            offloadToCpu = request.offloadToCpu,
            clipOnCpu = request.clipOnCpu,
            vaeTiling = request.vaeTiling,
            diffusionFlashAttention = request.diffusionFlashAttention,
            useCacheMode = request.useCacheMode,
            cacheMode = request.cacheMode ?? string.Empty,
            initImage = initImage,
            maskImage = maskImage,
            controlImage = controlImage
        };
    }

    private static bool TryBuildImagePayload(
        string imagePath,
        int targetWidth,
        int targetHeight,
        int channelCount,
        out StableDiffusionCppWorkerImagePayload payload,
        out string error)
    {
        payload = null;
        if (!StableDiffusionCppImageIO.TryLoadImageFileAsRawBytes(
                imagePath,
                targetWidth,
                targetHeight,
                channelCount,
                out byte[] bytes,
                out int width,
                out int height,
                out error))
        {
            return false;
        }

        payload = new StableDiffusionCppWorkerImagePayload
        {
            width = width,
            height = height,
            channelCount = channelCount,
            base64Data = Convert.ToBase64String(bytes)
        };
        return true;
    }

    private static StableDiffusionCppWorkerImagePayload BuildSolidMaskPayload(int width, int height)
    {
        int resolvedWidth = Math.Max(64, width);
        int resolvedHeight = Math.Max(64, height);
        var bytes = new byte[resolvedWidth * resolvedHeight];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = 255;
        }

        return new StableDiffusionCppWorkerImagePayload
        {
            width = resolvedWidth,
            height = resolvedHeight,
            channelCount = 1,
            base64Data = Convert.ToBase64String(bytes)
        };
    }

    private static async Task<StableDiffusionCppWorkerGenerateResponse> SendGenerateRequestAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppWorkerGenerateRequest payload,
        CancellationToken cancellationToken)
    {
        Uri baseUri;
        lock (WorkerLock)
        {
            baseUri = _baseUri;
        }

        if (baseUri == null)
        {
            return null;
        }

        string json = JsonUtility.ToJson(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        int timeoutMs = Math.Max(30000, settings.processTimeoutSeconds * 1000);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        using HttpResponseMessage response = await HttpClient.PostAsync(
            new Uri(baseUri, "generate"),
            content,
            timeoutCts.Token);
        string responseJson = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return new StableDiffusionCppWorkerGenerateResponse
            {
                success = false,
                errorMessage = $"Sidecar worker returned HTTP {(int)response.StatusCode} with empty body."
            };
        }

        StableDiffusionCppWorkerGenerateResponse parsed =
            JsonUtility.FromJson<StableDiffusionCppWorkerGenerateResponse>(responseJson);
        if (parsed == null)
        {
            return new StableDiffusionCppWorkerGenerateResponse
            {
                success = false,
                errorMessage = $"Failed to parse sidecar worker response JSON: {responseJson}"
            };
        }

        if (!response.IsSuccessStatusCode && parsed.success)
        {
            parsed.success = false;
            if (string.IsNullOrWhiteSpace(parsed.errorMessage))
            {
                parsed.errorMessage = $"Sidecar worker returned HTTP {(int)response.StatusCode}.";
            }
        }

        return parsed;
    }

    private static List<string> WriteOutputImages(
        StableDiffusionCppWorkerImagePayload[] images,
        string baseOutputPath,
        string outputFormat,
        out string error)
    {
        error = null;
        var outputFiles = new List<string>();
        if (images == null || images.Length == 0)
        {
            error = "Stable Diffusion sidecar worker returned no image data.";
            return outputFiles;
        }

        string normalizedFormat = string.Equals(outputFormat, "jpg", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(outputFormat, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";

        for (int i = 0; i < images.Length; i++)
        {
            StableDiffusionCppWorkerImagePayload image = images[i];
            if (image == null || !image.HasData)
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(image.base64Data);
            }
            catch (Exception ex)
            {
                error = $"Failed to decode sidecar image payload {i}: {ex.Message}";
                return new List<string>();
            }

            string outputPath = ResolveIndexedOutputPath(baseOutputPath, i, images.Length, normalizedFormat);
            if (!StableDiffusionCppImageIO.TryWriteRawBytesToImageFile(
                    bytes,
                    image.width,
                    image.height,
                    image.channelCount,
                    outputPath,
                    normalizedFormat,
                    out string writeError))
            {
                error = writeError;
                return new List<string>();
            }

            outputFiles.Add(outputPath);
        }

        if (outputFiles.Count == 0)
        {
            error = "Stable Diffusion sidecar worker returned only empty images.";
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

    private static async Task<bool> EnsureWorkerProcessAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        CancellationToken cancellationToken)
    {
        if (await TryReuseRunningWorkerAsync(settings, prep, cancellationToken))
        {
            return true;
        }

        StopWorkerProcess();

        if (!TryResolveWorkerSourceProject(out string workerProjectPath, out string sourceError))
        {
            Debug.LogWarning($"[StableDiffusionCppSidecarWorker] {sourceError}");
            return false;
        }

        string workerInstallDirectory = GetWorkerInstallDirectory(settings, prep);
        string workerDllPath = Path.Combine(workerInstallDirectory, WorkerDllFileName);
        string workerStatePath = Path.Combine(workerInstallDirectory, WorkerStateFileName);
        if (!TryEnsureWorkerBinary(workerProjectPath, workerInstallDirectory, workerDllPath, out string buildError))
        {
            Debug.LogWarning($"[StableDiffusionCppSidecarWorker] {buildError}");
            return false;
        }

        int port = FindFreeLoopbackPort();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{Quote(workerDllPath)} --listen-ip {ListenIp} --listen-port {port} --parent-pid {Process.GetCurrentProcess().Id}",
            WorkingDirectory = workerInstallDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Debug.Log($"[StableDiffusionCppSidecarWorker] {e.Data}");
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Debug.LogWarning($"[StableDiffusionCppSidecarWorker] {e.Data}");
                }
            };

            if (!process.Start())
            {
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Uri baseUri = new Uri($"http://{ListenIp}:{port}/");
            if (!await WaitUntilHealthyAsync(baseUri, process, cancellationToken))
            {
                TryKillProcess(process);
                process.Dispose();
                return false;
            }

            lock (WorkerLock)
            {
                _workerProcess = process;
                _baseUri = baseUri;
                _workerStatePath = workerStatePath;
            }

            SaveWorkerState(workerStatePath, process.Id, port, workerDllPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppSidecarWorker] Failed to launch sidecar process: {ex.Message}");
            TryKillProcess(process);
            process.Dispose();
            return false;
        }
    }

    private static async Task<bool> TryReuseRunningWorkerAsync(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        CancellationToken cancellationToken)
    {
        Process process;
        Uri baseUri;
        lock (WorkerLock)
        {
            process = _workerProcess;
            baseUri = _baseUri;
        }

        if (process != null && !process.HasExited && baseUri != null &&
            await WaitUntilHealthyAsync(baseUri, process, cancellationToken, maxAttempts: 1))
        {
            return true;
        }

        string workerInstallDirectory = GetWorkerInstallDirectory(settings, prep);
        string workerStatePath = Path.Combine(workerInstallDirectory, WorkerStateFileName);
        if (!TryLoadWorkerState(workerStatePath, out StableDiffusionCppWorkerState state) ||
            state == null ||
            state.processId <= 0 ||
            state.port <= 0)
        {
            return false;
        }

        Process existing;
        try
        {
            existing = Process.GetProcessById(state.processId);
        }
        catch
        {
            return false;
        }

        if (TryResolveWorkerSourceProject(out string workerProjectPath, out _) &&
            !string.IsNullOrWhiteSpace(state.workerDllPath) &&
            File.Exists(state.workerDllPath))
        {
            DateTime latestSourceWriteUtc = GetLatestWorkerSourceWriteUtc(Path.GetDirectoryName(workerProjectPath));
            if (File.GetLastWriteTimeUtc(state.workerDllPath) < latestSourceWriteUtc)
            {
                TryKillProcess(existing);
                try
                {
                    existing.Dispose();
                }
                catch
                {
                    // Ignore stale process handle cleanup failures.
                }

                return false;
            }
        }

        Uri existingBaseUri = new Uri($"http://{ListenIp}:{state.port}/");
        if (!await WaitUntilHealthyAsync(existingBaseUri, existing, cancellationToken, maxAttempts: 1))
        {
            try
            {
                existing.Dispose();
            }
            catch
            {
                // Ignore stale process handle cleanup failures.
            }

            return false;
        }

        lock (WorkerLock)
        {
            _workerProcess = existing;
            _baseUri = existingBaseUri;
            _workerStatePath = workerStatePath;
        }

        return true;
    }

    private static async Task<bool> WaitUntilHealthyAsync(
        Uri baseUri,
        Process process,
        CancellationToken cancellationToken,
        int maxAttempts = 100)
    {
        if (baseUri == null || process == null)
        {
            return false;
        }

        int attempts = Math.Max(1, maxAttempts);
        for (int i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using HttpResponseMessage response = await HttpClient.GetAsync(
                    new Uri(baseUri, "health"),
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    StableDiffusionCppWorkerHealthResponse parsed =
                        JsonUtility.FromJson<StableDiffusionCppWorkerHealthResponse>(responseJson);
                    if (parsed != null && parsed.ok)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Retry until process becomes ready or exits.
            }

            if (i + 1 < attempts)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return false;
    }

    private static bool TryResolveWorkerSourceProject(out string projectPath, out string error)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        projectPath = Path.Combine(projectRoot, WorkerProjectRelativePath);
        if (File.Exists(projectPath))
        {
            error = null;
            return true;
        }

        error =
            $"Stable Diffusion sidecar worker project not found: {projectPath}. " +
            "Run from the Unity project workspace that contains the Tools/StableDiffusionCppSidecarWorker project.";
        return false;
    }

    private static bool TryEnsureWorkerBinary(
        string workerProjectPath,
        string workerInstallDirectory,
        string workerDllPath,
        out string error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(workerInstallDirectory);
        }
        catch (Exception ex)
        {
            error = $"Failed to create sidecar worker install directory '{workerInstallDirectory}': {ex.Message}";
            return false;
        }

        DateTime outputWriteUtc = File.Exists(workerDllPath)
            ? File.GetLastWriteTimeUtc(workerDllPath)
            : DateTime.MinValue;
        DateTime latestSourceWriteUtc = GetLatestWorkerSourceWriteUtc(Path.GetDirectoryName(workerProjectPath));
        if (File.Exists(workerDllPath) && outputWriteUtc >= latestSourceWriteUtc)
        {
            return true;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish {Quote(workerProjectPath)} -c Release -o {Quote(workerInstallDirectory)} --nologo",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                error = "Failed to start 'dotnet publish' for the Stable Diffusion sidecar worker.";
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                error =
                    "Failed to build Stable Diffusion sidecar worker with 'dotnet publish'. " +
                    $"ExitCode={process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}";
                return false;
            }

            if (!File.Exists(workerDllPath))
            {
                error =
                    $"Stable Diffusion sidecar worker build completed, but expected output was not found: {workerDllPath}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to build Stable Diffusion sidecar worker with 'dotnet publish': {ex.Message}";
            return false;
        }
    }

    private static DateTime GetLatestWorkerSourceWriteUtc(string workerProjectDirectory)
    {
        if (string.IsNullOrWhiteSpace(workerProjectDirectory) || !Directory.Exists(workerProjectDirectory))
        {
            return DateTime.MaxValue;
        }

        DateTime latest = DateTime.MinValue;
        string[] files = Directory.GetFiles(workerProjectDirectory, "*.cs", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            DateTime writeUtc = File.GetLastWriteTimeUtc(files[i]);
            if (writeUtc > latest)
            {
                latest = writeUtc;
            }
        }

        return latest;
    }

    private static string GetWorkerInstallDirectory(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep)
    {
        string runtimeVersion = settings != null && !string.IsNullOrWhiteSpace(settings.runtimeVersion)
            ? settings.runtimeVersion.Trim()
            : "default";
        string platformToken = prep != null
            ? prep.Platform.ToString()
            : StableDiffusionCppPlatformId.Unknown.ToString();
        return Path.Combine(
            Application.persistentDataPath,
            "sdcpp_sidecar",
            SanitizePathToken(runtimeVersion),
            SanitizePathToken(platformToken));
    }

    private static int FindFreeLoopbackPort()
    {
        TcpListener listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
                // Ignore probe port cleanup failures.
            }
        }
    }

    private static void StopWorkerProcess()
    {
        Process process;
        string workerStatePath;
        lock (WorkerLock)
        {
            process = _workerProcess;
            workerStatePath = _workerStatePath;
            _workerProcess = null;
            _baseUri = null;
            _workerStatePath = string.Empty;
        }

        TryKillProcess(process);
        try
        {
            process?.Dispose();
        }
        catch
        {
            // Ignore dispose failures.
        }

        if (string.IsNullOrWhiteSpace(workerStatePath))
        {
            return;
        }

        try
        {
            if (File.Exists(workerStatePath))
            {
                File.Delete(workerStatePath);
            }
        }
        catch
        {
            // Ignore state cleanup failures.
        }
    }

    private static void SaveWorkerState(string workerStatePath, int processId, int port, string workerDllPath)
    {
        if (string.IsNullOrWhiteSpace(workerStatePath))
        {
            return;
        }

        try
        {
            string parent = Path.GetDirectoryName(workerStatePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            string json = JsonUtility.ToJson(new StableDiffusionCppWorkerState
            {
                processId = processId,
                port = port,
                workerDllPath = workerDllPath ?? string.Empty
            });
            File.WriteAllText(workerStatePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppSidecarWorker] Failed to write worker state: {ex.Message}");
        }
    }

    private static bool TryLoadWorkerState(string workerStatePath, out StableDiffusionCppWorkerState state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(workerStatePath) || !File.Exists(workerStatePath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(workerStatePath, Encoding.UTF8);
            state = JsonUtility.FromJson<StableDiffusionCppWorkerState>(json);
            return state != null;
        }
        catch
        {
            return false;
        }
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
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppSidecarWorker] Failed to kill sidecar worker process: {ex.Message}");
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

    private static string Quote(string value)
    {
        if (value == null)
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
