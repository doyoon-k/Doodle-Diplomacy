# First Contact Semantic Evaluation Data

`semantic_diverse_v1.json` is a deterministic regression suite for the three
production semantic decisions used by First Contact:

- bootstrap CATEGORY membership
- category naming when a GROUP is seeded
- membership in an established GROUP category

The suite deliberately mixes Korean, English, Japanese, Chinese, Arabic, and
Spanish. It covers everyday objects, scientific and
technical terms, abstract concepts, fictional entities, lexical ambiguity,
cross-language groupings, and association-only negative pairs.
Emoji is intentionally excluded because it is not a supported player input.

Version 1 contains 787 scored cases built from 606 unique labels:

- 269 bootstrap CATEGORY decisions: 114 matches, 114 mismatches, 41 ambiguous
- 110 GROUP seed decisions: 70 valid shared categories, 40 association-only or
  unrelated pairs
- 408 GROUP membership decisions: 180 joins, 180 rejects, 48 ambiguous

Run it from Unity while outside Play Mode:

`Tools > First Contact > Run Diverse Semantic Dataset`

The runner copies the suite to
`Temp/FirstContactSemanticEvaluation/request.json`, consumes and deletes that
request, then writes the latest report to
`Temp/FirstContactSemanticEvaluation/result.json`. It uses the production
Gemma model and prompt pipeline assets; it does not load a scene or invoke the
drawing/VLM path.

`semantic_wild_v1.json` is a separate opt-in stress suite for explicit or
controversial labels. It includes sexual anatomy and acts, pornography, sex
toys, psychoactive drugs, profanity, political and religious concepts, crime,
self-harm, bodily fluids, nudity, extremist groups, and racial slurs. The file
contains uncensored offensive terms intentionally; it verifies semantic
classification rather than endorsing or presenting that language to players.
Keep its score separate from the general regression suite.

Version 1 contains 234 scored cases:

- 8 bootstrap CATEGORY decisions
- 30 GROUP seed decisions: 15 shared categories and 15 association-only pairs
- 196 GROUP membership decisions across 14 topics: 84 joins, 84 rejects,
  and 28 deliberately ambiguous labels

Run it from Unity while outside Play Mode:

`Tools > First Contact > Run Wild Semantic Dataset`

Expectations are intentionally limited to cases with a defensible answer.
Ambiguous labels are scored as `uncertain`; GROUP seed cases avoid ambiguous
expected category names and instead score whether a valid category is present.
