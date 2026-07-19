# Narrative Desk

Narrative Desk is the local dialogue, UI copy, terminology, and localization authoring
surface for Doodle Diplomacy. Dialogue is designed around gameplay situations rather
than a flat string list; non-dialogue copy is organized by the screen on which the
player sees it.

## Source of truth

- Authored scenarios live in `LlamaSharpDemo/Assets/Narrative/*.narrative.json`.
- Non-dialogue UI copy lives in
  `LlamaSharpDemo/Assets/Localization/Authoring/ui_copy.catalog.json`.
- `first_contact_day1.narrative.json` currently owns the First Contact intro bridge,
  onboarding cues, reactive guidance from Dr. Hwang, and alien reaction captions.
- Unity generates `NarrativeScenarioAsset` assets under
  `Assets/Generated/Narrative` and merges narrative-owned strings into the existing
  `LocalizedStringTable`.
- Unity merges the UI copy catalog into the same string table and writes an ownership
  manifest under `Assets/Generated/Localization`. Removing a catalog entry removes
  only a key previously owned by that catalog.
- The old cue list in `FirstContactNarrativeSettings` remains as a runtime fallback.
  A missing or temporarily invalid generated asset therefore does not break gameplay.

Do not hand-edit generated scenario assets or Narrative Desk-owned rows in the
Localization Workbench. The workbench identifies both dialogue-owned and UI
catalog-owned rows, makes them read-only, and links back to Narrative Desk.

## Opening the editor

From Unity, choose **Tools > Narrative Desk > Open Narrative Desk**. The launcher
starts the local server when necessary and opens `http://127.0.0.1:4317`.

For a first-time setup outside Unity:

1. Open `Tools/NarrativeDesk` in a terminal.
2. Run `npm install` once.
3. Run `npm start`, then open `http://127.0.0.1:4317`.

The server listens only on the local machine. Saving replaces the JSON atomically and
keeps a `.bak` copy. Unity notices the changed JSON and synchronizes the generated
asset and localization table automatically.

## Authoring workflow

Choose one of the three modes at the top:

- **대사 흐름** edits authored beats with their gameplay context and subtitle preview.
- **UI 문구** edits menus, HUD, terminal, captions, errors, and shared system
  text. The left side filters by screen; the preview uses a surface-specific layout
  and shows nearby strings from that screen. Each row also reports source references;
  the pseudo-status **unused** filters strings for which no static or registered
  dynamic use was found. Runtime-localized model context such as category descriptors
  remains in the catalog with `audience: internal`, but is excluded from UI copy lists,
  search, screen counts, previews, deep links, and live traces.
- **용어집** records preferred English/Korean terminology, definitions, and forbidden
  or contextual usage. Glossary rows guide copy but are not emitted to the runtime
  string table themselves.

For dialogue work:

1. Choose a section from the left-hand flow.
2. Choose the gameplay beat from the ordered center list.
3. Edit its situation, event, condition, before/after actions, timing, source line,
   and translations in the right pane.
4. Use the in-page subtitle card for quick bilingual copy review.
5. Use **Unity에서 보기** to render the selected line in the active scene.
6. In Play Mode, use a context checkpoint or play normally. Active beats appear in
   the web editor's live trace. Click the gold LIVE banner to clear obstructing
   filters and jump directly to the currently executing beat.
7. Save with the button or Ctrl/Cmd+S. The editor also performs a short delayed save
   after edits.

The validator blocks saves with duplicate beat ids, invalid section references,
conflicting localization keys, or mismatched placeholders such as `{category}`.
Missing target-language copy is shown as a warning.

The UI catalog audit scans gameplay scripts, data assets, and narrative sources. It
reports source locations for used keys, missing direct `L10n.T` keys, and unused
catalog rows. Dynamic First Contact category and meaning families are
registered as dynamic use so they are not misreported as dead strings.

## UI live trace

UI tracing is intentionally not a log of every localization lookup. An authored
screen declares a short editor-only collection scope, and all keys resolved inside
that scope are sent as one deduplicated screen snapshot. Repainting the same screen
does not add another event.

The UI copy mode shows a green banner such as `LIVE UI · probe_review · 7개 문구`.
It does not steal the current selection or scroll automatically. Click **현재 UI로
이동** to filter the center list to that snapshot and select its focused or first
matching key. Explicit errors and interactions may nominate one key as the focus;
ordinary static labels remain members of the screen snapshot.

## Player-build boundary

The WebSocket client, local server launcher, asset importer, scene preview commands,
and checkpoints are all under an `Editor` folder. Runtime scripts only contain
conditional narrative and UI screen-collection calls; their bodies and call sites are
removed from non-editor builds.
The shipped game therefore has no Narrative Desk network or server dependency.

## Adding another scenario

Copy an existing `.narrative.json`, give it a unique `scenarioId`, and retain schema
version 1. The local editor discovers it automatically. Use stable beat ids and
localization keys: ids are used for tracing and deep links, while keys are merged into
the game's localization table.

Run `npm test` in `Tools/NarrativeDesk` after changing the data contract or validator.
