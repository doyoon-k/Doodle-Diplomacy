# Stable Diffusion CPP Runtime (Unity)

This project now includes a shipping-oriented integration path for `stable-diffusion.cpp`.

## Goal

- Package runtime binaries with the game.
- Bootstrap those binaries to `persistentDataPath` at runtime.
- Generate images from Unity with a persistent sidecar worker process when possible.
- Fall back to launching the local `sd-cli` process for requests that rely on raw CLI-only options.

## Added Components

- `StableDiffusionCppSettings` (`ScriptableObject`)
  - Per-platform runtime package mapping
  - Model path, defaults, timeout, global CLI args
  - Persistent sidecar worker preference
- `StableDiffusionCppRuntime`
  - Runtime preparation (`StreamingAssets -> persistentDataPath`)
  - Persistent sidecar worker generation + one-shot process fallback
  - Process execution + cancellation + timeout + output detection
- `StableDiffusionCppGeneratorWindow` (Editor)
  - Menu: `Tools/AI/Stable Diffusion CPP/Generator`
  - Prompt/steps/cfg/seed/sampler input
  - Generate + cancel + preview + execution log

## Expected File Layout

```text
Assets/StreamingAssets/
  SDCpp/
    win-x64/
      sd-cli.exe
      stable-diffusion.dll
      cudart64_12.dll
      cublas64_12.dll
      cublasLt64_12.dll
      (required runtime DLLs)
  SDModels/
    stable-diffusion-v1-5-pruned-emaonly-Q4_1.gguf
    sd_turbo-f16-q8_0.gguf
```

## First Use

1. Open `Tools > LLM Pipeline > Setup Wizard`.
2. In `2-2) Stable Diffusion Models`, download a preset into `Assets/StreamingAssets/SDModels`.
3. Put runtime files under `Assets/StreamingAssets/SDCpp/win-x64`.
4. Open:
   - `Tools/AI/Stable Diffusion CPP/Generator`
5. Click `Prepare Runtime`, then `Generate`.

## Runtime Packaging Notes

- Runtime binaries are installed to:
  - `%persistentDataPath%/sdcpp/<runtimeVersion>/<platform>/...`
- Sidecar worker binaries are built to:
  - `%persistentDataPath%/sdcpp_sidecar/<runtimeVersion>/<platform>/...`
- Model is loaded from `StreamingAssets` by default.
- Optional model copy to persistent path is configurable.
- If `preferPersistentNativeWorker` is enabled, the first supported generation request builds/launches `Tools/StableDiffusionCppSidecarWorker` as a localhost worker process. That process loads `stable-diffusion.dll`, creates one cached `sd_ctx`, and reuses it across subsequent compatible requests.
- Because the model runs in a separate process, it avoids same-name CUDA DLL collisions with Unity/LlamaSharp native plugins inside the Editor/Player process.
- The worker project is built with `dotnet publish` on first launch or when source files change, so a local .NET SDK/runtime must be available on the development machine.
- If you need to free that cached context and stop the worker process manually (for example when leaving a paint mode), call `StableDiffusionCppRuntime.ReleasePersistentWorker()`.
- In-flight sidecar generation still cannot be interrupted inside the C API, so canceling kills the worker process and the next request restarts it. One-shot `sd-cli` fallback also supports process kill.

## Notes

- The Setup Wizard now includes a Stable Diffusion installer path for recommended GGUF presets.
- Large model binaries are intentionally excluded from Git history; keep them in external storage and download on setup.
