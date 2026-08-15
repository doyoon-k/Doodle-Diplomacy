# Gameplay and Presentation Transition Architecture

This project frequently moves between authored presentation, camera work, and
interactive gameplay without loading a new scene. Those transitions must retain
intentional world state while discarding temporary sequence state.

## Ownership

- `GameFlowDirector` decides why the outgoing entry is leaving.
- `GameplayModeHost` owns the active mode and delivers the exit reason.
- A gameplay mode maps that reason to a domain-specific handoff policy.
- A sequence controller stops coroutines, input locks, dialogue, and other
  temporary presentation state.
- A dedicated continuity object owns authored props whose placement must survive
  the presentation-to-gameplay handoff.
- `CameraController` owns fixed gameplay shots. Fixed shots stay under its stable
  rig; only device views that intentionally travel with a prop may be parented to
  that prop.

No runtime transition may rebuild authored scene geometry, props, cameras, or
placement anchors.

## Exit contract

`GameplayModeExitReason` distinguishes these cases:

- `Completed`: the player reached the intended next phase. Commit the authored
  handoff pose/state needed by that phase.
- `Cancelled`: the current flow was stopped. Restore its entry state.
- `Replaced`: another mode superseded the current mode without completing it.
  Restore its entry state unless that replacement explicitly represents a
  completed handoff.
- `HostDestroyed`: the host or scene is being torn down. Release temporary state
  and restore safely.

Legacy modes may continue implementing `IGameplayMode.Exit()`. Modes whose final
state depends on the reason also implement `IGameplayModeExitHandler`.

The caller that knows the player's intent must supply the reason. In particular,
normal `LoadNextEntry` / `CompleteCurrentEntry` transitions and the Facility's
same-scene intro-to-translation transition use `Completed`; generic mode swaps do
not imply completion.

## Facility equipment continuity

The terminal and tablet have four authored concepts:

1. The equipment objects themselves.
2. Briefing-room placement anchors.
3. Carry sockets on the characters.
4. Meeting-room placement anchors.

`FirstContactEquipmentContinuity` is the single runtime owner of their movement
and interaction snapshots. During the carry sequence it temporarily disables
colliders/interactions and follows the sockets. It restores those interaction
states when the equipment is set down or the sequence exits.

On a completed Facility handoff, the objects are committed to their meeting-room
anchors. On cancellation, replacement, or teardown, they return to their
briefing-room anchors. Sequence cleanup must never silently choose between these
two outcomes without an exit reason.

## Camera hierarchy rule

Fixed shots (`Default`, `FreeLook`, and `AlienReaction`) must be descendants of
the stable `CameraController` rig. Parenting a fixed shot to an animated actor or
prop makes authored entrance motion move the camera itself, producing an
unintended sweep before Cinemachine blending even begins.

The tablet and terminal cameras are intentional exceptions because their view
origins travel with those devices. `ValidateStableShotHierarchy` enforces the
fixed-shot portion of this contract, and the Facility scene configuration test
guards the authored hierarchy.

## Adding a future interleaved sequence

Before implementing a new transition:

1. Identify the outgoing mode, incoming mode, and the code that knows whether
   the transition completed or was interrupted.
2. List every persistent scene object and assign exactly one continuity owner.
3. Author start, carry/motion, and committed end anchors in the scene or prefab.
4. Keep transient cleanup separate from commit/reset behavior.
5. Keep fixed cameras on a stable rig and animate subjects independently.
6. Add one lifecycle test for completed versus interrupted exit, plus one scene
   configuration test for authored references and camera hierarchy.

This is a deliberately small lifecycle seam rather than a replacement for every
existing sequence. New modes can adopt it incrementally while legacy modes keep
their current `Exit()` behavior.
