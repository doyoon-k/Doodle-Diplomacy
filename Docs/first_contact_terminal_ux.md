# First Contact Terminal UX Style

## Purpose

The First Contact terminal is an in-world analysis device. It should feel like a machine printing sparse signal-state output, not like a tutorial UI explaining mechanics to the player.

The player learns category calibration through repeated terminal patterns:

```text
CATEGORY -> PROBE -> TRACE -> GROUP -> CALIBRATION
```

The player draws concrete objects. The system records alien response traces, clusters similar responses, and calibrates each known bootstrap category against a stable group. `MEANING` is reserved for interpretations produced from alien signals rather than echoing a category the player was already given.

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
| `CALIBRATION` | Confirmation that a known bootstrap category has enough stable response traces. | Completion line, e.g. `CALIBRATION COMPLETE`. |

## Game Glossary

Use this glossary for First Contact player-facing copy and localization.

| English source term | Korean player term | Meaning | Notes |
|---|---|---|---|
| `PROBE` | `표본` | One player-submitted drawing used to collect an alien response. | Use `시각 표본` in science-officer prose when extra clarity is useful. Do not use `탐침`. |
| `PROBE LABEL` | `표본 라벨` | The player-entered name for the drawing. | The visible label can be localized; the internal canonical label may be English. |
| `VISUAL PROBE` | `시각 표본` | A drawing as a visual response sample. | Prefer this in prose over bare `표본` when the sentence could be ambiguous. |
| `TRACE` | `추적` | One recorded response trace from the alien. | Keep short in terminal lines. |
| `GROUP` | `군집` | A cluster of similar traces. | Use for stability state. |
| `MEANING` | `의미` | The rough interpretation opened by a stable group. | Do not use as a bootstrap goal. |
| `CATEGORY` | `분류` | The current bootstrap target type. | Keep raw category names uppercase unless explicitly localized. |
| `SIGNAL` | `신호` | The waveform or response signal shown by the device. | Use with `표본` as `시각 표본 신호`. |
| `CALIBRATION` | `보정` | Confirmation that a known bootstrap category has been linked to a stable response group. | Use `보정 완료` on category completion. |

Avoid these as player-facing terminal terms:

| Avoid | Reason | Replacement |
|---|---|---|
| `TOKEN` | Too implementation-oriented. | `MEANING` for interpreted output. |
| `SELECT-ONE` | Internal request mode, not an alien word. | Hide it; use terminal choices. |
| `TARGET CLUSTER` | Too tutorial/debug-like. | `CATEGORY` and `GROUP`. |
| `SUGGESTED PROBES` | Makes the player follow examples as answers. | `DRAW RELATED OBJECT`; optional hints belong in officer dialogue. |
| `탐침` | Too technical and not intuitive for the player action. | `표본`; use `시각 표본` in prose when needed. |

## Formatting Rules

- Headers are short bracket tags, e.g. `[PROBE SEQUENCE]`, `[SIGNAL CAPTURE]`, `[CLUSTER TRACE]`.
- Lines use `KEY: VALUE` where possible.
- Dynamic raw values such as category names and meaning labels stay uppercase.
- Choices use a `>` cursor.
- Continue prompts use the standard terminal prompt style.
- Any terminal state waiting for player input should show a blinking cursor at the active input point.
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

### Probe Review

Shown after the tablet drawing is captured. The drawing preview is displayed inside the terminal screen, and the player types the probe label directly into the terminal.

```text
[PROBE REVIEW]

IMAGE CAPTURED
PROBE LABEL: FIRE_
CHANNEL: PROBE SEQUENCE

SUBMIT: ENTER
REDRAW: ESC
```

### Signal Capture

Shown after the drawing label is accepted and an alien response trace is recorded.

```text
[SIGNAL CAPTURE]

PROBE LABEL: FIRE
CATEGORY: THREAT
TRACE: 01/03
GROUP: FORMING

PRESS ENTER TO CONTINUE
```

### Cluster Trace

Shown when the response group for a known bootstrap category becomes stable enough to complete calibration.

```text
[CLUSTER TRACE]

CATEGORY: THREAT
TRACE: 03/03
GROUP: STABLE
CALIBRATION COMPLETE

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

## Meaning, Category, And Calibration Distinction

`CATEGORY` is the current bootstrap goal. It can be shown before the player draws.

`CALIBRATION` confirms that enough stable response traces have been linked to that known category.

`MEANING` is an interpretation produced when the calibrated map is later applied to an alien signal. Do not echo the known category as a newly discovered meaning on bootstrap completion.

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
CALIBRATION COMPLETE
```

Later translated alien output may show an uncertain interpretation such as `MEANING: [DEFENSE?]` when that is genuinely derived from a signal.

## Localization Rules

- Put terminal text behind `first_contact.terminal.*` localization keys.
- Preserve the terse terminal style in translations.
- Keep raw category and meaning values stable unless the design explicitly localizes them.
- Prefer short Korean labels over explanatory Korean sentences for terminal body lines.
- Translate `PROBE` as `표본`, not `탐침`.
- Use science-officer dialogue for localized prose explanations.

## Implementation Checklist

Before committing terminal UX work:

- No player-facing `SELECT-ONE`.
- No player-facing `TOKEN`.
- Terminal body uses bracket headers and `KEY: VALUE` lines.
- Category probe prompts say `DRAW RELATED OBJECT`, not a fixed object name.
- Bootstrap category completion says `CALIBRATION COMPLETE` and does not echo `CATEGORY` as `MEANING`.
- Concrete example lists are not shown in terminal by default.
- New player-facing strings use localization keys.
- Science-officer prose is separate from terminal body text.
