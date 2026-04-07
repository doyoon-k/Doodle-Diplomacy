using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class StableDiffusionCppSdServerWorker
{
    private const string ServerExecutableFileName = "sd-server.exe";
    private const string ListenIp = "127.0.0.1";

    private static readonly object WorkerLock = new object();
    private static readonly object LogLock = new object();
    private static readonly HttpClient HttpClient =
        new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private static Process _serverProcess;
    private static Uri _baseUri;
    private static bool _isBusy;
    private static bool _cancelRequested;
    private static ServerSignature _loadedSignature;
    private static readonly StringBuilder StdOutBuffer = new StringBuilder(8192);
    private static readonly StringBuilder StdErrBuffer = new StringBuilder(4096);
    private static bool _runtimeCleanupRegistered;

    [Serializable]
    private sealed class Txt2ImgRequestDto
    {
        public string prompt = string.Empty;
        public string negative_prompt = string.Empty;
        public int width = 512;
        public int height = 512;
        public int steps = 20;
        public float cfg_scale = 7.0f;
        public int seed = 42;
        public int batch_size = 1;
        public string sampler_name = "euler_a";
        public string scheduler = "discrete";
    }

    [Serializable]
    private sealed class Txt2ImgResponseDto
    {
        public string[] images = Array.Empty<string>();
        public string info = string.Empty;
    }

    private sealed class ServerSignature
    {
        public string RuntimeInstallDirectory;
        public string ServerExecutablePath;
        public string ModelPath;
        public string VaePath;
        public bool OffloadToCpu;
        public bool ClipOnCpu;
        public bool VaeTiling;
        public bool DiffusionFlashAttention;
        public bool UseCacheMode;
        public string CacheMode;
        public string CacheOption;
        public string CachePreset;

        public static ServerSignature From(
            StableDiffusionCppPreparationResult prep,
            StableDiffusionCppGenerationRequest request)
        {
            return new ServerSignature
            {
                RuntimeInstallDirectory = prep != null ? prep.RuntimeInstallDirectory ?? string.Empty : string.Empty,
                ServerExecutablePath = prep != null
                    ? Path.Combine(prep.RuntimeInstallDirectory ?? string.Empty, ServerExecutableFileName)
                    : string.Empty,
                ModelPath = prep != null ? prep.ModelPath ?? string.Empty : string.Empty,
                VaePath = prep != null ? prep.VaePath ?? string.Empty : string.Empty,
                OffloadToCpu = request != null && request.offloadToCpu,
                ClipOnCpu = request != null && request.clipOnCpu,
                VaeTiling = request != null && request.vaeTiling,
                DiffusionFlashAttention = request != null && request.diffusionFlashAttention,
                UseCacheMode = request != null && request.useCacheMode,
                CacheMode = request != null ? request.cacheMode ?? string.Empty : string.Empty,
                CacheOption = request != null ? request.cacheOption ?? string.Empty : string.Empty,
                CachePreset = request != null ? request.cachePreset ?? string.Empty : string.Empty
            };
        }

        public bool Matches(ServerSignature other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(RuntimeInstallDirectory, other.RuntimeInstallDirectory, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ServerExecutablePath, other.ServerExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ModelPath, other.ModelPath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(VaePath, other.VaePath, StringComparison.OrdinalIgnoreCase) &&
                   OffloadToCpu == other.OffloadToCpu &&
                   ClipOnCpu == other.ClipOnCpu &&
                   VaeTiling == other.VaeTiling &&
                   DiffusionFlashAttention == other.DiffusionFlashAttention &&
                   UseCacheMode == other.UseCacheMode &&
                   string.Equals(CacheMode, other.CacheMode, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(CacheOption, other.CacheOption, StringComparison.Ordinal) &&
                   string.Equals(CachePreset, other.CachePreset, StringComparison.OrdinalIgnoreCase);
        }
    }

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterRuntimeCleanup()
    {
        if (_runtimeCleanupRegistered)
        {
            return;
        }

        Application.quitting += ReleaseContextOnQuit;
        _runtimeCleanupRegistered = true;
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorCleanup()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseContextOnQuit;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseContextOnQuit;
        UnityEditor.EditorApplication.quitting -= ReleaseContextOnQuit;
        UnityEditor.EditorApplication.quitting += ReleaseContextOnQuit;
    }
#endif

    internal static bool CanUsePersistentServer(
        StableDiffusionCppSettings settings,
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request)
    {
        if (settings == null || prep == null || request == null)
        {
            return false;
        }

        if (!settings.preferPersistentNativeWorker || !settings.preferSdServerBackend)
        {
            return false;
        }

        if (settings.enablePersistentWorkerProgressPreview)
        {
            return false;
        }

        if (request.mode != StableDiffusionCppGenerationMode.Txt2Img)
        {
            return false;
        }

        if (request.RequiresInitImage || request.RequiresMaskImage || request.RequiresControlImage)
        {
            return false;
        }

        if (!string.Equals(request.outputFormat, "png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.globalAdditionalArguments) ||
            !string.IsNullOrWhiteSpace(request.extraArgumentsRaw))
        {
            return false;
        }

        string serverPath = Path.Combine(prep.RuntimeInstallDirectory ?? string.Empty, ServerExecutableFileName);
        return File.Exists(serverPath);
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

        StopServerProcess();
    }

    internal static void ReleaseContext()
    {
        StopServerProcess();
    }

    internal static async Task<bool> PrewarmAsync(
        StableDiffusionCppPreparationResult prep,
        StableDiffusionCppGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (prep == null || request == null || IsBusy)
        {
            return false;
        }

        if (!TryAcquireWorker())
        {
            return false;
        }

        try
        {
            RegisterRuntimeCleanup();
            ServerSignature signature = ServerSignature.From(prep, request);
            return await EnsureServerProcessAsync(signature, cancellationToken);
        }
        finally
        {
            ReleaseWorker();
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
            RegisterRuntimeCleanup();

            ServerSignature signature = ServerSignature.From(prep, request);
            if (!await EnsureServerProcessAsync(signature, cancellationToken))
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Failed to start bundled sd-server process.",
                    -1,
                    commandLine,
                    CaptureStdOut(0),
                    CaptureStdErr(0),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancellationToken.IsCancellationRequested);
            }

            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(CancelActiveGeneration);

            int stdoutStart = GetStdOutLength();
            int stderrStart = GetStdErrLength();

            Txt2ImgRequestDto payload = BuildTxt2ImgPayload(request);
            string json = JsonUtility.ToJson(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            int timeoutMs = Math.Max(30000, settings.processTimeoutSeconds * 1000);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            HttpResponseMessage response = null;
            string responseJson = string.Empty;
            try
            {
                response = await HttpClient.PostAsync(new Uri(_baseUri, "sdapi/v1/txt2img"), content, timeoutCts.Token);
                responseJson = await response.Content.ReadAsStringAsync();
            }
            catch (OperationCanceledException)
            {
                bool cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
                StopServerProcess();
                return StableDiffusionCppGenerationResult.Failed(
                    cancelled ? "Generation cancelled." : $"Process timed out after {settings.processTimeoutSeconds} seconds.",
                    -1,
                    commandLine,
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc,
                    cancelled: cancelled,
                    timedOut: !cancelled);
            }
            finally
            {
                response?.Dispose();
            }

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Bundled sd-server returned an empty response body.",
                    -1,
                    commandLine,
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            Txt2ImgResponseDto parsed = JsonUtility.FromJson<Txt2ImgResponseDto>(responseJson);
            if (parsed == null || parsed.images == null || parsed.images.Length == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "Bundled sd-server completed but returned no images.",
                    -1,
                    commandLine,
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            List<string> outputFiles = WriteOutputImages(parsed.images, workerOutputPath, out string writeError);
            if (outputFiles.Count == 0)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    writeError ?? "Bundled sd-server returned image data but no output file was written.",
                    -1,
                    commandLine,
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            if (!persistOutputToRequestedDirectory)
            {
                return StableDiffusionCppGenerationResult.Succeeded(
                    outputFiles,
                    commandLine,
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
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
                    CaptureStdOut(stdoutStart),
                    CaptureStdErr(stderrStart),
                    requestedOutputDirectory,
                    DateTime.UtcNow - startUtc);
            }

            return StableDiffusionCppGenerationResult.Succeeded(
                finalOutputs,
                commandLine,
                CaptureStdOut(stdoutStart),
                CaptureStdErr(stderrStart),
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc);
        }
        catch (Exception ex)
        {
            bool cancelled = cancellationToken.IsCancellationRequested || IsCancellationRequested();
            if (cancelled)
            {
                StopServerProcess();
            }

            return StableDiffusionCppGenerationResult.Failed(
                cancelled
                    ? "Generation cancelled."
                    : $"Bundled sd-server generation failed due to exception: {ex}",
                -1,
                commandLine,
                CaptureStdOut(0),
                CaptureStdErr(0),
                requestedOutputDirectory,
                DateTime.UtcNow - startUtc,
                cancelled: cancelled);
        }
        finally
        {
            ReleaseWorker();
        }
    }

    private static Txt2ImgRequestDto BuildTxt2ImgPayload(StableDiffusionCppGenerationRequest request)
    {
        return new Txt2ImgRequestDto
        {
            prompt = request.prompt ?? string.Empty,
            negative_prompt = request.negativePrompt ?? string.Empty,
            width = Math.Max(64, request.width),
            height = Math.Max(64, request.height),
            steps = Math.Max(1, request.steps),
            cfg_scale = Math.Max(0.1f, request.cfgScale),
            seed = request.seed,
            batch_size = Math.Max(1, request.batchCount),
            sampler_name = string.IsNullOrWhiteSpace(request.sampler) ? "euler_a" : request.sampler.Trim(),
            scheduler = string.IsNullOrWhiteSpace(request.scheduler) ? "discrete" : request.scheduler.Trim()
        };
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

    private static async Task<bool> EnsureServerProcessAsync(ServerSignature signature, CancellationToken cancellationToken)
    {
        Process process;
        Uri baseUri;
        lock (WorkerLock)
        {
            process = _serverProcess;
            baseUri = _baseUri;
        }

        if (process != null &&
            !process.HasExited &&
            baseUri != null &&
            _loadedSignature != null &&
            _loadedSignature.Matches(signature) &&
            await WaitUntilReadyAsync(baseUri, process, cancellationToken, 1))
        {
            return true;
        }

        StopServerProcess();

        if (signature == null ||
            string.IsNullOrWhiteSpace(signature.ServerExecutablePath) ||
            !File.Exists(signature.ServerExecutablePath))
        {
            return false;
        }

        int port = FindFreeLoopbackPort();
        var startInfo = new ProcessStartInfo
        {
            FileName = signature.ServerExecutablePath,
            Arguments = BuildServerArguments(signature, port),
            WorkingDirectory = signature.RuntimeInstallDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var serverProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        lock (LogLock)
        {
            StdOutBuffer.Clear();
            StdErrBuffer.Clear();
        }

        serverProcess.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (LogLock)
            {
                StdOutBuffer.AppendLine(e.Data);
            }
        };

        serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            lock (LogLock)
            {
                StdErrBuffer.AppendLine(e.Data);
            }
        };

        try
        {
            if (!serverProcess.Start())
            {
                serverProcess.Dispose();
                return false;
            }

            serverProcess.BeginOutputReadLine();
            serverProcess.BeginErrorReadLine();

            Uri serverBaseUri = new Uri($"http://{ListenIp}:{port}/");
            if (!await WaitUntilReadyAsync(serverBaseUri, serverProcess, cancellationToken))
            {
                TryKillProcess(serverProcess);
                serverProcess.Dispose();
                return false;
            }

            lock (WorkerLock)
            {
                _serverProcess = serverProcess;
                _baseUri = serverBaseUri;
                _loadedSignature = signature;
            }

            return true;
        }
        catch
        {
            TryKillProcess(serverProcess);
            serverProcess.Dispose();
            return false;
        }
    }

    private static string BuildServerArguments(ServerSignature signature, int port)
    {
        var parts = new List<string>
        {
            "-m " + Quote(signature.ModelPath),
            "--listen-ip " + ListenIp,
            "--listen-port " + port.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(signature.VaePath))
        {
            parts.Add("--vae " + Quote(signature.VaePath));
        }

        if (signature.OffloadToCpu)
        {
            parts.Add("--offload-to-cpu");
        }

        if (signature.ClipOnCpu)
        {
            parts.Add("--clip-on-cpu");
        }

        if (signature.VaeTiling)
        {
            parts.Add("--vae-tiling");
        }

        if (signature.DiffusionFlashAttention)
        {
            parts.Add("--diffusion-fa");
        }

        if (signature.UseCacheMode)
        {
            string cacheMode = string.IsNullOrWhiteSpace(signature.CacheMode)
                ? "easycache"
                : signature.CacheMode.Trim();
            parts.Add("--cache-mode " + Quote(cacheMode));

            if (!string.IsNullOrWhiteSpace(signature.CacheOption))
            {
                parts.Add("--cache-option " + Quote(signature.CacheOption.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(signature.CachePreset))
            {
                parts.Add("--cache-preset " + Quote(signature.CachePreset.Trim()));
            }
        }

        return string.Join(" ", parts);
    }

    private static async Task<bool> WaitUntilReadyAsync(
        Uri baseUri,
        Process process,
        CancellationToken cancellationToken,
        int maxAttempts = 120)
    {
        if (baseUri == null || process == null)
        {
            return false;
        }

        for (int i = 0; i < Math.Max(1, maxAttempts); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using HttpResponseMessage response = await HttpClient.GetAsync(baseUri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Retry until process becomes ready or exits.
            }

            if (i + 1 < maxAttempts)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return false;
    }

    private static int FindFreeLoopbackPort()
    {
        System.Net.Sockets.TcpListener listener = null;
        try
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
                // Ignore probe cleanup failures.
            }
        }
    }

    private static void StopServerProcess()
    {
        Process process;
        lock (WorkerLock)
        {
            process = _serverProcess;
            _serverProcess = null;
            _baseUri = null;
            _loadedSignature = null;
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
        catch
        {
            // Ignore kill failures.
        }
    }

    private static List<string> WriteOutputImages(
        string[] images,
        string baseOutputPath,
        out string error)
    {
        error = null;
        var outputFiles = new List<string>();
        if (images == null || images.Length == 0)
        {
            error = "Bundled sd-server returned no image data.";
            return outputFiles;
        }

        for (int i = 0; i < images.Length; i++)
        {
            string imageBase64 = images[i];
            if (string.IsNullOrWhiteSpace(imageBase64))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(imageBase64);
            }
            catch (Exception ex)
            {
                error = $"Failed to decode sd-server image payload {i}: {ex.Message}";
                return new List<string>();
            }

            string outputPath = ResolveIndexedOutputPath(baseOutputPath, i, images.Length);
            try
            {
                File.WriteAllBytes(outputPath, bytes);
                outputFiles.Add(outputPath);
            }
            catch (Exception ex)
            {
                error = $"Failed to write sd-server image payload {i} to '{outputPath}': {ex.Message}";
                return new List<string>();
            }
        }

        if (outputFiles.Count == 0)
        {
            error = "Bundled sd-server returned only empty images.";
        }

        return outputFiles;
    }

    private static string ResolveIndexedOutputPath(string baseOutputPath, int imageIndex, int imageCount)
    {
        string parent = Path.GetDirectoryName(baseOutputPath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseOutputPath);
        string extension = Path.GetExtension(baseOutputPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        string fileName = imageCount > 1
            ? fileNameWithoutExtension + "_" + imageIndex.ToString(CultureInfo.InvariantCulture) + extension
            : fileNameWithoutExtension + extension;

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

    private static int GetStdOutLength()
    {
        lock (LogLock)
        {
            return StdOutBuffer.Length;
        }
    }

    private static int GetStdErrLength()
    {
        lock (LogLock)
        {
            return StdErrBuffer.Length;
        }
    }

    private static string CaptureStdOut(int startIndex)
    {
        lock (LogLock)
        {
            return CaptureBufferSlice(StdOutBuffer, startIndex);
        }
    }

    private static string CaptureStdErr(int startIndex)
    {
        lock (LogLock)
        {
            return CaptureBufferSlice(StdErrBuffer, startIndex);
        }
    }

    private static string CaptureBufferSlice(StringBuilder builder, int startIndex)
    {
        if (builder == null || builder.Length == 0)
        {
            return string.Empty;
        }

        int safeStart = Mathf.Clamp(startIndex, 0, builder.Length);
        return builder.ToString(safeStart, builder.Length - safeStart);
    }

    private static string Quote(string value)
    {
        string safe = value ?? string.Empty;
        return "\"" + safe.Replace("\"", "\\\"") + "\"";
    }

    private static void ReleaseContextOnQuit()
    {
        ReleaseContext();
    }
}
