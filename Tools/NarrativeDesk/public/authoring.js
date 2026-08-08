const slug = (value = "") => String(value)
  .trim()
  .toLowerCase()
  .replace(/[^a-z0-9]+/g, "_")
  .replace(/^_+|_+$/g, "");

const roundTenth = (value) => Math.round(value * 10) / 10;

export function estimateDialogueSeconds(sourceText = "", localizedTexts = []) {
  const source = String(sourceText).trim();
  const words = source ? source.split(/\s+/).length : 0;
  const sourceSeconds = words / 2.7 + 0.8;
  const translatedSeconds = (localizedTexts || []).reduce((longest, item) => {
    const compact = String(item?.text || "").replace(/\s+/g, "");
    return Math.max(longest, compact.length / 7.5 + 0.8);
  }, 0);
  return roundTenth(Math.min(12, Math.max(1.8, sourceSeconds, translatedSeconds)));
}

export function speakerPresets(document) {
  const presets = new Map();
  for (const beat of document?.beats || []) {
    const id = String(beat?.speakerId || "").trim();
    const fallback = String(beat?.speakerFallback || "").trim();
    const localizationKey = String(beat?.speakerLocalizationKey || "").trim();
    if (!id && !fallback) continue;
    const key = id || fallback.toLowerCase();
    if (!presets.has(key)) presets.set(key, { id, fallback, localizationKey });
  }
  return [...presets.values()].sort((left, right) =>
    (left.fallback || left.id).localeCompare(right.fallback || right.id));
}

function nextSerial(document) {
  document.authoring ||= {};
  let serial = Number(document.authoring.nextBeatSerial);
  if (!Number.isSafeInteger(serial) || serial < 1) {
    serial = (document.beats?.length || 0) + 1;
  }
  document.authoring.nextBeatSerial = serial + 1;
  return serial;
}

function identityScope(anchor, sectionId) {
  const trigger = String(anchor?.triggerEvent || "").trim();
  const triggerParts = trigger.split(".").filter(Boolean);
  if (triggerParts[0]?.toLowerCase() === "intro") triggerParts.shift();
  return slug(triggerParts.join("_")) || slug(sectionId) || "narrative";
}

function localizationScope(document, anchor, sectionId) {
  const scenarioRoot = slug(document?.scenarioId || "narrative").replace(/_day\d+$/, "") || "narrative";
  const trigger = String(anchor?.triggerEvent || "").trim().toLowerCase().replace(/[^a-z0-9.]+/g, ".").replace(/^\.+|\.+$/g, "");
  return trigger ? `${scenarioRoot}.${trigger}` : `${scenarioRoot}.narrative.${slug(sectionId) || "unassigned"}`;
}

export function allocateBeatIdentity(document, anchor, sectionId) {
  const ids = new Set((document?.beats || []).map((beat) => String(beat?.id || "").toLowerCase()));
  const keys = new Set((document?.beats || []).map((beat) => String(beat?.localizationKey || "").toLowerCase()));
  const scope = identityScope(anchor, sectionId);
  const keyScope = localizationScope(document, anchor, sectionId);

  while (true) {
    const serial = String(nextSerial(document)).padStart(4, "0");
    const id = `${scope}_line_${serial}`;
    const localizationKey = `${keyScope}.line_${serial}`;
    if (!ids.has(id.toLowerCase()) && !keys.has(localizationKey.toLowerCase())) {
      return { id, localizationKey };
    }
  }
}

function insertionOrder(document, anchor, sectionId) {
  const siblings = (document?.beats || [])
    .filter((beat) => beat.sectionId === sectionId)
    .sort((left, right) => Number(left.order || 0) - Number(right.order || 0));

  if (!siblings.length) return 0;
  if (!anchor || anchor.sectionId !== sectionId) return Number(siblings.at(-1).order || 0) + 10;

  let index = siblings.indexOf(anchor);
  const next = siblings[index + 1];
  if (!next) return Number(anchor.order || 0) + 10;

  const gap = Number(next.order || 0) - Number(anchor.order || 0);
  if (gap >= 2) return Number(anchor.order || 0) + Math.floor(gap / 2);

  siblings.forEach((beat, siblingIndex) => { beat.order = siblingIndex * 10; });
  index = siblings.indexOf(anchor);
  return index * 10 + 5;
}

export function createDialogueBeat(document, options = {}) {
  const beats = document?.beats || [];
  let anchor = beats.find((beat) => beat.id === options.anchorBeatId) || null;
  const requestedSection = options.sectionId || anchor?.sectionId || document?.sections?.[0]?.id || "";
  if (!anchor) {
    anchor = beats
      .filter((beat) => beat.sectionId === requestedSection)
      .sort((left, right) => Number(left.order || 0) - Number(right.order || 0))
      .at(-1) || null;
  }

  const sectionId = anchor?.sectionId || requestedSection;
  const identity = allocateBeatIdentity(document, anchor, sectionId);
  const sourceText = String(options.sourceText || "");
  const localizedTexts = (document?.locales || [])
    .filter((locale) => locale !== document.sourceLocale)
    .map((locale) => ({
      locale,
      text: String(options.translations?.[locale] || ""),
      status: "draft",
    }));
  const speaker = options.speaker || {};
  const automaticSeconds = estimateDialogueSeconds(sourceText, localizedTexts);

  return {
    id: identity.id,
    sectionId,
    order: insertionOrder(document, anchor, sectionId),
    enabled: true,
    type: "dialogue",
    status: "draft",
    runtimeCue: "",
    briefingLookTarget: Number(anchor?.briefingLookTarget || 0),
    triggerEvent: String(anchor?.triggerEvent || ""),
    condition: "",
    repeat: String(anchor?.repeat || "once"),
    speakerId: String(speaker.id || ""),
    speakerLocalizationKey: String(speaker.localizationKey || ""),
    speakerFallback: String(speaker.fallback || ""),
    localizationKey: identity.localizationKey,
    sourceText,
    advance: String(anchor?.advance || "automatic"),
    minimumSeconds: options.automaticDuration === false
      ? Math.max(0.1, Number(options.minimumSeconds || automaticSeconds))
      : automaticSeconds,
    situation: "",
    beforeAction: "",
    afterAction: "",
    stageDirection: "",
    tags: [],
    localizedTexts,
  };
}

export function duplicateNarrativeBeat(document, sourceBeat) {
  if (!sourceBeat) return null;
  const copy = structuredClone(sourceBeat);
  const identity = allocateBeatIdentity(document, sourceBeat, sourceBeat.sectionId);
  copy.id = identity.id;
  copy.localizationKey = identity.localizationKey;
  copy.order = insertionOrder(document, sourceBeat, sourceBeat.sectionId);
  copy.status = "draft";
  return copy;
}

export function playbackConnection(beat) {
  if (String(beat?.triggerEvent || "").trim()) {
    return { state: "connected", label: "게임 재생 연결됨", detail: `발생 이벤트 · ${beat.triggerEvent}` };
  }
  if (String(beat?.runtimeCue || "").trim()) {
    return { state: "direct", label: "직접 실행 큐 연결", detail: `실행 큐 · ${beat.runtimeCue}` };
  }
  return { state: "unconnected", label: "게임에서 재생되지 않을 수 있음", detail: "발생 이벤트나 실행 큐가 없습니다." };
}
