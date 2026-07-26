import test from "node:test";
import assert from "node:assert/strict";
import {
  createDialogueBeat,
  duplicateNarrativeBeat,
  estimateDialogueSeconds,
  playbackConnection,
} from "../public/authoring.js";

function sampleDocument() {
  return {
    scenarioId: "first_contact_day1",
    sourceLocale: "en-US",
    locales: ["en-US", "ko-KR"],
    sections: [{ id: "pizza", title: "Pizza shop" }],
    beats: [
      {
        id: "pizza_joke", sectionId: "pizza", order: 10, triggerEvent: "intro.pizza.encounter",
        repeat: "once-per-session", advance: "automatic", speakerId: "director", speakerFallback: "DIRECTOR",
      },
      { id: "pizza_laugh", sectionId: "pizza", order: 20, triggerEvent: "intro.pizza.encounter" },
    ],
  };
}

test("creates a dialogue after the selected beat with inherited playback context", () => {
  const document = sampleDocument();
  const beat = createDialogueBeat(document, {
    anchorBeatId: "pizza_joke",
    speaker: { id: "president", fallback: "PRESIDENT", localizationKey: "speaker.president" },
    sourceText: "We stopped for pizza.",
    translations: { "ko-KR": "피자를 먹으러 들렀습니다." },
    automaticDuration: true,
  });

  assert.equal(beat.id, "pizza_encounter_line_0003");
  assert.equal(beat.localizationKey, "first_contact.intro.pizza.encounter.line_0003");
  assert.equal(beat.sectionId, "pizza");
  assert.equal(beat.order, 15);
  assert.equal(beat.triggerEvent, "intro.pizza.encounter");
  assert.equal(beat.runtimeCue, "");
  assert.equal(beat.speakerFallback, "PRESIDENT");
  assert.equal(beat.localizedTexts[0].text, "피자를 먹으러 들렀습니다.");
  assert.ok(beat.minimumSeconds >= 1.8);
});

test("allocates stable readable identities without reusing the serial", () => {
  const document = sampleDocument();
  const first = createDialogueBeat(document, { anchorBeatId: "pizza_joke", sourceText: "One" });
  document.beats.push(first);
  const second = createDialogueBeat(document, { anchorBeatId: first.id, sourceText: "Two" });

  assert.equal(first.id, "pizza_encounter_line_0003");
  assert.equal(second.id, "pizza_encounter_line_0004");
  first.sourceText = "The rewritten first line";
  assert.equal(first.id, "pizza_encounter_line_0003");
  assert.equal(first.localizationKey, "first_contact.intro.pizza.encounter.line_0003");
});

test("estimates a readable duration and reports playback connection state", () => {
  assert.ok(estimateDialogueSeconds("A short line.", [{ locale: "ko-KR", text: "조금 더 긴 한국어 대사입니다." }]) >= 2);
  assert.equal(playbackConnection({ triggerEvent: "intro.car" }).state, "connected");
  assert.equal(playbackConnection({ runtimeCue: "SpecialShot" }).state, "direct");
  assert.equal(playbackConnection({}).state, "unconnected");
});

test("duplicates stage beats with a new generated identity", () => {
  const document = sampleDocument();
  document.beats[0].type = "system";
  document.beats[0].runtimeCue = "AwkwardSilence";
  const copy = duplicateNarrativeBeat(document, document.beats[0]);
  assert.equal(copy.id, "pizza_encounter_line_0003");
  assert.equal(copy.runtimeCue, "AwkwardSilence");
  assert.equal(copy.type, "system");
  assert.equal(copy.status, "draft");
});
