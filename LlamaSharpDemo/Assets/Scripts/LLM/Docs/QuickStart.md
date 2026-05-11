# LLM Pipeline QuickStart

This guide gets the package running quickly with either local GGUF, cloud APIs, or a mixed pipeline.

Read `PackageOverview.md` first for architecture and scope.

## 1) Import Package

1. Import the `.unitypackage`.
2. Wait for script compilation.

## 2) Decide Your Inference Mode

1. Local-only: all steps use `LlmGenerationProfile`
2. Cloud-only: all steps use `CloudGenerationProfile`
3. Mixed: some steps local, some cloud

## 3) Local Setup (only if you use local steps)

1. Open `Tools > LLM Pipeline > Setup Wizard`.
2. Click `Use Recommended`.
3. Click `Install/Update Dependencies`.
4. Click `Apply Backend Configuration`.
5. Put `.gguf` model files under `Assets/StreamingAssets/Models/`.
6. Click `Apply Model To All Local Profiles`.
7. Click `Run Quick Validation`.

## 4) Cloud Setup (only if you use cloud steps)

1. Create `CloudGenerationProfile` assets from:
   - `Create > LLM > Cloud Generation Profile`
2. Choose `provider` and `modelId`.
3. Set API key through environment variable (recommended):
   - OpenAI: `OPENAI_API_KEY`
   - Anthropic: `ANTHROPIC_API_KEY`
   - Gemini: `GEMINI_API_KEY`
   - DeepSeek: `DEEPSEEK_API_KEY`
   - Kimi: `MOONSHOT_API_KEY`
4. Optional editor-only override:
   - Create `CloudCredentialOverridesAsset` under an `Editor` folder
   - Keep it out of source control and out of builds

## 5) Assign Profiles Per Pipeline Step

1. Open `Window > LLM > Prompt Pipeline Editor`.
2. Select your `PromptPipelineAsset`.
3. For each step, assign either:
   - local `LlmGenerationProfile`, or
   - cloud `CloudGenerationProfile`

No migration is required for existing local profile assets.

## 6) Run

1. Open your scene.
2. Press Play.
3. The runtime routes each step automatically:
   - local profile -> local LLamaSharp service
   - cloud profile -> cloud direct API service

## 7) Security Notes

1. Cloud v1 uses direct client API calls.
2. Use personal keys only.
3. Do not hardcode shared or production keys in project assets/scripts.

## First-Run Notes

1. Local first call can be slower due to model warm-up.
2. If active pipelines are cloud-only, local preload is skipped.
