#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LLama.Abstractions;
using NUnit.Framework;
using UnityEngine;

public sealed class SafeTranslateResponseChainLinkEditModeTests
{
    [TearDown]
    public void TearDown()
    {
        LlmServiceLocator.Unregister(LlmServiceLocator.Current);
    }

    [Test]
    public void Execute_EnglishLocaleSkipsLlmCall()
    {
        var service = new FakeLlmService("{\"response\":\"ignored\"}");
        LlmServiceLocator.Register(service);

        var link = new SafeTranslateResponseChainLink();
        var state = new PipelineState();
        state.SetString(PromptPipelineConstants.AnswerKey, "Alien1: Hold position.");
        state.SetString(PromptPipelineConstants.TargetLocaleKey, "en-US");

        RunEnumerator(link.Execute(state, _ => { }));

        Assert.AreEqual(0, service.CallCount);
        Assert.AreEqual("Alien1: Hold position.", state.GetString(PromptPipelineConstants.AnswerKey));
    }

    [Test]
    public void Execute_DisabledTranslationSkipsLlmCall()
    {
        var service = new FakeLlmService("{\"response\":\"ignored\"}");
        LlmServiceLocator.Register(service);

        var link = new SafeTranslateResponseChainLink();
        var state = new PipelineState();
        state.SetString(PromptPipelineConstants.AnswerKey, "Alien1: Hold position.");
        state.SetString(PromptPipelineConstants.TargetLocaleKey, "ko-KR");
        state.SetString(PromptPipelineConstants.LlmTranslationEnabledKey, "false");

        RunEnumerator(link.Execute(state, _ => { }));

        Assert.AreEqual(0, service.CallCount);
        Assert.AreEqual("Alien1: Hold position.", state.GetString(PromptPipelineConstants.AnswerKey));
    }

    [Test]
    public void Execute_NonEnglishLocaleStoresTranslatedJsonResponse()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService("{\"response\":\"Alien1: 위치를 지켜.\"}");
        LlmServiceLocator.Register(service);

        try
        {
            var link = new SafeTranslateResponseChainLink(profile);
            var state = new PipelineState();
            state.SetString(PromptPipelineConstants.AnswerKey, "Alien1: Hold position.");
            state.SetString(PromptPipelineConstants.TargetLocaleKey, "ko-KR");
            state.SetString(PromptPipelineConstants.TargetLanguageKey, "Korean");
            state.SetString(PromptPipelineConstants.TargetLanguageNativeNameKey, "한국어");

            RunEnumerator(link.Execute(state, _ => { }));

            Assert.AreEqual(1, service.CallCount);
            Assert.AreEqual("Alien1: 위치를 지켜.", state.GetString(PromptPipelineConstants.AnswerKey));
            StringAssert.Contains("Do not summarize, expand, soften, intensify, censor, or rewrite", service.LastPrompt);
            StringAssert.DoesNotContain("Keep the tone and brevity suitable for in-game UI", service.LastPrompt);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void Execute_CustomSourceAndOutputKeysStoresLocalizedLabel()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService("{\"response\":\"사과\"}");
        LlmServiceLocator.Register(service);

        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["sourceKey"] = "label",
                ["outputKey"] = "localized_label",
                ["localeKey"] = PromptPipelineConstants.TargetLocaleKey,
                ["languageKey"] = PromptPipelineConstants.TargetLanguageKey,
                ["nativeLanguageKey"] = PromptPipelineConstants.TargetLanguageNativeNameKey,
                ["enabledKey"] = PromptPipelineConstants.LlmTranslationEnabledKey,
                ["promptStyle"] = "label",
            };
            var link = new SafeTranslateResponseChainLink(parameters, profile);
            var state = new PipelineState();
            state.SetString("label", "apple");
            state.SetString(PromptPipelineConstants.TargetLocaleKey, "ko-KR");
            state.SetString(PromptPipelineConstants.TargetLanguageKey, "Korean");
            state.SetString(PromptPipelineConstants.TargetLanguageNativeNameKey, "한국어");

            RunEnumerator(link.Execute(state, _ => { }));

            Assert.AreEqual(1, service.CallCount);
            Assert.AreEqual("apple", state.GetString("label"));
            Assert.AreEqual("사과", state.GetString("localized_label"));
            StringAssert.Contains("short English UI label", service.LastPrompt);
            StringAssert.Contains("apple", service.LastPrompt);
            StringAssert.DoesNotContain("speaker labels", service.LastPrompt);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void Execute_InvalidJsonKeepsSourceText()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService("plain translation without json");
        LlmServiceLocator.Register(service);

        try
        {
            var link = new SafeTranslateResponseChainLink(profile);
            var state = new PipelineState();
            state.SetString(PromptPipelineConstants.AnswerKey, "Alien1: Hold position.");
            state.SetString(PromptPipelineConstants.TargetLocaleKey, "ko-KR");
            state.SetString(PromptPipelineConstants.TargetLanguageKey, "Korean");

            RunEnumerator(link.Execute(state, _ => { }));

            Assert.AreEqual(1, service.CallCount);
            Assert.AreEqual("Alien1: Hold position.", state.GetString(PromptPipelineConstants.AnswerKey));
            Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void RunEnumerator(System.Collections.IEnumerator enumerator)
    {
        while (enumerator != null && enumerator.MoveNext())
        {
            if (enumerator.Current is System.Collections.IEnumerator nested)
            {
                RunEnumerator(nested);
            }
        }
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly string _response;

        public FakeLlmService(string response)
        {
            _response = response;
        }

        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; }

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
            LastPrompt = userPrompt;
            onResponse?.Invoke(_response);
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
