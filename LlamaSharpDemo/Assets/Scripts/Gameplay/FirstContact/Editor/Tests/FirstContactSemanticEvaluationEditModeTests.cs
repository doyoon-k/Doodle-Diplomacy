#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;

namespace DoodleDiplomacy.Gameplay.FirstContact.Editor.Tests
{
    public sealed class FirstContactSemanticEvaluationEditModeTests
    {
        [Test]
        public void RequestContractParsesAllProductionEvaluationKinds()
        {
            const string json = @"{
              ""runId"": ""contract-test"",
              ""bootstrapCases"": [{
                ""id"": ""bootstrap-1"",
                ""categoryDefinition"": ""visible food"",
                ""subject"": ""사과"",
                ""expectedDecision"": ""ordinary_match""
              }],
              ""groupSeedCases"": [{
                ""id"": ""seed-1"",
                ""newMeaning"": ""사과"",
                ""existingMembers"": [""바나나""],
                ""expectedCategoryPresence"": ""non_empty""
              }],
              ""groupMembershipCases"": [{
                ""id"": ""membership-1"",
                ""newMeaning"": ""수박"",
                ""existingCategory"": ""과일"",
                ""expectedDecision"": ""join""
              }]
            }";

            bool parsed = FirstContactSemanticEvaluationRunner.TryDeserializeRequest(
                json,
                out FirstContactSemanticEvaluationRequest request,
                out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual("contract-test", request.runId);
            Assert.AreEqual(1, request.bootstrapCases.Length);
            Assert.AreEqual(1, request.groupSeedCases.Length);
            Assert.AreEqual(1, request.groupMembershipCases.Length);
            CollectionAssert.AreEqual(
                new[] { "바나나" },
                request.groupSeedCases[0].existingMembers);
        }

        [Test]
        public void RequestContractRejectsEmptyCaseSet()
        {
            bool parsed = FirstContactSemanticEvaluationRunner.TryDeserializeRequest(
                "{\"runId\":\"empty\"}",
                out _,
                out string error);

            Assert.IsFalse(parsed);
            StringAssert.Contains("no cases", error);
        }

        [Test]
        public void RequestContractCountsCompactEvaluationSets()
        {
            const string json = @"{
              ""runId"": ""set-contract-test"",
              ""bootstrapSets"": [{
                ""idPrefix"": ""food"",
                ""categoryDefinition"": ""food"",
                ""ordinaryMatches"": [""사과"", ""bread""],
                ""mismatches"": [""hammer""],
                ""uncertainSubjects"": [""date""]
              }],
              ""groupMembershipSets"": [{
                ""idPrefix"": ""fruit"",
                ""existingCategory"": ""과일"",
                ""joins"": [""사과"", ""mango""],
                ""rejects"": [""총""],
                ""uncertainMeanings"": [""date""]
              }]
            }";

            bool parsed = FirstContactSemanticEvaluationRunner.TryDeserializeRequest(
                json,
                out FirstContactSemanticEvaluationRequest request,
                out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(8, FirstContactSemanticEvaluationRunner.CountCases(request));
        }

        [Test]
        public void DiverseDatasetParsesWithExpectedCoverage()
        {
            string path = FirstContactSemanticEvaluationRunner.DiverseDatasetPath;
            Assert.IsTrue(File.Exists(path), $"Dataset not found: {path}");

            bool parsed = FirstContactSemanticEvaluationRunner.TryDeserializeRequest(
                File.ReadAllText(path),
                out FirstContactSemanticEvaluationRequest request,
                out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual("semantic-diverse-v1", request.runId);
            Assert.AreEqual(19, request.bootstrapSets.Length);
            Assert.AreEqual(110, request.groupSeedCases.Length);
            Assert.AreEqual(30, request.groupMembershipSets.Length);
            Assert.AreEqual(787, FirstContactSemanticEvaluationRunner.CountCases(request));
        }

        [TestCase("non_empty", "fruit", true)]
        [TestCase("non_empty", "", false)]
        [TestCase("empty", "", true)]
        [TestCase("empty", "fruit", false)]
        [TestCase(".fruit", "fruit", true)]
        public void GroupSeedExpectationSupportsPresenceAndNormalizedExactMatch(
            string expected,
            string category,
            bool shouldPass)
        {
            Assert.AreEqual(
                shouldPass,
                FirstContactSemanticEvaluationRunner.EvaluateExpectation(
                    FirstContactSemanticEvaluationRunner.GroupSeedKind,
                    expected,
                    string.Empty,
                    category));
        }

        [TestCase("ordinary_match", "ordinary_match", true)]
        [TestCase("category_mismatch", "ordinary_match", false)]
        [TestCase("join", "join", true)]
        public void DecisionExpectationsRequireExactDecision(
            string expected,
            string decision,
            bool shouldPass)
        {
            Assert.AreEqual(
                shouldPass,
                FirstContactSemanticEvaluationRunner.EvaluateExpectation(
                    FirstContactSemanticEvaluationRunner.BootstrapKind,
                    expected,
                    decision,
                    string.Empty));
        }
    }
}
#endif
