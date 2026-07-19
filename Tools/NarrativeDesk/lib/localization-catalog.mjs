import fs from "node:fs/promises";

import { placeholders } from "./validation.mjs";

const defaultScreens = [
  { id: "title.main", title: "타이틀 화면", surface: "menu", description: "게임 시작과 설정 진입 메뉴" },
  { id: "settings", title: "설정", surface: "menu", description: "언어를 포함한 설정 화면" },
  { id: "day1.hud", title: "1일차 HUD", surface: "hud", description: "그리기와 제출 과정의 조작 문구" },
  { id: "first_contact.intro", title: "첫 접촉 인트로", surface: "caption", description: "임시 인트로 카드와 진행 안내" },
  { id: "first_contact.terminal", title: "첫 접촉 터미널", surface: "terminal", description: "PROBE / TRACE / PATTERN 상태를 표시하는 장치 화면" },
  { id: "first_contact.feedback", title: "표본 판독 결과", surface: "system", description: "표본 검사와 오류 상태 문구" },
  { id: "first_contact.semantic_map", title: "의미 지도", surface: "terminal", description: "반응 패턴과 의미 지도 시각화" },
  { id: "dialogue.shared", title: "대화 공통", surface: "dialogue", description: "화자명과 대화 진행 안내" },
  { id: "system", title: "기타 시스템", surface: "system", description: "다른 화면에 속하지 않은 공통 문구" },
];

const defaultTerms = [
  term("probe", "PROBE", "표본", "플레이어가 그려 외계 반응을 수집하는 그림 한 장.", "한국어 터미널에서는 ‘표본’, 설명 대사에서는 필요할 때 ‘시각 표본’. ‘탐침’은 사용하지 않음."),
  term("probe_label", "PROBE LABEL", "표본 라벨", "플레이어가 그림에 입력한 사물 이름.", "화면에 보이는 라벨은 현지화할 수 있음."),
  term("visual_probe", "VISUAL PROBE", "시각 표본", "외계 반응을 얻기 위해 보내는 그림.", "설명 대사에서 bare ‘표본’이 모호할 때 사용."),
  term("trace", "TRACE", "추적", "표본 하나에 대해 기록된 외계 반응 하나.", "터미널에서는 짧게 유지."),
  term("pattern", "PATTERN", "패턴", "서로 비슷한 외계 반응에서 추출한 공통 형태.", "설명문에서는 ‘반응 패턴’, 터미널에서는 ‘패턴’."),
  term("meaning", "MEANING", "의미", "안정된 반응 패턴을 실제 외계 신호에 적용해 얻은 해석.", "이미 알려준 분류 목표를 되풀이하는 용도로 사용하지 않음."),
  term("category", "CATEGORY", "분류", "현재 표본을 모으는 보정 목표 유형.", "내부 용어 SELECT-ONE을 플레이어에게 노출하지 않음."),
  term("signal", "SIGNAL", "신호", "장치가 수신한 파형 또는 반응 신호.", "설명 대사에서는 ‘시각 표본 신호’ 사용 가능."),
  term("calibration", "CALIBRATION", "보정", "알려진 분류와 안정된 반응 패턴의 연결 완료.", "분류 완료 화면은 ‘보정 완료’로 표시."),
];

function term(id, sourceTerm, targetTerm, definition, notes) {
  return { id, sourceTerm, targetTerm, definition, notes, status: "final" };
}

export function createEmptyCatalog() {
  return {
    schemaVersion: 1,
    catalogId: "game_ui",
    title: "Doodle Diplomacy UI Copy",
    sourceLocale: "en-US",
    locales: ["en-US", "ko-KR"],
    screens: structuredClone(defaultScreens),
    terms: structuredClone(defaultTerms),
    entries: [],
  };
}

export async function createCatalogFromUnityTable(tablePath, excludedKeys = new Set()) {
  const catalog = createEmptyCatalog();
  let yaml;
  try {
    yaml = await fs.readFile(tablePath, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return catalog;
    throw error;
  }

  catalog.entries = parseUnityStringTable(yaml)
    .filter((entry) => !excludedKeys.has(entry.key.toLowerCase()))
    .map((entry) => ({
      ...entry,
      ...inferCopyMetadata(entry.key),
      audience: inferCopyAudience(entry.key),
      context: humanizeKey(entry.key),
      status: "final",
      tags: [],
      localizedTexts: entry.localizedTexts.map((localized) => ({ ...localized, status: "final" })),
    }))
    .sort((a, b) => a.key.localeCompare(b.key));
  return catalog;
}

export function parseUnityStringTable(yaml) {
  const lines = String(yaml || "").replace(/\r\n/g, "\n").split("\n");
  const entries = [];
  let entry = null;
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    const keyMatch = line.match(/^  - key:\s*(.*)$/);
    if (keyMatch) {
      if (entry?.key) entries.push(entry);
      const scalar = readYamlScalar(lines, index, keyMatch[1]);
      index = scalar.endIndex;
      entry = { key: scalar.value, sourceText: "", localizedTexts: [] };
      continue;
    }
    if (!entry) continue;

    const sourceMatch = line.match(/^    sourceText:\s*(.*)$/);
    if (sourceMatch) {
      const scalar = readYamlScalar(lines, index, sourceMatch[1]);
      index = scalar.endIndex;
      entry.sourceText = scalar.value;
      continue;
    }

    const localeMatch = line.match(/^    - locale:\s*(.*)$/);
    if (localeMatch) {
      const scalar = readYamlScalar(lines, index, localeMatch[1]);
      index = scalar.endIndex;
      entry.localizedTexts.push({ locale: scalar.value, text: "" });
      continue;
    }

    const textMatch = line.match(/^      text:\s*(.*)$/);
    if (textMatch && entry.localizedTexts.length) {
      const scalar = readYamlScalar(lines, index, textMatch[1]);
      index = scalar.endIndex;
      entry.localizedTexts.at(-1).text = scalar.value;
    }
  }
  if (entry?.key) entries.push(entry);
  return entries;
}

function readYamlScalar(lines, startIndex, initial) {
  const value = String(initial || "").trim();
  if (value.startsWith("'")) {
    const fragments = [value];
    let index = startIndex;
    while (!endsWithYamlSingleQuote(fragments.at(-1)) && index + 1 < lines.length) {
      index++;
      fragments.push(lines[index].trim());
    }
    const joined = fragments.join(" ");
    return {
      value: joined.slice(1, joined.endsWith("'") ? -1 : undefined).replace(/''/g, "'"),
      endIndex: index,
    };
  }
  if (!value.startsWith('"')) return { value: decodePlainScalar(value), endIndex: startIndex };

  const fragments = [value];
  let index = startIndex;
  while (!endsWithUnescapedQuote(fragments.at(-1)) && index + 1 < lines.length) {
    index++;
    fragments.push(lines[index].trim());
  }
  const joined = fragments.join(" ");
  try {
    return { value: JSON.parse(joined), endIndex: index };
  } catch {
    return { value: joined.slice(1, joined.endsWith('"') ? -1 : undefined), endIndex: index };
  }
}

function endsWithYamlSingleQuote(value) {
  if (!value.endsWith("'")) return false;
  let quotes = 0;
  for (let index = value.length - 1; index >= 0 && value[index] === "'"; index--) quotes++;
  return quotes % 2 === 1;
}

function endsWithUnescapedQuote(value) {
  if (!value.endsWith('"')) return false;
  let slashes = 0;
  for (let index = value.length - 2; index >= 0 && value[index] === "\\"; index--) slashes++;
  return slashes % 2 === 0;
}

function decodePlainScalar(value) {
  if (value === "''" || value === '""') return "";
  if (value.startsWith("'") && value.endsWith("'")) return value.slice(1, -1).replace(/''/g, "'");
  return value;
}

export function inferCopyMetadata(key) {
  const normalized = String(key || "").toLowerCase();
  if (/^first_contact\.terminal\.category\.[^.]+\.descriptor$/.test(normalized)) {
    return { domain: "first_contact.model", surface: "internal", screenId: "" };
  }
  if (normalized.startsWith("ui.title.")) return { domain: "ui.title", surface: "menu", screenId: "title.main" };
  if (normalized.startsWith("ui.settings.")) return { domain: "ui.settings", surface: "menu", screenId: "settings" };
  if (normalized.startsWith("ui.day1.")) return { domain: "ui.day1", surface: "hud", screenId: "day1.hud" };
  if (normalized.startsWith("first_contact.placeholder.")) return { domain: "first_contact.intro", surface: "caption", screenId: "first_contact.intro" };
  if (normalized.startsWith("first_contact.terminal.semantic_map.")) return { domain: "first_contact.terminal", surface: "terminal", screenId: "first_contact.semantic_map" };
  if (normalized.startsWith("first_contact.terminal.reason.") || normalized.startsWith("first_contact.terminal.status.")) {
    return { domain: "first_contact.terminal", surface: "system", screenId: "first_contact.feedback" };
  }
  if (normalized.startsWith("first_contact.terminal.")) return { domain: "first_contact.terminal", surface: "terminal", screenId: "first_contact.terminal" };
  if (normalized.startsWith("speaker.") || normalized.startsWith("dialogue.")) return { domain: "dialogue", surface: "dialogue", screenId: "dialogue.shared" };
  return { domain: normalized.split(".").slice(0, 2).join(".") || "system", surface: "system", screenId: "system" };
}

export function inferCopyAudience(key) {
  return /^first_contact\.terminal\.category\.[^.]+\.descriptor$/i.test(String(key || ""))
    ? "internal"
    : "player";
}

function humanizeKey(key) {
  return String(key || "")
    .split(".")
    .slice(-3)
    .join(" · ")
    .replaceAll("_", " ");
}

export function validateCatalog(catalog, narrativeKeys = new Set()) {
  const issues = [];
  const push = (severity, message, key = "", field = "") => issues.push({ severity, message, key, field, source: "catalog" });
  if (!catalog || typeof catalog !== "object") return [{ severity: "error", message: "The UI copy catalog is not a JSON object.", source: "catalog" }];
  if (!String(catalog.catalogId || "").trim()) push("error", "catalogId is required.", "", "catalogId");
  if (!String(catalog.sourceLocale || "").trim()) push("error", "sourceLocale is required.", "", "sourceLocale");
  if (!Array.isArray(catalog.entries)) push("error", "entries must be an array.", "", "entries");

  const keys = new Set();
  const screenIds = new Set((catalog.screens || []).map((screen) => String(screen.id || "").trim()));
  const targetLocales = (catalog.locales || []).filter((locale) => locale !== catalog.sourceLocale);
  for (const entry of catalog.entries || []) {
    const key = String(entry?.key || "").trim();
    if (!key) push("error", "A UI copy entry has no key.", "", "key");
    else if (keys.has(key.toLowerCase())) push("error", `Duplicate localization key: ${key}`, key, "key");
    else keys.add(key.toLowerCase());
    if (narrativeKeys.has(key.toLowerCase())) push("error", `${key} is owned by a dialogue beat. Edit it in Dialogue Flow instead.`, key, "key");
    if (!String(entry?.sourceText || "").trim()) push("warning", "Source text is empty.", key, "sourceText");
    if (entry?.screenId && !screenIds.has(entry.screenId)) push("warning", `Unknown screen: ${entry.screenId}`, key, "screenId");
    if (entry?.audience && !["player", "internal"].includes(entry.audience)) push("error", `Unknown copy audience: ${entry.audience}`, key, "audience");

    const sourceVariables = placeholders(entry?.sourceText);
    for (const locale of targetLocales) {
      const translated = (entry?.localizedTexts || []).find((item) => item.locale === locale);
      if (!translated || !String(translated.text || "").trim()) {
        push("warning", `Missing ${locale} translation.`, key, `localizedTexts.${locale}`);
        continue;
      }
      const translatedVariables = placeholders(translated.text);
      if (sourceVariables.join("|") !== translatedVariables.join("|")) {
        push("error", `${locale} placeholders do not match source.`, key, `localizedTexts.${locale}`);
      }
    }

    if (String(entry?.domain || "").startsWith("first_contact.terminal")) {
      const allText = [entry.sourceText, ...(entry.localizedTexts || []).map((item) => item.text)].join("\n");
      if (/\bSELECT-ONE\b|\bTOKEN\b/i.test(allText)) push("error", "First Contact terminal copy exposes an internal term (SELECT-ONE or TOKEN).", key, "sourceText");
      if (allText.includes("탐침")) push("warning", "First Contact uses ‘표본’ instead of ‘탐침’.", key, "localizedTexts");
    }
  }

  const termIds = new Set();
  for (const item of catalog.terms || []) {
    const id = String(item?.id || "").trim();
    if (!id) push("error", "A glossary term has no id.", "", "terms");
    else if (termIds.has(id.toLowerCase())) push("error", `Duplicate glossary id: ${id}`, id, "terms");
    else termIds.add(id.toLowerCase());
  }
  return issues;
}
