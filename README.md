# Unity LLM / VLM / Stable Diffusion Integration

A Unity 6 AI integration project for building prompt pipelines that can route between local GGUF inference, cloud model APIs, vision-capable model calls, structured JSON outputs, and local image generation through `stable-diffusion.cpp`.

The project is centered on reusable Unity tooling:

- Local LLM inference with `LLamaSharp` and GGUF models.
- Vision-capable LLM/VLM requests through pipeline image inputs and optional multimodal projector configuration.
- Prompt pipeline assets with step-based execution, JSON parsing, retry handling, and state merging.
- A visual GraphView pipeline editor with validation and editor-time simulation.
- Cloud generation profiles for OpenAI, Anthropic, Gemini, DeepSeek, and Kimi.
- Per-step routing between local `LlmGenerationProfile` assets and cloud `CloudGenerationProfile` assets.
- Local image generation with `stable-diffusion.cpp`, packaged runtime binaries, and a persistent sidecar worker path.

## Pipeline Editor

### Visual pipeline authoring

![Pipeline graph editor](image-1.png)

### Editor-time pipeline simulation

![Pipeline simulation panel](image.png)

## Repository Layout

- `LlamaSharpDemo/` - Unity project folder.
- `LlamaSharpDemo/Assets/Scripts/LLM/` - prompt pipeline runtime, profiles, cloud adapters, editor tools, and package docs.
- `LlamaSharpDemo/Assets/Scripts/AI/StableDiffusionCpp/` - `stable-diffusion.cpp` runtime integration.
- `LlamaSharpDemo/Assets/StreamingAssets/Models/` - expected location for local GGUF LLM/VLM model files.
- `LlamaSharpDemo/Assets/StreamingAssets/SDCpp/` - expected location for packaged `stable-diffusion.cpp` runtime binaries.
- `LlamaSharpDemo/Assets/StreamingAssets/SDModels/` - expected location for Stable Diffusion model files.
- `Docs/` - supplemental AI tooling documentation.

## Setup

1. Open `LlamaSharpDemo` in Unity Hub with Unity `6000.2.11f1`.
2. Open `Tools > LLM Pipeline > Setup Wizard`.
3. Click `Use Recommended`, then install/update dependencies and apply the backend configuration.
4. Put local `.gguf` model files under `Assets/StreamingAssets/Models/`.
5. For local vision profiles, configure the model profile with the required multimodal projector file if the selected model requires one.
6. In the setup wizard, apply the selected model to local `LlmGenerationProfile` assets and run quick validation.
7. Open `Window > LLM > Prompt Pipeline Editor` to create, validate, save, and simulate `PromptPipelineAsset` files.

## Cloud Profiles

Cloud calls use `CloudGenerationProfile` assets and provider-specific API keys. Prefer environment variables over serialized credentials:

| Provider | Environment variable |
|---|---|
| OpenAI | `OPENAI_API_KEY` |
| Anthropic | `ANTHROPIC_API_KEY` |
| Gemini | `GEMINI_API_KEY` |
| DeepSeek | `DEEPSEEK_API_KEY` |
| Kimi | `MOONSHOT_API_KEY` |

Each pipeline step can reference either a local profile or a cloud profile. Runtime routing selects the matching service per step, so local-only, cloud-only, and mixed pipelines can share the same pipeline asset format.

## Stable Diffusion CPP

Optional local image generation uses `stable-diffusion.cpp`.

Expected layout:

```text
Assets/StreamingAssets/
  SDCpp/
    win-x64/
      sd-cli.exe
      stable-diffusion.dll
      (required runtime DLLs)
  SDModels/
    stable-diffusion-v1-5-pruned-emaonly-Q4_1.gguf
    sd_turbo-f16-q8_0.gguf
```

Use `Tools > AI > Stable Diffusion CPP > Generator` to prepare the runtime, run generation requests, preview outputs, and inspect execution logs.

Large model files and native runtime binaries are expected to be supplied outside the repository.

## Documentation

Recommended AI/tooling references:

1. [`PackageOverview.md`](LlamaSharpDemo/Assets/Scripts/LLM/Docs/PackageOverview.md)
2. [`QuickStart.md`](LlamaSharpDemo/Assets/Scripts/LLM/Docs/QuickStart.md)
3. [`PipelineEditorGuide.md`](LlamaSharpDemo/Assets/Scripts/LLM/Docs/PipelineEditorGuide.md)
4. [`Troubleshooting.md`](LlamaSharpDemo/Assets/Scripts/LLM/Docs/Troubleshooting.md)
5. [`Docs/PipelineEditorGuide.md`](Docs/PipelineEditorGuide.md)
6. [`Docs/StableDiffusionCppRuntime.md`](Docs/StableDiffusionCppRuntime.md)

## Notes

- Do not enable multiple native backend variants for the Unity Editor at the same time.
- Keep cloud API keys out of source control.
- Keep large model binaries and generated runtime packages out of Git unless there is an explicit distribution reason.
