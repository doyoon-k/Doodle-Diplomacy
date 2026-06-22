using System.Collections.Generic;
using DoodleDiplomacy.Gameplay.FirstContact;
using NUnit.Framework;
using UnityEngine;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class FirstContactEditModeTests
    {
        [Test]
        public void RuntimeFallbackQuestionsAreValidSelectOneRequests()
        {
            FirstContactQuestionSettings settings = ScriptableObject.CreateInstance<FirstContactQuestionSettings>();
            try
            {
                Assert.AreEqual(5, FirstContactRuntimeFallbackQuestions.Count);

                for (int i = 0; i < FirstContactRuntimeFallbackQuestions.Count; i++)
                {
                    FirstContactQuestionDefinition question = FirstContactRuntimeFallbackQuestions.GetQuestion(i);

                    Assert.IsTrue(
                        FirstContactQuestionValidator.Validate(question, settings, out string error),
                        $"Fallback question {i} should validate: {error}");
                    Assert.AreEqual("SELECT-ONE", question.primitiveTokens[question.primitiveTokens.Length - 1]);
                    Assert.GreaterOrEqual(question.unknownSlots.Length, 1);
                    Assert.LessOrEqual(question.unknownSlots.Length, 3);
                }
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void QuestionValidatorRejectsNonSelectOneRequests()
        {
            FirstContactQuestionSettings settings = ScriptableObject.CreateInstance<FirstContactQuestionSettings>();
            try
            {
                var question = new FirstContactQuestionDefinition
                {
                    primitiveTokens = new[] { "YOU", "[UNKNOWN-01]" },
                    unknownSlots = new[]
                    {
                        new FirstContactUnknownSlotDefinition
                        {
                            id = "UNKNOWN-01",
                            targetConcept = "important"
                        }
                    }
                };

                bool valid = FirstContactQuestionValidator.Validate(question, settings, out string error);

                Assert.IsFalse(valid);
                StringAssert.Contains("SELECT-ONE", error);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void QuestionValidatorRejectsBannedQuestionTokens()
        {
            FirstContactQuestionSettings settings = ScriptableObject.CreateInstance<FirstContactQuestionSettings>();
            try
            {
                var question = new FirstContactQuestionDefinition
                {
                    primitiveTokens = new[] { "YOU", "WHY", "[UNKNOWN-01]", "SELECT-ONE" },
                    unknownSlots = new[]
                    {
                        new FirstContactUnknownSlotDefinition
                        {
                            id = "UNKNOWN-01",
                            targetConcept = "important"
                        }
                    }
                };

                bool valid = FirstContactQuestionValidator.Validate(question, settings, out string error);

                Assert.IsFalse(valid);
                StringAssert.Contains("WHY", error);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AlienQuestionDisplayLineReflectsTranslationStage()
        {
            var definition = new FirstContactQuestionDefinition
            {
                primitiveTokens = new[] { "YOU", "EARTH", "[UNKNOWN-01]", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "defense",
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[OBJECT?]",
                            partial = "[DEFENSE-RELATED?]",
                            solved = "DEFENSE"
                        }
                    }
                }
            };
            AlienQuestion question = AlienQuestion.FromDefinition(definition, FirstContactQuestionSource.Fallback);
            UnknownSlot slot = question.FindUnknown("UNKNOWN-01");

            Assert.AreEqual("YOU / EARTH / [UNKNOWN-01] / SELECT-ONE", question.BuildDisplayLine());

            slot.TryAdvanceTo(FirstContactTranslationStage.Hint, 0.5f);
            Assert.AreEqual("YOU / EARTH / [OBJECT?] / SELECT-ONE", question.BuildDisplayLine());

            slot.TryAdvanceTo(FirstContactTranslationStage.Partial, 0.6f);
            Assert.AreEqual("YOU / EARTH / [DEFENSE-RELATED?] / SELECT-ONE", question.BuildDisplayLine());

            slot.TryAdvanceTo(FirstContactTranslationStage.Solved, 0.8f);
            Assert.AreEqual("YOU / EARTH / DEFENSE / SELECT-ONE", question.BuildDisplayLine());
        }

        [Test]
        public void EmbeddingWrapperNormalizesLabelsWithoutSemanticPrefix()
        {
            var service = new FirstContactEmbeddingService(null, null);

            Assert.AreEqual("shield wall", FirstContactEmbeddingService.NormalizeText("  Shield   Wall  "));
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
        public void StableClusterCanAutoPartiallyDecodeFutureUnknown()
        {
            FirstContactSemanticSettings settings = ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            try
            {
                settings.clusterJoinThreshold = 0.5f;
                settings.minClusterMembers = 3;
                settings.minClusterCohesion = 0.5f;
                settings.clusterAutoPartialThreshold = 0.58f;

                var embedding = new FirstContactEmbeddingService(null, settings);
                var memory = new FirstContactSemanticMemory(embedding, settings, null);
                memory.AddCard(CreateCard("shield", Unit(1f, 0f, 0f)));
                memory.AddCard(CreateCard("wall", Unit(0.98f, 0.2f, 0f)));
                memory.AddCard(CreateCard("helmet", Unit(0.97f, 0.22f, 0f)));

                Assert.AreEqual(1, memory.Clusters.Count);
                Assert.IsTrue(memory.Clusters[0].IsStable);
                Assert.AreEqual("[PROTECTION?]", memory.Clusters[0].DisplayName);

                var definition = new FirstContactQuestionDefinition
                {
                    primitiveTokens = new[] { "YOU", "HOME", "[UNKNOWN-01]", "SELECT-ONE" },
                    unknownSlots = new[]
                    {
                        new FirstContactUnknownSlotDefinition
                        {
                            id = "UNKNOWN-01",
                            targetConcept = "defense",
                            stageTexts = new FirstContactStageTexts
                            {
                                hint = "[OBJECT?]",
                                partial = "[DEFENSE-RELATED?]",
                                solved = "DEFENSE"
                            }
                        }
                    }
                };
                AlienQuestion question = AlienQuestion.FromDefinition(definition, FirstContactQuestionSource.Fallback);
                UnknownSlot slot = question.FindUnknown("UNKNOWN-01");
                slot.TargetEmbedding = new TargetConceptEmbedding
                {
                    TargetConcept = "defense",
                    Vector = Unit(1f, 0f, 0f)
                };

                var resolver = new FirstContactUnknownResolver(embedding, settings);
                bool changed = resolver.ApplyAutomaticClusterHints(question, memory);

                Assert.IsTrue(changed);
                Assert.AreEqual(FirstContactTranslationStage.Partial, slot.Stage);
                Assert.AreEqual("YOU / HOME / [DEFENSE-RELATED?] / SELECT-ONE", question.BuildDisplayLine());
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

        private static SemanticCardRecord CreateCard(string label, float[] vector)
        {
            return new SemanticCardRecord
            {
                Label = label,
                Embedding = vector,
                Source = FirstContactCardSource.DecodeSample
            };
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
