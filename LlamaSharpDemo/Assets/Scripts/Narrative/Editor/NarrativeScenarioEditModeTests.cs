using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Narrative.Editor
{
    public sealed class NarrativeScenarioEditModeTests
    {
        private const string ScenarioPath =
            "Assets/Narrative/first_contact_day1.narrative.json";

        [Test]
        public void FirstContactDocument_DeserializesAndMapsRuntimeCue()
        {
            string absolutePath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                ScenarioPath));
            string json = File.ReadAllText(absolutePath);
            NarrativeScenarioAsset scenario = ScriptableObject.CreateInstance<NarrativeScenarioAsset>();
            try
            {
                scenario.ApplyDocument(NarrativeScenarioJson.Parse(json));
                Assert.That(scenario.ScenarioId, Is.EqualTo("first_contact_day1"));
                Assert.That(scenario.Beats.Count, Is.EqualTo(45));
                Assert.That(
                    scenario.TryGetBeatByRuntimeCue("CategoryCalibrated", out NarrativeBeat beat),
                    Is.True);
                Assert.That(beat.id, Is.EqualTo("category_calibrated"));
                Assert.That(beat.WaitForAdvance, Is.False);
                Assert.That(
                    scenario.TryGetBeatByRuntimeCue("PreflightIntro", out NarrativeBeat preflightBeat),
                    Is.True);
                Assert.That(preflightBeat.id, Is.EqualTo("preflight_intro"));
                Assert.That(preflightBeat.WaitForAdvance, Is.True);
                Assert.That(preflightBeat.sourceText, Does.Not.Contain("{category}"));
                Assert.That(
                    scenario.TryGetBeatByRuntimeCue("PreflightDrawing", out NarrativeBeat preflightDrawing),
                    Is.True);
                Assert.That(preflightDrawing.sourceText, Does.Not.Contain("{category}"));
                Assert.That(preflightDrawing.sourceText, Does.Contain("any one concrete object"));
            }
            finally
            {
                Object.DestroyImmediate(scenario);
            }
        }

        [Test]
        public void FirstContactDocument_ContainsReactiveGuidanceAndTranslations()
        {
            string absolutePath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                ScenarioPath));
            NarrativeScenarioAsset scenario = ScriptableObject.CreateInstance<NarrativeScenarioAsset>();
            try
            {
                scenario.ApplyDocument(NarrativeScenarioJson.Parse(File.ReadAllText(absolutePath)));
                Assert.That(
                    scenario.TryGetBeat("doctor_hwang_probe_multiple_objects", out NarrativeBeat beat),
                    Is.True);
                Assert.That(beat.type, Is.EqualTo("reactive"));
                Assert.That(beat.localizedTexts, Has.Count.EqualTo(1));
                Assert.That(beat.localizedTexts[0].locale, Is.EqualTo("ko-KR"));
                Assert.That(beat.localizedTexts[0].text, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(scenario);
            }
        }
    }
}
