using System;
using System.Collections.Generic;

public enum StableDiffusionCppGenerationMode
{
    Txt2Img = 0,
    Img2Img = 1,
    Inpaint = 2,
    Sketch = 3
}

[Serializable]
public class StableDiffusionCppGenerationRequest
{
    public StableDiffusionCppGenerationMode mode = StableDiffusionCppGenerationMode.Txt2Img;
    public string prompt = "a cinematic photo of a red cube on a white table";
    public string negativePrompt = string.Empty;
    public string initImagePath = string.Empty;
    public string maskImagePath = string.Empty;
    public string controlImagePath = string.Empty;
    public string controlNetPathOverride = string.Empty;
    public int width = 512;
    public int height = 512;
    public int steps = 20;
    public float cfgScale = 7.0f;
    public float strength = 0.75f;
    public bool overrideImageCfgScale;
    public float imageCfgScale = 7.0f;
    public bool useInitImageDimensions;
    public int seed = 42;
    public int batchCount = 1;
    public string sampler = "euler_a";
    public string scheduler = "discrete";
    public string modelPathOverride = string.Empty;
    public bool useControlNet;
    public float controlStrength = 0.9f;
    public string outputDirectory = string.Empty;
    public string outputFileName = string.Empty;
    public string outputFormat = "png";
    public string extraArgumentsRaw = string.Empty;
    public bool offloadToCpu;
    public bool clipOnCpu;
    public bool vaeTiling;
    public bool diffusionFlashAttention;
    public bool useCacheMode;
    public string cacheMode = "easycache";
    public string cacheOption = string.Empty;
    public string cachePreset = string.Empty;
    public bool persistOutputToRequestedDirectory = true;

    public bool RequiresInitImage =>
        mode == StableDiffusionCppGenerationMode.Img2Img ||
        mode == StableDiffusionCppGenerationMode.Inpaint;

    public bool RequiresMaskImage => mode == StableDiffusionCppGenerationMode.Inpaint;
    public bool RequiresControlImage => mode == StableDiffusionCppGenerationMode.Sketch || useControlNet;
    public bool RequiresControlNetModel => mode == StableDiffusionCppGenerationMode.Sketch || useControlNet;

    public StableDiffusionCppGenerationRequest Clone()
    {
        return new StableDiffusionCppGenerationRequest
        {
            mode = mode,
            prompt = prompt,
            negativePrompt = negativePrompt,
            initImagePath = initImagePath,
            maskImagePath = maskImagePath,
            controlImagePath = controlImagePath,
            controlNetPathOverride = controlNetPathOverride,
            width = width,
            height = height,
            steps = steps,
            cfgScale = cfgScale,
            strength = strength,
            overrideImageCfgScale = overrideImageCfgScale,
            imageCfgScale = imageCfgScale,
            useInitImageDimensions = useInitImageDimensions,
            seed = seed,
            batchCount = batchCount,
            sampler = sampler,
            scheduler = scheduler,
            modelPathOverride = modelPathOverride,
            useControlNet = useControlNet,
            controlStrength = controlStrength,
            outputDirectory = outputDirectory,
            outputFileName = outputFileName,
            outputFormat = outputFormat,
            extraArgumentsRaw = extraArgumentsRaw,
            offloadToCpu = offloadToCpu,
            clipOnCpu = clipOnCpu,
            vaeTiling = vaeTiling,
            diffusionFlashAttention = diffusionFlashAttention,
            useCacheMode = useCacheMode,
            cacheMode = cacheMode,
            cacheOption = cacheOption,
            cachePreset = cachePreset,
            persistOutputToRequestedDirectory = persistOutputToRequestedDirectory
        };
    }
}

public sealed class StableDiffusionCppPreparationResult
{
    public bool Success { get; private set; }
    public string ErrorMessage { get; private set; }
    public string RuntimeInstallDirectory { get; private set; }
    public string ExecutablePath { get; private set; }
    public string ModelPath { get; private set; }
    public string VaePath { get; private set; }
    public StableDiffusionCppPlatformId Platform { get; private set; }

    public static StableDiffusionCppPreparationResult Fail(string error)
    {
        return new StableDiffusionCppPreparationResult
        {
            Success = false,
            ErrorMessage = error ?? "Unknown preparation failure."
        };
    }

    public static StableDiffusionCppPreparationResult Ok(
        StableDiffusionCppPlatformId platform,
        string runtimeInstallDirectory,
        string executablePath,
        string modelPath,
        string vaePath)
    {
        return new StableDiffusionCppPreparationResult
        {
            Success = true,
            Platform = platform,
            RuntimeInstallDirectory = runtimeInstallDirectory,
            ExecutablePath = executablePath,
            ModelPath = modelPath,
            VaePath = vaePath
        };
    }
}

public sealed class StableDiffusionCppGenerationResult
{
    public bool Success { get; private set; }
    public bool Cancelled { get; private set; }
    public bool TimedOut { get; private set; }
    public int ExitCode { get; private set; }
    public string ErrorMessage { get; private set; }
    public string OutputDirectory { get; private set; }
    public string CommandLine { get; private set; }
    public string StdOut { get; private set; }
    public string StdErr { get; private set; }
    public IReadOnlyList<string> OutputFiles { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public bool GpuTelemetryAvailable { get; private set; }
    public int PeakGpuMemoryMiB { get; private set; }
    public int PeakGpuUtilizationPercent { get; private set; }
    public int GpuTelemetrySamples { get; private set; }

    public static StableDiffusionCppGenerationResult Failed(
        string error,
        int exitCode,
        string commandLine,
        string stdOut,
        string stdErr,
        string outputDirectory,
        TimeSpan elapsed,
        bool cancelled = false,
        bool timedOut = false,
        bool gpuTelemetryAvailable = false,
        int peakGpuMemoryMiB = 0,
        int peakGpuUtilizationPercent = 0,
        int gpuTelemetrySamples = 0)
    {
        return new StableDiffusionCppGenerationResult
        {
            Success = false,
            Cancelled = cancelled,
            TimedOut = timedOut,
            ExitCode = exitCode,
            ErrorMessage = error ?? "Generation failed.",
            OutputDirectory = outputDirectory,
            CommandLine = commandLine,
            StdOut = stdOut ?? string.Empty,
            StdErr = stdErr ?? string.Empty,
            OutputFiles = Array.Empty<string>(),
            Elapsed = elapsed,
            GpuTelemetryAvailable = gpuTelemetryAvailable,
            PeakGpuMemoryMiB = peakGpuMemoryMiB,
            PeakGpuUtilizationPercent = peakGpuUtilizationPercent,
            GpuTelemetrySamples = gpuTelemetrySamples
        };
    }

    public static StableDiffusionCppGenerationResult Succeeded(
        IReadOnlyList<string> outputFiles,
        string commandLine,
        string stdOut,
        string stdErr,
        string outputDirectory,
        TimeSpan elapsed,
        int exitCode = 0,
        bool gpuTelemetryAvailable = false,
        int peakGpuMemoryMiB = 0,
        int peakGpuUtilizationPercent = 0,
        int gpuTelemetrySamples = 0)
    {
        return new StableDiffusionCppGenerationResult
        {
            Success = true,
            Cancelled = false,
            TimedOut = false,
            ExitCode = exitCode,
            ErrorMessage = string.Empty,
            OutputDirectory = outputDirectory,
            CommandLine = commandLine,
            StdOut = stdOut ?? string.Empty,
            StdErr = stdErr ?? string.Empty,
            OutputFiles = outputFiles ?? Array.Empty<string>(),
            Elapsed = elapsed,
            GpuTelemetryAvailable = gpuTelemetryAvailable,
            PeakGpuMemoryMiB = peakGpuMemoryMiB,
            PeakGpuUtilizationPercent = peakGpuUtilizationPercent,
            GpuTelemetrySamples = gpuTelemetrySamples
        };
    }
}
