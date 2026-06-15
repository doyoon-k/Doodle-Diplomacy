using System.IO;
using System.Text.Json;
using DoodleDiplomacy.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class Day1PrototypeEditModeTests
    {
        [Test]
        public void VisualStimulusParsingAcceptsValidJson()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1,\"label\":\"apple\"}",
                out VisualStimulusClassificationResult result);

            Assert.IsTrue(parsed, result?.error);
            Assert.AreEqual(1, result.objectCount);
            Assert.AreEqual("apple", result.label);
            Assert.AreEqual(string.Empty, result.localizedLabel);
        }

        [Test]
        public void VisualStimulusParsingAcceptsLocalizedLabel()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1,\"label\":\"apple\",\"localized_label\":\"사과\"}",
                out VisualStimulusClassificationResult result);

            Assert.IsTrue(parsed, result?.error);
            Assert.AreEqual(1, result.objectCount);
            Assert.AreEqual("apple", result.label);
            Assert.AreEqual("사과", result.localizedLabel);
        }

        [Test]
        public void VisualStimulusParsingRejectsMissingKeys()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1}",
                out VisualStimulusClassificationResult result);

            Assert.IsFalse(parsed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.error));
        }

        [Test]
        public void VisualStimulusParsingRejectsForbiddenKeys()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1,\"label\":\"apple\",\"confidence\":0.91}",
                out VisualStimulusClassificationResult result);

            Assert.IsFalse(parsed);
            StringAssert.Contains("forbidden", result.error);
        }

        [Test]
        public void VisualStimulusParsingRejectsEmptyLabel()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1,\"label\":\"\"}",
                out VisualStimulusClassificationResult result);

            Assert.IsFalse(parsed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.error));
        }

        [Test]
        public void VisualStimulusParsingRejectsLegacyCandidatesKey()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":1,\"label\":\"apple\",\"candidates\":[\"apple\"]}",
                out VisualStimulusClassificationResult result);

            Assert.IsFalse(parsed);
            StringAssert.Contains("unexpected", result.error);
        }

        [Test]
        public void VisualStimulusParsingKeepsMultiObjectResult()
        {
            bool parsed = VisualStimulusClassificationResult.TryFromJson(
                "{\"object_count\":2,\"label\":\"multiple objects\"}",
                out VisualStimulusClassificationResult result);

            Assert.IsTrue(parsed, result?.error);
            Assert.AreEqual(2, result.objectCount);
            Assert.AreEqual("multiple objects", result.label);
        }

        [TestCase("written text")]
        [TestCase("apple and written text")]
        [TestCase("letter A")]
        [TestCase("handwriting")]
        [TestCase("number 12")]
        public void VisualStimulusTextLabelsAreRecognized(string label)
        {
            Assert.IsTrue(VisualStimulusClassificationResult.LabelIndicatesWrittenText(label));
        }

        [TestCase("abstract symbol")]
        [TestCase("ritual icon")]
        [TestCase("textured ball")]
        [TestCase("envelope")]
        public void VisualStimulusNonTextLabelsAreNotRecognizedAsText(string label)
        {
            Assert.IsFalse(VisualStimulusClassificationResult.LabelIndicatesWrittenText(label));
        }

        [Test]
        public void Day1ReactionEvaluationParsingAcceptsValidJson()
        {
            bool parsed = Day1ReactionEvaluationResult.TryFromJson(
                "{\"reaction_tier\":\"strong\",\"reason\":\"weapon-like stimulus\"}",
                out Day1ReactionEvaluationResult result);

            Assert.IsTrue(parsed, result?.error);
            Assert.AreEqual(ReactionTier.Strong, result.reactionTier);
            Assert.AreEqual("weapon-like stimulus", result.reason);
        }

        [Test]
        public void Day1ReactionEvaluationParsingAcceptsTierOnlyJson()
        {
            bool parsed = Day1ReactionEvaluationResult.TryFromJson(
                "{\"reaction_tier\":\"moderate\"}",
                out Day1ReactionEvaluationResult result);

            Assert.IsTrue(parsed, result?.error);
            Assert.AreEqual(ReactionTier.Moderate, result.reactionTier);
            Assert.AreEqual(string.Empty, result.reason);
        }

        [Test]
        public void Day1ReactionEvaluationParsingRejectsUnknownTier()
        {
            bool parsed = Day1ReactionEvaluationResult.TryFromJson(
                "{\"reaction_tier\":\"extreme\",\"reason\":\"unsupported\"}",
                out Day1ReactionEvaluationResult result);

            Assert.IsFalse(parsed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.error));
        }

        [TestCase("blank")]
        [TestCase("simple line")]
        [TestCase("dot")]
        [TestCase("basic shape")]
        [TestCase("geometric shapes")]
        [TestCase("circle")]
        [TestCase("island")]
        [TestCase("characters")]
        [TestCase("letters")]
        [TestCase("written symbols")]
        public void Day1SubmissionPolicyBlocksNonStimuli(string label)
        {
            Assert.IsTrue(Day1StimulusSubmissionPolicy.IsBlockedLabel(label));
        }

        [TestCase("characters")]
        [TestCase("letter")]
        [TestCase("letters")]
        [TestCase("alphabet characters")]
        [TestCase("written symbols")]
        [TestCase("glyphs")]
        [TestCase("number")]
        [TestCase("handwriting")]
        public void Day1SubmissionPolicyDetectsWrittenTextAliases(string label)
        {
            Assert.IsTrue(Day1StimulusSubmissionPolicy.IsWrittenTextLabel(label));
        }

        [TestCase("cartoon character")]
        [TestCase("abstract symbol")]
        [TestCase("heart symbol")]
        [TestCase("textured ball")]
        public void Day1SubmissionPolicyDoesNotTreatObjectLabelsAsWrittenText(string label)
        {
            Assert.IsFalse(Day1StimulusSubmissionPolicy.IsWrittenTextLabel(label));
        }

        [TestCase("abstract symbol")]
        [TestCase("heart symbol")]
        [TestCase("handgun")]
        [TestCase("apple")]
        public void Day1SubmissionPolicyAllowsRecognizableStimuli(string label)
        {
            Assert.IsFalse(Day1StimulusSubmissionPolicy.IsBlockedLabel(label));
        }

        [TestCase("action")]
        [TestCase("action or scene")]
        [TestCase("running")]
        [TestCase("person running")]
        [TestCase("running person")]
        [TestCase("two people interacting")]
        [TestCase("relationship scene")]
        public void Day1SubmissionPolicyDetectsActionsAndScenes(string label)
        {
            Assert.IsTrue(Day1StimulusSubmissionPolicy.IsActionOrSceneLabel(label));
        }

        [TestCase("shoe")]
        [TestCase("running shoe")]
        [TestCase("dancing shoes")]
        [TestCase("chair")]
        public void Day1SubmissionPolicyDoesNotTreatObjectNamesAsActions(string label)
        {
            Assert.IsFalse(Day1StimulusSubmissionPolicy.IsActionOrSceneLabel(label));
        }

        [TestCase(1, "apple", true)]
        [TestCase(2, "two breasts", true)]
        [TestCase(2, "breasts", true)]
        [TestCase(2, "pair of eyes", true)]
        [TestCase(2, "two apples", true)]
        [TestCase(3, "three apples", true)]
        [TestCase(5, "apples", true)]
        [TestCase(3, "three eyes", true)]
        [TestCase(2, "apple and banana", false)]
        [TestCase(2, "apple, banana", false)]
        [TestCase(2, "apple with banana", false)]
        [TestCase(2, "different fruits", false)]
        [TestCase(2, "multiple objects", false)]
        public void Day1SubmissionPolicyAllowsOnlySingleStimulusObjectCounts(
            int objectCount,
            string label,
            bool expected)
        {
            Assert.AreEqual(expected, Day1StimulusSubmissionPolicy.IsAllowedObjectCount(objectCount, label));
        }

        [Test]
        public void StimulusLibrarySavesApprovedRecordsAndStickerKeys()
        {
            string root = MakeTempRoot();
            var host = new GameObject("Day1StimulusLibraryTest");
            var library = host.AddComponent<Day1StimulusLibrary>();
            library.SetRootPathForTests(root);

            try
            {
                library.BeginSession();
                Assert.AreEqual(0, library.ApprovedRecords.Count);
                Assert.AreEqual(0, Directory.GetFiles(root, "*.png").Length);

                byte[] png = CreatePngBytes();
                DoodleDiplomacy.Data.Day1StimulusRecord record =
                    library.SaveApprovedStimulus(1, "Handgun", ReactionTier.Strong, png);

                Assert.AreEqual(1, library.ApprovedRecords.Count);
                Assert.AreEqual("handgun", record.label);
                Assert.AreEqual(ReactionTier.Strong, record.reactionTier);
                Assert.IsTrue(File.Exists(record.imagePath));
                StringAssert.StartsWith("day1_slot_01_handgun", record.stickerKey);
                Assert.IsTrue(File.Exists(library.StimuliManifestPath));
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void ProfilePayloadContainsOnlySlotLabelAndReactionTier()
        {
            string root = MakeTempRoot();
            var host = new GameObject("Day1PayloadTest");
            var library = host.AddComponent<Day1StimulusLibrary>();
            library.SetRootPathForTests(root);

            try
            {
                library.BeginSession();
                library.SaveApprovedStimulus(1, "apple", ReactionTier.Subtle, CreatePngBytes());
                library.WriteProfilePayload();

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(library.ProfilePayloadPath));
                JsonElement stimulus = document.RootElement.GetProperty("stimuli")[0];
                Assert.IsTrue(stimulus.TryGetProperty("slot", out _));
                Assert.IsTrue(stimulus.TryGetProperty("label", out _));
                Assert.IsTrue(stimulus.TryGetProperty("reactionTier", out JsonElement tier));
                Assert.AreEqual("Subtle", tier.GetString());
                Assert.IsFalse(stimulus.TryGetProperty("imagePath", out _));
                Assert.IsFalse(stimulus.TryGetProperty("stickerKey", out _));
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static string MakeTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "DoodleDiplomacyDay1Tests", System.Guid.NewGuid().ToString("N"));
        }

        private static byte[] CreatePngBytes()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
    }
}
