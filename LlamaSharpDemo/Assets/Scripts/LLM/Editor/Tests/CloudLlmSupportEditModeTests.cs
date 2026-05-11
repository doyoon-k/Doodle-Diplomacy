#if UNITY_EDITOR
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using LLama.Abstractions;
using NUnit.Framework;
using UnityEngine;

public sealed class CloudLlmSupportEditModeTests
{
    private const string OpenAiEnvVar = "OPENAI_API_KEY";
    private string _originalOpenAiApiKey;

    [SetUp]
    public void SetUp()
    {
        _originalOpenAiApiKey = Environment.GetEnvironmentVariable(OpenAiEnvVar);
        Environment.SetEnvironmentVariable(OpenAiEnvVar, null);
        SetEditorOverrideResolver(null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(OpenAiEnvVar, _originalOpenAiApiKey);
        SetEditorOverrideResolver(null);
    }

    [Test]
    public void CloudProfileValidation_RequiresModelId()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "   ";

        bool valid = profile.TryValidate(out string error);

        Assert.IsFalse(valid);
        StringAssert.Contains("Model ID", error);
    }

    [Test]
    public void CloudProfileValidation_RejectsEndpointBaseUrl()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "gpt-4.1-mini";
        profile.baseUrl = "https://api.openai.com/v1/chat/completions";

        bool valid = profile.TryValidate(out string error);

        Assert.IsFalse(valid);
        StringAssert.Contains("API root", error);
    }

    [Test]
    public void CloudProfileValidation_NormalizesGeminiModelPrefix()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.Gemini;
        profile.modelId = "models/gemini-2.5-flash";

        string effective = profile.ResolveEffectiveModelId();
        bool valid = profile.TryValidate(out string error);

        Assert.AreEqual("gemini-2.5-flash", effective);
        Assert.IsTrue(valid, error);
    }

    [Test]
    public void OpenAiAdapter_MapsHeadersAndBody()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "gpt-4.1-mini";

        object adapter = CreateAdapter("OpenAiCompatibleAdapter", CloudProvider.OpenAI);
        using var request = (HttpRequestMessage)Invoke(adapter, "CreateRequest", profile, "sk-test-123456", "sys", "user");
        string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.AreEqual("Bearer", request.Headers.Authorization?.Scheme);
        Assert.AreEqual("sk-test-123456", request.Headers.Authorization?.Parameter);
        StringAssert.Contains("/chat/completions", request.RequestUri?.ToString());
        StringAssert.Contains("\"model\":\"gpt-4.1-mini\"", body);
        StringAssert.Contains("\"messages\"", body);
    }

    [Test]
    public void AnthropicAdapter_MapsHeadersAndBody()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.Anthropic;
        profile.modelId = "claude-sonnet-4-20250514";

        object adapter = CreateAdapter("AnthropicAdapter");
        using var request = (HttpRequestMessage)Invoke(adapter, "CreateRequest", profile, "ant-key-xyz", "sys", "user");
        string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.IsTrue(request.Headers.Contains("x-api-key"));
        Assert.IsTrue(request.Headers.Contains("anthropic-version"));
        StringAssert.Contains("\"model\":\"claude-sonnet-4-20250514\"", body);
        StringAssert.Contains("\"system\":\"sys\"", body);
    }

    [Test]
    public void GeminiAdapter_MapsHeadersAndBody()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.Gemini;
        profile.modelId = "models/gemini-2.5-flash";

        object adapter = CreateAdapter("GeminiAdapter");
        using var request = (HttpRequestMessage)Invoke(adapter, "CreateRequest", profile, "gem-key-xyz", "sys", "user");
        string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.IsTrue(request.Headers.Contains("x-goog-api-key"));
        StringAssert.Contains("/models/gemini-2.5-flash:generateContent", request.RequestUri?.ToString());
        StringAssert.Contains("\"system_instruction\"", body);
        StringAssert.Contains("\"maxOutputTokens\"", body);
    }

    [Test]
    public void ApiKeyResolver_PrioritizesEnvironmentVariableOverEditorOverride()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "gpt-4.1-mini";

        Environment.SetEnvironmentVariable(OpenAiEnvVar, "env-key-123456");
        SetEditorOverrideResolver(_ => "override-key-abcdef");

        bool resolved = CloudApiKeyResolver.TryResolve(profile, out string key, out string source);

        Assert.IsTrue(resolved);
        Assert.AreEqual("env-key-123456", key);
        Assert.AreEqual($"env:{OpenAiEnvVar}", source);
    }

    [Test]
    public void ApiKeyResolver_UsesEditorOverrideWhenEnvIsMissing()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "gpt-4.1-mini";

        Environment.SetEnvironmentVariable(OpenAiEnvVar, null);
        SetEditorOverrideResolver(_ => "override-key-abcdef");

        bool resolved = CloudApiKeyResolver.TryResolve(profile, out string key, out string source);

        Assert.IsTrue(resolved);
        Assert.AreEqual("override-key-abcdef", key);
        Assert.AreEqual("editor-local-override-test-hook", source);
    }

    [Test]
    public void SecretMasking_DoesNotExposeRawValue()
    {
        const string apiKey = "sk-secret-1234567890";
        string masked = CloudSecretMasking.Mask(apiKey);
        string maskedHeader = CloudSecretMasking.MaskAuthorizationHeader("Bearer", apiKey);

        Assert.AreNotEqual(apiKey, masked);
        StringAssert.StartsWith("sk-s", masked);
        StringAssert.EndsWith("90", masked);
        StringAssert.Contains("*", masked);
        Assert.Less(maskedHeader.IndexOf(apiKey, StringComparison.Ordinal), 0);
    }

    [Test]
    public void RetryPolicy_RetriesOnlyOn429And5xx()
    {
        MethodInfo method = typeof(CloudDirectLlmService).GetMethod(
            "ShouldRetry",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        bool retry429 = (bool)method.Invoke(null, new object[] { HttpStatusCode.TooManyRequests });
        bool retry503 = (bool)method.Invoke(null, new object[] { HttpStatusCode.ServiceUnavailable });
        bool retry400 = (bool)method.Invoke(null, new object[] { HttpStatusCode.BadRequest });
        bool retry401 = (bool)method.Invoke(null, new object[] { HttpStatusCode.Unauthorized });

        Assert.IsTrue(retry429);
        Assert.IsTrue(retry503);
        Assert.IsFalse(retry400);
        Assert.IsFalse(retry401);
    }

    [Test]
    public void RoutingService_LocalPreloadRequirement_FollowsPipelineComposition()
    {
        var host = new GameObject("RoutingLlmServiceTestHost");
        var routing = host.AddComponent<RoutingLlmService>();

        var localProfile = CreateLocalProfile();
        var cloudProfile = CreateCloudProfile();
        var localAsset = CreatePipelineAssetWithProfile(localProfile);
        var cloudAsset = CreatePipelineAssetWithProfile(cloudProfile);
        var emptyAsset = ScriptableObject.CreateInstance<PromptPipelineAsset>();

        bool localRequired = routing.RequiresLocalPreload(localAsset);
        bool cloudRequired = routing.RequiresLocalPreload(cloudAsset);
        bool emptyRequired = routing.RequiresLocalPreload(emptyAsset);

        Assert.IsTrue(localRequired);
        Assert.IsFalse(cloudRequired);
        Assert.IsTrue(emptyRequired);

        UnityEngine.Object.DestroyImmediate(localAsset);
        UnityEngine.Object.DestroyImmediate(cloudAsset);
        UnityEngine.Object.DestroyImmediate(emptyAsset);
        UnityEngine.Object.DestroyImmediate(localProfile);
        UnityEngine.Object.DestroyImmediate(cloudProfile);
        UnityEngine.Object.DestroyImmediate(host);
    }

    [Test]
    public void JsonChainLink_RetriesUntilJsonParseSucceeds()
    {
        var profile = CreateCloudProfile();
        profile.provider = CloudProvider.OpenAI;
        profile.modelId = "gpt-4.1-mini";

        var fakeService = new FakeLlmService("not-json", "{\"answer\":\"ok\"}");
        var link = new JSONLLMStateChainLink(
            fakeService,
            profile,
            "Return JSON",
            maxRetries: 2,
            delayBetweenRetries: 0f);

        var state = new PipelineState();
        PipelineState finalState = null;
        RunEnumerator(link.Execute(state, s => finalState = s));

        Assert.AreEqual(2, fakeService.CallCount);
        Assert.IsNotNull(finalState);
        Assert.IsTrue(finalState.TryGetString("answer", out string answer));
        Assert.AreEqual("ok", answer);

        UnityEngine.Object.DestroyImmediate(profile);
    }

    [Test]
    public void PipelineExecutor_SupportsMixedLocalAndCloudProfiles()
    {
        var local = CreateLocalProfile();
        local.model = "Models/local.gguf";

        var cloud = CreateCloudProfile();
        cloud.provider = CloudProvider.OpenAI;
        cloud.modelId = "gpt-4.1-mini";

        var asset = ScriptableObject.CreateInstance<PromptPipelineAsset>();
        asset.steps = new System.Collections.Generic.List<PromptPipelineStep>
        {
            new PromptPipelineStep
            {
                guid = "step-1",
                nextStepGuid = "step-2",
                stepName = "Local Step",
                stepKind = PromptPipelineStepKind.CompletionLlm,
                llmProfile = local,
                userPromptTemplate = "local prompt"
            },
            new PromptPipelineStep
            {
                guid = "step-2",
                stepName = "Cloud Step",
                stepKind = PromptPipelineStepKind.CompletionLlm,
                llmProfile = cloud,
                userPromptTemplate = "cloud prompt: {{response}}"
            }
        };

        var fakeService = new FakeLlmService("local-result", "cloud-result");
        StateSequentialChainExecutor executor = asset.BuildExecutor(fakeService);
        PipelineState finalState = null;
        RunEnumerator(executor.Execute(new PipelineState(), s => finalState = s));

        Assert.AreEqual(2, fakeService.CallCount);
        Assert.IsNotNull(finalState);
        Assert.IsTrue(finalState.TryGetString(PromptPipelineConstants.AnswerKey, out string response));
        Assert.AreEqual("cloud-result", response);

        UnityEngine.Object.DestroyImmediate(asset);
        UnityEngine.Object.DestroyImmediate(local);
        UnityEngine.Object.DestroyImmediate(cloud);
    }

    private static CloudGenerationProfile CreateCloudProfile()
    {
        return ScriptableObject.CreateInstance<CloudGenerationProfile>();
    }

    private static LlmGenerationProfile CreateLocalProfile()
    {
        return ScriptableObject.CreateInstance<LlmGenerationProfile>();
    }

    private static PromptPipelineAsset CreatePipelineAssetWithProfile(BaseLlmGenerationProfile profile)
    {
        var asset = ScriptableObject.CreateInstance<PromptPipelineAsset>();
        asset.steps = new System.Collections.Generic.List<PromptPipelineStep>
        {
            new PromptPipelineStep
            {
                stepName = "Step",
                stepKind = PromptPipelineStepKind.CompletionLlm,
                llmProfile = profile
            }
        };
        return asset;
    }

    private static object CreateAdapter(string typeName, params object[] args)
    {
        Type adapterType = typeof(CloudGenerationProfile).Assembly.GetType(typeName, throwOnError: true);
        return Activator.CreateInstance(
            adapterType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: args,
            culture: null);
    }

    private static object Invoke(object instance, string methodName, params object[] args)
    {
        MethodInfo method = instance.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on '{instance.GetType().FullName}'.");
        return method.Invoke(instance, args);
    }

    private static void SetEditorOverrideResolver(Func<CloudProvider, string> resolver)
    {
        FieldInfo field = typeof(CloudApiKeyResolver).GetField(
            "EditorOverrideResolverForTests",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "CloudApiKeyResolver.EditorOverrideResolverForTests field was not found.");
        field.SetValue(null, resolver);
    }

    private static void RunEnumerator(System.Collections.IEnumerator enumerator)
    {
        while (enumerator != null && enumerator.MoveNext())
        {
        }
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly System.Collections.Generic.Queue<string> _responses;
        public int CallCount { get; private set; }

        public FakeLlmService(params string[] responses)
        {
            _responses = new System.Collections.Generic.Queue<string>(responses ?? Array.Empty<string>());
        }

        public ILLamaExecutor GetExecutor(BaseLlmGenerationProfile settings)
        {
            return null;
        }

        public System.Collections.IEnumerator GenerateCompletion(
            BaseLlmGenerationProfile settings,
            string userPrompt,
            Action<string> onResponse)
        {
            yield return GenerateCompletionWithState(settings, userPrompt, null, onResponse);
        }

        public System.Collections.IEnumerator GenerateCompletionWithState(
            BaseLlmGenerationProfile settings,
            string userPrompt,
            PipelineState state,
            Action<string> onResponse)
        {
            CallCount++;
            onResponse?.Invoke(_responses.Count > 0 ? _responses.Dequeue() : string.Empty);
            yield break;
        }

        public System.Collections.IEnumerator GenerateCompletionWithImage(
            BaseLlmGenerationProfile settings,
            string userPrompt,
            PipelineState state,
            Texture2D image,
            Action<string> onResponse)
        {
            yield return GenerateCompletionWithState(settings, userPrompt, state, onResponse);
        }

        public System.Collections.IEnumerator ChatCompletion(
            BaseLlmGenerationProfile settings,
            ChatMessage[] messages,
            Action<string> onResponse)
        {
            yield return GenerateCompletionWithState(settings, string.Empty, null, onResponse);
        }

        public System.Collections.IEnumerator Embed(
            BaseLlmGenerationProfile settings,
            string[] inputs,
            Action<float[][]> onEmbeddings)
        {
            onEmbeddings?.Invoke(Array.Empty<float[]>());
            yield break;
        }
    }
}
#endif
