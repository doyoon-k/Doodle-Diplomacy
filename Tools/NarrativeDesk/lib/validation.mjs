const placeholderPattern = /\{([A-Za-z_][A-Za-z0-9_]*)\}/g;

export function placeholders(text = "") {
  return [...new Set([...String(text).matchAll(placeholderPattern)].map((match) => match[1]))].sort();
}

export function validateScenario(document) {
  const issues = [];
  const push = (severity, message, beatId = "", field = "") =>
    issues.push({ severity, message, beatId, field });

  if (!document || typeof document !== "object") {
    push("error", "The scenario document is not a JSON object.");
    return issues;
  }

  if (!String(document.scenarioId || "").trim()) push("error", "scenarioId is required.", "", "scenarioId");
  if (!String(document.title || "").trim()) push("warning", "The scenario title is empty.", "", "title");
  if (!String(document.sourceLocale || "").trim()) push("error", "sourceLocale is required.", "", "sourceLocale");
  if (!Array.isArray(document.beats)) push("error", "beats must be an array.", "", "beats");

  const beatIds = new Set();
  const keys = new Map();
  const sectionIds = new Set((document.sections || []).map((section) => section.id));
  const locales = (document.locales || []).filter((locale) => locale !== document.sourceLocale);

  for (const beat of document.beats || []) {
    const id = String(beat?.id || "").trim();
    if (!id) push("error", "Beat id is required.", "", "id");
    else if (beatIds.has(id)) push("error", `Duplicate beat id: ${id}`, id, "id");
    else beatIds.add(id);

    if (beat?.sectionId && !sectionIds.has(beat.sectionId)) {
      push("error", `Unknown section: ${beat.sectionId}`, id, "sectionId");
    }

    if (!String(beat?.localizationKey || "").trim()) {
      push("warning", "No localization key. This beat will not be exported to the string table.", id, "localizationKey");
    } else {
      const key = beat.localizationKey.trim();
      if (keys.has(key) && keys.get(key) !== beat.sourceText) {
        push("error", `Localization key ${key} has conflicting source text.`, id, "localizationKey");
      }
      keys.set(key, beat.sourceText || "");
    }

    if (["dialogue", "reactive"].includes(beat?.type) && !String(beat?.sourceText || "").trim()) {
      push("error", "Dialogue text is empty.", id, "sourceText");
    }

    const sourceVariables = placeholders(beat?.sourceText);
    for (const locale of locales) {
      const translated = (beat?.localizedTexts || []).find((item) => item.locale === locale);
      if (!translated || !String(translated.text || "").trim()) {
        push("warning", `Missing ${locale} translation.`, id, `localizedTexts.${locale}`);
        continue;
      }

      const translatedVariables = placeholders(translated.text);
      if (sourceVariables.join("|") !== translatedVariables.join("|")) {
        push(
          "error",
          `${locale} placeholders (${translatedVariables.join(", ") || "none"}) do not match source (${sourceVariables.join(", ") || "none"}).`,
          id,
          `localizedTexts.${locale}`,
        );
      }
    }
  }

  for (const entry of document.localizationEntries || []) {
    const key = String(entry?.key || "").trim();
    if (!key) push("error", "An additional localization entry has no key.", entry?.beatId || "", "localizationEntries");
    if (keys.has(key) && keys.get(key) !== entry.sourceText) {
      push("error", `Localization key ${key} has conflicting source text.`, entry?.beatId || "", "localizationEntries");
    }
    keys.set(key, entry?.sourceText || "");

    const sourceVariables = placeholders(entry?.sourceText);
    for (const translated of entry?.localizedTexts || []) {
      const translatedVariables = placeholders(translated.text);
      if (sourceVariables.join("|") !== translatedVariables.join("|")) {
        push("error", `${translated.locale} placeholders do not match source for ${key}.`, entry?.beatId || "", "localizationEntries");
      }
    }
  }

  return issues;
}
