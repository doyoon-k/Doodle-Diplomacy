# First Contact — AI Pipeline Flow

> **v0.1 | 2026.04**
> 게임 내 AI 파이프라인의 실행 순서, 데이터 흐름, 상호 연결을 설명하는 문서

---

## 1. 전체 파이프라인 흐름도

```mermaid
flowchart TB
    subgraph Round["<b>ROUND LOOP</b> &nbsp; RoundManager"]
        direction TB

        P0["<b>Phase 0</b><br/>사물 이미지 생성<br/><i>Stable Diffusion txt2img</i>"]
        DRAW(["플레이어가 그림을 그림<br/><i>DrawingCanvas</i>"])
        P1["<b>Phase 1</b><br/>VLM 그림 해석<br/><i>VisionImageSummaryPipeline</i>"]
        P2["<b>Phase 2</b><br/>부관 프리뷰<br/><i>AdjutantPreviewPipeline</i>"]
        P3["<b>Phase 3</b><br/>외계인 판정<br/><i>JudgmentPipeline</i>"]
        P4["<b>Phase 4</b><br/>텔레파시 출력<br/><i>후처리 (LLM 호출 없음)</i>"]

        P0 --> DRAW
        DRAW --> P1
        P1 -->|LastAnalysis| P2
        P1 -->|LastAnalysis| P3
        P3 -->|scene_reading + judgment_reason| P4
    end

    style P0 fill:#4a6741,stroke:#6b8f63,color:#fff
    style DRAW fill:#555,stroke:#888,color:#fff
    style P1 fill:#3b5998,stroke:#5b79b8,color:#fff
    style P2 fill:#8b6914,stroke:#ab8934,color:#fff
    style P3 fill:#8b1a1a,stroke:#ab3a3a,color:#fff
    style P4 fill:#4a2d6b,stroke:#6a4d8b,color:#fff
```

### 호출 경로

```mermaid
flowchart LR
    subgraph preview["GetPreview()"]
        direction LR
        A1["Phase 1<br/>VLM"] --> A2["Phase 2<br/>부관"]
    end

    subgraph judgment["GetJudgment()"]
        direction LR
        B1["Phase 1<br/>VLM<br/><i>캐시 있으면 스킵</i>"] --> B3["Phase 3<br/>판정"]
        B3 --> B4["Phase 4<br/>텔레파시"]
    end

    style A1 fill:#3b5998,color:#fff
    style A2 fill:#8b6914,color:#fff
    style B1 fill:#3b5998,color:#fff
    style B3 fill:#8b1a1a,color:#fff
    style B4 fill:#4a2d6b,color:#fff
```

> [!info] 캐싱 규칙
> Phase 1은 `LastAnalysis`가 이미 존재하면 재실행하지 않는다. `GetPreview()`가 먼저 호출된 경우, 이후 `GetJudgment()`는 Phase 1을 스킵하고 캐시된 해석을 재사용한다.

---

## 2. 오케스트레이터: AIPipelineBridge

`AIPipelineBridge` (`Assets/Scripts/AI/AIPipelineBridge.cs`)는 모든 AI 파이프라인 호출을 관장하는 싱글턴 MonoBehaviour이다.

> [!abstract] 핵심 역할
> - 각 파이프라인에 필요한 `PipelineState`를 조립하여 `GamePipelineRunner`에 넘김
> - 파이프라인 결과를 파싱하여 `Last*` 프로퍼티에 저장
> - 텔레파시 출력에 신호 왜곡(corruption) 후처리 적용
> - 폴백(fallback) 로직으로 파이프라인 누락/실패 시 대체 텍스트 생성

| 메서드 | 실행 파이프라인 | 호출 시점 |
|--------|:---:|-----------|
| `GenerateObjects()` | SD txt2img ×2 | 라운드 시작 시 |
| `GetPreview()` | VLM → 부관 프리뷰 | 제출 전 프리뷰 요청 시 |
| `GetJudgment()` | VLM → 판정 → 텔레파시 후처리 | 제출 후 |
| `GetTelepathy()` | 없음 (캐시 반환) | 판정 이후 텔레파시 표시 시 |

---

## 3. Phase 0 — 사물 이미지 생성

```mermaid
flowchart LR
    subgraph SD["Stable Diffusion Sidecar"]
        direction TB
        PA["objectPromptA<br/><code>a glowing alien artifact cube...</code>"]
        PB["objectPromptB<br/><code>an alien crystal sphere...</code>"]
    end

    PA -->|txt2img| TA["Texture2D A"]
    PB -->|txt2img| TB["Texture2D B"]
    TA --> MON["monitorDisplay<br/>.ShowObjects(A, B)"]
    TB --> MON

    style SD fill:#4a6741,stroke:#6b8f63,color:#fff
    style TA fill:#2d4a2d,color:#fff
    style TB fill:#2d4a2d,color:#fff
    style MON fill:#333,color:#fff
```

| 항목 | 값 |
|------|-----|
| **엔진** | stable-diffusion.cpp sidecar process |
| **입력** | `objectPromptA`, `objectPromptB` (Inspector 설정) |
| **출력** | `_lastObjTexA`, `_lastObjTexB` (Texture2D) |
| **타임아웃** | 120초 |
| **사전 준비** | `EnsureObjectGenerationPreparation()` — SD 서버 prewarm |

---

## 4. Phase 1 — VLM 그림 해석

> **파이프라인**: `VisionImageSummaryPipeline`
> **프로필**: `AlianImageInterpretationProfile` (VLM + mmproj)

```mermaid
flowchart TB
    subgraph input["입력 PipelineState"]
        I1["<code>reference_image</code><br/>Texture2D — 플레이어 그림"]
        I2["<code>target_objects</code><br/><i>Object A: ... / Object B: ...</i>"]
    end

    subgraph pipeline["VisionImageSummaryPipeline"]
        direction TB
        STEP["<b>AnalyzeReferenceImage</b><br/>stepKind: CompletionLlm<br/>useVision: true"]
        SYS["System Prompt<br/><i>Treat as rough sketch...<br/>describe visible scene...<br/>end with overall interpretation</i>"]
        USR["User Prompt<br/><code>Prompt objects: { {target_objects} }<br/>Generate a full general interpretation...</code>"]
    end

    subgraph output["출력"]
        OUT["<code>response</code><br/><i>Overall, this looks like a human<br/>offering the crystal to the cube...</i>"]
        CACHE["<b>LastAnalysis</b> = response"]
    end

    input --> pipeline
    pipeline --> output
    OUT --> CACHE

    style input fill:#1a3a5c,color:#fff
    style pipeline fill:#3b5998,color:#fff
    style output fill:#1a3a5c,color:#fff
```

> [!important] 특이사항
> - `LastAnalysis`가 이미 있으면 **스킵** (Phase 2, 3에서 캐시 재사용)
> - 빈 캔버스 감지 시 `"(blank drawing)"` 설정 후 즉시 종료
> - **temperature: 0.2** — 낮은 창의성, 사실적 묘사 유도
> - VLM projector: `mmproj-model-f16-4B.gguf`

---

## 5. Phase 2 — 부관 프리뷰

> **파이프라인**: `AdjutantPreviewPipeline`
> **프로필**: `HumanImageHintProfile`

```mermaid
flowchart TB
    ENSURE["EnsureGeneralInterpretation<br/><i>Phase 1 실행 또는 캐시 사용</i>"]

    subgraph input["입력 PipelineState"]
        I1["<code>analysis</code><br/>LastAnalysis (Phase 1 결과)"]
        I2["<code>reference_image</code><br/>Texture2D — 플레이어 그림"]
        I3["<code>target_objects</code><br/>Object A: ... / Object B: ..."]
    end

    subgraph pipeline["AdjutantPreviewPipeline"]
        STEP["<b>AdjutantPreview</b><br/>stepKind: CompletionLlm<br/>useVision: true<br/>requireImage: false"]
        SYS["System Prompt<br/><i>You are a human adjutant...<br/>speak as uncertain observer...<br/>Do NOT talk about alien preferences</i>"]
    end

    subgraph output["출력"]
        RAW["<code>response</code><br/><i>It looks like someone is placing<br/>the crystal near the cube...</i>"]
        SAN["SanitizePreviewDialogue()<br/><i>접두사/부적절 질문 제거</i>"]
        FINAL["<b>LastPreviewDialogue</b>"]
    end

    ENSURE --> input
    input --> pipeline
    pipeline --> RAW
    RAW --> SAN
    SAN --> FINAL

    style ENSURE fill:#555,color:#fff
    style input fill:#5a4a14,color:#fff
    style pipeline fill:#8b6914,color:#fff
    style output fill:#5a4a14,color:#fff
```

> [!tip] 폴백 로직
> 파이프라인 미설정 또는 실패 시 `BuildFallbackPreviewDialogue()` 실행:
> - blank drawing → *"I cannot read any marks on the canvas yet..."*
> - 그 외 → *"It looks like {analysis}. Is that what you intended?"*

> [!example] 후처리: SanitizePreviewDialogue()
> LLM이 종종 생성하는 불필요한 패턴을 제거:
> - `"Here is..."`, `"Adjutant:"` 등 **접두사 제거**
> - `"Do you want me to elaborate?"` 등 **부적절한 질문 제거** → `"Does that match what you intended?"` 으로 교체

---

## 6. Phase 3 — 외계인 판정

> **파이프라인**: `JudgmentPipeline`
> **프로필**: `JudgmentProfile`

```mermaid
flowchart TB
    ENSURE["EnsureGeneralInterpretation<br/><i>Phase 1 실행 또는 캐시 사용</i>"]

    subgraph input["입력 PipelineState"]
        I1["<code>analysis</code><br/>LastAnalysis (Phase 1 결과)"]
        I2["<code>target_objects</code><br/>Object A: ... / Object B: ..."]
        I3["<code>alien_personality</code><br/><i>cooperative, empathetic</i>"]
    end

    subgraph pipeline["JudgmentPipeline"]
        STEP["<b>AlienJudgment</b><br/>stepKind: JsonLlm<br/>useVision: true<br/>requireImage: true<br/>jsonMaxRetries: 2"]
        SYS["System Prompt<br/><i>You are an alien delegation...<br/>Axis1: cooperation-vs-dominance<br/>Axis2: efficiency-vs-empathy</i>"]
        SCHEMA["JSON Schema:<br/><code>axis1</code> enum[5] / <code>axis2</code> enum[5]<br/><code>scene_reading</code> string<br/><code>judgment_reason</code> string"]
    end

    subgraph output["출력 PipelineState"]
        O1["<code>axis1</code>: <i>satisfied</i>"]
        O2["<code>axis2</code>: <i>neutral</i>"]
        O3["<code>scene_reading</code>: <i>The human is offering...</i>"]
        O4["<code>judgment_reason</code>: <i>The gesture reads as sincere...</i>"]
    end

    subgraph store["저장"]
        S1["LastAxis1 / LastAxis2<br/><i>ParseSatisfaction()으로 enum 변환</i>"]
        S2["LastJudgmentSceneReading"]
        S3["LastJudgmentReason"]
    end

    ENSURE --> input
    input --> pipeline
    pipeline --> output
    output --> store

    style ENSURE fill:#555,color:#fff
    style input fill:#5a1a1a,color:#fff
    style pipeline fill:#8b1a1a,color:#fff
    style output fill:#5a1a1a,color:#fff
    style store fill:#3a0a0a,color:#fff
```

### SatisfactionLevel 매핑

```mermaid
flowchart LR
    VD["very_dissatisfied<br/><b>-2</b>"] --- D["dissatisfied<br/><b>-1</b>"] --- N["neutral<br/><b>0</b>"] --- S["satisfied<br/><b>+1</b>"] --- VS["very_satisfied<br/><b>+2</b>"]

    style VD fill:#8b0000,color:#fff
    style D fill:#a04040,color:#fff
    style N fill:#666,color:#fff
    style S fill:#407040,color:#fff
    style VS fill:#006400,color:#fff
```

### AlienPersonality → 라벨 변환

```mermaid
flowchart LR
    subgraph axis1["축 1: cooperationVsDomination"]
        A1["< 0"] -->|라벨| L1["<b>cooperative</b>"]
        A2["≥ 0"] -->|라벨| L2["<b>dominant</b>"]
    end

    subgraph axis2["축 2: efficiencyVsEmpathy"]
        B1["< 0"] -->|라벨| L3["<b>efficiency-driven</b>"]
        B2["≥ 0"] -->|라벨| L4["<b>empathetic</b>"]
    end

    style L1 fill:#2d6a4f,color:#fff
    style L2 fill:#6a2d2d,color:#fff
    style L3 fill:#2d4a6a,color:#fff
    style L4 fill:#6a4a2d,color:#fff
```

> [!example] 예시
> `cooperationVsDomination = -0.7`, `efficiencyVsEmpathy = 0.3`
> → `"cooperative, empathetic"`

---

## 7. Phase 4 — 텔레파시 출력

> [!warning] 현재 독립 LLM 호출 없이, Phase 3의 결과를 후처리하여 생성한다.

```mermaid
flowchart TB
    subgraph build["① BuildFallbackJudgmentTranscript()"]
        direction TB
        CHECK{빈 그림?}
        CHECK -->|Yes| BLANK["고정 메시지 3줄<br/><i>No marks were detected...<br/>No relationship...<br/>A visible action is required.</i>"]
        CHECK -->|No| LINES["4줄 조립"]

        LINES --> L1["line 1: LastJudgmentSceneReading"]
        LINES --> L2["line 2: LastJudgmentReason"]
        LINES --> L3["line 3: 성향별 힌트 문장"]
        LINES --> L4["line 4: 만족도 요약"]
    end

    subgraph format["② FormatTelepathyTerminalOutput()"]
        direction TB
        HDR["헤더: <code>[TRANSLATOR v1.0]</code>"]
        PREFIX["각 줄 앞에 <code>> </code> 접두사"]
        CORRUPT["40% 라인에 신호 왜곡 적용<br/><i>1~3줄</i>"]
        CURSOR["마지막: <code>> _</code>"]
    end

    subgraph result["출력 예시"]
        EX["<code>[TRANSLATOR v1.0]<br/>> The human is offering the crystal sphere<br/>> The gesture reads as sinc... ~:~///<br/>> Emotional clarity and sincere care matter<br/>> The delegation [////] unconvinced.<br/>> _</code>"]
    end

    build --> format
    format --> result
    result --> LAST["<b>LastTelepathy</b>"]

    style build fill:#3a2050,color:#fff
    style format fill:#4a2d6b,color:#fff
    style result fill:#2a1040,color:#fff
    style LAST fill:#1a0830,color:#fff
```

### 신호 왜곡 (Corruption)

```mermaid
flowchart LR
    subgraph params["파라미터"]
        P1["corruptedLineRatio<br/><b>0.4</b>"]
        P2["corruptionStrength<br/><b>0.45</b>"]
        P3["minCorruptedLines<br/><b>1</b>"]
        P4["maxCorruptedLines<br/><b>3</b>"]
    end

    subgraph methods["왜곡 기법 (랜덤 선택)"]
        M1["RedactRandomWords<br/><i>단어 → [////]</i>"]
        M2["InsertStaticBurst<br/><i>중간에 ~:~ /// 삽입</i>"]
        M3["TruncateWithSignalDrop<br/><i>문장 잘림 + ... |||</i>"]
        M4["DistortWordWithNoise<br/><i>글자 → /|#~:.=-</i>"]
    end

    params --> methods

    style params fill:#3a2050,color:#fff
    style methods fill:#4a2d6b,color:#fff
```

### 성향별 힌트 문장

| 성향 라벨 | 힌트 문장 |
|:---:|-----------|
| cooperative + efficiency-driven | *"Clear cause and effect matters more than decoration."* |
| cooperative + empathetic | *"Emotional clarity and sincere care matter more than precision."* |
| dominant + efficiency-driven | *"Control, discipline, and obvious results matter most."* |
| dominant + empathetic | *"Recognition, emotional weight, and proper deference matter most."* |

> [!note] TelepathyPipeline에 대하여
> `TelepathyPipeline` (LLM 기반 외계인 대화 조각 생성) 에셋이 프로젝트에 존재하고 `telepathyPipeline` 필드에 연결되어 있지만, 현재 `GetTelepathyRoutine()`은 이를 호출하지 않고 `LastTelepathy` 캐시를 그대로 반환한다. 텔레파시 출력은 **판정 결과의 후처리**로만 생성된다.

---

## 8. PipelineState 키 레퍼런스

### 입력 키 (AIPipelineBridge가 주입)

```mermaid
flowchart LR
    subgraph bridge["AIPipelineBridge"]
        IMG["reference_image<br/><i>Texture2D</i>"]
        TGT["target_objects<br/><i>string</i>"]
        ANA["analysis<br/><i>string</i>"]
        PER["alien_personality<br/><i>string</i>"]
    end

    subgraph phases["주입 대상"]
        P1["Phase 1 VLM"]
        P2["Phase 2 부관"]
        P3["Phase 3 판정"]
    end

    IMG -->|Phase 1, 2| P1
    IMG -->|Phase 1, 2| P2
    TGT -->|Phase 1, 2, 3| P1
    TGT -->|Phase 1, 2, 3| P2
    TGT -->|Phase 1, 2, 3| P3
    ANA -->|Phase 2, 3| P2
    ANA -->|Phase 2, 3| P3
    PER -->|Phase 3 only| P3

    style bridge fill:#333,color:#fff
    style P1 fill:#3b5998,color:#fff
    style P2 fill:#8b6914,color:#fff
    style P3 fill:#8b1a1a,color:#fff
```

| 키 | 타입 | 설명 | 주입 시점 |
|----|------|------|:---------:|
| `reference_image` | Texture2D | 플레이어의 그림 텍스처 | Phase 1, 2 |
| `target_objects` | string | `"Object A: ...\nObject B: ..."` | Phase 1, 2, 3 |
| `analysis` | string | Phase 1의 VLM 해석 결과 | Phase 2, 3 |
| `alien_personality` | string | `"cooperative, empathetic"` 등 | Phase 3 |

### 출력 키 (파이프라인이 생성)

| 키 | 타입 | 생성 파이프라인 | 설명 |
|----|------|:---------:|------|
| `response` | string | VLM, 부관 | 일반 텍스트 응답 (CompletionLlm 기본 출력) |
| `axis1` | string | 판정 | 축1 만족도 (`very_dissatisfied` ~ `very_satisfied`) |
| `axis2` | string | 판정 | 축2 만족도 |
| `scene_reading` | string | 판정 | 그림에서 읽은 장면 해석 |
| `judgment_reason` | string | 판정 | 판정 이유 설명 |

### target_objects 가공 과정

```mermaid
flowchart TB
    RAW["SD 프롬프트<br/><code>a glowing alien artifact cube,<br/>dark background, dramatic lighting,<br/>product photo</code>"]
    SIMPLIFY["SimplifyTargetObjectPrompt()<br/><i>첫 번째 쉼표 앞까지 자름 → 관사 제거</i>"]
    RESULT["<code>glowing alien artifact cube</code>"]
    FINAL["최종 target_objects:<br/><code>Object A: glowing alien artifact cube<br/>Object B: alien crystal sphere</code>"]

    RAW --> SIMPLIFY --> RESULT --> FINAL

    style RAW fill:#333,color:#fff
    style SIMPLIFY fill:#555,color:#fff
    style RESULT fill:#444,color:#fff
    style FINAL fill:#333,color:#fff
```

---

## 9. LlmGenerationProfile 파라미터 비교

| 파라미터 | AlianImageInterp | HumanImageHint | Judgment | Telepathy |
|---------|:---:|:---:|:---:|:---:|
| **모델** | gemma-3-4b-q4 | gemma-3-4b-q4 | gemma-3-4b-q4 | gemma-3-4b-q4 |
| **VLM projector** | mmproj-f16-4B | mmproj-f16-4B | — | — |
| **temperature** | ==0.2== | ==0.2== | ==0.4== | ==0.85== |
| **top_p** | 0.9 | 0.9 | 0.9 | 0.95 |
| **top_k** | 40 | 40 | 40 | 40 |
| **repeat_penalty** | 1.05 | 1.05 | 1.1 | 1.1 |
| **num_predict** | 400 | 400 | 400 | 400 |
| **contextSize** | 1024 | 1024 | 1024 | 1024 |
| **JSON 스키마** | — | — | 4필드 | — |

> [!tip] Temperature 설계 의도
> temperature가 단계를 거칠수록 높아진다:
> - **Phase 1·2** (0.2) — 해석은 정확해야 함
> - **Phase 3** (0.4) — 판정에는 약간의 변동성
> - **Telepathy** (0.85) — 대화 조각에는 높은 다양성

---

## 10. 레거시 파이프라인

현재 게임 루프에서 사용하지 않지만 프로젝트에 남아 있는 파이프라인들:

> [!warning] 사용되지 않는 에셋
> | 파이프라인 | 설명 | 비고 |
> |-----------|------|------|
> | `TelepathyPipeline` | LLM으로 외계인 내부 대화 조각 생성 | 필드에 연결되어 있지만 호출되지 않음 |
> | `WordsSelectionPipeline` | JSON 기반 단어 선택 | 이전 프로토타입 잔여물 |
> | `CharacterItemUsePipeline` | 캐릭터 아이템 사용 시 스탯 변경 + 스킬 생성 | 이전 프로토타입 잔여물 |
