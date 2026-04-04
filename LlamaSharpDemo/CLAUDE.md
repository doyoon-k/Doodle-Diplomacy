# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6 (6000.2.11f1) project integrating local LLM inference (LLamaSharp) and local image generation (stable-diffusion.cpp). The project centers on a drawing/painting prototype where players draw on a canvas and AI analyzes or transforms their artwork. Uses URP rendering pipeline and the new Input System.

## Build & Run

This is a Unity project — open the `LlamaSharpDemo` folder in Unity Hub. There is no CLI build command; all building and testing is done through the Unity Editor.

- **Scenes**: `Assets/Scenes/DrawingPrototype.unity` (drawing/painting prototype), `Assets/Scenes/SampleScene.unity`
- **Tests**: Run via Unity Test Runner (Window > General > Test Runner)
- **MCP integration**: The project uses `com.ivanmurzak.unity.mcp` (v0.63.3) for Unity Editor MCP tools — use the `mcp__ai-game-developer__*` tools to interact with the Unity Editor directly

## Architecture

### LLM Pipeline System (`Assets/Scripts/LLM/`)

The core abstraction is a sequential chain pipeline that passes mutable state between steps:

- **`ILlmService`** — Provider-agnostic LLM contract. Two implementations exist:
  - `RuntimeLlamaSharpService` (MonoBehaviour) — runtime, registers via `LlmServiceLocator` on Awake
  - `LlamaSharpEditorService` — editor-only, used by editor tools
- **`LlmServiceLocator`** — Static service locator; call `Require()` to get the active `ILlmService`
- **`LlmGenerationProfile`** (ScriptableObject) — Configures model path (GGUF), sampling params (temperature, top_p, top_k), context size, GPU layers, JSON schema constraints, system prompt template, and optional vision projector path
- **`PipelineState`** — Mutable key-value bag with two channels: text (`string`) for prompt template rendering and object (`object`) for rich data like `Texture2D`/`Sprite`. Keys are case-sensitive, trimmed
- **`PromptTemplate`** — Renders `{{varName}}` placeholders against `PipelineState`
- **`IStateChainLink`** — Single pipeline step interface: `Execute(PipelineState, Action<PipelineState>)` returns `IEnumerator` (coroutine-based)
- **`StateSequentialChainExecutor`** — Runs links sequentially; stops on `pipeline_error` key
- **Built-in chain links**:
  - `JSONLLMStateChainLink` — LLM call expecting JSON output, parses and merges keys into state. Supports retries
  - `CompletionChainLink` — Plain text LLM call, stores result in `response` key
- **`PromptPipelineAsset`** (ScriptableObject) — Serialized pipeline definition with a visual GraphView editor. Steps can be JsonLlm, CompletionLlm, or CustomLink (resolved by type name via reflection)
- **`GamePipelineRunner`** (MonoBehaviour, singleton) — Runtime pipeline executor, wraps `StateSequentialChainExecutor` with coroutine error handling

**Key constants**: `PromptPipelineConstants.AnswerKey = "response"`, `PromptPipelineConstants.ErrorKey = "pipeline_error"`

**JSON schema enforcement**: `LlmGenerationProfile` builds JSON schema from structured `JsonFieldDefinition` list. Schema is delivered via GBNF grammar, prompt append, or auto mode (`JsonSchemaDeliveryMode`). The grammar builder lives in `LlamaSharpInterop.JsonSchemaGrammarBuilder`.

### Custom Pipeline Links

To create a custom link: implement both `IStateChainLink` and `ICustomLinkStateProvider`. The system resolves constructors in this priority order:
1. `(Dictionary<string, string>, ScriptableObject)`
2. `(ScriptableObject)`
3. `(Dictionary<string, string>)`
4. Bindable simple-type constructor by parameter name
5. Parameterless

### Stable Diffusion Integration (`Assets/Scripts/AI/StableDiffusionCpp/`)

Local image generation using stable-diffusion.cpp via a sidecar .NET worker process (`Tools/StableDiffusionCppSidecarWorker/`). The sidecar communicates over HTTP on localhost. Key types:
- `StableDiffusionCppRuntime` — Static API for generation, manages the native process
- `StableDiffusionCppSidecarWorker` — Launches and manages the .NET sidecar process
- `StableDiffusionCppSettings` (ScriptableObject) — Global settings (binary paths, defaults)
- `StableDiffusionCppModelProfile` (ScriptableObject) — Per-model settings (SD 1.5, SD Turbo, etc.)

### Drawing System (`Assets/Scripts/Drawing/`)

Canvas-based drawing prototype with tools (Brush, Eraser, Fill, SketchGuide, StickerMaskErase). Key components:
- `DrawingBoardController` — Handles pointer input on a collider-backed surface, paints into runtime texture
- `DrawingCanvas` — Core texture manipulation
- `DrawingHistory` — Undo/redo support
- `DrawingStickerLayer` / `DrawingStickerExtractor` — Sticker extraction and overlay
- `DrawingSketchGuideGenerator` — Uses StableDiffusion ControlNet for sketch-based guidance
- `DrawingExportBridge` — Connects drawing output to LLM pipeline for analysis

### Editor Tools (`Assets/Scripts/LLM/Editor/`)

- `PromptPipelineGraphView` / `PromptPipelineGraphWindow` — Visual node graph editor for pipeline authoring
- `PromptPipelineSimulator` — Test pipelines in-editor with mock state
- `LlmGenerationProfileEditor` — Custom inspector for LLM profiles
- `ItemDataGenerator` — LLM-powered item generation editor tool (ScriptableObjects still exist under `Assets/ScriptableObjects/Items/Generated/`)
- `LlmPipelineSetupWizard` — Guided setup for new pipelines

## Key Patterns

- All LLM inference is coroutine-based (`IEnumerator`), bridging async LLamaSharp calls to Unity's main thread via `Task.Run` + polling
- `LlamaSharpInterop` is the central interop layer — model param creation, prompt building, inference execution, grammar caching
- A `SemaphoreSlim(1,1)` in `LlamaSharpInterop.InferenceGate` serializes all inference calls
- GGUF models are expected in `StreamingAssets/` by default (configurable per profile)
- ScriptableObject assets in `Assets/ScriptableObjects/` configure LLM profiles (`LlmProfiles/`), pipelines (`Pipeline/`), and SD model profiles (`StableDiffusion/`)
