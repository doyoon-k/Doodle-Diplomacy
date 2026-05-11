# LLM Pipeline Troubleshooting

Common setup/runtime issues and fixes for local + cloud mode.

## A) Duplicate Local Plugin Name Errors

Example:
- `Multiple plugins with the same name 'llama' ... only one plugin at a time can be used by Editor`

Cause:
- Multiple local backend variants enabled together.

Fix:
1. Open `Tools > LLM Pipeline > Setup Wizard`.
2. Select one backend.
3. Click `Install/Update Dependencies`.
4. Click `Apply Backend Configuration`.

## B) Local Native Init Failure

Example:
- `LLama.Exceptions.RuntimeError: The native library cannot be correctly loaded`

Fix checklist:
1. Verify backend was applied in Setup Wizard.
2. Re-run `Install/Update Dependencies`.
3. Run `Run NuGet Restore`.
4. Confirm required DLLs exist in `Assets/Plugins/x86_64`.

## C) Local Model Not Found

Example:
- `Model file not found: ...`

Fix:
1. Put `.gguf` under `Assets/StreamingAssets/Models`.
2. Use Setup Wizard `Apply Model To All Local Profiles`.
3. Check `model` path in each `LlmGenerationProfile`.

## D) Cloud API Key Missing

Example:
- `API key missing for provider ...`

Fix:
1. Set provider-specific environment variable.
2. Or create `CloudCredentialOverridesAsset` under an `Editor` folder.
3. Confirm `CloudGenerationProfile.apiKeyEnvironmentVariable` override if customized.

Priority order:
1. Environment variable
2. Editor-only override asset

## E) Cloud Authentication or Request Errors (401/403/400)

Behavior:
- Cloud v1 fails immediately on auth/request-format errors.

Checklist:
1. Verify API key value and provider match.
2. Check `modelId` validity for that provider.
3. Check `baseUrl` points to API root (not full endpoint path).

## F) Cloud Throttling / Transient Server Errors

Behavior:
- Automatic retry only for `429` and `5xx` (max 2 retries, exponential backoff).

If it still fails:
1. Reduce traffic.
2. Increase limits on your provider account.
3. Retry manually after cooldown.

## G) Cloud JSON Parse Failures

Symptoms:
- `Failed to parse valid JSON after ... attempts`

Fix:
1. Simplify prompt.
2. Reduce schema complexity in JSON fields.
3. Increase step-level `jsonMaxRetries`.

## H) Vision/Embedding Not Supported in Cloud v1

Behavior:
- Cloud service returns explicit not-supported errors for:
  - `GenerateCompletionWithImage`
  - `Embed`

Use local profile steps for these features until cloud scope expands.

## I) Preload Never Becomes Ready

Check pipeline composition:
1. If pipeline has local steps, GGUF preload must complete.
2. If pipeline is cloud-only, readiness should be immediate.

If local preload is required:
1. Verify model path and backend setup.
2. Check runtime logs for local load errors.

## J) Security Warning: Key Exposure Risk

Avoid:
1. Hardcoding API keys in scenes/prefabs/scripts
2. Committing editor override key assets

Recommended:
1. Environment variables for day-to-day development
2. Editor override asset only for local temporary workflow
