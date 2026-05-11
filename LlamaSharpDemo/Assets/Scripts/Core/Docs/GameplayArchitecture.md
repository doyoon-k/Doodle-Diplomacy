# Gameplay Architecture (Refactor Baseline)

## Scope
- This document covers gameplay runtime code only.
- `Assets/Scripts/AI` and `Assets/Scripts/LLM` are treated as external providers from gameplay's perspective.

## Dependency Rules
- `GameplayModeHost` is the runtime entry point for mode lifecycle and scene interaction routing.
- New gameplay modes implement `IGameplayMode` and receive dependencies through `GameplayModeContext`.
- `SceneReferenceHub` is the authoritative inspector-wired reference list for scene services. Do not create missing runtime components as fallbacks.
- `LegacyRoundModeAdapter` preserves the current prototype loop by forwarding mode interactions to `RoundManager`.
- `GameplayModeHost` configures `InteractionManager` through `SceneReferenceHub` so scene clicks route to the active mode.
- `RoundManager` orchestrates round flow only. It does not call AI pipeline implementation details directly.
- `RoundManager` delegates startup flow, state versioning, state entry routing, player action handling, input routing, inspector interaction binding, UI hints, camera mode application, interaction gates, drawing locks, preview terminal presentation, configured text lookup, and intro sequence preparation to focused round services.
- `RoundStateEntryActions` is only a dispatcher. State-specific entry behavior lives in opening, preparation, preview, and interpreter entry action classes.
- State entry action classes consume narrow context interfaces (`IRoundOpeningStateEntryContext`, `IRoundPreparationStateEntryContext`, `IRoundPreviewStateEntryContext`, `IRoundInterpreterStateEntryContext`) through `RoundStateEntryContextAdapters` instead of the full round context.
- Gameplay accesses AI through `IRoundAiGateway` only.
- `AIPipelineRoundAiGateway` is the single adapter that binds gameplay to `AIPipelineBridge`.
- `InteractionManager` interaction toggling is policy driven via `IInteractionPolicy` + `InteractionStateContext`.
- The current legacy mode uses `LegacyRoundInteractionPolicy`, which delegates to `InteractionStatePolicy`.
- `DrawingBoardController` owns drawing input/canvas/history/preview lifecycle only.
- Drawing input device polling is isolated in `DrawingInputReader`.
- Drawing surface UV and BoxCollider projection math is isolated in `DrawingSurfaceMapper`.
- Drawing brush preview renderer/material/mesh lifecycle is isolated in `DrawingBrushPreview`.
- Drawing stroke history capture is isolated in `DrawingStrokeHistory`.
- Drawing display texture composition is isolated in `DrawingDisplayComposer`.
- Drawing original surface texture sampling is isolated in `DrawingSurfaceTextureSampler`.
- Drawing board runtime material binding is isolated in `DrawingBoardMaterialBinding`.
- `DrawingExportBridge` is the drawing-to-AI handoff point; drawing module does not reference LLM classes.
- Gameplay modes access drawing through `IDrawingFeature`; drawing texture internals stay behind export methods rather than direct texture exposure.
- Core drawing locks use `RoundDrawingInteractionGate` over `IDrawingFeature`, not `DrawingBoardController` internals.
- Gameplay modes access camera through `ICameraModeService`; core round flow applies camera intents through `RoundCameraModeApplier`.
- Gameplay modes expose interaction state application through `IInteractionStateService`; `RoundInteractionGate` no longer depends on `InteractionManager` directly.
- UI controllers subscribe to `GameplayModeHost` state first and keep `RoundManager.OnStateChanged` only as a compatibility fallback.
- Physical tablet controls access drawing commands and visual state through `IDrawingControlFeature`, while keeping the serialized `DrawingBoardController` scene reference for inspector compatibility.
- Physical tablet pointer input is isolated in `TabletControlInputReader`.
- Physical tablet button command mapping is isolated in `TabletControlCommands`.
- Physical tablet hit targets use `TabletControlTarget`.

## Runtime Component Map (GameScene)
- `GameManager`
  - `SceneReferenceHub` (explicit scene dependency map)
  - `GameplayModeHost` (active gameplay mode lifecycle/router)
  - `LegacyRoundModeAdapter` (current Doodle Diplomacy mode)
  - `RoundManager` (state machine orchestration)
  - `ScoreManager`
  - `AIPipelineBridge` (external AI runtime)
  - `GamePipelineRunner`
- `RoundManager` references:
  - `IInteractionStateService`, `ICameraModeService`
  - `IDrawingFeature` injected from `GameplayModeContext`; serialized `DrawingBoardController` remains only as a legacy inspector fallback.
  - Serialized `InteractionManager` and `CameraController` remain only as legacy inspector fallbacks.
  - UI controllers (`TitleScreenController`, `PreviewButtonPanel`, `EndingController`)
  - terminal/monitor/subtitle/alien reaction components
  - focused helpers (`RoundStartupFlow`, `RoundStateMachine`, `RoundStateEntryActions`, `RoundOpeningStateEntryActions`, `RoundPreparationStateEntryActions`, `RoundPreviewStateEntryActions`, `RoundInterpreterStateEntryActions`, `RoundPlayerActionHandler`, `RoundInputRouter`, `RoundInteractableEventBinder`, `RoundHintPresenter`, `RoundCameraModeApplier`, `RoundInteractionGate`, `RoundDrawingInteractionGate`, `RoundPreviewTerminalPresenter`, `RoundTextProvider`, `RoundIntroSequenceProvider`)
- `InteractionManager` controls clickability of `InteractableObject` instances through the active `IInteractionPolicy`, then routes accepted clicks to `GameplayModeHost`.
- `RoundManager` only binds legacy `InteractableObject.OnInteracted` UnityEvents when no `GameplayModeHost` route is available.
- `CameraController` uses preset transitions + free-look assist helpers:
  - `CameraHoverFocusController`
  - `CameraEdgeBrowseController`

## Removed/Isolated Features
- Sketch guide, sticker, and drawing analysis panel code paths were removed from gameplay runtime.
- Debug-only runtime helpers (`GameTestStarter`, `DebugStateAdvancer`, `WordPairPoolQualityInspector`) were removed from `GameScene`.

## Allowed Cross-Module References
- `Gameplay` -> `Core`, `Interaction`, `Camera`, `Drawing`, `Dialogue`, `UI` through narrow service interfaces/context.
- `Core` -> `Interaction`, `Camera`, `UI`, `Drawing` (orchestration only)
- `Drawing` -> no `LLM` direct reference
- `UI` -> `Core` state APIs only
- `Interaction` -> `Core` state/context types and `GameplayModeHost` for routing only

## Notes
- Scene wiring is authoritative. When adding gameplay components, connect them through `SceneReferenceHub` instead of searching or creating fallbacks at runtime.
- Runtime material/texture instances may be created for canvas display state, but asset references, shaders, scene components, and gameplay services must be inspector-wired.
- New gameplay-to-AI calls must be added to `IRoundAiGateway` first, then implemented in `AIPipelineRoundAiGateway`.
