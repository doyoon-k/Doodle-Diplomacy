import {
  createDialogueBeat,
  duplicateNarrativeBeat,
  estimateDialogueSeconds,
  playbackConnection,
  speakerPresets,
} from "./authoring.js";

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

const state = {
  project: null,
  document: null,
  catalog: null,
  scenarioIssues: [],
  catalogIssues: [],
  catalogUsage: { byKey: {}, unusedKeys: [], missingKeys: [] },
  newsTiming: [],
  mode: "dialogue",
  selectedBeatId: "",
  selectedCopyKey: "",
  selectedTermId: "",
  selectedGroupId: "",
  dirtyScenario: false,
  dirtyCatalog: false,
  saving: false,
  websocket: null,
  unityOnline: false,
  traces: [],
  latestNarrativeTrace: null,
  latestUiTrace: null,
  activeTraceBeatId: "",
  activeUiKeys: new Set(),
  filterLiveUi: false,
  saveTimer: null,
};

const placeholders = (text = "") => [...new Set([...String(text).matchAll(/\{([A-Za-z_][A-Za-z0-9_]*)\}/g)].map((match) => match[1]))];
const escapeHtml = (text = "") => String(text).replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
const isPlayerFacingCopy = (entry) => Boolean(entry) && entry.audience !== "internal";
const playerFacingCopies = () => state.catalog?.entries.filter(isPlayerFacingCopy) || [];
const currentBeat = () => state.document?.beats.find((beat) => beat.id === state.selectedBeatId) || null;
const currentCopy = () => playerFacingCopies().find((entry) => entry.key === state.selectedCopyKey) || null;
const currentTerm = () => state.catalog?.terms.find((term) => term.id === state.selectedTermId) || null;
const sectionById = (id) => state.document?.sections.find((section) => section.id === id);
const screenById = (id) => state.catalog?.screens.find((screen) => screen.id === id);
const translationFor = (entry, locale, create = false) => {
  let translation = entry?.localizedTexts?.find((item) => item.locale === locale);
  if (!translation && create && entry) {
    entry.localizedTexts ||= [];
    translation = { locale, text: "", status: "draft" };
    entry.localizedTexts.push(translation);
  }
  return translation;
};
const localizedText = (entry, locale, sourceLocale) => {
  if (!entry) return "";
  return locale === sourceLocale ? entry.sourceText || "" : translationFor(entry, locale)?.text || entry.sourceText || "";
};
const allIssues = () => [
  ...state.scenarioIssues.map((issue) => ({ source: "scenario", ...issue })),
  ...state.catalogIssues
    .filter((issue) => {
      if (!issue.key) return true;
      const entry = state.catalog?.entries.find((item) => item.key === issue.key);
      return !entry || isPlayerFacingCopy(entry);
    })
    .map((issue) => ({ source: "catalog", ...issue })),
];

async function api(url, options = {}) {
  const response = await fetch(url, { headers: { "Content-Type": "application/json", ...(options.headers || {}) }, ...options });
  const payload = await response.json();
  if (!response.ok) {
    const error = new Error(payload.error || `HTTP ${response.status}`);
    error.payload = payload;
    throw error;
  }
  return payload;
}

async function loadProject() {
  state.project = await api("/api/project");
  const select = $("#scenarioSelect");
  select.innerHTML = state.project.scenarios.map((scenario) =>
    `<option value="${escapeHtml(scenario.scenarioId)}">${escapeHtml(scenario.title || scenario.scenarioId)}</option>`).join("");
  if (!state.project.scenarios.length) throw new Error("Assets/Narrative에 시나리오가 없습니다.");

  const catalogPayload = await api("/api/localization/catalog");
  state.catalog = catalogPayload.document;
  state.catalogIssues = catalogPayload.issues || [];
  state.catalogUsage = catalogPayload.usage || state.catalogUsage;
  state.selectedCopyKey = playerFacingCopies()[0]?.key || "";
  state.selectedTermId = state.catalog.terms[0]?.id || "";
  state.newsTiming = await api("/api/media/news")
    .then((payload) => payload.items || [])
    .catch(() => []);
  await loadScenario(state.project.scenarios[0].scenarioId, false);

  const params = new URLSearchParams(location.search);
  const requestedMode = ["dialogue", "copy", "terms"].includes(params.get("mode")) ? params.get("mode") : "";
  const requestedKey = params.get("key");
  const requestedBeat = params.get("beat");
  const requestedTerm = params.get("term");
  const matchingBeat = state.document.beats.find((beat) => beat.id === requestedBeat || beat.localizationKey === requestedKey);
  const matchingCopy = playerFacingCopies().find((entry) => entry.key === requestedKey);
  const internalCopyRequested = state.catalog.entries.some((entry) => entry.key === requestedKey && !isPlayerFacingCopy(entry));
  const matchingTerm = state.catalog.terms.find((term) => term.id === requestedTerm);
  if (matchingBeat) state.selectedBeatId = matchingBeat.id;
  if (matchingCopy) state.selectedCopyKey = matchingCopy.key;
  if (matchingTerm) state.selectedTermId = matchingTerm.id;
  state.mode = requestedMode || (matchingCopy ? "copy" : matchingTerm ? "terms" : "dialogue");
  configureFilters();
  renderAll();
  if (internalCopyRequested) updateUrlForSelection();
  setSaveStatus("동기화됨");
}

async function loadScenario(scenarioId, render = true) {
  if ((state.dirtyScenario || state.dirtyCatalog) && !confirm("저장하지 않은 변경을 버리고 다른 시나리오를 여시겠습니까?")) return;
  const payload = await api(`/api/scenarios/${encodeURIComponent(scenarioId)}`);
  state.document = payload.document;
  state.scenarioIssues = payload.issues || [];
  state.dirtyScenario = false;
  state.selectedBeatId = state.document.beats[0]?.id || "";
  state.selectedGroupId = "";
  $("#scenarioSelect").value = scenarioId;
  if (render) renderAll();
}

function setMode(mode, updateUrl = true) {
  if (!["dialogue", "copy", "terms"].includes(mode)) return;
  state.mode = mode;
  state.selectedGroupId = "";
  if (mode !== "copy") state.filterLiveUi = false;
  configureFilters();
  renderAll();
  if (updateUrl) updateUrlForSelection();
}

function updateUrlForSelection() {
  const params = new URLSearchParams();
  params.set("mode", state.mode);
  if (state.mode === "dialogue" && state.selectedBeatId) params.set("beat", state.selectedBeatId);
  if (state.mode === "copy" && state.selectedCopyKey) params.set("key", state.selectedCopyKey);
  if (state.mode === "terms" && state.selectedTermId) params.set("term", state.selectedTermId);
  history.replaceState(null, "", `?${params}`);
}

function renderAll() {
  if (!state.document || !state.catalog) return;
  renderModeChrome();
  renderSidebar();
  renderList();
  renderEditor();
  renderIssueCount();
  renderLiveBanners();
}

function renderModeChrome() {
  $$('[data-mode]').forEach((button) => button.classList.toggle("active", button.dataset.mode === state.mode));
  const dialogue = state.mode === "dialogue";
  $("#scenarioSelect").classList.toggle("hidden", !dialogue);
  $("#catalogIdentity").classList.toggle("hidden", dialogue);
  $("#catalogIdentity").textContent = state.mode === "copy" ? state.catalog.title : "First Contact Terminology";
  $("#sourceEyebrow").textContent = dialogue ? "SCENARIO" : state.mode === "copy" ? "COPY CATALOG" : "GLOSSARY";
  $("#sectionEyebrow").textContent = dialogue ? "FLOW" : state.mode === "copy" ? "SCREENS" : "STATUS";
  $("#showAllButton").textContent = dialogue ? "전체 비트 보기" : state.mode === "copy" ? "전체 문구 보기" : "전체 용어 보기";
  $("#syncDescription").textContent = dialogue
    ? "저장하면 Unity가 실행용 시나리오와 대사 번역을 자동 갱신합니다."
    : state.mode === "copy"
      ? "저장하면 Unity 번역표가 자동 갱신됩니다. 화면 미리보기는 구성 확인용입니다."
      : "용어집은 문구를 쓸 때의 기준입니다. 번역표에는 직접 출력되지 않습니다.";
  $(".checkpoint-actions").classList.toggle("hidden", !dialogue);
  $("#addBeatButton").title = dialogue ? "새 대사" : state.mode === "copy" ? "새 UI 문구" : "새 용어";
}

function configureFilters() {
  const previousType = $("#typeFilter").value;
  const previousStatus = $("#statusFilter").value;
  const typeOptions = state.mode === "dialogue"
    ? ["dialogue", "caption", "reactive", "reaction", "system"]
    : state.mode === "copy"
      ? ["terminal", "menu", "hud", "dialogue", "caption", "system"]
      : [];
  $("#typeFilter").innerHTML = `<option value="">${state.mode === "copy" ? "모든 표시 방식" : state.mode === "terms" ? "모든 유형" : "모든 유형"}</option>` +
    typeOptions.map((value) => `<option>${value}</option>`).join("");
  $("#typeFilter").value = typeOptions.includes(previousType) ? previousType : "";
  const statuses = state.mode === "dialogue" ? ["draft", "review", "final", "placeholder"] : state.mode === "copy" ? ["draft", "review", "final", "unused"] : ["draft", "review", "final"];
  $("#statusFilter").innerHTML = '<option value="">모든 상태</option>' + statuses.map((value) => `<option>${value}</option>`).join("");
  $("#statusFilter").value = statuses.includes(previousStatus) ? previousStatus : "";
  $("#typeFilter").classList.toggle("hidden", state.mode === "terms");
  $("#searchInput").placeholder = state.mode === "dialogue" ? "대사, 키, 상황 검색…" : state.mode === "copy" ? "문구, 키, 화면, 맥락 검색…" : "용어, 뜻, 사용 규칙 검색…";
}

function renderSidebar() {
  if (state.mode === "dialogue") {
    const errors = state.scenarioIssues.filter((issue) => issue.severity === "error").length;
    const warnings = state.scenarioIssues.filter((issue) => issue.severity === "warning").length;
    $("#scenarioMeta").innerHTML = `${state.document.beats.length} beats · ${state.document.locales.join(" / ")}<br>${errors ? `${errors} errors · ` : ""}${warnings} warnings`;
    $("#sectionList").innerHTML = [...state.document.sections].sort((a, b) => a.order - b.order).map((section) => {
      const count = state.document.beats.filter((beat) => beat.sectionId === section.id).length;
      return sidebarButton(section.id, section.title, count, section.summary);
    }).join("");
  } else if (state.mode === "copy") {
    const visibleIssues = allIssues().filter((issue) => issue.source === "catalog");
    const errors = visibleIssues.filter((issue) => issue.severity === "error").length;
    const warnings = visibleIssues.filter((issue) => issue.severity === "warning").length;
    const visibleEntries = playerFacingCopies();
    const unused = visibleEntries.filter((entry) => (state.catalogUsage.byKey?.[entry.key]?.count || 0) === 0).length;
    $("#scenarioMeta").innerHTML = `${visibleEntries.length} strings · ${state.catalog.locales.join(" / ")}<br>${errors ? `${errors} errors · ` : ""}${warnings} warnings · ${unused} unused`;
    $("#sectionList").innerHTML = state.catalog.screens.filter((screen) => visibleEntries.some((entry) => entry.screenId === screen.id)).map((screen) => {
      const count = visibleEntries.filter((entry) => entry.screenId === screen.id).length;
      return sidebarButton(screen.id, screen.title, count, screen.description);
    }).join("");
  } else {
    $("#scenarioMeta").innerHTML = `${state.catalog.terms.length} terms · EN / KO<br>권장어와 금지 규칙`;
    const statuses = ["final", "review", "draft"];
    $("#sectionList").innerHTML = statuses.map((status) => sidebarButton(status, status.toUpperCase(), state.catalog.terms.filter((term) => term.status === status).length, `${status} 상태의 용어`)).join("");
  }

  $$('[data-group]').forEach((button) => button.addEventListener("click", () => {
    state.selectedGroupId = button.dataset.group;
    state.filterLiveUi = false;
    renderSidebar();
    renderList();
    renderLiveBanners();
  }));
}

function sidebarButton(id, title, count, summary) {
  return `<button class="section-button ${state.selectedGroupId === id ? "active" : ""}" data-group="${escapeHtml(id)}">
    <span>${escapeHtml(title)}</span><span class="section-count">${count}</span><small>${escapeHtml(summary || "")}</small>
  </button>`;
}

function renderList() {
  if (state.mode === "dialogue") renderBeatList();
  else if (state.mode === "copy") renderCopyList();
  else renderTermList();
}

function filteredBeats() {
  const search = $("#searchInput").value.trim().toLowerCase();
  const type = $("#typeFilter").value;
  const status = $("#statusFilter").value;
  return [...state.document.beats]
    .filter((beat) => !state.selectedGroupId || beat.sectionId === state.selectedGroupId)
    .filter((beat) => !type || beat.type === type)
    .filter((beat) => !status || beat.status === status)
    .filter((beat) => !search || [beat.id, beat.localizationKey, beat.sourceText, beat.situation, beat.triggerEvent, beat.localizedTexts?.map((item) => item.text).join(" ")].join(" ").toLowerCase().includes(search))
    .sort((a, b) => (sectionById(a.sectionId)?.order || 0) - (sectionById(b.sectionId)?.order || 0) || a.order - b.order);
}

function renderBeatList() {
  const beats = filteredBeats();
  $("#beatList").innerHTML = beats.length ? beats.map((beat) => {
    const connection = playbackConnection(beat);
    const speaker = beat.speakerFallback || (beat.type === "system" ? "무대 지시" : "화자 없음");
    return `
    <button class="beat-card ${beat.id === state.selectedBeatId ? "active" : ""} ${beat.id === state.activeTraceBeatId ? "trace" : ""} ${connection.state === "unconnected" ? "unconnected" : ""}" data-beat="${escapeHtml(beat.id)}">
      <span class="beat-top"><span class="beat-id">${escapeHtml(speaker)}</span><span class="badges"><i class="badge">${escapeHtml(beat.status)}</i>${connection.state === "unconnected" ? '<i class="badge warning">재생 미연결</i>' : ""}</span></span>
      <span class="beat-copy">${escapeHtml(localizedPreview(beat, state.document))}</span>
      <span class="beat-context">${escapeHtml(sectionById(beat.sectionId)?.title || "섹션 없음")} · ${escapeHtml(beat.triggerEvent || beat.id)}</span>
    </button>`;
  }).join("") : emptyList("표시할 비트가 없습니다");
  $$('[data-beat]').forEach((button) => button.addEventListener("click", () => selectBeat(button.dataset.beat)));
}

function filteredCopies() {
  const search = $("#searchInput").value.trim().toLowerCase();
  const surface = $("#typeFilter").value;
  const status = $("#statusFilter").value;
  return playerFacingCopies()
    .filter((entry) => !state.filterLiveUi || !state.activeUiKeys.size || state.activeUiKeys.has(entry.key))
    .filter((entry) => state.filterLiveUi || !state.selectedGroupId || entry.screenId === state.selectedGroupId)
    .filter((entry) => !surface || entry.surface === surface)
    .filter((entry) => !status || (status === "unused" ? (state.catalogUsage.byKey?.[entry.key]?.count || 0) === 0 : entry.status === status))
    .filter((entry) => !search || [entry.key, entry.sourceText, entry.context, entry.domain, entry.screenId, entry.localizedTexts?.map((item) => item.text).join(" ")].join(" ").toLowerCase().includes(search))
    .sort((a, b) => (screenById(a.screenId)?.title || a.screenId).localeCompare(screenById(b.screenId)?.title || b.screenId) || a.key.localeCompare(b.key));
}

function renderCopyList() {
  const entries = filteredCopies();
  $("#beatList").innerHTML = entries.length ? entries.map((entry) => `
    <button class="beat-card ${entry.key === state.selectedCopyKey ? "active" : ""} ${state.activeUiKeys.has(entry.key) ? "live-ui" : ""}" data-copy="${escapeHtml(entry.key)}">
      <span class="beat-top"><span class="beat-id copy-key">${escapeHtml(entry.key)}</span><span class="badges"><i class="badge">${escapeHtml(entry.surface)}</i><i class="badge">${escapeHtml(entry.status)}</i>${(state.catalogUsage.byKey?.[entry.key]?.count || 0) === 0 ? '<i class="badge">unused</i>' : ""}</span></span>
      <span class="beat-copy">${escapeHtml(localizedPreview(entry, state.catalog))}</span>
      <span class="beat-context">${escapeHtml(screenById(entry.screenId)?.title || entry.context || entry.screenId || "화면 미지정")}</span>
    </button>`).join("") : emptyList(state.filterLiveUi ? "현재 화면에 대응하는 문구가 없습니다" : "표시할 UI 문구가 없습니다");
  $$('[data-copy]').forEach((button) => button.addEventListener("click", () => selectCopy(button.dataset.copy)));
}

function filteredTerms() {
  const search = $("#searchInput").value.trim().toLowerCase();
  const status = $("#statusFilter").value;
  return [...state.catalog.terms]
    .filter((term) => !state.selectedGroupId || term.status === state.selectedGroupId)
    .filter((term) => !status || term.status === status)
    .filter((term) => !search || [term.id, term.sourceTerm, term.targetTerm, term.definition, term.notes].join(" ").toLowerCase().includes(search))
    .sort((a, b) => a.sourceTerm.localeCompare(b.sourceTerm));
}

function renderTermList() {
  const terms = filteredTerms();
  $("#beatList").innerHTML = terms.length ? terms.map((term) => `
    <button class="beat-card ${term.id === state.selectedTermId ? "active" : ""}" data-term="${escapeHtml(term.id)}">
      <span class="beat-top"><span class="beat-id">${escapeHtml(term.id)}</span><span class="badges"><i class="badge">${escapeHtml(term.status)}</i></span></span>
      <span class="beat-copy">${escapeHtml(term.sourceTerm)} <b>→</b> ${escapeHtml(term.targetTerm)}</span>
      <span class="beat-context">${escapeHtml(term.definition || "정의 없음")}</span>
    </button>`).join("") : emptyList("표시할 용어가 없습니다");
  $$('[data-term]').forEach((button) => button.addEventListener("click", () => selectTerm(button.dataset.term)));
}

function localizedPreview(entry, owner) {
  const target = owner.locales.find((locale) => locale !== owner.sourceLocale);
  return translationFor(entry, target)?.text || entry.sourceText || "(빈 문구)";
}

function emptyList(message) {
  return `<div class="empty-state"><strong>${escapeHtml(message)}</strong><p>검색어나 필터를 지워 보세요.</p></div>`;
}

function selectBeat(id, updateUrl = true) {
  state.selectedBeatId = id;
  renderList(); renderEditor();
  if (updateUrl) updateUrlForSelection();
}

function selectCopy(key, updateUrl = true) {
  state.selectedCopyKey = key;
  renderList(); renderEditor();
  if (updateUrl) updateUrlForSelection();
}

function selectTerm(id, updateUrl = true) {
  state.selectedTermId = id;
  renderList(); renderEditor();
  if (updateUrl) updateUrlForSelection();
}

function renderEditor() {
  const beat = state.mode === "dialogue" ? currentBeat() : null;
  const copy = state.mode === "copy" ? currentCopy() : null;
  const term = state.mode === "terms" ? currentTerm() : null;
  $("#emptyEditor").classList.toggle("hidden", Boolean(beat || copy || term));
  $("#beatEditor").classList.toggle("hidden", !beat);
  $("#copyEditor").classList.toggle("hidden", !copy);
  $("#termEditor").classList.toggle("hidden", !term);
  if (!beat && !copy && !term) {
    $("#emptyEditor strong").textContent = state.mode === "dialogue" ? "비트를 선택하세요" : state.mode === "copy" ? "UI 문구를 선택하세요" : "용어를 선택하세요";
    $("#emptyEditor p").textContent = state.mode === "dialogue"
      ? "대사와 번역을 편집하거나, 선택한 위치 다음에 새 대사를 추가할 수 있습니다."
      : state.mode === "copy"
        ? "사용 화면, 맥락, 원문과 번역을 한 화면에서 편집할 수 있습니다."
        : "권장 번역과 사용 규칙을 정리할 수 있습니다.";
  }
  if (beat) renderBeatEditor(beat);
  if (copy) renderCopyEditor(copy);
  if (term) renderTermEditor(term);
}

function renderBeatEditor(beat) {
  renderBeatHeading(beat);
  $("#sectionField").innerHTML = state.document.sections.map((section) => `<option value="${escapeHtml(section.id)}">${escapeHtml(section.title)}</option>`).join("");
  $$('[data-field]').forEach((field) => {
    const name = field.dataset.field;
    field.value = name === "tags" ? (beat.tags || []).join(", ") : beat[name] ?? "";
  });
  $("#briefingLookTargetField").classList.toggle(
    "hidden",
    beat.sectionId !== "facility_briefing");
  renderSpeakerEditor(beat);
  renderPlaybackConnection(beat);
  renderMediaTiming(beat);
  renderLocaleEditors(beat, state.document, "#localeEditors", "#placeholderChips", renderBeatPreview, renderBeatList);
  populateLocaleSelect("#previewLocale", state.document);
  renderBeatPreview(beat);
}

function renderBeatHeading(beat) {
  $("#beatTitle").textContent = beat.speakerFallback || (beat.type === "system" ? "무대 지시" : "화자 없음");
  $("#beatTitleMeta").textContent = `${beat.id} · ${sectionById(beat.sectionId)?.title || beat.sectionId || "섹션 없음"}`;
}

function speakerOptionValue(preset) {
  return preset.id ? `id:${preset.id}` : `fallback:${preset.fallback}`;
}

function populateSpeakerSelect(select, speaker = {}, preferFirst = false) {
  const presets = speakerPresets(state.document);
  select.innerHTML = '<option value="__none__">화자 없음</option>' + presets.map((preset) =>
    `<option value="${escapeHtml(speakerOptionValue(preset))}" data-speaker-id="${escapeHtml(preset.id)}" data-speaker-fallback="${escapeHtml(preset.fallback)}" data-speaker-key="${escapeHtml(preset.localizationKey)}">${escapeHtml(preset.fallback || preset.id)}</option>`).join("") +
    '<option value="__custom__">＋ 새 화자…</option>';

  const matching = presets.find((preset) =>
    preset.id === String(speaker.id || "") &&
    preset.fallback === String(speaker.fallback || "") &&
    preset.localizationKey === String(speaker.localizationKey || ""));
  if (matching) select.value = speakerOptionValue(matching);
  else if (speaker.id || speaker.fallback) select.value = "__custom__";
  else if (preferFirst && presets.length) select.value = speakerOptionValue(presets.find((preset) => preset.id === "president") || presets[0]);
  else select.value = "__none__";
}

function selectedSpeaker(select, customIdSelector, customFallbackSelector) {
  if (select.value === "__none__") return { id: "", fallback: "", localizationKey: "" };
  if (select.value === "__custom__") {
    return {
      id: $(customIdSelector).value.trim(),
      fallback: $(customFallbackSelector).value.trim(),
      localizationKey: "",
    };
  }
  const option = select.selectedOptions[0];
  return {
    id: option?.dataset.speakerId || "",
    fallback: option?.dataset.speakerFallback || "",
    localizationKey: option?.dataset.speakerKey || "",
  };
}

function renderSpeakerEditor(beat) {
  const select = $("#speakerPreset");
  populateSpeakerSelect(select, {
    id: beat.speakerId,
    fallback: beat.speakerFallback,
    localizationKey: beat.speakerLocalizationKey,
  });
  const custom = select.value === "__custom__";
  $("#customSpeakerFields").classList.toggle("hidden", !custom);
  $("#customSpeakerId").value = custom ? beat.speakerId || "" : "";
  $("#customSpeakerFallback").value = custom ? beat.speakerFallback || "" : "";
}

function renderPlaybackConnection(beat) {
  const connection = playbackConnection(beat);
  const element = $("#playbackConnection");
  element.className = `playback-connection ${connection.state}`;
  element.innerHTML = `<b>${escapeHtml(connection.label)}</b><span>${escapeHtml(connection.detail)}</span>`;
}

function mediaTimingForBeat(beat) {
  return state.newsTiming.find((item) => item.triggerEvent === beat?.triggerEvent) || null;
}

function formatSeconds(value) {
  return `${Number(value || 0).toFixed(1)}초`;
}

function renderMediaTiming(beat) {
  const element = $("#mediaTiming");
  const media = mediaTimingForBeat(beat);
  if (!media) {
    element.className = "media-timing hidden";
    element.innerHTML = "";
    return;
  }

  const beats = state.document.beats
    .filter((candidate) => candidate.enabled !== false && candidate.triggerEvent === media.triggerEvent)
    .sort((left, right) => Number(left.order || 0) - Number(right.order || 0));
  const allocatedSeconds = beats.reduce((total, candidate) =>
    total + Math.max(0.1, Number(candidate.minimumSeconds || 0)), 0);
  const selectedIndex = beats.indexOf(beat);
  const selectedStart = beats.slice(0, Math.max(0, selectedIndex)).reduce((total, candidate) =>
    total + Math.max(0.1, Number(candidate.minimumSeconds || 0)), 0);
  const selectedEnd = selectedStart + Math.max(0.1, Number(beat.minimumSeconds || 0));
  const remaining = media.playbackSeconds - allocatedSeconds;
  const over = remaining < -0.01;
  const near = !over && remaining < 0.75;
  const denominator = Math.max(media.playbackSeconds, allocatedSeconds, 0.1);
  const segments = beats.map((candidate) => {
    const width = Math.max(0.5, Math.max(0.1, Number(candidate.minimumSeconds || 0)) / denominator * 100);
    return `<span class="timing-segment ${candidate === beat ? "selected" : ""}" style="width:${width.toFixed(2)}%" title="${escapeHtml(candidate.speakerFallback || candidate.id)} · ${formatSeconds(candidate.minimumSeconds)}"></span>`;
  }).join("");
  const typeLabel = media.mediaType === "still" ? "스틸 이미지" : "영상";
  const remainingLabel = over ? `${formatSeconds(Math.abs(remaining))} 초과` : `${formatSeconds(remaining)} 남음`;
  const capNote = media.capped
    ? `원본 ${formatSeconds(media.sourceSeconds)} · 게임에서는 최대 12.0초만 재생`
    : `${typeLabel} 전체 길이`;

  element.className = `media-timing ${over ? "over" : near ? "near" : ""}`;
  element.innerHTML = `
    <div class="media-timing-heading"><strong>${escapeHtml(media.assetName)}</strong><span>${escapeHtml(capNote)}</span></div>
    <div class="timing-metrics">
      <div class="timing-metric"><small>게임 재생 길이</small><b>${formatSeconds(media.playbackSeconds)}</b></div>
      <div class="timing-metric"><small>전체 대사 시간</small><b>${formatSeconds(allocatedSeconds)}</b></div>
      <div class="timing-metric remaining"><small>시간 여유</small><b>${remainingLabel}</b></div>
    </div>
    <div class="timing-track">${segments}</div>
    <div class="timing-detail"><span>이 대사 <b>${formatSeconds(selectedStart)}–${formatSeconds(selectedEnd)}</b></span><span>${beats.length}개 대사</span></div>`;
}

function applySpeakerToBeat(beat, speaker) {
  beat.speakerId = speaker.id;
  beat.speakerFallback = speaker.fallback;
  beat.speakerLocalizationKey = speaker.localizationKey;
  for (const name of ["speakerId", "speakerFallback", "speakerLocalizationKey"]) {
    const field = $(`[data-field="${name}"]`);
    if (field) field.value = beat[name] || "";
  }
  markDirty("scenario");
  renderBeatHeading(beat);
  renderBeatPreview(beat);
  renderBeatList();
}

function renderCopyEditor(entry) {
  $("#copyTitle").textContent = entry.key;
  $("#copyScreenField").innerHTML = state.catalog.screens.map((screen) => `<option value="${escapeHtml(screen.id)}">${escapeHtml(screen.title)}</option>`).join("");
  $$('[data-copy-field]').forEach((field) => {
    const name = field.dataset.copyField;
    field.value = name === "tags" ? (entry.tags || []).join(", ") : entry[name] ?? "";
  });
  const usage = state.catalogUsage.byKey?.[entry.key] || { count: 0, references: [] };
  $("#copyUsage").className = `usage-strip ${usage.count ? "used" : "unused"}`;
  $("#copyUsage").textContent = usage.count
    ? `사용 위치 ${usage.count}곳\n${usage.references.slice(0, 6).join("\n")}`
    : "사용 위치를 찾지 못했습니다. 삭제 전 동적 키나 에셋 참조인지 확인하세요.";
  renderLocaleEditors(entry, state.catalog, "#copyLocaleEditors", "#copyPlaceholderChips", renderCopyPreview, renderCopyList);
  populateLocaleSelect("#copyPreviewLocale", state.catalog);
  renderCopyPreview(entry);
}

function renderLocaleEditors(entry, owner, containerSelector, chipsSelector, previewCallback, listCallback) {
  $(containerSelector).innerHTML = owner.locales.map((locale) => {
    const source = locale === owner.sourceLocale;
    const value = source ? entry.sourceText || "" : translationFor(entry, locale)?.text || "";
    const status = source ? "source" : translationFor(entry, locale)?.status || "draft";
    return `<label class="locale-block"><strong>${escapeHtml(locale)} <em>${escapeHtml(status)}</em></strong><textarea data-owner-locale="${escapeHtml(locale)}">${escapeHtml(value)}</textarea></label>`;
  }).join("");
  $$(`${containerSelector} [data-owner-locale]`).forEach((textarea) => textarea.addEventListener("input", () => {
    const locale = textarea.dataset.ownerLocale;
    if (locale === owner.sourceLocale) entry.sourceText = textarea.value;
    else translationFor(entry, locale, true).text = textarea.value;
    markDirty(owner === state.document ? "scenario" : "catalog");
    renderPlaceholderChips(entry, chipsSelector);
    previewCallback(entry);
    listCallback();
  }));
  renderPlaceholderChips(entry, chipsSelector);
}

function populateLocaleSelect(selector, owner) {
  const select = $(selector);
  const previous = select.value;
  select.innerHTML = owner.locales.map((locale) => `<option>${escapeHtml(locale)}</option>`).join("");
  select.value = owner.locales.includes(previous) ? previous : owner.locales.at(-1);
}

function renderPlaceholderChips(entry, selector) {
  const variables = placeholders(entry.sourceText || "");
  $(selector).innerHTML = variables.length ? variables.map((value) => `<span class="chip">{${escapeHtml(value)}}</span>`).join("") : '<span class="chip">변수 없음</span>';
}

function sampleVariables(text) {
  return String(text || "").replace(/\{([^}]+)\}/g, (_, name) => ({ category: "위험", count: "03", required: "03", remaining: "01", group: "안정", label: "사과", meaning: "음식", signal: "[KRR]" })[name] || `[${name}]`);
}

function renderBeatPreview(beat = currentBeat()) {
  if (!beat) return;
  const locale = $("#previewLocale").value || state.document.sourceLocale;
  const text = sampleVariables(localizedText(beat, locale, state.document.sourceLocale));
  let speaker = beat.speakerFallback || "";
  const speakerEntry = state.catalog.entries.find((entry) => entry.key === beat.speakerLocalizationKey);
  if (speakerEntry) speaker = localizedText(speakerEntry, locale, state.catalog.sourceLocale) || speaker;
  $("#previewSpeaker").textContent = speaker;
  $("#previewText").textContent = text || "(표시할 텍스트 없음)";
}

function renderCopyPreview(entry = currentCopy()) {
  if (!entry) return;
  const locale = $("#copyPreviewLocale").value || state.catalog.sourceLocale;
  let neighbors = state.filterLiveUi && state.activeUiKeys.size
    ? playerFacingCopies().filter((item) => state.activeUiKeys.has(item.key))
    : playerFacingCopies().filter((item) => item.screenId === entry.screenId && item.surface === entry.surface);
  neighbors.sort((a, b) => a.key.localeCompare(b.key));
  const index = Math.max(0, neighbors.findIndex((item) => item.key === entry.key));
  const start = Math.max(0, Math.min(index - 3, neighbors.length - 7));
  neighbors = neighbors.slice(start, start + 7);
  if (!neighbors.some((item) => item.key === entry.key)) neighbors.unshift(entry);
  const preview = $("#uiCopyPreview");
  preview.className = `surface-preview ${escapeHtml(entry.surface || "system")}`;
  preview.innerHTML = neighbors.map((item) => `<button type="button" class="preview-copy-line ${item.key === entry.key ? "selected" : ""}" data-preview-key="${escapeHtml(item.key)}" title="${escapeHtml(item.key)}">${escapeHtml(sampleVariables(localizedText(item, locale, state.catalog.sourceLocale)) || "(빈 문구)")}</button>`).join("");
  $$('[data-preview-key]').forEach((button) => button.addEventListener("click", () => selectCopy(button.dataset.previewKey)));
}

function renderTermEditor(term) {
  $("#termTitle").textContent = term.id;
  $$('[data-term-field]').forEach((field) => field.value = term[field.dataset.termField] ?? "");
  $("#termPreviewSource").textContent = term.sourceTerm || "—";
  $("#termPreviewTarget").textContent = term.targetTerm || "—";
}

function markDirty(source) {
  if (source === "scenario") state.dirtyScenario = true;
  else state.dirtyCatalog = true;
  setSaveStatus("수정됨");
  clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(() => saveAll(true), 1600);
}

async function saveAll(automatic = false) {
  if (state.saving || (automatic && !state.dirtyScenario && !state.dirtyCatalog)) return;
  clearTimeout(state.saveTimer);
  state.saving = true;
  setSaveStatus("저장 중…");
  let failure = null;
  if (state.dirtyScenario || !automatic) {
    try {
      const payload = await api(`/api/scenarios/${encodeURIComponent(state.document.scenarioId)}`, { method: "PUT", body: JSON.stringify(state.document) });
      state.scenarioIssues = payload.issues || [];
      state.dirtyScenario = false;
    } catch (error) {
      state.scenarioIssues = error.payload?.issues || [{ severity: "error", message: error.message }];
      failure ||= error;
    }
  }
  if (state.dirtyCatalog || !automatic) {
    try {
      const payload = await api("/api/localization/catalog", { method: "PUT", body: JSON.stringify(state.catalog) });
      state.catalogIssues = payload.issues || [];
      state.catalogUsage = payload.usage || state.catalogUsage;
      state.dirtyCatalog = false;
    } catch (error) {
      state.catalogIssues = error.payload?.issues || [{ severity: "error", message: error.message }];
      failure ||= error;
    }
  }
  state.saving = false;
  renderIssueCount();
  if (failure) {
    setSaveStatus("검사 필요");
    if (!automatic) openDrawer("issues");
  } else {
    setSaveStatus("저장됨");
    if (!automatic) toast("저장 완료 · Unity 동기화 요청됨");
  }
}

function setSaveStatus(text) { $("#saveStatus").textContent = text; }
function renderIssueCount() { $("#issueCount").textContent = allIssues().length; }

function openDrawer(type) {
  $("#drawer").classList.remove("hidden");
  if (type === "issues") {
    const issues = allIssues();
    $("#drawerTitle").textContent = "문구 및 번역 검사";
    $("#drawerBody").innerHTML = issues.length ? issues.map((issue, index) => `
      <button class="issue-row ${issue.severity}" data-issue-index="${index}">
        <strong>${escapeHtml(issue.severity)}</strong><span>${escapeHtml(issue.beatId || issue.key || issue.source)}</span><span>${escapeHtml(issue.message)}</span>
      </button>`).join("") : '<div class="issue-row"><strong>OK</strong><span></span><span>발견된 문제가 없습니다.</span></div>';
    $$('[data-issue-index]').forEach((row) => row.addEventListener("click", () => jumpToIssue(issues[Number(row.dataset.issueIndex)])));
    return;
  }

  $("#drawerTitle").textContent = "Unity 라이브 추적 · 화면 단위로 정리됨";
  $("#drawerBody").innerHTML = state.traces.length ? [...state.traces].reverse().map((item) => {
    const index = state.traces.indexOf(item);
    if (item.kind === "ui") {
      return `<button class="trace-row" data-live-index="${index}"><small>${escapeHtml((item.trace.timestampUtc || "").slice(11, 19))}</small><strong>UI · ${escapeHtml(item.trace.screenId || "unknown")}</strong><span>${item.trace.keys?.length || 0}개 문구 · ${escapeHtml(item.trace.phase || "visible")}</span></button>`;
    }
    return `<button class="trace-row" data-live-index="${index}"><small>${escapeHtml((item.trace.timestampUtc || "").slice(11, 19))}</small><strong>${escapeHtml(item.trace.beatId || "unknown")}</strong><span>${escapeHtml(item.trace.phase || "event")}</span></button>`;
  }).join("") : '<div class="trace-row"><small>—</small><strong>대기 중</strong><span>대사는 비트로, UI는 화면 스냅샷으로 기록됩니다.</span></div>';
  $$('[data-live-index]').forEach((row) => row.addEventListener("click", () => {
    const item = state.traces[Number(row.dataset.liveIndex)];
    if (item?.kind === "ui") jumpToUiTrace(item.trace);
    else if (item) jumpToNarrativeTrace(item.trace);
  }));
}

function jumpToIssue(issue) {
  if (issue.source === "catalog" && issue.key) {
    setMode("copy", false); selectCopy(issue.key); return;
  }
  if (issue.beatId) {
    setMode("dialogue", false); selectBeat(issue.beatId);
  }
}

function beatForTrace(traceOrId) {
  const traceId = typeof traceOrId === "string" ? traceOrId : traceOrId?.beatId;
  return state.document?.beats.find((beat) => beat.id === traceId || beat.runtimeCue === traceId || beat.localizationKey === traceId) || null;
}

function jumpToNarrativeTrace(trace = state.latestNarrativeTrace) {
  const beat = beatForTrace(trace);
  if (!beat) return toast(`'${trace?.beatId || "unknown"}'에 대응하는 비트를 찾지 못했습니다.`);
  setMode("dialogue", false);
  state.selectedGroupId = beat.sectionId;
  clearFilters();
  renderSidebar(); selectBeat(beat.id);
  scrollSelected("[data-beat]", "beat", beat.id);
}

function jumpToUiTrace(trace = state.latestUiTrace) {
  const keys = trace?.keys || [];
  const focus = trace?.focusKey && playerFacingCopies().some((entry) => entry.key === trace.focusKey) ? trace.focusKey : keys.find((key) => playerFacingCopies().some((entry) => entry.key === key));
  if (!focus) return toast("현재 UI에 대응하는 카탈로그 문구를 찾지 못했습니다.");
  setMode("copy", false);
  state.selectedGroupId = "";
  state.filterLiveUi = true;
  clearFilters();
  renderSidebar(); selectCopy(focus);
  renderLiveBanners();
  scrollSelected("[data-copy]", "copy", focus);
}

function clearFilters() {
  $("#searchInput").value = "";
  $("#typeFilter").value = "";
  $("#statusFilter").value = "";
}

function scrollSelected(selector, dataName, value) {
  requestAnimationFrame(() => {
    const card = $$(selector).find((element) => element.dataset[dataName] === value);
    card?.scrollIntoView({ behavior: "smooth", block: "center" });
    card?.focus({ preventScroll: true });
  });
}

function addCurrentItem() {
  if (state.mode === "dialogue") return openNewBeatDialog();
  if (state.mode === "copy") return addCopy();
  addTerm();
}

function insertionAnchor() {
  const selected = currentBeat();
  if (selected && (!state.selectedGroupId || selected.sectionId === state.selectedGroupId)) return selected;
  const sectionId = state.selectedGroupId || selected?.sectionId || state.document.sections[0]?.id || "";
  return [...state.document.beats]
    .filter((beat) => beat.sectionId === sectionId)
    .sort((left, right) => Number(left.order || 0) - Number(right.order || 0))
    .at(-1) || null;
}

function openNewBeatDialog() {
  const dialog = $("#newBeatDialog");
  const anchor = insertionAnchor();
  const sectionId = anchor?.sectionId || state.selectedGroupId || state.document.sections[0]?.id || "";
  dialog.dataset.anchorBeatId = anchor?.id || "";
  dialog.dataset.sectionId = sectionId;

  const sectionTitle = sectionById(sectionId)?.title || sectionId || "섹션 없음";
  const anchorText = anchor ? localizedPreview(anchor, state.document) : "섹션의 첫 대사";
  const media = mediaTimingForBeat(anchor);
  const mediaLine = media ? `<br>${escapeHtml(media.assetName)} · 게임 재생 ${formatSeconds(media.playbackSeconds)}` : "";
  $("#newBeatContext").innerHTML = `<b>${escapeHtml(sectionTitle)} · ${anchor ? "선택한 대사 다음" : "섹션 끝"}</b><span>${escapeHtml(anchorText)}<br>${anchor?.triggerEvent ? `발생 이벤트 ${escapeHtml(anchor.triggerEvent)} 자동 상속` : "상속할 발생 이벤트가 없어 추가 후 고급 설정이 필요합니다."}${mediaLine}</span>`;

  populateSpeakerSelect($("#newBeatSpeaker"), {
    id: anchor?.speakerId,
    fallback: anchor?.speakerFallback,
    localizationKey: anchor?.speakerLocalizationKey,
  }, true);
  $("#newBeatSpeakerId").value = "";
  $("#newBeatSpeakerFallback").value = "";
  $("#newBeatCustomSpeaker").classList.toggle("hidden", $("#newBeatSpeaker").value !== "__custom__");

  $("#newBeatLocaleEditors").innerHTML = state.document.locales.map((locale) => {
    const source = locale === state.document.sourceLocale;
    return `<label class="locale-block"><strong>${escapeHtml(locale)} <em>${source ? "원문 · 필수" : "번역"}</em></strong><textarea data-new-beat-locale="${escapeHtml(locale)}" ${source ? "required" : ""} placeholder="${source ? "대사를 입력하세요" : "번역은 나중에 입력해도 됩니다"}"></textarea></label>`;
  }).join("");
  $("#newBeatAutomaticDuration").checked = true;
  $("#newBeatMinimumSeconds").disabled = true;
  $("#newBeatMinimumSeconds").value = "1.8";
  dialog.showModal();
  $(`[data-new-beat-locale="${state.document.sourceLocale}"]`)?.focus();
}

function updateNewBeatDuration() {
  if (!$("#newBeatAutomaticDuration").checked) return;
  const sourceText = $(`[data-new-beat-locale="${state.document.sourceLocale}"]`)?.value || "";
  const localizedTexts = $$('[data-new-beat-locale]').filter((field) => field.dataset.newBeatLocale !== state.document.sourceLocale)
    .map((field) => ({ locale: field.dataset.newBeatLocale, text: field.value }));
  $("#newBeatMinimumSeconds").value = estimateDialogueSeconds(sourceText, localizedTexts);
}

function submitNewBeat(event) {
  event.preventDefault();
  const sourceField = $(`[data-new-beat-locale="${state.document.sourceLocale}"]`);
  const sourceText = sourceField?.value.trim() || "";
  if (!sourceText) {
    toast("원문 대사를 입력하세요.");
    sourceField?.focus();
    return;
  }

  const speaker = selectedSpeaker($("#newBeatSpeaker"), "#newBeatSpeakerId", "#newBeatSpeakerFallback");
  if (!speaker.id && !speaker.fallback) {
    toast("화자를 선택하거나 새 화자를 입력하세요.");
    $("#newBeatSpeaker").focus();
    return;
  }

  const translations = {};
  $$('[data-new-beat-locale]').forEach((field) => {
    if (field.dataset.newBeatLocale !== state.document.sourceLocale) translations[field.dataset.newBeatLocale] = field.value.trim();
  });
  const dialog = $("#newBeatDialog");
  const beat = createDialogueBeat(state.document, {
    anchorBeatId: dialog.dataset.anchorBeatId,
    sectionId: dialog.dataset.sectionId,
    speaker,
    sourceText,
    translations,
    automaticDuration: $("#newBeatAutomaticDuration").checked,
    minimumSeconds: Number($("#newBeatMinimumSeconds").value),
  });
  state.document.beats.push(beat);
  state.selectedGroupId = beat.sectionId;
  state.selectedBeatId = beat.id;
  dialog.close();
  markDirty("scenario");
  renderAll();
  updateUrlForSelection();
  requestAnimationFrame(() => scrollSelected("[data-beat]", "beat", beat.id));
  toast("새 대사를 추가했습니다. ID와 번역 키는 자동으로 생성되었습니다.");
}

function addCopy() {
  let suffix = 1; while (state.catalog.entries.some((entry) => entry.key === `ui.new.copy_${suffix}`)) suffix++;
  const entry = {
    key: `ui.new.copy_${suffix}`, sourceText: "", domain: "ui.new", surface: "system", screenId: state.selectedGroupId || "system",
    context: "", status: "draft", audience: "player", tags: [],
    localizedTexts: state.catalog.locales.filter((locale) => locale !== state.catalog.sourceLocale).map((locale) => ({ locale, text: "", status: "draft" })),
  };
  state.catalog.entries.push(entry); state.filterLiveUi = false; markDirty("catalog"); renderAll(); selectCopy(entry.key);
}

function addTerm() {
  let suffix = 1; while (state.catalog.terms.some((term) => term.id === `new_term_${suffix}`)) suffix++;
  const term = { id: `new_term_${suffix}`, sourceTerm: "", targetTerm: "", definition: "", notes: "", status: "draft" };
  state.catalog.terms.push(term); markDirty("catalog"); renderAll(); selectTerm(term.id);
}

function duplicateBeat() {
  const beat = currentBeat(); if (!beat) return;
  const copy = duplicateNarrativeBeat(state.document, beat);
  state.document.beats.push(copy); markDirty("scenario"); renderAll(); selectBeat(copy.id);
}

function duplicateCopy() {
  const entry = currentCopy(); if (!entry) return;
  const copy = structuredClone(entry); let suffix = 2;
  while (state.catalog.entries.some((item) => item.key === `${entry.key}_${suffix}`)) suffix++;
  copy.key = `${entry.key}_${suffix}`; copy.status = "draft";
  state.catalog.entries.push(copy); markDirty("catalog"); renderAll(); selectCopy(copy.key);
}

function duplicateTerm() {
  const term = currentTerm(); if (!term) return;
  const copy = structuredClone(term); let suffix = 2;
  while (state.catalog.terms.some((item) => item.id === `${term.id}_${suffix}`)) suffix++;
  copy.id = `${term.id}_${suffix}`; copy.status = "draft";
  state.catalog.terms.push(copy); markDirty("catalog"); renderAll(); selectTerm(copy.id);
}

function deleteBeat() {
  const beat = currentBeat(); if (!beat || !confirm(`'${beat.id}' 비트를 삭제할까요?`)) return;
  const index = state.document.beats.indexOf(beat); state.document.beats.splice(index, 1);
  state.selectedBeatId = state.document.beats[Math.max(0, index - 1)]?.id || ""; markDirty("scenario"); renderAll();
}

function deleteCopy() {
  const entry = currentCopy(); if (!entry || !confirm(`'${entry.key}' 문구를 삭제할까요?`)) return;
  const index = state.catalog.entries.indexOf(entry); state.catalog.entries.splice(index, 1);
  state.selectedCopyKey = playerFacingCopies()[0]?.key || ""; markDirty("catalog"); renderAll();
}

function deleteTerm() {
  const term = currentTerm(); if (!term || !confirm(`'${term.sourceTerm || term.id}' 용어를 삭제할까요?`)) return;
  const index = state.catalog.terms.indexOf(term); state.catalog.terms.splice(index, 1);
  state.selectedTermId = state.catalog.terms[Math.max(0, index - 1)]?.id || ""; markDirty("catalog"); renderAll();
}

function moveBeat(direction) {
  const beat = currentBeat(); if (!beat) return;
  const siblings = state.document.beats.filter((item) => item.sectionId === beat.sectionId).sort((a, b) => a.order - b.order);
  const index = siblings.indexOf(beat); const target = siblings[index + direction]; if (!target) return;
  const order = beat.order; beat.order = target.order; target.order = order; markDirty("scenario"); renderList(); renderMediaTiming(beat);
}

function sendUnity(message) {
  if (!state.websocket || state.websocket.readyState !== WebSocket.OPEN || !state.unityOnline) return toast("Unity 에디터가 연결되어 있지 않습니다.");
  state.websocket.send(JSON.stringify(message));
}

function connectWebSocket() {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const socket = new WebSocket(`${protocol}//${location.host}/ws`);
  state.websocket = socket;
  socket.addEventListener("open", () => socket.send(JSON.stringify({ type: "hello", role: "web" })));
  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.type === "connection_state") {
      state.unityOnline = Boolean(message.roles?.unity);
      $("#unityStatus").classList.toggle("online", state.unityOnline);
      $("#unityStatus").childNodes[1].textContent = state.unityOnline ? " Unity 연결됨" : " Unity 연결 대기";
    }
    if (message.type === "narrative_trace") receiveNarrativeTrace(message.trace || message);
    if (message.type === "ui_copy_trace") receiveUiTrace(message.trace || message);
    if (message.type === "command_result") toast(message.message || (message.ok ? "Unity 명령 완료" : "Unity 명령 실패"));
  });
  socket.addEventListener("close", () => {
    state.unityOnline = false; $("#unityStatus").classList.remove("online");
    setTimeout(connectWebSocket, 1500);
  });
}

function receiveNarrativeTrace(trace) {
  state.latestNarrativeTrace = trace;
  state.activeTraceBeatId = beatForTrace(trace)?.id || "";
  addTrace("narrative", trace);
  if (state.mode === "dialogue") renderList();
  renderLiveBanners();
}

function receiveUiTrace(trace) {
  const visibleKeys = (trace.keys || []).filter((key) => playerFacingCopies().some((entry) => entry.key === key));
  if (!visibleKeys.length) return;
  trace = {
    ...trace,
    keys: visibleKeys,
    focusKey: visibleKeys.includes(trace.focusKey) ? trace.focusKey : visibleKeys[0],
  };
  const signature = `${trace.screenId}|${trace.focusKey}|${(trace.keys || []).join("|")}`;
  const previous = state.latestUiTrace;
  const previousSignature = previous ? `${previous.screenId}|${previous.focusKey}|${(previous.keys || []).join("|")}` : "";
  state.latestUiTrace = trace;
  state.activeUiKeys = new Set(trace.keys || []);
  if (signature !== previousSignature) addTrace("ui", trace);
  if (state.mode === "copy") { renderList(); renderEditor(); }
  renderLiveBanners();
}

function addTrace(kind, trace) {
  state.traces.push({ kind, trace });
  if (state.traces.length > 150) state.traces.shift();
  $("#traceCount").textContent = state.traces.length;
}

function renderLiveBanners() {
  const narrativeBanner = $("#activeTrace");
  const uiBanner = $("#activeUiTrace");
  narrativeBanner.classList.toggle("hidden", state.mode !== "dialogue" || !state.latestNarrativeTrace);
  uiBanner.classList.toggle("hidden", state.mode !== "copy" || !state.latestUiTrace);
  if (state.latestNarrativeTrace) {
    const beat = beatForTrace(state.latestNarrativeTrace);
    narrativeBanner.innerHTML = `<b>LIVE · ${escapeHtml(beat?.id || state.latestNarrativeTrace.beatId)} · ${escapeHtml(state.latestNarrativeTrace.phase)}</b><span>현재 대사로 이동 →</span>`;
  }
  if (state.latestUiTrace) {
    const trace = state.latestUiTrace;
    uiBanner.innerHTML = `<b>LIVE UI · ${escapeHtml(trace.screenId || "unknown")} · ${trace.keys?.length || 0}개 문구</b><span>${state.filterLiveUi ? "현재 화면 필터 중" : "현재 UI로 이동 →"}</span>`;
  }
}

function toast(message) {
  const element = $("#toast"); element.textContent = message; element.classList.remove("hidden");
  clearTimeout(toast.timer); toast.timer = setTimeout(() => element.classList.add("hidden"), 2600);
}

function wireEvents() {
  $$('[data-mode]').forEach((button) => button.addEventListener("click", () => setMode(button.dataset.mode)));
  $("#scenarioSelect").addEventListener("change", (event) => loadScenario(event.target.value));
  $("#showAllButton").addEventListener("click", () => { state.selectedGroupId = ""; state.filterLiveUi = false; renderSidebar(); renderList(); renderLiveBanners(); });
  ["#searchInput", "#typeFilter", "#statusFilter"].forEach((selector) => $(selector).addEventListener("input", renderList));
  $("#saveButton").addEventListener("click", () => saveAll(false));
  $("#validateButton").addEventListener("click", async () => { await saveAll(false); openDrawer("issues"); });
  $("#addBeatButton").addEventListener("click", addCurrentItem);
  $("#duplicateButton").addEventListener("click", duplicateBeat);
  $("#deleteButton").addEventListener("click", deleteBeat);
  $("#moveUpButton").addEventListener("click", () => moveBeat(-1));
  $("#moveDownButton").addEventListener("click", () => moveBeat(1));
  $("#duplicateCopyButton").addEventListener("click", duplicateCopy);
  $("#deleteCopyButton").addEventListener("click", deleteCopy);
  $("#duplicateTermButton").addEventListener("click", duplicateTerm);
  $("#deleteTermButton").addEventListener("click", deleteTerm);
  $("#previewLocale").addEventListener("change", () => renderBeatPreview());
  $("#copyPreviewLocale").addEventListener("change", () => renderCopyPreview());
  $("#previewUnityButton").addEventListener("click", () => {
    const beat = currentBeat(); if (beat) sendUnity({ type: "preview_beat", scenarioId: state.document.scenarioId, beatId: beat.id, locale: $("#previewLocale").value });
  });
  $("#copyPreviewUnityButton").addEventListener("click", () => sendUnity({ type: "set_locale", locale: $("#copyPreviewLocale").value }));
  $("#checkpointButton").addEventListener("click", () => sendUnity({ type: "play_checkpoint", scenarioId: state.document.scenarioId, checkpointId: $("#checkpointSelect").value }));
  $("#issuesToggle").addEventListener("click", () => openDrawer("issues"));
  $("#traceToggle").addEventListener("click", () => openDrawer("traces"));
  $("#activeTrace").addEventListener("click", () => jumpToNarrativeTrace());
  $("#activeUiTrace").addEventListener("click", () => jumpToUiTrace());
  $("#drawerClose").addEventListener("click", () => $("#drawer").classList.add("hidden"));

  $("#speakerPreset").addEventListener("change", () => {
    const beat = currentBeat(); if (!beat) return;
    const custom = $("#speakerPreset").value === "__custom__";
    $("#customSpeakerFields").classList.toggle("hidden", !custom);
    if (custom) {
      $("#customSpeakerId").value = beat.speakerId || "";
      $("#customSpeakerFallback").value = beat.speakerFallback || "";
      $("#customSpeakerFallback").focus();
      return;
    }
    applySpeakerToBeat(beat, selectedSpeaker($("#speakerPreset"), "#customSpeakerId", "#customSpeakerFallback"));
  });
  ["#customSpeakerId", "#customSpeakerFallback"].forEach((selector) => $(selector).addEventListener("input", () => {
    const beat = currentBeat(); if (!beat || $("#speakerPreset").value !== "__custom__") return;
    applySpeakerToBeat(beat, selectedSpeaker($("#speakerPreset"), "#customSpeakerId", "#customSpeakerFallback"));
  }));
  $("#estimateDurationButton").addEventListener("click", () => {
    const beat = currentBeat(); if (!beat) return;
    beat.minimumSeconds = estimateDialogueSeconds(beat.sourceText, beat.localizedTexts);
    $('[data-field="minimumSeconds"]').value = beat.minimumSeconds;
    markDirty("scenario");
    toast(`표시 시간을 ${beat.minimumSeconds.toFixed(1)}초로 계산했습니다.`);
  });

  $("#newBeatCloseButton").addEventListener("click", () => $("#newBeatDialog").close());
  $("#newBeatCancelButton").addEventListener("click", () => $("#newBeatDialog").close());
  $("#newBeatSpeaker").addEventListener("change", () => {
    const custom = $("#newBeatSpeaker").value === "__custom__";
    $("#newBeatCustomSpeaker").classList.toggle("hidden", !custom);
    if (custom) $("#newBeatSpeakerFallback").focus();
  });
  $("#newBeatAutomaticDuration").addEventListener("change", () => {
    $("#newBeatMinimumSeconds").disabled = $("#newBeatAutomaticDuration").checked;
    updateNewBeatDuration();
  });
  $("#newBeatLocaleEditors").addEventListener("input", updateNewBeatDuration);
  $("#newBeatForm").addEventListener("submit", submitNewBeat);

  $("#beatEditor").addEventListener("input", (event) => {
    const field = event.target.closest("[data-field]"); const beat = currentBeat(); if (!field || !beat) return;
    const name = field.dataset.field; const oldId = beat.id;
    if (name === "tags") beat.tags = field.value.split(",").map((value) => value.trim()).filter(Boolean);
    else if (name === "minimumSeconds" || name === "briefingLookTarget") beat[name] = Number(field.value || 0);
    else beat[name] = field.value;
    if (name === "id") state.selectedBeatId = beat.id;
    markDirty("scenario"); renderBeatHeading(beat); renderBeatPreview(beat);
    if (["id", "type", "status", "sectionId", "situation", "localizationKey", "triggerEvent"].includes(name)) renderList();
    if (["triggerEvent", "runtimeCue"].includes(name)) renderPlaybackConnection(beat);
    if (["minimumSeconds", "triggerEvent", "sectionId"].includes(name)) renderMediaTiming(beat);
    if (["speakerId", "speakerFallback", "speakerLocalizationKey"].includes(name)) renderSpeakerEditor(beat);
    if (name === "id" && oldId !== beat.id) updateUrlForSelection();
  });

  $("#copyEditor").addEventListener("input", (event) => {
    const field = event.target.closest("[data-copy-field]"); const entry = currentCopy(); if (!field || !entry) return;
    const name = field.dataset.copyField;
    if (name === "tags") entry.tags = field.value.split(",").map((value) => value.trim()).filter(Boolean);
    else entry[name] = field.value;
    if (name === "key") state.selectedCopyKey = entry.key;
    markDirty("catalog"); $("#copyTitle").textContent = entry.key; renderCopyPreview(entry);
    if (["key", "screenId", "surface", "status", "domain", "context"].includes(name)) { renderSidebar(); renderList(); }
    if (name === "key") updateUrlForSelection();
  });

  $("#termEditor").addEventListener("input", (event) => {
    const field = event.target.closest("[data-term-field]"); const term = currentTerm(); if (!field || !term) return;
    const name = field.dataset.termField; term[name] = field.value;
    if (name === "id") state.selectedTermId = term.id;
    markDirty("catalog");
    $("#termTitle").textContent = term.id;
    $("#termPreviewSource").textContent = term.sourceTerm || "—";
    $("#termPreviewTarget").textContent = term.targetTerm || "—";
    renderList();
    if (name === "id") updateUrlForSelection();
  });

  window.addEventListener("keydown", (event) => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") { event.preventDefault(); saveAll(false); }
  });
  window.addEventListener("beforeunload", (event) => {
    if (state.dirtyScenario || state.dirtyCatalog) { event.preventDefault(); event.returnValue = ""; }
  });
}

wireEvents();
configureFilters();
connectWebSocket();
loadProject().catch((error) => { setSaveStatus("불러오기 실패"); toast(error.message); console.error(error); });
