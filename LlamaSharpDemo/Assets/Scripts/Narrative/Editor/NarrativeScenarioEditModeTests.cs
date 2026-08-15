using System.IO;
using System.Linq;
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
                Assert.That(scenario.Beats.Count, Is.EqualTo(147));
                Assert.That(
                    scenario.Beats
                        .Where(item => item.sectionId == "preflight")
                        .All(item => !item.enabled),
                    Is.True,
                    "The retired meeting-room preflight must not run alongside briefing FOOD practice.");
                Assert.That(
                    scenario.TryGetBeatByRuntimeCue("CategoryCalibrated", out NarrativeBeat beat),
                    Is.True);
                Assert.That(beat.id, Is.EqualTo("category_calibrated"));
                Assert.That(beat.WaitForAdvance, Is.False);
                Assert.That(
                    scenario.Beats.Count(item =>
                        item.triggerEvent == "intro.facility.corridor"),
                    Is.EqualTo(6));
                Assert.That(
                    scenario.Beats.Count(item =>
                        item.triggerEvent == "intro.facility.briefing"),
                    Is.EqualTo(58));
                Assert.That(
                    scenario.Beats.Count(item =>
                        item.triggerEvent == "intro.meeting.arrival"),
                    Is.EqualTo(9));
                Assert.That(
                    scenario.TryGetBeat(
                        "facility_corridor_discovery_0073",
                        out NarrativeBeat corridorBeat),
                    Is.True);
                Assert.That(corridorBeat.WaitForAdvance, Is.False);
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_assignment_question_0078",
                        out NarrativeBeat briefingBeat),
                    Is.True);
                Assert.That(briefingBeat.WaitForAdvance, Is.True);
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_banana_example_0101",
                        out NarrativeBeat practiceSubjectBeat),
                    Is.True);
                Assert.That(
                    practiceSubjectBeat.runtimeCue,
                    Is.EqualTo("BriefingSlideBanana"));
                Assert.That(practiceSubjectBeat.sourceText, Does.Contain("response waveform"));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_practice_transition_0101a",
                        out NarrativeBeat practiceStartBeat),
                    Is.True);
                Assert.That(practiceStartBeat.order, Is.EqualTo(235));
                Assert.That(practiceStartBeat.sourceText, Does.Contain("FOOD"));
                Assert.That(practiceStartBeat.sourceText, Does.Contain("terminal"));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_first_response_debrief_0101b",
                        out NarrativeBeat firstResponseDebriefBeat),
                    Is.True);
                Assert.That(firstResponseDebriefBeat.order, Is.EqualTo(238));
                Assert.That(firstResponseDebriefBeat.sourceText, Does.Contain("response waveform"));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_database_limit_0102",
                        out NarrativeBeat practiceDebriefBeat),
                    Is.True);
                Assert.That(practiceDebriefBeat.order, Is.EqualTo(240));
                Assert.That(practiceDebriefBeat.runtimeCue, Is.EqualTo("BriefingSlideDatabase"));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_pattern_practice_transition_0110a",
                        out NarrativeBeat patternPracticeBeat),
                    Is.True);
                Assert.That(patternPracticeBeat.order, Is.EqualTo(325));
                Assert.That(patternPracticeBeat.sourceText, Does.Contain("FOOD"));
                Assert.That(patternPracticeBeat.sourceText, Does.Contain("shared pattern"));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_category_basis_0112",
                        out NarrativeBeat practiceDiscardBeat),
                    Is.True);
                Assert.That(practiceDiscardBeat.speakerId, Is.EqualTo("president"));
                Assert.That(practiceDiscardBeat.sourceText, Does.Contain("categories"));
                Assert.That(
                    scenario.TryGetBeat("briefing_basic_premise_0099", out NarrativeBeat premiseBeat),
                    Is.True);
                Assert.That(premiseBeat.runtimeCue, Is.EqualTo("BriefingSlideObjectSignal"));
                Assert.That(
                    scenario.Beats
                        .Where(item => item.triggerEvent == "intro.facility.briefing" && item.order <= 230)
                        .Any(item => item.runtimeCue == "BriefingProjectorTechnical"),
                    Is.True,
                    "The original technical explanation must remain before the hands-on practice.");
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_assignment_question_0078",
                        out NarrativeBeat assignmentQuestionBeat),
                    Is.True);
                Assert.That(
                    assignmentQuestionBeat.briefingLookTarget,
                    Is.EqualTo(BriefingLookTarget.Director));
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_presentation_end_0116a",
                        out NarrativeBeat presentationEndBeat),
                    Is.True);
                Assert.That(presentationEndBeat.runtimeCue, Is.Empty);
                Assert.That(
                    scenario.TryGetBeat(
                        "briefing_questions_0116b",
                        out NarrativeBeat questionsBeat),
                    Is.True);
                Assert.That(questionsBeat.runtimeCue, Is.EqualTo("BriefingQAndAStart"));
                Assert.That(
                    scenario.Beats
                        .Where(item =>
                            item.triggerEvent == "intro.facility.briefing" &&
                            item.order > questionsBeat.order)
                        .All(item =>
                            string.IsNullOrEmpty(item.runtimeCue) ||
                            !item.runtimeCue.StartsWith("BriefingSlide")),
                    Is.True,
                    "Q&A dialogue must not turn presentation slides back on.");
                Assert.That(
                    scenario.Beats
                        .Where(item =>
                            item.triggerEvent == "intro.facility.briefing" &&
                            item.speakerId == "director")
                        .All(item =>
                            item.runtimeCue == "BriefingLookDirector"),
                    Is.True,
                    "Every director line in the briefing must turn the president toward the director.");
                BriefingLookTarget[] validBriefingLookTargets =
                {
                    BriefingLookTarget.UseRuntimeCue,
                    BriefingLookTarget.KeepCurrent,
                    BriefingLookTarget.Director,
                    BriefingLookTarget.HwangPresentation,
                    BriefingLookTarget.HwangQa,
                    BriefingLookTarget.Projector
                };
                Assert.That(
                    scenario.Beats
                        .Where(item => item.sectionId == "facility_briefing")
                        .All(item =>
                            validBriefingLookTargets.Contains(
                                item.briefingLookTarget)),
                    Is.True,
                    "Briefing look targets must use a supported Narrative Desk option.");
                Assert.That(
                    scenario.TryGetBeat(
                        "meeting_director_cover_0139",
                        out NarrativeBeat meetingDirectorBeat),
                    Is.True);
                Assert.That(
                    meetingDirectorBeat.meetingLookTarget,
                    Is.EqualTo(MeetingLookTarget.Director));
                Assert.That(
                    scenario.TryGetBeat(
                        "meeting_hwang_ready_0144",
                        out NarrativeBeat meetingHwangBeat),
                    Is.True);
                Assert.That(
                    meetingHwangBeat.meetingLookTarget,
                    Is.EqualTo(MeetingLookTarget.Hwang));
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
