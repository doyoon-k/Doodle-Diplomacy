# LLM Pipeline Troubleshooting

This file lists common setup/runtime problems and fast fixes.

## A) Duplicate Plugin Name Errors

Example:
- `Multiple plugins with the same name 'llama' ... only one plugin at a time can be used by Editor`

Cause:
- CPU and CUDA backend native DLLs are enabled together for Editor.

Fix:
1. Open `Tools > LLM Pipeline > Setup Wizard`.
2. Choose one backend (`CPU` or `CUDA12`).
3. Click `Install/Update Dependencies`.
4. Click `Apply Backend Configuration`.
5. Reopen Unity if needed.

## B) `NativeApi` Initialization Failed

Example:
- `LLama.Exceptions.RuntimeError: The native library cannot be correctly loaded`

Common causes:
1. Backend binaries are missing.
2. CUDA backend selected but CUDA runtime dependencies are not available.
3. Backend package/runtime mismatch.

Fix checklist:
1. Confirm backend mode is applied in Setup Wizard.
2. If `NuGet` menu is missing, click `Install NuGetForUnity` and wait for import/compile.
3. Run `Install/Update Dependencies` then `Run NuGet Restore`.
4. Confirm required native DLLs exist in `Assets/Plugins/x86_64`.
5. For CUDA mode, confirm CUDA 12 runtime path is available on machine.
6. Restart Unity after dependency changes.

## C) `DllNotFoundException: llama` or `libllama`

Cause:
- Native DLL name/path is not resolvable by runtime.

Fix:
1. Keep required DLLs in `Assets/Plugins/x86_64`.
2. Avoid mixing old backend copies from multiple locations.
3. Re-run backend apply in Setup Wizard.

## D) Model Not Found

Example:
- `Model file not found: ...`

Fix:
1. Place `.gguf` under `Assets/StreamingAssets/Models`.
2. Apply model path via Setup Wizard (`Apply Model To All Profiles`).
3. Validate path in each `LlmGenerationProfile`.

## E) First Request Freezes or Stutters

Cause:
- Initial native/model warm-up and memory allocation.

Fix:
1. Keep preload enabled (default behavior in current runtime service).
2. Wait for preload completion before starting gameplay-critical inference.
3. Tune threads/context size if frame spikes are high.

## F) Slow Model Download in Editor

Likely factors:
1. Hugging Face anonymous throttling.
2. Network route/CDN variance.
3. Editor-side transfer overhead.

Fix:
1. Use Hugging Face access token.
2. Prefer browser download for very large models if faster in your environment.
3. Import completed file into `StreamingAssets/Models`.

## G) Infinite Import Loop for `.part` Files

Example:
- `An infinite import loop has been detected ... .gguf.part`

Cause:
- Temporary partial download file under `Assets` triggers repeated imports.

Fix:
1. Keep temp/partial files outside `Assets` while downloading.
2. Move final file into `Assets/StreamingAssets/Models` only after completion.

## H) `InvalidOperationException` About `UnityEngine.Input`

Example:
- `You are trying to read Input using the UnityEngine.Input class, but you have switched active Input handling to Input System package`

Cause:
- Project is set to `Input System Package` only, while gameplay code expects legacy input calls.

Fix:
1. Update to the latest package scripts (they now use `DemoInput` compatibility wrapper).
2. Or temporary workaround: set `Project Settings > Player > Active Input Handling` to `Both`.

## I) Sample Scene Characters/Terrain Appear Black

Cause:
- Sample scene sprites use URP 2D lit materials. This can render dark/black when:
  1. project is non-URP, or
  2. project is URP but active renderer is not `2D Renderer`.

Fix:
1. Latest sample includes runtime fallback in `SampleScene` to `Sprites/Default`.
2. For intended URP visuals, install/enable URP and use a `2D Renderer` asset in project graphics/quality settings.
