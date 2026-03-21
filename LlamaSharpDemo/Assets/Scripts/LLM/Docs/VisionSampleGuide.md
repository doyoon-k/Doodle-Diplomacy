# Vision Sample Guide

This guide explains the minimal multimodal reference sample added to the Unity package.

## Sample Assets

The sample consists of these files:

1. `Assets/ScriptableObjects/LlmProfiles/VisionImageSummary.asset`
2. `Assets/ScriptableObjects/Pipeline/VisionImageSummaryPipeline.asset`
3. `Assets/Scripts/LLM/Samples/VisionPipelineSampleRunner.cs`

## Important Setup Requirement

`VisionImageSummary.asset` intentionally leaves `visionProjectorModel` empty.

You must assign a matching local vision projector / `mmproj` file before vision inference can run.

Required model combination:

1. A vision-capable base `.gguf` model
2. The matching projector / `mmproj` file for that exact model family

If the projector is missing, the pipeline is expected to fail with a clear runtime error such as:

`Vision inference requires 'visionProjectorModel' on the LlmGenerationProfile.`

## Editor Simulator Flow

1. Open `Window > LLM > Prompt Pipeline Editor`.
2. Select `Assets/ScriptableObjects/Pipeline/VisionImageSummaryPipeline.asset`.
3. Enter a value for `analysis_goal`.
4. Drag either a `Sprite` or `Texture2D` into `reference_image`.
5. Optionally click `Validate Image` to confirm the sample image normalizes correctly.
6. Click `Run Pipeline`.

Expected inputs:

1. `analysis_goal`: text
2. `reference_image`: image (`Texture2D` or `Sprite`)

## Runtime Flow

1. Open a scene that contains both `GamePipelineRunner` and `RuntimeLlamaSharpService`.
2. Add `VisionPipelineSampleRunner` to a scene object.
3. Assign `VisionImageSummaryPipeline.asset` to `pipelineAsset`.
4. Optionally assign `textureAsset` and `spriteAsset`.
5. Use one of these context menu commands on the component:
   - `Run Sample/Use Texture Asset`
   - `Run Sample/Use Sprite Asset`
   - `Run Sample/Use Generated Texture`

The generated texture path creates a runtime `Texture2D`, stores it with `PipelineState.SetImage("reference_image", texture)`, runs the pipeline, and then destroys the temporary texture after completion.

## Image Handling Notes

1. `Texture2D` inputs are used directly.
2. `Sprite` inputs are cropped to the sprite rect before inference.
3. The sample pipeline step resizes the longest side to `1024` before the VLM request.

## Failure Cases To Expect

1. Missing projector / `mmproj`
   - The service should return a clear error.

2. Missing `reference_image`
   - The vision step should fail with a message equivalent to:
   - `Required image key 'reference_image' is missing.`

3. Text-only model
   - If the model/profile does not support vision, the sample should fail instead of silently falling back to text-only generation.

4. Unsupported image type
   - `VisionPipelineSampleRunner` rejects any input that is not a `Texture2D` or `Sprite`.
