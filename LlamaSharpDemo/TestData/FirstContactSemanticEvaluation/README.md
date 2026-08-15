# First Contact Semantic Evaluation Data

`semantic_diverse_v1.json` is a deterministic regression suite for the three
production semantic decisions used by First Contact:

- bootstrap CATEGORY membership
- category naming when a GROUP is seeded
- membership in an established GROUP category

The suite deliberately mixes Korean, English, Japanese, Chinese, Arabic,
Spanish, symbols, and emoji. It covers everyday objects, scientific and
technical terms, abstract concepts, fictional entities, lexical ambiguity,
cross-language groupings, and association-only negative pairs.

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

Expectations are intentionally limited to cases with a defensible answer.
Ambiguous labels are scored as `uncertain`; GROUP seed cases avoid ambiguous
expected category names and instead score whether a valid category is present.
