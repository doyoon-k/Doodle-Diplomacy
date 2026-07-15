#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Gameplay.FirstContact;
using LLama.Abstractions;
using NUnit.Framework;
using UnityEngine;

public sealed class FirstContactLabelAnalysisChainLinkEditModeTests
{
    [Test]
    public void AcceptUsesOriginalUnicodeLabelInsteadOfModelEcho()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService(
            "{\"label_decision\":\"accept\",\"classification_claim_text\":\"\",\"neutral_subject_label\":\"개\"}");
        try
        {
            var link = new FirstContactLabelAnalysisChainLink(
                new Dictionary<string, string> { ["maxSemanticAttempts"] = "2" },
                profile,
                service);
            PipelineState state = CreateState("사과", "사과");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual(1, service.CallCount);
            Assert.AreEqual("accept", state.GetString(FirstContactLabelAnalysisContract.DecisionKey));
            Assert.AreEqual(
                "사과",
                state.GetString(FirstContactLabelAnalysisContract.NeutralSubjectLabelKey));
            Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
            StringAssert.Contains("Original player label JSON string: \"사과\"", service.LastPrompt);
            StringAssert.DoesNotContain("\\uC0AC\\uACFC", service.LastPrompt);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void InvalidSemanticResponseIsCorrectedOnce()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService(
            "{\"label_decision\":\"classification_claim\",\"classification_claim_text\":\"female genitalia\",\"neutral_subject_label\":\"\"}",
            "{\"label_decision\":\"accept\",\"classification_claim_text\":\"\",\"neutral_subject_label\":\"여자 성기\"}");
        try
        {
            var link = new FirstContactLabelAnalysisChainLink(
                new Dictionary<string, string> { ["maxSemanticAttempts"] = "2" },
                profile,
                service);
            PipelineState state = CreateState("여자 성기", "female genitalia");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual(2, service.CallCount);
            Assert.AreEqual("accept", state.GetString(FirstContactLabelAnalysisContract.DecisionKey));
            Assert.IsFalse(state.ContainsString(PromptPipelineConstants.ErrorKey));
            StringAssert.Contains("Contract violation:", service.LastPrompt);
            StringAssert.DoesNotContain("helper", service.LastPrompt.ToLowerInvariant());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void RepeatedSemanticContradictionReturnsPipelineError()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService(
            "{\"label_decision\":\"classification_claim\",\"classification_claim_text\":\"female genitalia\",\"neutral_subject_label\":\"\"}");
        try
        {
            var link = new FirstContactLabelAnalysisChainLink(
                new Dictionary<string, string> { ["maxSemanticAttempts"] = "2" },
                profile,
                service);
            PipelineState state = CreateState("여자 성기", "female genitalia");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual(2, service.CallCount);
            Assert.IsTrue(state.ContainsString(PromptPipelineConstants.ErrorKey));
            StringAssert.Contains(
                "Label analysis contract failed",
                state.GetString(PromptPipelineConstants.ErrorKey));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void RepeatedTechnicalFailureReturnsPipelineError()
    {
        var profile = ScriptableObject.CreateInstance<LlmGenerationProfile>();
        var service = new FakeLlmService(string.Empty);
        try
        {
            var link = new FirstContactLabelAnalysisChainLink(
                new Dictionary<string, string> { ["maxSemanticAttempts"] = "2" },
                profile,
                service);
            PipelineState state = CreateState("사과", "apple");

            Run(link.Execute(state, _ => { }));

            Assert.AreEqual(2, service.CallCount);
            Assert.IsTrue(state.ContainsString(PromptPipelineConstants.ErrorKey));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static PipelineState CreateState(string original, string canonical)
    {
        var state = new PipelineState();
        state.SetString("probe_display_label", original);
        state.SetString("canonical_label", canonical);
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
        private readonly string[] _responses;

        public FakeLlmService(params string[] responses)
        {
            _responses = responses == null || responses.Length == 0
                ? new[] { string.Empty }
                : responses;
        }

        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

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
            LastPrompt = userPrompt ?? string.Empty;
            int index = Math.Min(CallCount, _responses.Length - 1);
            CallCount++;
            onResponse?.Invoke(_responses[index]);
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
