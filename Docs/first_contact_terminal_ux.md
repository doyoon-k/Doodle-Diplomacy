# First Contact Terminal UX Style

## Purpose

The First Contact terminal is an in-world analysis device. It should feel like a machine printing sparse signal-state output, not like a tutorial UI explaining mechanics to the player.

The player learns category calibration through repeated terminal patterns:

```text
CATEGORY -> PROBE -> TRACE -> PATTERN -> CALIBRATION
```

The player draws concrete objects. The system records alien response traces, extracts a shared response pattern from similar signals, and calibrates each known bootstrap category against a stable pattern. `MEANING` is reserved for interpretations produced from alien signals rather than echoing a category the player was already given.

## Core Voice

Use short status tags and `KEY: VALUE` lines.

Good:

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
PATTERN: UNSTABLE
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

The bad version explains the mechanic too directly and makes the terminal feel like a debug tutorial. Use Dr. Hwang's dialogue for prose guidance when needed.

## Vocabulary

Use these terms consistently in player-facing terminal text.

| Term | Meaning | Use |
|---|---|---|
| `PROBE` | A visual stimulus sent by the player. | Drawing action, probe channel, probe sequence. |
| `TRACE` | One recorded alien response to a probe. | Progress within a category, e.g. `TRACE: 02/03`. |
| `PATTERN` | The common response shape extracted from similar alien traces. | Stability state, e.g. `PATTERN: FORMING`. |
| `MEANING` | The interpreted meaning opened by a stable response pattern. | Result line, e.g. `MEANING: [THREAT?]`. |
| `CATEGORY` | A bootstrap target type the player is currently trying to sample. | Goal line, e.g. `CATEGORY: DEFENSE`. |
| `CALIBRATION` | Confirmation that a known bootstrap category has enough stable response traces. | Completion line, e.g. `CALIBRATION COMPLETE`. |

## Game Glossary

Use this glossary for First Contact player-facing copy and localization.

| English source term | Korean player term | Meaning | Notes |
|---|---|---|---|
| `PROBE` | `표본` | One player-submitted drawing used to collect an alien response. | Use `시각 표본` in Dr. Hwang's prose when extra clarity is useful. Do not use `탐침`. |
| `PROBE LABEL` | `표본 라벨` | The player-entered name for the drawing. | The visible label can be localized; the internal canonical label may be English. |
| `VISUAL PROBE` | `시각 표본` | A drawing as a visual response sample. | Prefer this in prose over bare `표본` when the sentence could be ambiguous. |
| `TRACE` | `추적` | One recorded response trace from the alien. | Keep short in terminal lines. |
| `PATTERN` | `패턴` | The shared form extracted from similar alien reactions. | Use `반응 패턴` in prose and `패턴` in terminal lines. |
| `MEANING` | `의미` | The rough interpretation opened by a stable response pattern. | Do not use as a bootstrap goal. |
| `CATEGORY` | `분류` | The current bootstrap target type. | Keep raw category names uppercase unless explicitly localized. |
| `SIGNAL` | `신호` | The waveform or response signal shown by the device. | Use with `표본` as `시각 표본 신호`. |
| `CALIBRATION` | `보정` | Confirmation that a known bootstrap category has been linked to a stable response pattern. | Use `보정 완료` on category completion. |

Avoid these as player-facing terminal terms:

| Avoid | Reason | Replacement |
|---|---|---|
| `TOKEN` | Too implementation-oriented. | `MEANING` for interpreted output. |
| `SELECT-ONE` | Internal request mode, not an alien word. | Hide it; use terminal choices. |
| `TARGET CLUSTER` | Too tutorial/debug-like. | `CATEGORY` and `PATTERN`. |
| `SUGGESTED PROBES` | Makes the player follow examples as answers. | `DRAW RELATED OBJECT`; optional hints belong in Dr. Hwang's dialogue. |
| `탐침` | Too technical and not intuitive for the player action. | `표본`; use `시각 표본` in prose when needed. |

## Formatting Rules

- Headers are short bracket tags, e.g. `[PROBE SEQUENCE]`, `[SIGNAL CAPTURE]`, `[RESPONSE ANALYSIS]`.
- Lines use `KEY: VALUE` where possible.
- Dynamic raw values such as category names and meaning labels stay uppercase.
- Choices use a `>` cursor.
- Continue prompts use the standard terminal prompt style.
- Any terminal state waiting for player input should show a blinking cursor at the active input point.
- Avoid full explanatory sentences in terminal body text.
- Do not show long paragraphs in the terminal.
- Do not use natural-language question text for alien communication in this loop.

## Standard Screens

### Probe Preflight

Shown before the alien delegation enters. This is a local equipment check: it uses the normal probe validation path but keeps the alien response channel closed and records no `TRACE` or `PATTERN` data.

```text
[PROBE PREFLIGHT]

RESPONSE CHANNEL: CLOSED

DRAW ONE OBJECT
PRESS ENTER TO CHECK
```

After a valid practice drawing:

```text
[PROBE PREFLIGHT]

PROBE LABEL: APPLE
PROBE CHECK: PASSED
RESPONSE CHANNEL: CLOSED

PREFLIGHT COMPLETE
```

Keep control explanations in Dr. Hwang's dialogue while the corresponding tablet controls pulse. Never show `TRACE`, `PATTERN`, or alien-response wording on the preflight result.
The preflight has no `CATEGORY`. It checks only whether the player can submit one concrete object with a matching label; category fit begins after the alien delegation arrives.

### Probe Sequence

Shown before the player chooses a concrete object to draw for the current category.

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
PATTERN: UNSTABLE
TRACE: 00/03

> DRAW RELATED OBJECT
PRESS ENTER TO SELECT
```

If a response pattern is already forming:

```text
[PROBE SEQUENCE]

CATEGORY: THREAT
PATTERN: FORMING
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

If the label is unsuitable or does not match the captured drawing, preserve the image
and return to this text-entry state with the previous label still editable. Require a
redraw only when the visual sample itself fails validation, such as an empty image,
multiple objects, or an unreadable subject.

Keep the correction reason visible while the player edits the label:

```text
[PROBE REVIEW]

CHECK REQUIRED: LABEL MISMATCH

IMAGE CAPTURED
PROBE LABEL: FIRE_
CHANNEL: PROBE SEQUENCE

SUBMIT: ENTER
REDRAW: ESC
```

Dr. Hwang's validation guidance is non-blocking and repeats after every failed
resubmission. It remains visible during editing and clears when the player resubmits;
the terminal correction reason follows the same lifecycle.

### Signal Capture

Shown after the drawing label is accepted and an alien response trace is recorded.

```text
[SIGNAL CAPTURE]

PROBE LABEL: FIRE
CATEGORY: THREAT
TRACE: 01/03
PATTERN: FORMING

PRESS ENTER TO CONTINUE
```

### Response Analysis

Shown when the response pattern for a known bootstrap category becomes stable enough to complete calibration.

```text
[RESPONSE ANALYSIS]

CATEGORY: THREAT
TRACE: 03/03
PATTERN: STABLE
CALIBRATION COMPLETE

PRESS ENTER TO CONTINUE
```

### New Response Pattern

Shown when off-category probes form a stable response pattern that is not connected to a known bootstrap meaning. This does not advance the active `CATEGORY` calibration.

```text
[NEW RESPONSE PATTERN]

PATTERN: STABLE
TRACES: 03
MEANING: UNASSIGNED

PRESS ENTER TO CONTINUE
```

After Dr. Hwang explains the discovery, the player assigns an interpretation. The terminal shows the submitted samples as evidence, but the typed meaning does not affect clustering.

```text
[MEANING ASSIGNMENT]

PATTERN: 01
SAMPLES: KNIFE / GUN / BOMB
MEANING: WEAPON_

SUBMIT: ENTER
```

After confirmation:

```text
[MEANING REGISTERED]

PATTERN: 01
MEANING: WEAPON

MEANING MAP UPDATED

PRESS ENTER TO CONTINUE
```

Player-authored meanings are user data, not localized copy. Preserve the submitted text for future translation output. Require a non-empty meaning, but do not validate it against the bootstrap categories or use it to alter the response pattern.

### Bootstrap Complete

Shown when the configured bootstrap categories are complete.

```text
[BOOTSTRAP COMPLETE]

TRANSLATOR READY
MEANING MAP SEEDED

PRESS ENTER TO CONTINUE
```

### Translation Demonstration

Shown after bootstrap calibration when the translator applies stable response patterns to a live alien signal. Calibrated segments render as meanings; unresolved segments remain as raw signal.

```text
[INCOMING TRANSMISSION]

SIGNAL: [KRR] [VOR] [THA]
MEANING: DANGER [VOR] FOOD
UNRESOLVED: 01

PRESS ENTER TO CONTINUE
```

This is a valid use of `MEANING`: the value is derived from an incoming signal after calibration rather than restating the current bootstrap category.

## Tutorial Guidance

Tutorial content should use the same terminal grammar as normal play.

Do not switch to instructional labels such as:

```text
TARGET CLUSTER: UNKNOWN RESPONSE GROUP
SUGGESTED PROBES:
```

If the player needs more guidance, use authored/localized dialogue from Dr. Hwang outside the terminal, for example:

```text
Dr. Hwang: Similar objects should produce a cleaner response pattern.
```

The terminal itself should remain sparse:

```text
CATEGORY: THREAT
PATTERN: FORMING
TRACE: 01/03
```

## Meaning, Category, And Calibration Distinction

`CATEGORY` is the current bootstrap goal. It can be shown before the player draws.

`CALIBRATION` confirms that enough response traces have formed a stable pattern linked to that known category.

`MEANING` is an interpretation produced when the calibrated map is later applied to an alien signal. Do not echo the known category as a newly discovered meaning on bootstrap completion.

An off-category stable pattern has no automatic `MEANING`. The player must assign one before that pattern can produce translated output. Known bootstrap patterns still receive their configured meaning through calibration.

Example:

```text
[PROBE SEQUENCE]
CATEGORY: DEFENSE
PATTERN: FORMING
TRACE: 02/03
```

Later:

```text
[RESPONSE ANALYSIS]
CATEGORY: DEFENSE
PATTERN: STABLE
CALIBRATION COMPLETE
```

Later translated alien output may show an uncertain interpretation such as `MEANING: [DEFENSE?]` when that is genuinely derived from a signal.

## Localization Rules

- Put terminal text behind `first_contact.terminal.*` localization keys.
- Preserve the terse terminal style in translations.
- Keep raw category and meaning values stable unless the design explicitly localizes them.
- Prefer short Korean labels over explanatory Korean sentences for terminal body lines.
- Translate `PROBE` as `표본`, not `탐침`.
- Use Dr. Hwang's dialogue for localized prose explanations.

## Implementation Checklist

Before committing terminal UX work:

- No player-facing `SELECT-ONE`.
- No player-facing `TOKEN`.
- Terminal body uses bracket headers and `KEY: VALUE` lines.
- Category probe prompts say `DRAW RELATED OBJECT`, not a fixed object name.
- Preflight prompts say `DRAW ONE OBJECT` and never show or evaluate a `CATEGORY`.
- Bootstrap category completion says `CALIBRATION COMPLETE` and does not echo `CATEGORY` as `MEANING`.
- Translation output preserves raw signal for any segment whose category has not been calibrated.
- Off-category stable patterns request a player-authored `MEANING`; they are never named from label keyword lists or the active bootstrap category.
- Player-authored meanings label translation output only and never influence pattern formation.
- Concrete example lists are not shown in terminal by default.
- New player-facing strings use localization keys.
- Dr. Hwang's prose is separate from terminal body text.
