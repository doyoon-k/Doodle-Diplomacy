# Prompt Pipeline Editor Guide

This guide explains how to build, test, and save a prompt pipeline with the built-in Graph Editor.

## 1) Open The Editor

1. Open `Window > LLM > Prompt Pipeline Editor`.
2. In the toolbar, use:
- `Pipeline Asset` to select an existing asset.
- `New Asset` to create a new pipeline asset.
- `Add Step` to add a step node.
- `Save`, `Validate`, `Run`, `Ping Asset`.

## 2) Create Or Load A Pipeline Asset

1. Click `New Asset`.
2. Save it under a folder such as `Assets/ScriptableObjects/Pipeline`.
3. Set `Name` and `Description` in the `Pipeline Info` section.

If you already have one, select it in `Pipeline Asset`.

## 3) Build The Step Flow

Each step node has:
- `Exec In` / `Exec Out` for execution order.
- `State In` / `State Out` for state key flow visualization.

To build a sequence:
1. Add steps with `Add Step`.
2. Connect each step `Exec Out` to the next step `Exec In`.
3. Keep the flow linear unless you intentionally design branching behavior outside this executor.

Useful editing shortcuts:
1. `Ctrl+Z` / `Cmd+Z`: Undo
2. `Ctrl+Shift+Z`, `Cmd+Shift+Z`, or `Ctrl+Y`: Redo

## 4) Configure Step Types

Every step has `Step Kind`.

### A) `JsonLlm`

Use when you need structured outputs merged into state.

Required:
1. Assign `LLM Profile`.
2. Fill `User Prompt Template`.

Optional:
1. `Max Retries`
2. `Retry Delay (s)`

Notes:
1. This mode expects JSON-compatible output and retry behavior is applied on parse failure.
2. Use `Insert State Key` to insert placeholders like `{{item_name}}`.

### B) `CompletionLlm`

Use when plain text generation is enough.

Required:
1. Assign `LLM Profile`.
2. Fill `User Prompt Template`.

Notes:
1. Output is generally written to a text response key (pipeline/runtime dependent).

### C) `CustomLink`

Use for deterministic logic in C# between LLM steps.

Required:
1. Choose type from `Known Types`.
2. Fill constructor parameters in `Custom Parameters`.
3. Assign `Custom Asset` when the custom link expects a `ScriptableObject`.

Notes:
1. The editor auto-detects constructor parameters for known custom link types.
2. Type-compatible classes must implement `IStateChainLink` and `ICustomLinkStateProvider`.

## 5) Run Simulation In Editor

The right panel has `Simulation`.

1. Fill input fields shown in the simulation panel.
2. Click `Run Pipeline`.
3. Check:
- `Status` label
- `Step Log`
- Graph state updates after completion

If run fails:
1. Read `Step Log` first.
2. Check missing `LLM Profile`, invalid JSON schema, or custom link parameter mismatch.

## 6) Validate And Save

1. Click `Validate` to inspect summary (`Steps`, `State Keys`).
2. Click `Save` after major edits.
3. Use `Ping Asset` to quickly locate the asset in Project view.

## 7) Use The Pipeline At Runtime

Typical runtime usage:
1. Assign the pipeline asset to your game component (example: `ItemManager.pipelineAsset`).
2. Provide initial state dictionary.
3. Execute through `GamePipelineRunner.RunPipeline(...)`.
4. Apply returned state to gameplay systems.

## 8) Recommended Workflow

1. Draft prompt logic in 2-3 small steps.
2. Simulate in editor until state output is stable.
3. Add runtime integration.
4. Tune prompts and retry settings with real gameplay data.
5. Export as `Core` or `Release` package.

