# FIRST CONTACT — 구현 계획서

Unity URP · 기존 프로젝트 확장 · v0.1 — 2026.04

---

## 1. 씬 세팅

### 1.1 씬 구성

| 씬 | 용도 | 설명 |
|---|---|---|
| TitleScene | 타이틀 화면 | 재플레이 시 진입점. 시작 버튼만 있는 심플한 씬 |
| GameScene | 메인 게임플레이 | 인트로 대사, 회담장, 5라운드, 결말 모두 포함 |

### 1.2 GameScene 하이어라키

```
GameScene
├─ GameManager              — RoundManager, ScoreManager, GameFlowController
├─ Environment
│   ├─ Room                 — 회담장 환경 (바닥, 벽, 조명 등)
│   ├─ SharedMonitor        — 공유 모니터 3D 오브젝트 + SharedMonitorDisplay
│   ├─ PlayerTable          — 테이블 + 태블릿 거치대
│   └─ InterpreterDesk      — 과학장교 책상 + 터미널 오브젝트
├─ Characters
│   ├─ Adjutant             — 부관 3D모델 + PortraitDisplay + InteractableObject
│   ├─ ScienceOfficer       — 과학장교 3D모델 + PortraitDisplay
│   └─ AlienGroup
│       ├─ AlienLeader      — 외계인 대표 + PortraitDisplay + InteractableObject
│       └─ AlienFollowers   — 외계인 수행원들
├─ Devices
│   ├─ Tablet               — TabletController + DrawingCanvas + InteractableObject
│   └─ InterpreterTerminal  — TerminalDisplay + InteractableObject
├─ UI
│   ├─ SubtitleCanvas       — 하단 자막 UI
│   ├─ ButtonCanvas         — 수정/제출 버튼 등 오버레이 UI
│   └─ EndingCanvas         — 결말 컷신 UI
├─ CameraRig                — CameraController
└─ DialogueManager          — DialogueSystem
```

---

## 2. 스크립트 모듈 목록

### 2.1 Phase 1 — 골격 시스템

게임 루프의 뼈대. 이것들이 없으면 나머지가 동작할 수 없다.

#### M1. RoundManager.cs

전체 게임 흐름을 제어하는 상태 머신. 모든 시스템의 허브.

| 항목 | 스펙 |
|---|---|
| 상태 | Intro, WaitingForRound, Presenting, Drawing, PreviewReady, Preview, Submitting, AlienReaction, InterpreterReady, Interpreter, Ending, Title |
| 책임 | 상태 전이 관리, 각 시스템에 진입/퇴장 이벤트 발행 |
| 의존성 | ScoreManager, CameraController, InteractionManager |
| 패턴 | Singleton, UnityEvent 기반 이벤트 발행 |

#### M2. ScoreManager.cs

라운드별 점수 누적 및 결말 분기 판정.

| 항목 | 스펙 |
|---|---|
| 입력 | 축별 만족도 카테고리 (AI 파이프라인에서 전달) |
| 처리 | 카테고리 → 점수 변환, 5라운드 가중치 적용, 누적 |
| 출력 | 현재 누적점수, 결말 타입 (EndingType enum) |
| 패턴 | ScriptableObject로 점수 구간 설정 분리 |

#### M3. CameraController.cs

카메라 위치/회전을 상태에 따라 전환.

| 항목 | 스펙 |
|---|---|
| 모드 | Default (1인칭 고정), FreeLook (좌우 시선), TabletView (태블릿 정면), TerminalZoom (해석기 확대) |
| 전환 | DOTween 또는 Cinemachine으로 부드러운 보간 |
| 입력 | FreeLook 모드에서 마우스 X축으로 시선 회전 (각도 제한 있음) |
| 의존성 | RoundManager의 상태 이벤트 |

#### M4. InteractionManager.cs

레이캐스트 기반 오브젝트 클릭 처리 및 상태별 활성화 제어.

| 항목 | 스펙 |
|---|---|
| 입력 | 마우스 클릭 → 레이캐스트 → InteractableObject 감지 |
| 활성화 제어 | RoundManager 상태에 따라 각 InteractableObject 활성화/비활성화 |
| 하이라이트 | 마우스 호버 시 상호작용 가능한 오브젝트 하이라이트 (선택적) |
| 패턴 | InteractableObject 컴포넌트를 각 오브젝트에 부착 |

#### M5. InteractableObject.cs

상호작용 가능한 오브젝트에 부착하는 컴포넌트.

| 항목 | 스펙 |
|---|---|
| 필드 | interactionID (enum: Alien, Tablet, Adjutant, Terminal), isActive (bool) |
| 이벤트 | OnInteracted (UnityEvent) — 클릭 시 발화 |
| 부착 대상 | AlienLeader, Tablet, Adjutant, InterpreterTerminal |

---

### 2.2 Phase 2 — 표현 시스템

플레이어에게 보이는 것들. 골격 위에 살을 붙인다.

#### M6. DialogueSystem.cs

대사 데이터를 순차 재생하는 시스템.

| 항목 | 스펙 |
|---|---|
| 입력 | DialogueSequence (ScriptableObject) — 대사 목록 |
| 표시 | 3D공간 텍스트 (WorldSpaceDialogue) 또는 하단자막 (SubtitleDisplay) |
| 진행 | 클릭으로 다음 대사, 타이핑 연출, 완료 시 콜백 |
| 데이터 | 캐릭터ID, 대사텍스트, 표시방식, 초상화ID, 진행조건 |

#### M7. WorldSpaceDialogue.cs

캐릭터 위치에 3D 텍스트를 표시하는 컴포넌트. 카메라를 향해 빌보드 처리.

#### M8. SubtitleDisplay.cs

화면 하단에 자막을 표시하는 UI 컴포넌트. 부관 나레이션 등에 사용.

#### M9. PortraitDisplay.cs

캐릭터 3D모델의 얼굴 부분에 2D 초상화 텍스처를 교체하는 컴포넌트.

| 항목 | 스펙 |
|---|---|
| 입력 | 표정 ID (enum 또는 string) |
| 처리 | 초상화 세트에서 해당 텍스처를 찾아 머티리얼에 적용 |
| 데이터 | PortraitSet (ScriptableObject) — 캐릭터별 표정 텍스처 모음 |

#### M10. SharedMonitorDisplay.cs

공유 모니터의 표시 내용을 제어.

| 항목 | 스펙 |
|---|---|
| 입력 | RoundManager 상태 이벤트 + 이미지 데이터 |
| 표시 모드 | Idle (빈 화면), Generating (SD 생성 과정), DisplayObjects (사물 2개), DisplaySubmission (제출물) |
| 연동 | 기존 SD 생성 시스템과 연결 필요 |

---

### 2.3 Phase 3 — 입력 장치

#### M11. TabletController.cs

태블릿의 물리적 동작 (들기/내려놓기) 및 상태 관리.

| 항목 | 스펙 |
|---|---|
| 상태 | OnTable, Raised, Neutral |
| 동작 | OnTable→Raised: 태블릿 들어올리기 애니메이션. Raised→Neutral: 완료 시 중간 위치로 이동 |
| 연동 | CameraController에 모드 전환 요청 |

#### M12. DrawingCanvas.cs

태블릿 표면에서의 드로잉/스티커 배치 처리. 레이캐스트 기반 입력.

| 항목 | 스펙 |
|---|---|
| 입력 | 태블릿 표면에 레이캐스트 → UV 좌표 변환 → RenderTexture에 그리기 |
| 기능 | 스티커 배치 (위치/크기/회전), 펜 드로잉, 지우기, 초기화 |
| 출력 | RenderTexture → Texture2D로 캡처 (VLM 전송용 + 모니터 표시용) |
| 비고 | 기존 드로잉 기능이 있다면 연동, 없다면 신규 구현 |

---

### 2.4 Phase 4 — 피드백 연출

#### M13. AlienReactionController.cs

외계인 반응 시퀀스를 제어. 판정 결과에 따라 표정 변화 + 나레이션 재생.

| 항목 | 스펙 |
|---|---|
| 입력 | 축별 만족도 카테고리 |
| 시퀀스 | 1) 외계인 고개 돌림 (초상화 교체) → 2) 웅성웅성 (표정변화 + 나레이션 자막) → 3) 최종 표정 → 4) 부관 나레이션 |
| 연동 | PortraitDisplay, DialogueSystem, RoundManager |

#### M14. TerminalDisplay.cs

해석기 터미널의 텍스트 표시 및 타이핑 연출.

| 항목 | 스펙 |
|---|---|
| 입력 | LLM이 생성한 텔레파시 조각 텍스트 |
| 연출 | 한 글자씩 타이핑되는 터미널 효과 (Coroutine) |
| 나가기 | 클릭 또는 ESC → CameraController에 복귀 요청 |
| 비고 | RenderTexture 또는 World Space Canvas로 구현 |

---

### 2.5 Phase 5 — AI 연동 & 마무리

#### M15. AIPipelineBridge.cs

기존 AI 파이프라인(SD, VLM, LLM)과 게임 루프를 연결하는 브리지 클래스.

| 항목 | 스펙 |
|---|---|
| 책임 | 기존 AI 기능을 RoundManager가 호출할 수 있는 인터페이스로 래핑 |
| 메서드 | GenerateObjects() → SD 사물 생성, GetPreview(Texture2D) → VLM 해석, GetJudgment(string, AlienProfile) → LLM 판정, GetTelepathy(string) → 해석기 텍스트 |
| 비고 | 기존 코드 구조에 맞춰 어댑터 패턴으로 구현 |

#### M16. EndingController.cs

결말 컷신 재생. 결말 타입에 따라 이미지 + 텍스트를 UI Canvas에 표시.

| 항목 | 스펙 |
|---|---|
| 입력 | EndingType (ScoreManager에서 전달) |
| 표시 | EndingCanvas에 배경 이미지 + 제목 + 설명 텍스트 |
| 데이터 | EndingData (ScriptableObject) — 결말별 에셋 |
| 종료 | 클릭 시 TitleScene으로 전환 |

#### M17. TitleScreenController.cs

타이틀 화면 UI 및 첫 플레이 판별.

| 항목 | 스펙 |
|---|---|
| 첫 플레이 판별 | PlayerPrefs로 플래그 관리 |
| 첫 플레이시 | TitleScene 건너뛰고 바로 GameScene 로드 |
| 재플레이시 | TitleScene에서 시작 버튼 → GameScene 로드 |

---

## 3. ScriptableObject 목록

에디터에서 편집 가능한 데이터 에셋.

| 이름 | 용도 | 주요 필드 |
|---|---|---|
| DialogueLine | 대사 한 줄 | characterID, text, displayMode, portraitID, advanceType |
| DialogueSequence | 대사 묶음 | List\<DialogueLine\>, onComplete callback ID |
| CharacterProfile | 캐릭터 정보 | name, model prefab, PortraitSet ref |
| PortraitSet | 초상화 모음 | Dictionary\<emotionID, Texture2D\> |
| AlienPersonality | 외계인 성향 | cooperationVsDomination (float), efficiencyVsEmpathy (float) |
| ScoreConfig | 점수 설정 | 결말 구간 경계값, 마지막라운드 가중치 |
| EndingData | 결말 데이터 | endingType, image, title, description |
| ObjectPool | 사물 풀 | List\<ObjectEntry\> 또는 조합 프리셋 |

---

## 4. 구현 순서 로드맵

### Phase 1 — 골격 (루프가 돌아가는 최소 단위)

목표: 상태 머신이 돌고, 오브젝트 클릭으로 상태가 전이되는 것을 확인.

| 순서 | 작업 | 산출물 |
|---|---|---|
| 1-1 | GameScene 씬 생성 + 회담장 환경 배치 (Placeholder) | 빈 회담장 공간 |
| 1-2 | RoundManager 상태머신 구현 | 상태 전이 로그 확인 |
| 1-3 | InteractableObject + InteractionManager | 클릭으로 상태 전이 확인 |
| 1-4 | CameraController 기본 모드 구현 | 시점 전환 확인 |
| 1-5 | ScoreManager | 점수 누적 + 결말 타입 출력 확인 |

> **Phase 1 완료 체크:** 콘솔 로그로 WaitingForRound → Drawing → PreviewReady → Preview → AlienReaction → InterpreterReady → WaitingForRound 순환이 확인되면 성공.

### Phase 2 — 표현 (보이는 것 만들기)

목표: 골격 위에 시각적 피드백을 붙여서 플레이 가능한 상태로 만들기.

| 순서 | 작업 | 산출물 |
|---|---|---|
| 2-1 | DialogueSystem + SubtitleDisplay + WorldSpaceDialogue | 인트로 대사 재생 확인 |
| 2-2 | 캐릭터 3D모델 배치 + PortraitDisplay | 표정 교체 확인 |
| 2-3 | SharedMonitorDisplay | 모니터에 이미지 표시 확인 |
| 2-4 | AlienReactionController | 외계인 반응 시퀀스 확인 |
| 2-5 | TerminalDisplay | 타이핑 연출 확인 |

> **Phase 2 완료 체크:** 인트로 대사가 나오고, 표정이 바뀌고, 모니터에 이미지가 뜨고, 터미널에 텍스트가 타이핑되면 성공.

### Phase 3 — 입력 장치 (태블릿)

목표: 플레이어가 실제로 그림을 그리고 제출할 수 있는 상태.

| 순서 | 작업 | 산출물 |
|---|---|---|
| 3-1 | TabletController (들기/내려놓기 애니메이션) | 태블릿 물리적 동작 확인 |
| 3-2 | DrawingCanvas (레이캐스트 입력 + 스티커 + 드로잉) | 태블릿에 그림 그리기 확인 |
| 3-3 | 수정/제출 버튼 UI + 흐름 연결 | 부관 프리뷰 → 수정/제출 흐름 확인 |

> **Phase 3 완료 체크:** 태블릿을 들고 그림을 그리고, 부관에게 보여주고, 수정하거나 제출하는 전체 흐름이 동작하면 성공.

### Phase 4 — AI 연동 & 완성

목표: 기존 AI 파이프라인과 연결해서 실제로 플레이 가능한 프로토타입 완성.

| 순서 | 작업 | 산출물 |
|---|---|---|
| 4-1 | AIPipelineBridge 구현 | AI 기능 호출 인터페이스 확인 |
| 4-2 | SharedMonitorDisplay와 SD 생성 연결 | 실제 사물 생성 확인 |
| 4-3 | VLM 프리뷰 연결 + LLM 판정 연결 | 실제 해석/판정 확인 |
| 4-4 | 해석기 텍스트 생성 연결 | 터미널 실제 출력 확인 |
| 4-5 | EndingController + TitleScreenController | 결말 → 타이틀 순환 확인 |
| 4-6 | 전체 5라운드 통합 테스트 | 플레이 가능한 프로토타입 |

> **Phase 4 완료 체크:** 첫 플레이부터 5라운드 완주 후 결말까지 도달하고, 타이틀에서 재플레이가 가능하면 프로토타입 완성.

---

## 5. 모듈 의존성 맵

화살표는 "의존한다"를 의미한다.

```
RoundManager (허브)
  ├→ ScoreManager
  ├→ CameraController
  ├→ InteractionManager → InteractableObject (x4)
  ├→ DialogueSystem → WorldSpaceDialogue / SubtitleDisplay
  ├→ SharedMonitorDisplay → AIPipelineBridge (SD)
  ├→ TabletController → DrawingCanvas
  ├→ AlienReactionController → PortraitDisplay / DialogueSystem
  ├→ TerminalDisplay → AIPipelineBridge (LLM)
  └→ EndingController

AIPipelineBridge (기존 AI 코드 래핑)
  ├→ SD 생성 모듈 (기존)
  ├→ VLM 해석 모듈 (기존)
  └→ LLM 판정 모듈 (기존)
```

---

## 6. 상태별 인터랙션 제어 매트릭스

| 상태 | 외계인 | 태블릿 | 부관 | 해석기 |
|---|---|---|---|---|
| WaitingForRound | ✅ 클릭 → 다음 라운드 | ❌ | ❌ | ❌ |
| Presenting | ❌ | ❌ | ❌ | ❌ |
| Drawing | ❌ | ✅ 레이캐스트 입력 | ❌ | ❌ |
| PreviewReady | ❌ | ✅ 재수정 가능 | ✅ 클릭 → 프리뷰 | ❌ |
| Preview | ❌ | ❌ | ❌ | ❌ |
| (수정/제출 선택) | ❌ | 수정 → Drawing | ❌ | ❌ |
| AlienReaction | ❌ | ❌ | ❌ | ❌ |
| InterpreterReady | ✅ 클릭 → 다음 라운드 | ❌ | ❌ | ✅ 클릭 → 확대 |
| Interpreter | ❌ | ❌ | ❌ | ✅ ESC/클릭 → 복귀 |
