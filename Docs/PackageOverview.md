# LLM Pipeline Package Overview

This document explains what the package does, which problems it solves, and what the demo project showcases.

## 1) What This Package Is

This package provides a **local LLM pipeline for Unity** based on **LLamaSharp + GGUF**.
It is designed to generate **structured AI outputs (JSON)** from game state and apply them directly to gameplay systems.

Core goals:
- Local/offline-capable inference
- Stateful prompt pipeline execution
- Reliable JSON schema-constrained outputs
- Unity Editor workflow for setup, validation, and packaging

## 2) Problems It Solves

Typical Unity + local LLM integration pain points are:

1. Backend setup complexity (`CPU`, `CUDA`, `Vulkan`, `Metal`)
2. Model path/profile management across scenes and projects
3. Stable parsing and merging of LLM results into gameplay state
4. Reusable packaging for migration to another project

This package addresses those with:

1. `LlmPipelineSetupWizard` for guided dependency/backend setup
2. `LlmGenerationProfile` + `PromptPipelineAsset` for data-driven configuration
3. `JSONLLMStateChainLink` for JSON generation + parse + merge + retry
4. `LlmPackageExportTool` for `Core` / `Release` `.unitypackage` export

## 3) Key Features

1. **Local GGUF Inference Runtime**
- `RuntimeLlamaSharpService` implements `ILlmService`
- Supports model preload and safe reinitialization when settings change

2. **State-Based Prompt Pipeline**
- Uses `Dictionary<string, string>` as shared state
- Supports mixed step types: `JsonLlm`, `CompletionLlm`, `CustomLink`

3. **Schema-Constrained JSON Output**
- `LlmGenerationProfile.jsonFields` drives schema generation
- Retries automatically on parse/schema failure

4. **Native Runtime Bootstrap**
- `LlamaNativeBootstrap` prepares plugin search paths and runtime layout
- Reduces backend initialization ambiguity on Windows

5. **Editor Tooling**
- Setup Wizard: dependency install, backend apply, model assignment, validation
- Prompt Pipeline Editor: graph-based pipeline authoring and simulation
- Packaging Tool: export `Core` / `Release` packages

6. **Demo Compatibility Helpers**
- `DemoInput` for Input System + Legacy compatibility
- `SampleSceneRenderCompatibility` for URP/non-URP sprite fallback handling

## 4) Main Components

| Area | Component | Responsibility |
|---|---|---|
| Runtime | `RuntimeLlamaSharpService` | Loads model, executes completion/json requests |
| Runtime | `LlamaSharpInterop` | Converts profile/runtime params to LLamaSharp calls |
| Runtime | `GamePipelineRunner` | Executes pipeline steps in sequence |
| Pipeline | `PromptPipelineAsset` | Defines step graph/order |
| Pipeline | `JSONLLMStateChainLink` | Structured JSON response and state merge |
| Pipeline | `CompletionChainLink` | Plain-text completion step |
| Pipeline | `StatTierEvaluationLink` | Example deterministic custom link |
| Profile | `LlmGenerationProfile` | Model path, runtime params, generation params, json schema |
| Editor | `LlmPipelineSetupWizard` | Setup/restore/apply/validate workflows |
| Editor | `LlmPackageExportTool` | Exports package variants |

## 5) What The Demo Shows

Demo title:
- **Adaptive Ability & Stat Evolution Engine**

Gameplay loop shown in the sample:

1. Player absorbs an item.
2. `ItemManager` builds state (`item_name`, `item_desc`, current character/stats).
3. `CharacterItemUsePipeline` runs:
- Step A: generates updated character/stats JSON
- Step B: generates new skill JSON from primitive combinations
4. Result state is applied to `PlayerStats` and `SkillManager`.
5. HUD reflects evolved stats/skills immediately.

Default demo controls:
- Move: `A/D`
- Jump: `Space`
- Basic attack: `J`
- Shoot: `K`
- Skills: `Q/E`
- Use item: `4`
- Swap item: `T`

## 6) Package Variants

1. `Core`
- Runtime + pipeline + profile + editor tooling only
- Best for integrating into an existing game project

2. `Release`
- `Core` + demo gameplay scripts/assets
- Best for showcase, onboarding, and reference implementation

## 7) Best Fit / Constraints

Good fit when:
1. You need local/offline-capable LLM-driven gameplay generation
2. You need structured outputs rather than free-form text only
3. You want authoring via ScriptableObjects and editor tooling

Important constraints:
1. `.gguf` model files are large and should be distributed separately
2. Backend must match user machine capability (`CPU/CUDA/Vulkan/Metal`)
3. URP sample visuals require correct renderer setup (2D Renderer for intended look)

## 8) Recommended Reading Order

1. `PackageOverview.md`
2. `Tutorial.html`
3. `QuickStart.md`
4. `PipelineEditorGuide.md`
5. `Troubleshooting.md`
