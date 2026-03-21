# Stable Diffusion CPP Runtime (Unity)

This project now includes a shipping-oriented integration path for `stable-diffusion.cpp`.

## Goal

- Package runtime binaries with the game.
- Bootstrap those binaries to `persistentDataPath` at runtime.
- Generate images from Unity by launching the local process.

## Added Components

- `StableDiffusionCppSettings` (`ScriptableObject`)
  - Per-platform runtime package mapping
  - Model path, defaults, timeout, global CLI args
- `StableDiffusionCppRuntime`
  - Runtime preparation (`StreamingAssets -> persistentDataPath`)
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
- Model is loaded from `StreamingAssets` by default.
- Optional model copy to persistent path is configurable.

## Notes

- The Setup Wizard now includes a Stable Diffusion installer path for recommended GGUF presets.
- Large model binaries are intentionally excluded from Git history; keep them in external storage and download on setup.
