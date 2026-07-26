import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { placeholders, validateScenario } from "../lib/validation.mjs";
import { parseUnityStringTable, validateCatalog } from "../lib/localization-catalog.mjs";
import { auditCatalogUsage } from "../lib/source-audit.mjs";

test("extracts unique placeholders", () => {
  assert.deepEqual(placeholders("{category} {count} {category}"), ["category", "count"]);
});

test("flags translated placeholder mismatch", () => {
  const issues = validateScenario({
    scenarioId: "test", title: "Test", sourceLocale: "en-US", locales: ["en-US", "ko-KR"],
    sections: [{ id: "a" }],
    beats: [{ id: "line", sectionId: "a", type: "dialogue", localizationKey: "line", sourceText: "Hi {name}", localizedTexts: [{ locale: "ko-KR", text: "안녕" }] }],
  });
  assert.ok(issues.some((issue) => issue.severity === "error" && issue.field === "localizedTexts.ko-KR"));
});

test("warns when an enabled beat has no in-game playback connection", () => {
  const issues = validateScenario({
    scenarioId: "test", sourceLocale: "en-US", locales: ["en-US"],
    sections: [{ id: "a" }],
    beats: [{ id: "orphan", sectionId: "a", type: "dialogue", sourceText: "Hello" }],
  });
  assert.ok(issues.some((issue) => issue.severity === "warning" && issue.beatId === "orphan" && issue.field === "triggerEvent"));
});

test("the checked-in First Contact scenario validates without errors", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const file = path.resolve(here, "../../../LlamaSharpDemo/Assets/Narrative/first_contact_day1.narrative.json");
  const document = JSON.parse(fs.readFileSync(file, "utf8"));
  const errors = validateScenario(document).filter((issue) => issue.severity === "error");
  assert.deepEqual(errors, []);
});

test("parses folded Unity YAML localization strings", () => {
  const entries = parseUnityStringTable(`entries:\n  - key: ui.test\n    sourceText: 'Value: {count}. This line\n      continues.'\n    translations:\n    - locale: ko-KR\n      text: "\\uAC12: {count}"\n`);
  assert.deepEqual(entries, [{
    key: "ui.test",
    sourceText: "Value: {count}. This line continues.",
    localizedTexts: [{ locale: "ko-KR", text: "값: {count}" }],
  }]);
});

test("the checked-in UI copy catalog validates without errors", () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const file = path.resolve(here, "../../../LlamaSharpDemo/Assets/Localization/Authoring/ui_copy.catalog.json");
  const catalog = JSON.parse(fs.readFileSync(file, "utf8"));
  assert.ok(catalog.entries.length > 0);
  assert.equal(
    catalog.entries.filter((entry) => entry.audience !== "internal").length +
      catalog.entries.filter((entry) => entry.audience === "internal").length,
    catalog.entries.length,
  );
  assert.deepEqual(
    catalog.entries.filter((entry) => entry.audience === "internal").map((entry) => entry.key).sort(),
    [
      "first_contact.terminal.category.danger.descriptor",
      "first_contact.terminal.category.food.descriptor",
      "first_contact.terminal.category.protection.descriptor",
      "first_contact.terminal.category.tool.descriptor",
    ],
  );
  assert.equal(catalog.terms.length, 9);
  assert.equal(catalog.entries.some((entry) => entry.key.startsWith("day1.")), false);
  assert.equal(catalog.entries.some((entry) => entry.key.startsWith("label.")), false);
  assert.equal(catalog.screens.some((screen) => screen.id === "labels"), false);
  assert.equal(
    catalog.screens.every((screen) => catalog.entries.some((entry) => entry.screenId === screen.id)),
    true,
  );
  assert.deepEqual(validateCatalog(catalog).filter((issue) => issue.severity === "error"), []);
});

test("the checked-in UI copy catalog has no missing runtime keys", async () => {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const projectRoot = path.resolve(here, "../../../LlamaSharpDemo");
  const catalog = JSON.parse(fs.readFileSync(path.join(projectRoot, "Assets/Localization/Authoring/ui_copy.catalog.json"), "utf8"));
  const scenario = JSON.parse(fs.readFileSync(path.join(projectRoot, "Assets/Narrative/first_contact_day1.narrative.json"), "utf8"));
  const narrativeKeys = new Set(scenario.beats.map((beat) => beat.localizationKey?.toLowerCase()).filter(Boolean));
  const usage = await auditCatalogUsage(projectRoot, catalog, narrativeKeys);

  assert.deepEqual(usage.missingKeys, []);
});
