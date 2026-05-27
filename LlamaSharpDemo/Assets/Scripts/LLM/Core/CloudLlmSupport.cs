using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LLama.Abstractions;
using UnityEngine;

/// <summary>
/// Editor-local API key override asset. Keep this asset under an Editor folder and out of source control.
/// </summary>
[CreateAssetMenu(fileName = "CloudCredentialOverrides", menuName = "LLM/Cloud Credential Overrides (Editor Local)")]
public sealed class CloudCredentialOverridesAsset : ScriptableObject
{
    [Tooltip("Editor-local API key overrides by cloud provider. Keep this asset out of source control.")]
    [SerializeField] private List<CloudCredentialOverrideEntry> entries = new();

    public bool TryGetApiKey(CloudProvider provider, out string apiKey)
    {
        apiKey = null;
        if (entries == null)
        {
            return false;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CloudCredentialOverrideEntry entry = entries[i];
            if (entry == null || entry.provider != provider || string.IsNullOrWhiteSpace(entry.apiKey))
            {
                continue;
            }

            apiKey = entry.apiKey.Trim();
            return true;
        }

        return false;
    }
}

[Serializable]
public sealed class CloudCredentialOverrideEntry
{
    [Tooltip("Cloud provider this editor-local API key belongs to.")]
    public CloudProvider provider;
    [Tooltip("API key used in the Unity editor for this provider. Keep this out of source control.")]
    [TextArea(1, 3)] public string apiKey;
}

public static class CloudApiKeyResolver
{
#if UNITY_EDITOR
    internal static Func<CloudProvider, string> EditorOverrideResolverForTests;
#endif

    public static bool TryResolve(CloudGenerationProfile profile, out string apiKey, out string sourceLabel)
    {
        apiKey = null;
        sourceLabel = null;
        if (profile == null)
        {
            return false;
        }

        string envName = profile.ResolveApiKeyEnvironmentVariable();
        if (!string.IsNullOrWhiteSpace(envName))
        {
            string envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                apiKey = envValue.Trim();
                sourceLabel = $"env:{envName}";
                return true;
            }
        }

#if UNITY_EDITOR
        if (EditorOverrideResolverForTests != null)
        {
            string testOverride = EditorOverrideResolverForTests(profile.provider);
            if (!string.IsNullOrWhiteSpace(testOverride))
            {
                apiKey = testOverride.Trim();
                sourceLabel = "editor-local-override-test-hook";
                return true;
            }
        }

        if (TryResolveEditorLocalOverride(profile.provider, out string editorOverride))
        {
            apiKey = editorOverride;
            sourceLabel = "editor-local-override-asset";
            return true;
        }
#endif

        return false;
    }

#if UNITY_EDITOR
    private static bool TryResolveEditorLocalOverride(CloudProvider provider, out string apiKey)
    {
        apiKey = null;
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CloudCredentialOverridesAsset");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path) ||
                path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            CloudCredentialOverridesAsset asset =
                UnityEditor.AssetDatabase.LoadAssetAtPath<CloudCredentialOverridesAsset>(path);
            if (asset == null)
            {
                continue;
            }

            if (asset.TryGetApiKey(provider, out apiKey))
            {
                return !string.IsNullOrWhiteSpace(apiKey);
            }
        }

        return false;
    }
#endif
}

public static class CloudSecretMasking
{
    public static string Mask(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        string value = secret.Trim();
        if (value.Length <= 4)
        {
            return new string('*', value.Length);
        }

        int head = Math.Min(4, value.Length - 2);
        int tail = Math.Min(2, value.Length - head);
        int stars = Math.Max(0, value.Length - head - tail);

        return value.Substring(0, head) +
               new string('*', stars) +
               value.Substring(value.Length - tail);
    }

    public static string MaskAuthorizationHeader(string scheme, string parameter)
    {
        string normalizedScheme = string.IsNullOrWhiteSpace(scheme) ? "Bearer" : scheme.Trim();
        return $"{normalizedScheme} {Mask(parameter)}";
    }
}

internal interface ICloudProviderAdapter
{
    CloudProvider Provider { get; }
    HttpRequestMessage CreateRequest(
        CloudGenerationProfile profile,
        string apiKey,
        string systemPrompt,
        string userPrompt);
    bool TryExtractText(string rawJson, out string text, out string error);
}

internal static class CloudProviderAdapterRegistry
{
    private static readonly Dictionary<CloudProvider, ICloudProviderAdapter> Adapters =
        new Dictionary<CloudProvider, ICloudProviderAdapter>
        {
            { CloudProvider.OpenAI, new OpenAiCompatibleAdapter(CloudProvider.OpenAI) },
            { CloudProvider.DeepSeek, new OpenAiCompatibleAdapter(CloudProvider.DeepSeek) },
            { CloudProvider.Kimi, new OpenAiCompatibleAdapter(CloudProvider.Kimi) },
            { CloudProvider.Anthropic, new AnthropicAdapter() },
            { CloudProvider.Gemini, new GeminiAdapter() }
        };

    public static bool TryGet(CloudProvider provider, out ICloudProviderAdapter adapter)
    {
        return Adapters.TryGetValue(provider, out adapter);
    }
}

internal sealed class OpenAiCompatibleAdapter : ICloudProviderAdapter
{
    public CloudProvider Provider { get; }

    public OpenAiCompatibleAdapter(CloudProvider provider)
    {
        Provider = provider;
    }

    public HttpRequestMessage CreateRequest(
        CloudGenerationProfile profile,
        string apiKey,
        string systemPrompt,
        string userPrompt)
    {
        string endpoint = JoinUrl(profile.ResolveBaseUrl(), "chat/completions");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var messages = new List<Dictionary<string, object>>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            });
        }

        messages.Add(new Dictionary<string, object>
        {
            ["role"] = "user",
            ["content"] = userPrompt ?? string.Empty
        });

        var body = new Dictionary<string, object>
        {
            ["model"] = profile.ResolveEffectiveModelId(),
            ["messages"] = messages,
            ["stream"] = false,
            ["temperature"] = Mathf.Max(0f, profile.modelParams.temperature),
            ["top_p"] = Mathf.Clamp01(profile.modelParams.top_p),
            ["max_tokens"] = profile.modelParams.num_predict > 0 ? profile.modelParams.num_predict : 1024
        };

        string json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public bool TryExtractText(string rawJson, out string text, out string error)
    {
        text = null;
        error = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = "Empty response body.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("choices", out JsonElement choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                JsonElement firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content))
                {
                    if (content.ValueKind == JsonValueKind.String)
                    {
                        text = content.GetString();
                        return true;
                    }

                    if (content.ValueKind == JsonValueKind.Array)
                    {
                        var builder = new StringBuilder();
                        foreach (JsonElement part in content.EnumerateArray())
                        {
                            if (part.ValueKind != JsonValueKind.Object ||
                                !part.TryGetProperty("text", out JsonElement textPart) ||
                                textPart.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            builder.Append(textPart.GetString());
                        }

                        text = builder.ToString();
                        return true;
                    }
                }
            }

            error = "No choices[0].message.content in response.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string JoinUrl(string baseUrl, string relativePath)
    {
        string left = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        string right = (relativePath ?? string.Empty).Trim().TrimStart('/');
        return $"{left}/{right}";
    }
}

internal sealed class AnthropicAdapter : ICloudProviderAdapter
{
    public CloudProvider Provider => CloudProvider.Anthropic;

    public HttpRequestMessage CreateRequest(
        CloudGenerationProfile profile,
        string apiKey,
        string systemPrompt,
        string userPrompt)
    {
        string endpoint = JoinUrl(profile.ResolveBaseUrl(), "v1/messages");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new Dictionary<string, object>
        {
            ["model"] = profile.ResolveEffectiveModelId(),
            ["max_tokens"] = profile.modelParams.num_predict > 0 ? profile.modelParams.num_predict : 1024,
            ["temperature"] = Mathf.Clamp01(profile.modelParams.temperature),
            ["top_p"] = Mathf.Clamp01(profile.modelParams.top_p),
            ["messages"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = userPrompt ?? string.Empty
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            body["system"] = systemPrompt;
        }

        string json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public bool TryExtractText(string rawJson, out string text, out string error)
    {
        text = null;
        error = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = "Empty response body.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("content", out JsonElement content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                error = "No content array in response.";
                return false;
            }

            var builder = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!block.TryGetProperty("type", out JsonElement typeToken) ||
                    typeToken.ValueKind != JsonValueKind.String ||
                    !string.Equals(typeToken.GetString(), "text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!block.TryGetProperty("text", out JsonElement textToken) ||
                    textToken.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                builder.Append(textToken.GetString());
            }

            text = builder.ToString();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string JoinUrl(string baseUrl, string relativePath)
    {
        string left = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        string right = (relativePath ?? string.Empty).Trim().TrimStart('/');
        return $"{left}/{right}";
    }
}

internal sealed class GeminiAdapter : ICloudProviderAdapter
{
    public CloudProvider Provider => CloudProvider.Gemini;

    public HttpRequestMessage CreateRequest(
        CloudGenerationProfile profile,
        string apiKey,
        string systemPrompt,
        string userPrompt)
    {
        string endpoint = JoinUrl(
            profile.ResolveBaseUrl(),
            $"models/{Uri.EscapeDataString(profile.ResolveEffectiveModelId())}:generateContent");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new Dictionary<string, object>
        {
            ["contents"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["parts"] = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object>
                        {
                            ["text"] = userPrompt ?? string.Empty
                        }
                    }
                }
            },
            ["generationConfig"] = new Dictionary<string, object>
            {
                ["temperature"] = Mathf.Max(0f, profile.modelParams.temperature),
                ["topP"] = Mathf.Clamp01(profile.modelParams.top_p),
                ["topK"] = Mathf.Max(1, Mathf.RoundToInt(profile.modelParams.top_k)),
                ["maxOutputTokens"] = profile.modelParams.num_predict > 0 ? profile.modelParams.num_predict : 1024
            }
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            body["system_instruction"] = new Dictionary<string, object>
            {
                ["parts"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["text"] = systemPrompt }
                }
            };
        }

        string json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public bool TryExtractText(string rawJson, out string text, out string error)
    {
        text = null;
        error = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = "Empty response body.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("candidates", out JsonElement candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                error = "No candidates array in response.";
                return false;
            }

            JsonElement first = candidates[0];
            if (!first.TryGetProperty("content", out JsonElement content) ||
                !content.TryGetProperty("parts", out JsonElement parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                error = "No candidates[0].content.parts in response.";
                return false;
            }

            var builder = new StringBuilder();
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object ||
                    !part.TryGetProperty("text", out JsonElement textToken) ||
                    textToken.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                builder.Append(textToken.GetString());
            }

            text = builder.ToString();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string JoinUrl(string baseUrl, string relativePath)
    {
        string left = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        string right = (relativePath ?? string.Empty).Trim().TrimStart('/');
        return $"{left}/{right}";
    }
}

/// <summary>
/// Direct-to-provider cloud service adapter. Supports text/json only in v1.
/// </summary>
public sealed class CloudDirectLlmService : ILlmService, IDisposable
{
    private readonly bool _logTraffic;
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly object _operationLock = new object();
    private CancellationTokenSource _activeRequestCts;
    private Task<string> _activeRequestTask;
    private bool _disposed;

    public CloudDirectLlmService(bool logTraffic = false)
    {
        _logTraffic = logTraffic;
    }

    public ILLamaExecutor GetExecutor(BaseLlmGenerationProfile settings)
    {
        return null;
    }

    public IEnumerator GenerateCompletion(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        Action<string> onResponse)
    {
        yield return GenerateCompletionWithState(settings, userPrompt, null, onResponse);
    }

    public IEnumerator GenerateCompletionWithState(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Action<string> onResponse)
    {
        if (_disposed)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (settings is not CloudGenerationProfile cloudProfile)
        {
            Debug.LogError("[CloudDirectLlmService] CloudGenerationProfile is required.");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (!CloudProviderAdapterRegistry.TryGet(cloudProfile.provider, out ICloudProviderAdapter adapter))
        {
            Debug.LogError($"[CloudDirectLlmService] Unsupported cloud provider: {cloudProfile.provider}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (!cloudProfile.TryValidate(out string validationError))
        {
            Debug.LogError($"[CloudDirectLlmService] Invalid cloud profile '{cloudProfile.name}': {validationError}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (!CloudApiKeyResolver.TryResolve(cloudProfile, out string apiKey, out string keySource))
        {
            Debug.LogError(
                $"[CloudDirectLlmService] API key missing for provider '{cloudProfile.provider}'. Expected env var '{cloudProfile.ResolveApiKeyEnvironmentVariable()}' or editor-local override asset.");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        string systemPrompt = LlamaSharpInterop.RenderSystemPrompt(cloudProfile, state);
        string userContent = LlamaSharpInterop.BuildUserPromptContent(
            cloudProfile,
            userPrompt,
            requiresJson: !string.IsNullOrWhiteSpace(cloudProfile.format));

        if (_logTraffic)
        {
            string apiKeyVar = cloudProfile.ResolveApiKeyEnvironmentVariable();
            string maskedHeader = CloudSecretMasking.MaskAuthorizationHeader("Bearer", apiKey);
            Debug.Log(
                $"[CloudDirectLlmService] Provider={cloudProfile.provider}, Model={cloudProfile.ResolveEffectiveModelId()}, KeySource={keySource}, ApiKeyEnv={apiKeyVar}, Authorization={maskedHeader}");
            Debug.Log($"[CloudDirectLlmService] System Prompt:\n{systemPrompt}\nUser Prompt:\n{userContent}");
        }

        CancellationTokenSource requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(cloudProfile.requestTimeoutSeconds));
        Task<string> requestTask = Task.Run(() =>
            RequestWithRetryAsync(cloudProfile, adapter, apiKey, systemPrompt, userContent, requestCts.Token),
            requestCts.Token);
        RegisterActiveRequest(requestTask, requestCts);

        while (!requestTask.IsCompleted)
        {
            yield return null;
        }

        Exception failure = GetTaskException(requestTask);
        if (failure != null)
        {
            Debug.LogError($"[CloudDirectLlmService] Request failed: {failure.Message}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        onResponse?.Invoke(LlamaSharpInterop.SanitizeCompletion(requestTask.Result ?? string.Empty, cloudProfile));
    }

    public IEnumerator GenerateCompletionWithImage(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Texture2D image,
        Action<string> onResponse)
    {
        Debug.LogError("[CloudDirectLlmService] Vision requests are not supported in cloud v1.");
        onResponse?.Invoke(string.Empty);
        yield break;
    }

    public IEnumerator ChatCompletion(
        BaseLlmGenerationProfile settings,
        ChatMessage[] messages,
        Action<string> onResponse)
    {
        string prompt = FlattenMessages(messages);
        yield return GenerateCompletion(settings, prompt, onResponse);
    }

    public IEnumerator Embed(
        BaseLlmGenerationProfile settings,
        string[] inputs,
        Action<float[][]> onEmbeddings)
    {
        Debug.LogError("[CloudDirectLlmService] Embeddings are not supported in cloud v1.");
        onEmbeddings?.Invoke(Array.Empty<float[]>());
        yield break;
    }

    public void CancelActiveOperations()
    {
        CancellationTokenSource cts = null;
        lock (_operationLock)
        {
            cts = _activeRequestCts;
        }

        TryCancel(cts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelActiveOperations();
        _httpClient.Dispose();
    }

    private async Task<string> RequestWithRetryAsync(
        CloudGenerationProfile profile,
        ICloudProviderAdapter adapter,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        int maxAttempts = Mathf.Max(1, profile.maxRetries + 1);
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using HttpRequestMessage request = adapter.CreateRequest(profile, apiKey, systemPrompt, userPrompt);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            bool shouldRetry = ShouldRetry(response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                string errorMessage = TryExtractHttpErrorMessage(responseBody);
                if (shouldRetry && attempt < maxAttempts)
                {
                    await DelayForRetryAsync(profile, attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Cloud API call failed ({(int)response.StatusCode} {response.StatusCode}). {errorMessage}");
            }

            if (!adapter.TryExtractText(responseBody, out string text, out string parseError))
            {
                throw new InvalidOperationException($"Cloud API response parse failed: {parseError}");
            }

            return text ?? string.Empty;
        }

        throw new InvalidOperationException("Cloud API call failed after retries.");
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return code == 429 || (code >= 500 && code <= 599);
    }

    private static async Task DelayForRetryAsync(
        CloudGenerationProfile profile,
        int attempt,
        CancellationToken cancellationToken)
    {
        float baseDelay = Mathf.Max(0.1f, profile.retryBackoffSeconds);
        double multiplier = Math.Pow(2d, Math.Max(0, attempt - 1));
        int delayMs = (int)(baseDelay * multiplier * 1000d);
        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
    }

    private static string TryExtractHttpErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "Empty error body.";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    return errorElement.GetString();
                }

                if (errorElement.ValueKind == JsonValueKind.Object &&
                    errorElement.TryGetProperty("message", out JsonElement messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                {
                    return messageElement.GetString();
                }
            }
        }
        catch
        {
            // ignore parse failure and fall back to raw snippet.
        }

        const int MaxLength = 300;
        string compact = responseBody.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (compact.Length <= MaxLength)
        {
            return compact;
        }

        return compact.Substring(0, MaxLength) + "...";
    }

    private void RegisterActiveRequest(Task<string> task, CancellationTokenSource cancellationSource)
    {
        lock (_operationLock)
        {
            _activeRequestTask = task;
            _activeRequestCts = cancellationSource;
        }

        task.ContinueWith(
            _ => ReleaseActiveRequest(task, cancellationSource),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void ReleaseActiveRequest(Task<string> task, CancellationTokenSource cancellationSource)
    {
        bool dispose = false;
        lock (_operationLock)
        {
            if (ReferenceEquals(_activeRequestTask, task))
            {
                _activeRequestTask = null;
            }

            if (ReferenceEquals(_activeRequestCts, cancellationSource))
            {
                _activeRequestCts = null;
                dispose = true;
            }
        }

        if (dispose)
        {
            cancellationSource.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource cancellationSource)
    {
        try
        {
            if (cancellationSource != null && !cancellationSource.IsCancellationRequested)
            {
                cancellationSource.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static Exception GetTaskException(Task task)
    {
        if (task == null || !task.IsFaulted)
        {
            return null;
        }

        return task.Exception?.GetBaseException() ?? task.Exception;
    }

    private static string FlattenMessages(ChatMessage[] messages)
    {
        if (messages == null || messages.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (ChatMessage message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.content))
            {
                continue;
            }

            string role = string.IsNullOrWhiteSpace(message.role) ? "user" : message.role;
            builder.Append(role);
            builder.Append(": ");
            builder.AppendLine(message.content);
        }

        return builder.ToString();
    }
}
