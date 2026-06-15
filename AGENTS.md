# Repository Agent Notes

## First Contact Terminal UX

Before changing First Contact terminal copy, flow presentation, localization keys, or terminal-facing terminology, read:

- `Docs/first_contact_terminal_ux.md`

The First Contact terminal is an in-world analysis device, not an explanatory tutorial panel. Keep terminal text sparse, state-driven, and consistent with the shared PROBE / TRACE / GROUP / MEANING / CATEGORY vocabulary.

Important constraints:

- Do not expose `SELECT-ONE` as player-facing terminal text.
- Do not use `TOKEN` as player-facing terminology; use `MEANING` for interpreted results.
- Use `CATEGORY` for bootstrap target types and `MEANING` for the interpretation opened by a stable response group.
- Keep tutorial guidance in the same terminal grammar as the rest of the game; avoid prose like "Target cluster" or "Suggested probes" unless it appears as authored science-officer dialogue outside the terminal.
- Put player-facing terminal strings behind localization keys under `first_contact.terminal.*`.
