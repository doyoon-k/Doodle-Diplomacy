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

        [TestCase("handgun", ReactionTier.Strong)]
        [TestCase("reproductive organ icon", ReactionTier.Strong)]
        [TestCase("abstract symbol", ReactionTier.Moderate)]
        [TestCase("body part", ReactionTier.Moderate)]
        [TestCase("apple", ReactionTier.Subtle)]
        [TestCase("vehicle icon", ReactionTier.Subtle)]
        [TestCase("simple shape", ReactionTier.None)]
        [TestCase("unlisted sculpture", ReactionTier.Subtle)]
        [TestCase("weapon-shaped ritual icon", ReactionTier.Strong)]
        public void ReactionTierMatchingUsesKeywordPriority(string label, ReactionTier expected)
        {
            Assert.AreEqual(expected, Day1ReactionTierEvaluator.Evaluate(label));
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
