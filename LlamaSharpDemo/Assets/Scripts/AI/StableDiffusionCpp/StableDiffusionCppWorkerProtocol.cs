using System;

[Serializable]
public sealed class StableDiffusionCppWorkerImagePayload
{
    public int width;
    public int height;
    public int channelCount = 3;
    public string base64Data = string.Empty;

    public bool HasData =>
        width > 0 &&
        height > 0 &&
        channelCount > 0 &&
        !string.IsNullOrEmpty(base64Data);
}

[Serializable]
public sealed class StableDiffusionCppWorkerGenerateRequest
{
    public string runtimeInstallDirectory = string.Empty;
    public string nativeLibraryPath = string.Empty;
    public string modelPath = string.Empty;
    public string vaePath = string.Empty;
    public string controlNetPath = string.Empty;

    public string prompt = string.Empty;
    public string negativePrompt = string.Empty;
    public int width = 512;
    public int height = 512;
    public int steps = 20;
    public float cfgScale = 7.0f;
    public float imageCfgScale = 7.0f;
    public bool overrideImageCfgScale;
    public float strength = 0.75f;
    public int seed = 42;
    public int batchCount = 1;
    public string sampler = "euler_a";
    public string scheduler = "discrete";
    public float controlStrength = 0.9f;
    public bool offloadToCpu;
    public bool clipOnCpu;
    public bool vaeTiling;
    public bool diffusionFlashAttention;
    public bool useCacheMode;
    public string cacheMode = "easycache";

    public StableDiffusionCppWorkerImagePayload initImage;
    public StableDiffusionCppWorkerImagePayload maskImage;
    public StableDiffusionCppWorkerImagePayload controlImage;
}

[Serializable]
public sealed class StableDiffusionCppWorkerGenerateResponse
{
    public bool success;
    public bool cancelled;
    public string errorMessage = string.Empty;
    public string stdOut = string.Empty;
    public string stdErr = string.Empty;
    public StableDiffusionCppWorkerImagePayload[] images = Array.Empty<StableDiffusionCppWorkerImagePayload>();
}

[Serializable]
public sealed class StableDiffusionCppWorkerHealthResponse
{
    public bool ok;
    public int processId;
    public bool hasLoadedContext;
    public bool isBusy;
}

[Serializable]
public sealed class StableDiffusionCppWorkerState
{
    public int processId;
    public int port;
    public string workerDllPath = string.Empty;
}
