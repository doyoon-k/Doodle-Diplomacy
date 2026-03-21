# LLM Pipeline QuickStart

This guide gets the package running in a fresh Unity project quickly.

For package intent, feature map, and demo explanation, read `PackageOverview.md` first.
For graph authoring workflow, read `PipelineEditorGuide.md`.
For full scene setup + LlmGenerationProfile details, read `Tutorial.html`.
For a minimal multimodal example, see `VisionSampleGuide.md`.

## 1) Import Package

1. Import the provided `.unitypackage`.
2. Wait for compilation to finish.

## 2) Open Setup Wizard

1. Open `Tools > LLM Pipeline > Setup Wizard`.
2. Click `Use Recommended` to auto-pick based on current machine.
3. You can also select backend manually:
   - `CPU` for maximum compatibility
   - `CUDA12` for NVIDIA GPU acceleration
   - `Vulkan` for Vulkan-capable systems
   - `Metal` for macOS (mapped to CPU backend package with Metal acceleration support)
4. If `NuGet` menu is missing, click `Install NuGetForUnity` and wait for package import/compile.
5. Click `Install/Update Dependencies`.
6. Click `Apply Backend Configuration`.

## 3) Add Model (GGUF)

Use one of these:

1. Copy model manually to:
   - `Assets/StreamingAssets/Models/<your-model>.gguf`
2. Or use Wizard download section to fetch from Hugging Face directly.

## 4) Apply Model to Profiles

1. In Setup Wizard, choose a model from `StreamingAssets Model`.
2. Click `Apply Model To All Profiles`.

## 5) Validate

1. Click `Validate Setup`.
2. Ensure no duplicate native plugin errors are shown.

## 6) Run Sample

1. Open `Assets/Scenes/SampleScene.unity`.
2. Press Play and execute your demo flow.

## 7) Edit Pipeline (Optional)

1. Open `Window > LLM > Prompt Pipeline Editor`.
2. Select `Assets/ScriptableObjects/Pipeline/CharacterItemUsePipeline.asset`.
3. Click `Run` to simulate with test state values.
4. Save changes and test in Play mode.

## First-Run Note

The first request can be slower due to native/model warm-up.  
`RuntimeLlamaSharpService` preloads on `Awake` to reduce this delay.
