# First Contact Terminal UX Style

## Purpose

The First Contact terminal is an in-world analysis device. It should feel like a machine printing sparse signal-state output, not like a tutorial UI explaining mechanics to the player.

The player learns the loop through repeated terminal patterns:

```text
PROBE -> TRACE -> GROUP -> MEANING
```

The player draws concrete objects. The system records alien response traces, clusters similar responses, and opens rough meanings from stable groups.

## Core Voice

Use short status tags and `KEY: VALUE` lines.

Good:

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
GROUP: UNSTABLE
TRACE: 00/03

> DRAW RELATED OBJECT
PRESS ENTER TO SELECT
```

Bad:

```text
[RESPONSE CLUSTER TRAINING]

TARGET CLUSTER: UNKNOWN RESPONSE GROUP
SUGGESTED PROBES:
FIRE
KNIFE
MONSTER
```

The bad version explains the mechanic too directly and makes the terminal feel like a debug tutorial. Use science-officer dialogue for prose guidance when needed.

## Vocabulary

Use these terms consistently in player-facing terminal text.

| Term | Meaning | Use |
|---|---|---|
| `PROBE` | A visual stimulus sent by the player. | Drawing action, probe channel, probe sequence. |
| `TRACE` | One recorded alien response to a probe. | Progress within a category, e.g. `TRACE: 02/03`. |
| `GROUP` | A cluster of similar alien response traces. | Stability state, e.g. `GROUP: FORMING`. |
| `MEANING` | The interpreted meaning opened by a stable group. | Result line, e.g. `MEANING: [THREAT?]`. |
| `CATEGORY` | A bootstrap target type the player is currently trying to sample. | Goal line, e.g. `CATEGORY: DEFENSE`. |

Avoid these as player-facing terminal terms:

| Avoid | Reason | Replacement |
|---|---|---|
| `TOKEN` | Too implementation-oriented. | `MEANING` for interpreted output. |
| `SELECT-ONE` | Internal request mode, not an alien word. | Hide it; use terminal choices. |
| `TARGET CLUSTER` | Too tutorial/debug-like. | `CATEGORY` and `GROUP`. |
| `SUGGESTED PROBES` | Makes the player follow examples as answers. | `DRAW RELATED OBJECT`; optional hints belong in officer dialogue. |

## Formatting Rules

- Headers are short bracket tags, e.g. `[PROBE SEQUENCE]`, `[SIGNAL CAPTURE]`, `[CLUSTER TRACE]`.
- Lines use `KEY: VALUE` where possible.
- Dynamic raw values such as category names and meaning labels stay uppercase.
- Choices use a `>` cursor.
- Continue prompts use the standard terminal prompt style.
- Avoid full explanatory sentences in terminal body text.
- Do not show long paragraphs in the terminal.
- Do not use natural-language question text for alien communication in this loop.

## Standard Screens

### Probe Sequence

Shown before the player chooses a concrete object to draw for the current category.

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
GROUP: UNSTABLE
TRACE: 00/03

> DRAW RELATED OBJECT
PRESS ENTER TO SELECT
```

If the group already has traces:

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
GROUP: FORMING
TRACE: 02/03

> DRAW RELATED OBJECT
PRESS ENTER TO SELECT
```

Do not list fixed examples here by default. The category should invite player choice.

### Probe Channel Open

Shown while opening the tablet path for a category probe.

```text
[PROBE CHANNEL OPEN]

CATEGORY: THREAT
TRACE: 01/03

DRAW RELATED OBJECT
```

### Signal Capture

Shown after the drawing is classified and an alien response trace is recorded.

```text
[SIGNAL CAPTURE]

VISUAL READ: FIRE
CATEGORY: THREAT
TRACE: 01/03
GROUP: FORMING

PRESS ENTER TO CONTINUE
```

### Cluster Trace

Shown when the response group becomes stable enough to open a meaning candidate.

```text
[CLUSTER TRACE]

CATEGORY: THREAT
TRACE: 03/03
GROUP: STABLE
MEANING: [THREAT?]

PRESS ENTER TO CONTINUE
```

### Bootstrap Complete

Shown when the configured bootstrap categories are complete.

```text
[BOOTSTRAP COMPLETE]

TRANSLATOR READY
MEANING MAP SEEDED

PRESS ENTER TO CONTINUE
```

## Tutorial Guidance

Tutorial content should use the same terminal grammar as normal play.

Do not switch to instructional labels such as:

```text
TARGET CLUSTER: UNKNOWN RESPONSE GROUP
SUGGESTED PROBES:
```

If the player needs more guidance, use authored/localized science-officer dialogue outside the terminal, for example:

```text
Science Officer: Similar objects should produce a cleaner response group.
```

The terminal itself should remain sparse:

```text
CATEGORY: THREAT
GROUP: FORMING
TRACE: 01/03
```

## Meaning And Category Distinction

`CATEGORY` is the current bootstrap goal. It can be shown before the player draws.

`MEANING` is the interpretation produced after the response group stabilizes.

Example:

```text
[PROBE SEQUENCE]
CATEGORY: DEFENSE
GROUP: FORMING
TRACE: 02/03
```

Later:

```text
[CLUSTER TRACE]
CATEGORY: DEFENSE
GROUP: STABLE
MEANING: [DEFENSE?]
```

Do not use `MEANING` as a goal label before the group stabilizes.

## Localization Rules

- Put terminal text behind `first_contact.terminal.*` localization keys.
- Preserve the terse terminal style in translations.
- Keep raw category and meaning values stable unless the design explicitly localizes them.
- Prefer short Korean labels over explanatory Korean sentences for terminal body lines.
- Use science-officer dialogue for localized prose explanations.

## Implementation Checklist

Before committing terminal UX work:

- No player-facing `SELECT-ONE`.
- No player-facing `TOKEN`.
- Terminal body uses bracket headers and `KEY: VALUE` lines.
- Category probe prompts say `DRAW RELATED OBJECT`, not a fixed object name.
- Concrete example lists are not shown in terminal by default.
- New player-facing strings use localization keys.
- Science-officer prose is separate from terminal body text.
