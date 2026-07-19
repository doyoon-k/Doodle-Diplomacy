# First Contact Multilingual Embedding Benchmark

Measured on 2026-07-19 with:

- model: `embeddinggemma-300M-Q8_0.gguf`
- input prefix: `task: sentence similarity | query: `
- normalized cosine similarity
- LLamaSharp 0.26.0 using the same GGUF and native runtime as the game

## Result summary

| Set | Minimum | Average | Maximum |
|---|---:|---:|---:|
| Same-concept translations | 0.834288 | 0.901297 | 0.929460 |
| Different concepts | 0.625507 | 0.753051 | 0.900046 |

The distributions overlap. A single embedding threshold cannot safely distinguish direct translations from nearby-but-different concepts. In particular, `칼` / `검` scored `0.900046`, while the valid translation pair `knife` / `칼` scored `0.834288`.

The runtime policy therefore uses:

- `0.96` and above: strong semantic duplicate evidence
- `0.75` through `0.96`: bounded LLM review, at most three nearest candidates
- below `0.75`: not a semantic duplicate candidate

Exact image equality remains an immediate duplicate. Exact normalized label equality is immediate only when both cards have the same known CATEGORY.

## Measured pairs

| Expected | Left | Right | Score |
|---|---|---|---:|
| Same | apple | 사과 | 0.879202 |
| Same | apple | りんご | 0.897366 |
| Same | apple | 苹果 | 0.915224 |
| Same | apple | manzana | 0.921943 |
| Same | apple | pomme | 0.895158 |
| Same | apple | Apfel | 0.929460 |
| Same | apple | تفاحة | 0.909718 |
| Same | knife | 칼 | 0.834288 |
| Same | knife | ナイフ | 0.894341 |
| Same | shield | 방패 | 0.878675 |
| Same | shield | 盾 | 0.920765 |
| Same | bread | 빵 | 0.928691 |
| Same | bread | パン | 0.912033 |
| Different | apple | pear | 0.807061 |
| Different | 사과 | 배 | 0.639604 |
| Different | knife | sword | 0.827904 |
| Different | 칼 | 검 | 0.900046 |
| Different | bread | cake | 0.783084 |
| Different | apple | bread | 0.705928 |
| Different | knife | hammer | 0.693285 |
| Different | shield | armor | 0.845600 |
| Different | apple | shield | 0.625507 |
| Different | bread | knife | 0.702486 |
| Diagnostic | 배 | pear | 0.651899 |
| Diagnostic | 배 | ship | 0.747129 |
| Diagnostic | bat | 박쥐 | 0.786862 |
| Diagnostic | bat | baseball bat | 0.921865 |

The diagnostic rows demonstrate why CATEGORY context and an uncertainty outcome are required for polysemous labels.
