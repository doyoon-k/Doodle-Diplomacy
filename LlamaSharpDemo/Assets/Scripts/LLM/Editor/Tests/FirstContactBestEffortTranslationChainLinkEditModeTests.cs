#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Gameplay.FirstContact;
using LLama.Abstractions;
using NUnit.Framework;
using UnityEngine;

public sealed class FirstContactBestEffortTranslationChainLinkEditModeTests
{
    [Test]
    public void ValidEnglishTranslationBecomesOptionalHelperLabel()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService("{\"canonical_label\":\"female genitalia\"}");
        try
        {
            var link = new FirstContactBestEffortTranslationChainLink(
                new Dictionary<string, string>(),
                profile,
                service);
            PipelineState state = CreateState("여자 성기");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual("female genitalia", state.GetString("canonical_label"));
            Assert.AreEqual("true", state.GetString("translation_available"));
            Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void RepeatedNonEnglishOutputFallsBackWithoutRejectingPlayerLabel()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService("{\"canonical_label\":\"개\"}");
        try
        {
            var link = new FirstContactBestEffortTranslationChainLink(
                new Dictionary<string, string> { ["maxTranslationAttempts"] = "2" },
                profile,
                service);
            PipelineState state = CreateState("보지");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual(2, service.CallCount);
            Assert.AreEqual("보지", state.GetString("canonical_label"));
            Assert.AreEqual("false", state.GetString("translation_available"));
            Assert.IsTrue(state.ContainsString("translation_warning"));
            Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void MissingTranslationRuntimeFallsBackWithoutPipelineError()
    {
        var link = new FirstContactBestEffortTranslationChainLink();
        PipelineState state = CreateState("사과");

        Run(link.Execute(state, _ => { }));

        Assert.AreEqual("사과", state.GetString("canonical_label"));
        Assert.AreEqual("false", state.GetString("translation_available"));
        Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
    }

    private static PipelineState CreateState(string original)
    {
        var state = new PipelineState();
        state.SetString("probe_display_label", original);
        state.SetString(PromptPipelineConstants.SourceLocaleKey, "ko-KR");
        return state;
    }

    private static void Run(IEnumerator enumerator)
    {
        while (enumerator != null && enumerator.MoveNext())
        {
            if (enumerator.Current is IEnumerator nested)
            {
                Run(nested);
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

        public ILLamaExecutor GetExecutor(BaseLlmGenerationProfile settings) => null;

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
            CallCount++;
            onResponse?.Invoke(_response);
            yield break;
        }

        public IEnumerator GenerateCompletionWithImage(
            BaseLlmGenerationProfile settings,
            string userPrompt,
            PipelineState state,
            Texture2D image,
            Action<string> onResponse)
        {
            yield return GenerateCompletionWithState(settings, userPrompt, state, onResponse);
        }

        public IEnumerator ChatCompletion(
            BaseLlmGenerationProfile settings,
            ChatMessage[] messages,
            Action<string> onResponse)
        {
            yield return GenerateCompletionWithState(settings, string.Empty, null, onResponse);
        }
    }
}
#endif
