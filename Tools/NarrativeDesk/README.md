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

Run `npm test` to validate the authoring data contract.
