using System.Collections.Generic;
using System.Reflection;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Gameplay.FirstContact;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class FirstContactEditModeTests
    {
        [Test]
        public void BootstrapCategoryConfigUsesOrderedDesignerDefinitionsAndRejectsDuplicateIds()
        {
            var config = ScriptableObject.CreateInstance<FirstContactModeConfig>();
            try
            {
                config.bootstrapCategories = new List<FirstContactBootstrapCategoryDefinition>
                {
                    new()
                    {
                        id = "danger",
                        categoryDisplayName = "DANGER",
                        meaningDisplayName = "[DANGER?]",
                        descriptorText = "visible hazards",
                        clusterLabelKeywords = new List<string> { "blade" },
                        requiredTraceCount = 0
                    },
                    new()
                    {
                        id = "shelter",
                        categoryDisplayName = "SHELTER",
                        meaningDisplayName = "[SHELTER?]",
                        descriptorText = "visible protective shelters",
                        requiredTraceCount = 4
                    }
                };

                Assert.IsTrue(config.TryGetBootstrapCategories(
                    out IReadOnlyList<FirstContactBootstrapCategoryDefinition> categories,
                    out string error));
                Assert.IsEmpty(error);
                Assert.AreEqual("danger", categories[0].Id);
                Assert.AreEqual("shelter", categories[1].Id);
                Assert.AreEqual(3, categories[0].ResolveRequiredTraceCount(3));
                Assert.AreEqual(4, categories[1].ResolveRequiredTraceCount(3));
                Assert.IsTrue(categories[0].MatchesClusterLabel("ceremonial blade"));

                config.bootstrapCategories[1].id = "danger";
                Assert.IsFalse(config.TryGetBootstrapCategories(out _, out error));
                StringAssert.Contains("unique", error);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void BootstrapSessionOwnsCategoryProgressAndStability()
        {
            var definitions = new List<FirstContactBootstrapCategoryDefinition>
            {
                new()
                {
                    id = "danger",
                    categoryDisplayName = "Danger",
                    meaningDisplayName = "Threat",
                    descriptorText = "visible hazards"
                },
                new()
                {
                    id = "food",
                    categoryDisplayName = "Food",
                    meaningDisplayName = "Edible",
                    descriptorText = "edible objects"
                }
            };
            var session = new FirstContactBootstrapSession(definitions, defaultRequiredTraceCount: 2);
            var service = new FirstContactEmbeddingService(null, null);

            Assert.AreEqual("danger", session.ActiveCategory.Id);
            Assert.IsFalse(session.IsComplete);

            session.ActiveCategory.SetDescriptorEmbedding(new[] { 1f, 0f });
            var firstCard = new SemanticCardRecord
            {
                Label = "knife",
                Embedding = new[] { 1f, 0f }
            };
            FirstContactBootstrapProbeFit firstFit =
                session.ActiveCategory.EvaluateCandidate(firstCard, service);

            Assert.IsTrue(session.ActiveCategory.RecordProbe(firstCard, firstFit, categoryAccepted: true));
            Assert.IsFalse(session.ActiveCategory.IsStable);

            var secondCard = new SemanticCardRecord
            {
                Label = "fire",
                Embedding = new[] { 0.9f, 0.1f }
            };
            FirstContactBootstrapProbeFit secondFit =
                session.ActiveCategory.EvaluateCandidate(secondCard, service);

            Assert.IsTrue(session.ActiveCategory.RecordProbe(secondCard, secondFit, categoryAccepted: true));
            Assert.IsTrue(session.ActiveCategory.IsStable);

            session.AdvanceCategory();
            Assert.AreEqual("food", session.ActiveCategory.Id);
            session.AdvanceCategory();
            Assert.IsTrue(session.IsComplete);
        }

        [Test]
        public void BootstrapMapBuilderKeepsOnlyTheActiveCategoryContext()
        {
            var definition = new FirstContactBootstrapCategoryDefinition
            {
                id = "danger",
                categoryDisplayName = "Danger",
                meaningDisplayName = "Threat",
                descriptorText = "visible hazards"
            };
            var category = new FirstContactBootstrapCategoryState(definition, 2);
            var activeCard = new SemanticCardRecord
            {
                Id = "active",
                Label = "knife",
                LocalizedLabel = "knife",
                Embedding = new[] { 1f, 0f },
                BootstrapCategoryId = "danger",
                BootstrapCategoryEvaluated = true,
                BootstrapCategoryAccepted = true
            };
            var unrelatedCard = new SemanticCardRecord
            {
                Id = "other",
                Label = "apple",
                LocalizedLabel = "apple",
                Embedding = new[] { 0f, 1f },
                BootstrapCategoryId = "food",
                BootstrapCategoryEvaluated = true,
                BootstrapCategoryAccepted = true
            };
            var settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                var builder = new FirstContactBootstrapMapBuilder(
                    new FirstContactEmbeddingService(null, settings));
                builder.Reset(1234);

                FirstContactSemanticMapSnapshot snapshot = builder.Build(
                    new List<SemanticCardRecord> { activeCard, unrelatedCard },
                    new List<SemanticClusterRecord>(),
                    activeCard,
                    category,
                    includeActiveCard: true,
                    settings);

                Assert.IsNotNull(snapshot.FindNode("B:danger"));
                Assert.IsNotNull(snapshot.FindNode("C:active"));
                Assert.IsNull(snapshot.FindNode("C:other"));
                Assert.AreEqual(2, snapshot.Nodes.Count);
                Assert.AreEqual(1, snapshot.Links.Count);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SemanticMapDisplayReusesLabelsAcrossSnapshotUpdates()
        {
            var terminalObject = new GameObject("Terminal", typeof(RectTransform), typeof(Canvas));
            try
            {
                var screenObject = new GameObject("Screen", typeof(RectTransform));
                screenObject.transform.SetParent(terminalObject.transform, false);
                ((RectTransform)screenObject.transform).sizeDelta = new Vector2(1024f, 512f);

                TerminalDisplay terminal = terminalObject.AddComponent<TerminalDisplay>();
                SetPrivateField(terminal, "screenPanel", screenObject);
                SetPrivateField(terminal, "enableScroll", false);
                FirstContactSemanticMapDisplay display =
                    terminalObject.AddComponent<FirstContactSemanticMapDisplay>();

                display.ShowFullMap(CreateMapSnapshot("C:first"));
                FirstContactSemanticMapGraphic graphic =
                    terminalObject.GetComponentInChildren<FirstContactSemanticMapGraphic>(true);
                Assert.IsNotNull(graphic);

                TextMeshProUGUI[] firstLabels = graphic.GetComponentsInChildren<TextMeshProUGUI>(true);
                Assert.AreEqual(1, firstLabels.Length);
                TextMeshProUGUI firstLabel = firstLabels[0];

                display.ShowFullMap(CreateMapSnapshot("C:first", "C:second"));
                TextMeshProUGUI[] expandedLabels = graphic.GetComponentsInChildren<TextMeshProUGUI>(true);
                Assert.AreEqual(2, expandedLabels.Length);

                display.ShowFullMap(CreateMapSnapshot("C:first"));
                TextMeshProUGUI[] reusedLabels = graphic.GetComponentsInChildren<TextMeshProUGUI>(true);
                Assert.AreEqual(2, reusedLabels.Length);
                Assert.AreSame(firstLabel, reusedLabels[0]);
                Assert.IsTrue(reusedLabels[0].gameObject.activeSelf);
                Assert.IsFalse(reusedLabels[1].gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void SemanticMapTransitionBuilderDoesNotMutateSourceSnapshots()
        {
            FirstContactSemanticMapSnapshot before = CreateMapSnapshot("B:danger");
            FirstContactSemanticMapSnapshot after = CreateMapSnapshot("B:danger", "C:active");
            FirstContactSemanticMapNode category = after.FindNode("B:danger");
            FirstContactSemanticMapNode activeCard = after.FindNode("C:active");
            category.Position = new Vector2(-0.35f, 0.2f);
            activeCard.Position = new Vector2(0.55f, -0.45f);
            activeCard.Pulse = 0.27f;
            Vector2 sourcePosition = activeCard.Position;
            float sourcePulse = activeCard.Pulse;

            FirstContactSemanticMapSnapshot frame =
                FirstContactSemanticMapTransitionBuilder.BuildBootstrapResultFrame(
                    before,
                    after,
                    activeCard.Id,
                    category.Id,
                    accepted: true,
                    becameStable: false,
                    progress: 0.5f);

            FirstContactSemanticMapNode frameCard = frame.FindNode(activeCard.Id);
            Assert.IsNotNull(frameCard);
            Assert.AreNotSame(activeCard, frameCard);
            Assert.AreEqual(sourcePosition, activeCard.Position);
            Assert.AreEqual(sourcePulse, activeCard.Pulse);
            Assert.AreNotEqual(sourcePosition, frameCard.Position);
        }

        [Test]
        public void SemanticMapDisplayValidationDoesNotCreateLabelHierarchy()
        {
            var terminalObject = new GameObject("Terminal", typeof(RectTransform), typeof(Canvas));
            try
            {
                terminalObject.AddComponent<TerminalDisplay>();
                FirstContactSemanticMapDisplay display =
                    terminalObject.AddComponent<FirstContactSemanticMapDisplay>();

                var mapObject = new GameObject(
                    "SemanticMap",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(FirstContactSemanticMapGraphic));
                mapObject.transform.SetParent(terminalObject.transform, false);
                FirstContactSemanticMapGraphic graphic =
                    mapObject.GetComponent<FirstContactSemanticMapGraphic>();
                SetPrivateField(display, "mapGraphic", graphic);

                InvokePrivateMethod(display, "OnValidate");

                Assert.IsNull(graphic.transform.Find("SemanticMapLabels"));
            }
            finally
            {
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void SemanticMapDisplayAppliesPersistentStyleToRuntimeMap()
        {
            var terminalObject = new GameObject("Terminal", typeof(RectTransform), typeof(Canvas));
            FirstContactSemanticMapStyle style =
                ScriptableObject.CreateInstance<FirstContactSemanticMapStyle>();
            try
            {
                var screenObject = new GameObject("Screen", typeof(RectTransform));
                screenObject.transform.SetParent(terminalObject.transform, false);
                ((RectTransform)screenObject.transform).sizeDelta = new Vector2(1024f, 512f);

                TerminalDisplay terminal = terminalObject.AddComponent<TerminalDisplay>();
                SetPrivateField(terminal, "screenPanel", screenObject);
                SetPrivateField(terminal, "enableScroll", false);
                FirstContactSemanticMapDisplay display =
                    terminalObject.AddComponent<FirstContactSemanticMapDisplay>();
                style.mapHorizontalPaddingRatio = 0.08f;
                style.miniMap.mapHeightRatio = 0.41f;
                style.showMiniMapLabels = false;

                display.SetStyle(style);
                display.ShowMiniMap(CreateMapSnapshot("C:first"));

                FirstContactSemanticMapGraphic graphic =
                    terminalObject.GetComponentInChildren<FirstContactSemanticMapGraphic>(true);
                Assert.IsNotNull(graphic);
                Assert.AreSame(style, GetPrivateField<FirstContactSemanticMapStyle>(graphic, "_style"));
                Assert.AreEqual(style.mapHorizontalPaddingRatio, graphic.rectTransform.anchorMin.x);
                Assert.AreEqual(1f - style.miniMap.mapHeightRatio, graphic.rectTransform.anchorMin.y);
                Assert.AreEqual(0, graphic.GetComponentsInChildren<TextMeshProUGUI>(true).Length);
            }
            finally
            {
                Object.DestroyImmediate(style);
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void SemanticMapDisplayPreservesAuthoredPrefabLayout()
        {
            var terminalObject = new GameObject("Terminal", typeof(RectTransform), typeof(Canvas));
            try
            {
                var screenObject = new GameObject("Screen", typeof(RectTransform));
                screenObject.transform.SetParent(terminalObject.transform, false);

                TerminalDisplay terminal = terminalObject.AddComponent<TerminalDisplay>();
                SetPrivateField(terminal, "screenPanel", screenObject);
                SetPrivateField(terminal, "enableScroll", false);

                var mapObject = new GameObject(
                    "SemanticMap",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(FirstContactSemanticMapGraphic));
                mapObject.transform.SetParent(screenObject.transform, false);
                RectTransform mapRect = mapObject.GetComponent<RectTransform>();
                mapRect.anchorMin = new Vector2(0.17f, 0.29f);
                mapRect.anchorMax = new Vector2(0.83f, 0.71f);
                mapRect.offsetMin = new Vector2(12f, 18f);
                mapRect.offsetMax = new Vector2(-24f, -30f);

                FirstContactSemanticMapDisplay display =
                    terminalObject.AddComponent<FirstContactSemanticMapDisplay>();
                SetPrivateField(display, "mapGraphic", mapObject.GetComponent<FirstContactSemanticMapGraphic>());

                Vector2 anchorMin = mapRect.anchorMin;
                Vector2 anchorMax = mapRect.anchorMax;
                Vector2 offsetMin = mapRect.offsetMin;
                Vector2 offsetMax = mapRect.offsetMax;

                display.ShowFullMap(CreateMapSnapshot("C:first"));

                Assert.AreEqual(anchorMin, mapRect.anchorMin);
                Assert.AreEqual(anchorMax, mapRect.anchorMax);
                Assert.AreEqual(offsetMin, mapRect.offsetMin);
                Assert.AreEqual(offsetMax, mapRect.offsetMax);
            }
            finally
            {
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void BrainwaveDisplayPreservesAuthoredPrefabLayout()
        {
            var terminalObject = new GameObject("Terminal", typeof(RectTransform), typeof(Canvas));
            try
            {
                var screenObject = new GameObject("Screen", typeof(RectTransform));
                screenObject.transform.SetParent(terminalObject.transform, false);

                TerminalDisplay terminal = terminalObject.AddComponent<TerminalDisplay>();
                SetPrivateField(terminal, "screenPanel", screenObject);
                SetPrivateField(terminal, "enableScroll", false);

                var graphObject = new GameObject(
                    "BrainwaveGraph",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(BrainwaveGraphDisplay));
                graphObject.transform.SetParent(screenObject.transform, false);
                RectTransform graphRect = graphObject.GetComponent<RectTransform>();
                graphRect.anchorMin = new Vector2(0.11f, 0.73f);
                graphRect.anchorMax = new Vector2(0.89f, 0.91f);
                graphRect.offsetMin = new Vector2(9f, 15f);
                graphRect.offsetMax = new Vector2(-21f, -27f);

                TerminalBrainwaveDisplay display =
                    terminalObject.AddComponent<TerminalBrainwaveDisplay>();
                SetPrivateField(display, "brainwaveGraph", graphObject.GetComponent<BrainwaveGraphDisplay>());

                Vector2 anchorMin = graphRect.anchorMin;
                Vector2 anchorMax = graphRect.anchorMax;
                Vector2 offsetMin = graphRect.offsetMin;
                Vector2 offsetMax = graphRect.offsetMax;

                display.PlaySearching("layout-test", 0, 1234);

                Assert.AreEqual(anchorMin, graphRect.anchorMin);
                Assert.AreEqual(anchorMax, graphRect.anchorMax);
                Assert.AreEqual(offsetMin, graphRect.offsetMin);
                Assert.AreEqual(offsetMax, graphRect.offsetMax);
            }
            finally
            {
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void ProbePreviewSwitchesAuthoredSlotsWithoutChangingTheirLayouts()
        {
            var host = new GameObject("ProbePreviewDisplay");
            var texture = new Texture2D(4, 2);
            try
            {
                FirstContactProbePreviewDisplay display =
                    host.AddComponent<FirstContactProbePreviewDisplay>();
                RectTransform reviewRoot = CreateProbePreviewSlot(
                    host.transform,
                    "Review",
                    new Vector2(0.12f, 0.5f),
                    new Vector2(0.88f, 0.93f),
                    out RawImage reviewImage,
                    out AspectRatioFitter reviewAspect);
                RectTransform dispatchRoot = CreateProbePreviewSlot(
                    host.transform,
                    "Dispatch",
                    new Vector2(0.54f, 0.36f),
                    new Vector2(0.93f, 0.76f),
                    out RawImage dispatchImage,
                    out AspectRatioFitter dispatchAspect);
                display.ConfigureReview(reviewRoot, reviewImage, reviewAspect, null);
                display.ConfigureDispatch(dispatchRoot, dispatchImage, dispatchAspect, null);

                Vector2 reviewAnchorMin = reviewRoot.anchorMin;
                Vector2 reviewAnchorMax = reviewRoot.anchorMax;
                Vector2 dispatchAnchorMin = dispatchRoot.anchorMin;
                Vector2 dispatchAnchorMax = dispatchRoot.anchorMax;

                Assert.IsTrue(display.Show(texture, useDispatchLayout: false, scanActive: false));
                Assert.IsTrue(reviewRoot.gameObject.activeSelf);
                Assert.IsFalse(dispatchRoot.gameObject.activeSelf);
                Assert.AreSame(texture, reviewImage.texture);
                Assert.AreEqual(2f, reviewAspect.aspectRatio);

                Assert.IsTrue(display.Show(texture, useDispatchLayout: true, scanActive: false));
                Assert.IsFalse(reviewRoot.gameObject.activeSelf);
                Assert.IsTrue(dispatchRoot.gameObject.activeSelf);
                Assert.AreSame(texture, dispatchImage.texture);
                Assert.AreEqual(2f, dispatchAspect.aspectRatio);
                Assert.AreEqual(reviewAnchorMin, reviewRoot.anchorMin);
                Assert.AreEqual(reviewAnchorMax, reviewRoot.anchorMax);
                Assert.AreEqual(dispatchAnchorMin, dispatchRoot.anchorMin);
                Assert.AreEqual(dispatchAnchorMax, dispatchRoot.anchorMax);

                display.Clear();
                Assert.IsFalse(reviewRoot.gameObject.activeSelf);
                Assert.IsFalse(dispatchRoot.gameObject.activeSelf);
                Assert.IsNull(reviewImage.texture);
                Assert.IsNull(dispatchImage.texture);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TerminalScreenPlaneEditorFindsScreenFromEditableLayoutChild()
        {
            var terminalObject = new GameObject("Terminal");
            try
            {
                var canvasObject = new GameObject("TerminalCanvas", typeof(RectTransform));
                canvasObject.transform.SetParent(terminalObject.transform, false);
                var screenObject = new GameObject("ScreenPanel", typeof(RectTransform));
                screenObject.transform.SetParent(canvasObject.transform, false);
                var layoutObject = new GameObject("EditableTerminalLayout", typeof(RectTransform));
                layoutObject.transform.SetParent(screenObject.transform, false);
                var childObject = new GameObject("ProbePreview_Dispatch", typeof(RectTransform));
                childObject.transform.SetParent(layoutObject.transform, false);

                Assert.IsTrue(global::TerminalScreenPlaneLayoutEditor.TryGetScreenRect(
                    childObject.transform,
                    out RectTransform resolvedScreen));
                Assert.AreSame(screenObject.GetComponent<RectTransform>(), resolvedScreen);
            }
            finally
            {
                Object.DestroyImmediate(terminalObject);
            }
        }

        [Test]
        public void ProbeFeedbackMapsDomainIssuesWithoutControllerState()
        {
            FirstContactProbeLabelFeedback feedback =
                FirstContactProbeFeedback.ResolveLabelIssue(
                    FirstContactProbeLabelIssue.ClassificationClaim);

            Assert.AreEqual(
                "first_contact.terminal.status.label_classification_claim",
                feedback.StatusKey);
            Assert.AreEqual(
                "first_contact.officer.probe_label_classification_claim",
                feedback.OfficerLineKey);
            Assert.IsTrue(FirstContactProbeFeedback.IsFatalValidationFailure(
                "GamePipelineRunner is missing."));
            Assert.IsFalse(FirstContactProbeFeedback.IsFatalValidationFailure(
                "Temporary model response failure."));
        }

        [Test]
        public void ProbeWorkingStateAppliesCaptureAndLabelResultsAtomically()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                var state = new FirstContactProbeWorkingState();
                state.Reset(FirstContactCardSource.BootstrapProbe);
                byte[] pngBytes = { 1, 2, 3 };

                Assert.IsTrue(state.TryApplyCapture(
                    FirstContactProbeCaptureResult.Succeeded(texture, pngBytes)));
                Assert.IsTrue(state.TrySetSubmittedLabel("knife", "Knife"));
                Assert.IsTrue(state.TryApplyLabelAnalysis(new FirstContactProbeLabelResult
                {
                    CanonicalLabel = "blade",
                    TranslationAvailable = true
                }));

                FirstContactProbeDraft draft = state.CreateDraft();
                Assert.AreSame(texture, draft.Texture);
                Assert.AreSame(pngBytes, state.PngBytes);
                Assert.AreEqual("blade", draft.CanonicalLabel);
                Assert.AreEqual("Knife", draft.DisplayLabel);
                Assert.IsTrue(draft.TranslationAvailable);
                Assert.AreEqual("Knife", state.PreferredLabel);

                Assert.IsFalse(state.TrySetSubmittedLabel(string.Empty, "Invalid"));
                Assert.AreEqual("blade", state.CanonicalLabel);
                Assert.AreEqual("Knife", state.DisplayLabel);
                Assert.IsFalse(state.TryApplyLabelAnalysis(
                    FirstContactProbeLabelResult.Failed("analysis failed")));
                Assert.AreEqual("blade", state.CanonicalLabel);
                Assert.IsTrue(state.TranslationAvailable);

                Assert.IsFalse(state.TryApplyCapture(
                    FirstContactProbeCaptureResult.Failed("capture failed")));
                Assert.AreSame(texture, state.Texture);
                Assert.AreSame(pngBytes, state.PngBytes);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ProbeWorkingStateResetClearsTheWholePendingProbe()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                var state = new FirstContactProbeWorkingState();
                state.Reset(FirstContactCardSource.BootstrapProbe);
                state.TryApplyCapture(FirstContactProbeCaptureResult.Succeeded(
                    texture,
                    new byte[] { 1 }));
                state.TrySetSubmittedLabel("knife", "Knife");

                state.Reset(FirstContactCardSource.BootstrapProbe);

                Assert.IsFalse(state.HasCapture);
                Assert.IsNull(state.Texture);
                Assert.IsNull(state.PngBytes);
                Assert.IsEmpty(state.CanonicalLabel);
                Assert.IsEmpty(state.DisplayLabel);
                Assert.IsFalse(state.TranslationAvailable);
                Assert.IsEmpty(state.PreferredLabel);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ClusterFormationTrackerDetectsAStableTransition()
        {
            var card = new SemanticCardRecord { Id = "card-1" };
            var cluster = new SemanticClusterRecord
            {
                Id = "cluster-1",
                IsStable = true,
                ProvisionalName = "warning"
            };
            cluster.Members.Add(card);
            var before = new List<FirstContactClusterTransitionSnapshot>
            {
                new("cluster-1", isStable: false, memberCount: 1)
            };

            FirstContactClusterFormationEvent formation =
                FirstContactClusterFormationTracker.BuildFormation(
                    card,
                    cluster,
                    before,
                    formationEdges: null);

            Assert.IsTrue(formation.HasCluster);
            Assert.IsTrue(formation.BecameStable);
            Assert.IsTrue(formation.ShouldAnimate);
            Assert.AreEqual("C:card-1", formation.ActiveCardNodeId);
            Assert.AreEqual("K:cluster-1", formation.ClusterNodeId);
        }

        [Test]
        public void TerminalTextEntrySessionOwnsInputLifetimeWithoutAVisibleTerminal()
        {
            var session = new TerminalTextEntrySession(null);
            session.Begin(
                "initial",
                32,
                onChanged: null,
                onSubmitted: null,
                onCancelled: null);

            Assert.IsTrue(session.IsActive);
            Assert.AreEqual("initial", session.Value);
            Assert.AreEqual("initial", session.RenderedValue);

            session.End();
            Assert.IsFalse(session.IsActive);
        }

        [Test]
        public void EmbeddingWrapperNormalizesLabelsAndBuildsMultilingualSimilarityInput()
        {
            var service = new FirstContactEmbeddingService(null, null);

            Assert.AreEqual("shield wall", FirstContactEmbeddingService.NormalizeText("  Shield   Wall  "));
            Assert.AreEqual(
                "task: sentence similarity | query: 바나나",
                FirstContactEmbeddingService.BuildEmbeddingInput("  바나나  "));
            Assert.IsTrue(
                service.TryBuildCentroid(
                    new List<float[]>
                    {
                        new[] { 1f, 0f },
                        new[] { 1f, 0f }
                    },
                    out float[] centroid));
            Assert.AreEqual(1f, centroid[0], 0.0001f);
            Assert.AreEqual(0f, centroid[1], 0.0001f);
            Assert.AreEqual(1f, service.Similarity(new[] { 1f, 0f }, centroid), 0.0001f);
        }

        [Test]
        public void ProbeLabelResultReadsCanonicalLabelAndSuitability()
        {
            var state = new PipelineState();
            state.SetString("canonical_label", " Knife ");
            state.SetString("is_suitable", "true");
            state.SetString("reason", string.Empty);

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.AreEqual("Knife", result.CanonicalLabel);
            Assert.IsTrue(result.IsSuitable);
            Assert.IsTrue(result.IsSuccess);
        }

        [Test]
        public void ProbeLabelResultRejectsClaimWithoutConcreteSubjectReduction()
        {
            var state = new PipelineState();
            state.SetString("canonical_label", "don't look");
            state.SetString("has_classification_claim", "true");
            state.SetString("classification_claim_text", "don't look");
            state.SetString("neutral_subject_label", string.Empty);
            state.SetString("label_reason", string.Empty);
            state.SetString("is_suitable", "true");
            state.SetString("reason", string.Empty);

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.IsTrue(result.HasClassificationClaim);
            Assert.IsFalse(result.IsSuitable);
        }

        [Test]
        public void UnifiedLabelAnalysisAcceptsExplicitAnatomicalSubject()
        {
            var state = new PipelineState();
            state.SetString("probe_display_label", "여자 성기");
            state.SetString("canonical_label", "여자 성기");
            state.SetString("translation_available", "false");
            state.SetString(FirstContactLabelAnalysisContract.DecisionKey, "accept");
            state.SetString(FirstContactLabelAnalysisContract.ClassificationClaimTextKey, string.Empty);
            state.SetString(FirstContactLabelAnalysisContract.NeutralSubjectLabelKey, "여자 성기");

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.IsSuitable);
            Assert.IsFalse(result.HasClassificationClaim);
            Assert.IsFalse(result.TranslationAvailable);
            Assert.AreEqual("여자 성기", result.CanonicalLabel);
        }

        [Test]
        public void UnifiedLabelAnalysisRejectsWholeLabelAsClassificationClaim()
        {
            var state = new PipelineState();
            state.SetString("probe_display_label", "여자 성기");
            state.SetString("canonical_label", "female genitalia");
            state.SetString(FirstContactLabelAnalysisContract.DecisionKey, "classification_claim");
            state.SetString(FirstContactLabelAnalysisContract.ClassificationClaimTextKey, "female genitalia");
            state.SetString(FirstContactLabelAnalysisContract.NeutralSubjectLabelKey, string.Empty);

            bool valid = FirstContactLabelAnalysisContract.TryValidate(
                state,
                out _,
                out string error);

            Assert.IsFalse(valid);
            StringAssert.Contains("neutral subject", error);
        }

        [Test]
        public void UnifiedLabelAnalysisAcceptsRemovableClassificationClaim()
        {
            var state = new PipelineState();
            state.SetString("probe_display_label", "dangerous triangle");
            state.SetString("canonical_label", "dangerous triangle");
            state.SetString(FirstContactLabelAnalysisContract.DecisionKey, "classification_claim");
            state.SetString(FirstContactLabelAnalysisContract.ClassificationClaimTextKey, "dangerous");
            state.SetString(FirstContactLabelAnalysisContract.NeutralSubjectLabelKey, "triangle");

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.IsTrue(result.HasClassificationClaim);
            Assert.IsFalse(result.IsSuitable);
            Assert.AreEqual("triangle", result.NeutralSubjectLabel);
            Assert.AreEqual(
                FirstContactProbeLabelIssue.ClassificationClaim,
                result.LabelIssue);
        }

        [TestCase("action_or_abstract", FirstContactProbeLabelIssue.ActionOrAbstract)]
        [TestCase("broad_category", FirstContactProbeLabelIssue.BroadCategory)]
        [TestCase("multiple_subjects", FirstContactProbeLabelIssue.MultipleSubjects)]
        public void UnifiedLabelAnalysisPreservesPlayerFacingIssue(
            string decision,
            FirstContactProbeLabelIssue expectedIssue)
        {
            var state = new PipelineState();
            state.SetString("probe_display_label", "test label");
            state.SetString("canonical_label", "test label");
            state.SetString(FirstContactLabelAnalysisContract.DecisionKey, decision);
            state.SetString(FirstContactLabelAnalysisContract.ClassificationClaimTextKey, string.Empty);
            state.SetString(FirstContactLabelAnalysisContract.NeutralSubjectLabelKey, string.Empty);

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.IsFalse(result.IsSuitable);
            Assert.AreEqual(expectedIssue, result.LabelIssue);
        }

        [Test]
        public void UnifiedLabelAnalysisLetsInconclusiveModelJudgmentReachVisionValidation()
        {
            var state = new PipelineState();
            state.SetString("probe_display_label", "ambiguous label");
            state.SetString("canonical_label", "ambiguous label");
            FirstContactLabelAnalysisContract.ApplyInconclusive(state, "contract remained unstable");

            bool parsed = FirstContactProbeLabelResult.TryFromPipelineState(
                state,
                out FirstContactProbeLabelResult result);

            Assert.IsTrue(parsed);
            Assert.IsTrue(result.AnalysisInconclusive);
            Assert.IsTrue(result.IsSuitable);
        }

        [Test]
        public void ProbeValidationNormalizesContradictoryObjectCount()
        {
            var state = new PipelineState();
            state.SetString("is_blank", "true");
            state.SetString("object_count", "1");
            state.SetString("has_text_or_symbol", "false");
            state.SetString("is_scene_or_action", "false");
            state.SetString("label_match", "match");

            bool parsed = FirstContactProbeValidationResult.TryFromPipelineState(
                state,
                out FirstContactProbeValidationResult result);

            Assert.IsTrue(parsed);
            Assert.AreEqual(0, result.ObjectCount);
            Assert.AreEqual("unclear", result.LabelMatch);
        }

        [Test]
        public void ProbeValidationCollectsAllIndependentVisualIssues()
        {
            FirstContactVlmSettings settings = ScriptableObject.CreateInstance<FirstContactVlmSettings>();
            try
            {
                settings.rejectBlank = true;
                settings.rejectWrittenText = true;
                settings.rejectActionOrScene = true;
                settings.rejectMultipleObjects = true;
                var result = new FirstContactProbeValidationResult
                {
                    IsBlank = false,
                    HasTextOrSymbol = true,
                    IsSceneOrAction = false,
                    ObjectCount = 2,
                    LabelMatch = "match"
                };

                IReadOnlyList<FirstContactProbeVisualIssue> issues =
                    result.CollectRejectedVisualIssues(settings);

                CollectionAssert.AreEqual(
                    new[]
                    {
                        FirstContactProbeVisualIssue.TextOrSymbol,
                        FirstContactProbeVisualIssue.MultipleObjects
                    },
                    issues);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void BlankVisualIssueSuppressesRedundantSecondaryIssues()
        {
            FirstContactVlmSettings settings = ScriptableObject.CreateInstance<FirstContactVlmSettings>();
            try
            {
                settings.rejectBlank = true;
                settings.rejectWrittenText = true;
                settings.rejectActionOrScene = true;
                settings.rejectMultipleObjects = true;
                var result = new FirstContactProbeValidationResult
                {
                    IsBlank = true,
                    HasTextOrSymbol = true,
                    IsSceneOrAction = true,
                    ObjectCount = 0
                };

                IReadOnlyList<FirstContactProbeVisualIssue> issues =
                    result.CollectRejectedVisualIssues(settings);

                CollectionAssert.AreEqual(
                    new[] { FirstContactProbeVisualIssue.Blank },
                    issues);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void FailedCategoryFitDoesNotAcceptProbe()
        {
            FirstContactBootstrapCategoryFitResult result =
                FirstContactBootstrapCategoryFitResult.Failed("technical failure");

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.FitsCategory);
            Assert.AreEqual("uncertain", result.EvidenceType);
        }

        [TestCase("symbolic_or_contextual")]
        [TestCase("neutral_or_generic")]
        [TestCase("uncertain")]
        public void CategoryFitPreservesAuthoredRejectionEvidence(string evidenceType)
        {
            var state = new PipelineState();
            state.SetString("fits_category", "true");
            state.SetString("evidence_type", evidenceType);
            state.SetString("reason", "model detail");

            bool parsed = FirstContactBootstrapCategoryFitResult.TryFromPipelineState(
                state,
                out FirstContactBootstrapCategoryFitResult result);

            Assert.IsTrue(parsed);
            Assert.IsFalse(result.FitsCategory);
            Assert.AreEqual(evidenceType, result.EvidenceType);
        }

        [Test]
        public void DuplicateDetectorFindsSemanticDuplicateAcrossRecordedCards()
        {
            FirstContactSemanticSettings settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                settings.bootstrapDuplicateSemanticThreshold = 0.96f;
                var embedding = new FirstContactEmbeddingService(null, settings);
                var recorded = new SemanticCardRecord
                {
                    Label = "knife",
                    Embedding = Unit(1f, 0f, 0f)
                };
                var candidate = new SemanticCardRecord
                {
                    Label = "blade",
                    Embedding = Unit(0.999f, 0.02f, 0f)
                };

                bool duplicateFound = FirstContactProbeDuplicateDetector.TryFindDuplicate(
                    candidate,
                    new List<SemanticCardRecord> { recorded },
                    embedding,
                    settings,
                    out SemanticCardRecord duplicate);

                Assert.IsTrue(duplicateFound);
                Assert.AreSame(recorded, duplicate);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GraphClusteringSeparatesEmergingGroupFromNearbyCentroid()
        {
            FirstContactSemanticSettings settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                settings.clusterJoinThreshold = 0.62f;
                settings.clusterNeighborCount = 2;
                settings.minClusterMembers = 3;
                settings.minClusterCohesion = 0.5f;
                settings.minClusterPairwiseSimilarity = 0.62f;

                var embedding = new FirstContactEmbeddingService(null, settings);
                var memory = new FirstContactSemanticMemory(embedding, settings, null);
                memory.AddCard(CreateCard("knife", Unit(1f, 0f, 0f)));
                memory.AddCard(CreateCard("hammer", Unit(0.92f, 0.39f, 0f)));
                memory.AddCard(CreateCard("shield", Unit(0.92f, -0.39f, 0f)));
                memory.AddCard(CreateCard("banana", Unit(0.65f, 0f, 0.76f)));
                memory.AddCard(CreateCard("apple", Unit(0.6f, 0.1f, 0.79f)));
                memory.AddCard(CreateCard("watermelon", Unit(0.6f, -0.1f, 0.79f)));

                Assert.AreEqual(2, memory.Clusters.Count);
                Assert.AreEqual(2, memory.StableClusters.Count);
                Assert.IsTrue(HasClusterWithLabels(memory, "banana", "apple", "watermelon"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GraphClusteringDoesNotStabilizeLooseSimilarityChain()
        {
            FirstContactSemanticSettings settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                settings.clusterJoinThreshold = 0.62f;
                settings.clusterNeighborCount = 2;
                settings.minClusterMembers = 3;
                settings.minClusterCohesion = 0.5f;
                settings.minClusterPairwiseSimilarity = 0.62f;

                var embedding = new FirstContactEmbeddingService(null, settings);
                var memory = new FirstContactSemanticMemory(embedding, settings, null);
                memory.AddCard(CreateCard("alpha", Unit(1f, 0f, 0f)));
                memory.AddCard(CreateCard("bridge", Unit(0.65f, 0.76f, 0f)));
                memory.AddCard(CreateCard("omega", Unit(0.1f, 0.99f, 0f)));

                Assert.AreEqual(1, memory.Clusters.Count);
                Assert.IsFalse(memory.Clusters[0].IsStable);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void GraphClusteringUsesConfiguredKeywordsForStableGroupMeaning()
        {
            FirstContactSemanticSettings settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                settings.clusterJoinThreshold = 0.62f;
                settings.clusterNeighborCount = 2;
                settings.minClusterMembers = 3;
                settings.minClusterCohesion = 0.5f;
                settings.minClusterPairwiseSimilarity = 0.62f;
                var categories = new List<FirstContactBootstrapCategoryDefinition>
                {
                    new()
                    {
                        id = "signal",
                        categoryDisplayName = "SIGNAL",
                        meaningDisplayName = "[SIGNAL?]",
                        descriptorText = "visible electrical signals",
                        clusterLabelKeywords = new List<string> { "spark" }
                    }
                };

                var embedding = new FirstContactEmbeddingService(null, settings);
                var memory = new FirstContactSemanticMemory(embedding, settings, null, categories);
                memory.AddCard(CreateCard("spark", Unit(1f, 0f, 0f)));
                memory.AddCard(CreateCard("arc", Unit(0.99f, 0.1f, 0f)));
                memory.AddCard(CreateCard("flash", Unit(0.99f, -0.1f, 0f)));

                Assert.AreEqual(1, memory.StableClusters.Count);
                Assert.AreEqual("[SIGNAL?]", memory.StableClusters[0].DisplayName);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        private static SemanticCardRecord CreateCard(string label, float[] vector)
        {
            return new SemanticCardRecord
            {
                Label = label,
                Embedding = vector,
                Source = FirstContactCardSource.BootstrapProbe
            };
        }

        private static FirstContactSemanticMapSnapshot CreateMapSnapshot(params string[] nodeIds)
        {
            var snapshot = new FirstContactSemanticMapSnapshot();
            for (int i = 0; i < nodeIds.Length; i++)
            {
                snapshot.Nodes.Add(new FirstContactSemanticMapNode
                {
                    Id = nodeIds[i],
                    Label = nodeIds[i],
                    Kind = FirstContactSemanticMapNodeKind.Card,
                    Position = new Vector2(-0.4f + i * 0.4f, 0f),
                    IsActive = i == 0
                });
            }

            return snapshot;
        }

        private static RectTransform CreateProbePreviewSlot(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            out RawImage image,
            out AspectRatioFitter aspect)
        {
            var rootObject = new GameObject(name, typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;

            var imageObject = new GameObject(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            imageObject.transform.SetParent(root, false);
            image = imageObject.GetComponent<RawImage>();
            aspect = imageObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            return root;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field: {fieldName}");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing field: {fieldName}");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Missing method: {methodName}");
            method.Invoke(target, null);
        }

        private static bool HasClusterWithLabels(
            FirstContactSemanticMemory memory,
            params string[] labels)
        {
            for (int i = 0; i < memory.Clusters.Count; i++)
            {
                SemanticClusterRecord cluster = memory.Clusters[i];
                if (cluster.Members.Count != labels.Length)
                {
                    continue;
                }

                bool matchedAll = true;
                for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    bool matchedLabel = false;
                    for (int memberIndex = 0; memberIndex < cluster.Members.Count; memberIndex++)
                    {
                        if (cluster.Members[memberIndex].Label == labels[labelIndex])
                        {
                            matchedLabel = true;
                            break;
                        }
                    }

                    if (!matchedLabel)
                    {
                        matchedAll = false;
                        break;
                    }
                }

                if (matchedAll)
                {
                    return true;
                }
            }

            return false;
        }

        private static float[] Unit(params float[] values)
        {
            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i] * values[i];
            }

            float magnitude = Mathf.Sqrt(sum);
            var normalized = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                normalized[i] = values[i] / magnitude;
            }

            return normalized;
        }
    }
}
