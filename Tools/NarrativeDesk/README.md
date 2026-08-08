# Narrative Desk

Local dialogue, UI copy, terminology, and localization authoring for Doodle Diplomacy.
Scenario JSON files own dialogue beats, while
`Assets/Localization/Authoring/ui_copy.catalog.json` owns non-dialogue strings. Unity
merges both sources into the runtime localization table.

## Run

1. Run `npm install` once in this folder.
2. Run `npm start`.
3. Open `http://127.0.0.1:4317`.

The Unity editor bridge connects automatically while the editor is open. Saving a
scenario uses an atomic file replacement and keeps a `.bak` copy beside the source.
The bridge is editor-only and is not included in player builds.

Use the top tabs for **대사 흐름**, **UI 문구**, and **용어집**. UI live tracking is
grouped into deduplicated screen snapshots and only changes the current selection when
**현재 UI로 이동** is clicked. Catalog entries marked with `audience: internal` remain
available to the runtime localization importer but are never shown in UI copy authoring.

## Adding dialogue

Select the line that should come immediately before the new dialogue, then press **＋**.
The add-dialogue window only asks for the speaker, source text, translation, and display
duration. It inherits the selected line's section and trigger event, inserts the line in
sequence, and generates a stable beat ID and localization key. Generated identities stay
unchanged when copy or speaker details are edited.

The main editor keeps technical fields under **고급 설정**. `runtimeCue` should remain
empty for an ordinary line; it is only for Unity-side camera, actor, or sequence hooks.
A yellow playback warning means that a beat has neither a trigger event nor a runtime cue
and may not be reachable in game.

In the First Contact facility briefing, the main dialogue card also exposes
**브리핑 시선 대상**. Choose **현재 시선 유지**, **국장**, **황 박사**, or
**프로젝터 화면** per line. This is independent from `runtimeCue`, so a response can
keep the current slide while the president looks at a character.

News beats also show a media timing budget read from the First Contact scene and the
referenced MP4 file. The card compares the in-game clip duration with the sum of all
dialogue durations on the same `intro.news.clip.*` event and highlights overflow in red.
Still-image timing uses the authored `stillImageSeconds` value.

Run `npm test` to validate the authoring data contract.
