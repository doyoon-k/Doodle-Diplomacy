# LLM Pipeline Package Overview

This document explains what the package does and how local GGUF + cloud API inference work together.

## 1) What This Package Is

This package provides a Unity LLM pipeline that supports:

1. Local inference with LLamaSharp + GGUF (`LlmGenerationProfile`)
2. Cloud inference with multiple providers (`CloudGenerationProfile`)
3. Step-level mixed execution (local and cloud in one pipeline)

Core goals:

1. Keep existing local GGUF workflows fully compatible
2. Add cloud inference without requiring pipeline migration
3. Keep JSON pipeline behavior consistent across providers

## 2) Problems It Solves

Typical Unity integration pain points:

1. Backend setup complexity for local inference (`CPU`, `CUDA`, `Vulkan`, `Metal`)
2. Switching between local and cloud providers per task
3. Stable parsing of model output into gameplay state
4. Packaging reusable tooling for Asset Store consumers

This package addresses those with:

1. `LlmPipelineSetupWizard` for local backend/model setup
2. Profile type split: local `LlmGenerationProfile` + `CloudGenerationProfile`
3. `RoutingLlmService` / `RoutingEditorLlmService` automatic service dispatch
4. Shared JSON handling via `JSONLLMStateChainLink` retry+parse loop

## 3) Key Features

1. **Local Runtime (legacy-compatible)**
- `RuntimeLlamaSharpService` remains available and unchanged in behavior for local profiles.

2. **Cloud Runtime (v1 scope)**
- `CloudDirectLlmService` supports text/JSON calls for:
  - OpenAI
  - Anthropic
  - Gemini
  - DeepSeek (OpenAI-compatible adapter)
  - Kimi (OpenAI-compatible adapter)

3. **Service Routing**
- `ILlmService` now accepts `BaseLlmGenerationProfile`.
- Routing selects local/cloud implementation from profile type.

4. **Step-Level Mixing**
- `PromptPipelineStep.llmProfile` now references `BaseLlmGenerationProfile`.
- A single pipeline can include local and cloud steps together.

5. **Consistent JSON Policy**
- JSON constraints use prompt guidance + parse retry flow in v1.
- Cloud HTTP retries are limited to `429` and `5xx` with exponential backoff.

6. **Credential Policy**
- Primary source: environment variables
- Optional editor-only override asset (`CloudCredentialOverridesAsset` under an `Editor` folder)
- Secrets are masked in cloud traffic logs

7. **Scope Boundaries**
- Cloud v1 supports text/JSON only
- Cloud vision and embeddings intentionally return not-supported errors

## 4) Main Components

| Area | Component | Responsibility |
|---|---|---|
| Profile | `BaseLlmGenerationProfile` | Shared prompt/JSON/sampling settings |
| Profile | `LlmGenerationProfile` | Local GGUF profile (legacy-compatible) |
| Profile | `CloudGenerationProfile` | Provider/model/baseUrl/retry/key-env settings |
| Runtime | `RoutingLlmService` | Routes per-step call to local or cloud |
| Runtime | `RuntimeLlamaSharpService` | Local LLamaSharp inference |
| Runtime | `CloudDirectLlmService` | Cloud provider HTTP inference |
| Runtime | `LlamaSharpInterop` | Shared prompt/inference parameter helpers |
| Pipeline | `PromptPipelineAsset` | Defines pipeline step graph/order |
| Pipeline | `JSONLLMStateChainLink` | JSON generation + parse + merge + retry |
| Pipeline | `CompletionChainLink` | Plain-text completion step |
| Editor | `RoutingEditorLlmService` | Editor simulation routing |
| Editor | `CloudGenerationProfileEditor` | Cloud profile inspector |
| Editor | `LlmPipelineSetupWizard` | Local backend/model setup wizard |
| Editor | `LlmPackageExportTool` | Core / Release export |

## 5) Preload Behavior

LLM preparation is now pipeline-aware:

1. If active pipelines contain at least one local step, local preload is required.
2. If active pipelines are cloud-only, preload is treated as ready immediately.

## 6) Security Notice (Cloud Direct Calls)

Cloud requests are direct client calls in v1.

1. Use your own personal API keys.
2. Never hardcode production/shared keys in assets or source code.
3. Keep editor override assets out of builds and out of source control.

## 7) Package Variants

1. `Core`
- Runtime + pipeline + profile + editor tooling

2. `Release`
- `Core` + demo gameplay scripts/assets

## 8) Recommended Reading Order

1. `PackageOverview.md`
2. `Tutorial.html`
3. `QuickStart.md`
4. `PipelineEditorGuide.md`
5. `Troubleshooting.md`
