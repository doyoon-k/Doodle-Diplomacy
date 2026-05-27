using System;
using UnityEngine;

[CreateAssetMenu(fileName = "CloudGenerationProfile", menuName = "LLM/Cloud Generation Profile")]
public class CloudGenerationProfile : BaseLlmGenerationProfile
{
    [Header("Cloud Provider")]
    [Tooltip("Cloud API provider used by this generation profile.")]
    public CloudProvider provider = CloudProvider.OpenAI;

    [Tooltip("Model identifier for the selected provider (for example: gpt-4.1-mini, claude-sonnet-4-20250514, gemini-2.5-flash).")]
    public string modelId = "gpt-4.1-mini";

    [Tooltip("Optional custom base URL override. Leave empty to use provider defaults.")]
    public string baseUrl;

    [Tooltip("Optional environment variable override for API key. Leave empty to use provider defaults.")]
    public string apiKeyEnvironmentVariable;

    [Header("Request Policy")]
    [Min(5f)]
    [Tooltip("Maximum seconds to wait for one cloud request before timing out.")]
    public float requestTimeoutSeconds = 120f;

    [Range(0, 2)]
    [Tooltip("Transient retry count for 429/5xx responses.")]
    public int maxRetries = 2;

    [Min(0.1f)]
    [Tooltip("Base delay (seconds) for exponential retry backoff.")]
    public float retryBackoffSeconds = 1f;

    public string ResolveEffectiveModelId()
    {
        string normalized = modelId?.Trim() ?? string.Empty;
        if (provider == CloudProvider.Gemini &&
            normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("models/".Length);
        }

        return normalized;
    }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.Trim().TrimEnd('/');
        }

        return provider switch
        {
            CloudProvider.OpenAI => "https://api.openai.com/v1",
            CloudProvider.Anthropic => "https://api.anthropic.com",
            CloudProvider.Gemini => "https://generativelanguage.googleapis.com/v1beta",
            CloudProvider.DeepSeek => "https://api.deepseek.com",
            CloudProvider.Kimi => "https://api.moonshot.ai/v1",
            _ => "https://api.openai.com/v1"
        };
    }

    public string ResolveApiKeyEnvironmentVariable()
    {
        if (!string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            return apiKeyEnvironmentVariable.Trim();
        }

        return provider switch
        {
            CloudProvider.OpenAI => "OPENAI_API_KEY",
            CloudProvider.Anthropic => "ANTHROPIC_API_KEY",
            CloudProvider.Gemini => "GEMINI_API_KEY",
            CloudProvider.DeepSeek => "DEEPSEEK_API_KEY",
            CloudProvider.Kimi => "MOONSHOT_API_KEY",
            _ => "OPENAI_API_KEY"
        };
    }

    public bool TryValidate(out string error)
    {
        string normalizedModelId = ResolveEffectiveModelId();
        if (string.IsNullOrWhiteSpace(normalizedModelId))
        {
            error = "Model ID is required.";
            return false;
        }

        if (normalizedModelId.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
        {
            error = "Model ID contains invalid whitespace characters.";
            return false;
        }

        if (!TryValidateBaseUrl(out error))
        {
            return false;
        }

        if (!TryValidateProviderSpecificRules(normalizedModelId, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        requestTimeoutSeconds = Mathf.Max(5f, requestTimeoutSeconds);
        maxRetries = Mathf.Clamp(maxRetries, 0, 2);
        retryBackoffSeconds = Mathf.Max(0.1f, retryBackoffSeconds);
        modelId = ResolveEffectiveModelId();
    }

    private bool TryValidateBaseUrl(out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return true;
        }

        string normalized = baseUrl.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri parsed))
        {
            error = "Base URL must be an absolute URL.";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Base URL must use http or https.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parsed.Query) || !string.IsNullOrWhiteSpace(parsed.Fragment))
        {
            error = "Base URL must not include query string or fragment.";
            return false;
        }

        return true;
    }

    private bool TryValidateProviderSpecificRules(string normalizedModelId, out string error)
    {
        string normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();

        switch (provider)
        {
            case CloudProvider.OpenAI:
            case CloudProvider.DeepSeek:
            case CloudProvider.Kimi:
                if (normalizedBaseUrl.EndsWith("/chat/completions", StringComparison.Ordinal))
                {
                    error = "Base URL should point to API root, not /chat/completions.";
                    return false;
                }

                break;

            case CloudProvider.Anthropic:
                if (normalizedBaseUrl.EndsWith("/v1/messages", StringComparison.Ordinal) ||
                    normalizedBaseUrl.EndsWith("/messages", StringComparison.Ordinal))
                {
                    error = "Base URL should point to API root, not /v1/messages.";
                    return false;
                }

                break;

            case CloudProvider.Gemini:
                if (normalizedBaseUrl.IndexOf(":generatecontent", StringComparison.Ordinal) >= 0 ||
                    normalizedBaseUrl.EndsWith("/models", StringComparison.Ordinal))
                {
                    error = "Base URL should point to Gemini API version root, not model endpoint path.";
                    return false;
                }

                break;
        }

        if (normalizedModelId.IndexOf('/') >= 0 &&
            provider != CloudProvider.Gemini)
        {
            error = "Model ID should not be a path for this provider.";
            return false;
        }

        error = null;
        return true;
    }
}

public enum CloudProvider
{
    OpenAI = 0,
    Anthropic = 1,
    Gemini = 2,
    DeepSeek = 3,
    Kimi = 4
}
