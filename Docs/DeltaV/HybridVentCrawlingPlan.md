# Delta-V Hybrid Vent Crawling System Plan

## Goals and Constraints

This design introduces an RMC14-style vent crawling flow for Delta-V while preserving existing movement architecture:

- **Container-based traversal** between vent network nodes.
- **No physics step rewrites** (do not modify solver stepping behavior).
- **Integration with existing movement stack** (`SharedMoverController`, standing/collision state, mob collision).
- **Normal movement suppressed while vent crawling**.
- **Standing/collision state restored cleanly on exit**.

## Existing Delta-V / SS14 Architecture to Hook Into

### Movement entry point
- `SharedMoverController.HandleMobMovement()` is the central movement execution path used by both client and server mover controllers.
- It already short-circuits movement if `InputMoverComponent.CanMove` is false.

Integration implication: vent crawling should primarily gate movement by toggling `CanMove` and by providing a dedicated vent-crawl update path outside normal physics walking.

### Standing and collision shape/state
- `StandingStateSystem.Down()` and `Stand()` adjust standing visuals and collision masks (`MidImpassable` layer removal/restoration on hard fixtures).
- `CrawlUnderObjectsSystem` already demonstrates safe fixture mask mutation and restoration through saved fixture-mask snapshots (`ChangedFixtures`).

Integration implication: entering vent crawl should transition entity to a down-like/compact state and preserve/restore mask data deterministically.

### Mob collision subsystem
- `SharedMobCollisionSystem` handles entity-vs-entity push behavior and uses cancellable events (`AttemptMobCollideEvent`, `AttemptMobTargetCollideEvent`).
- `StandingStateSystem` already cancels these events when down.

Integration implication: vent crawling entities should opt out of push interactions via event cancellation and/or movement suppression components.

## New Components

### 1) `VentCrawlerComponent` (capability marker)
**Purpose:** Marks entities allowed to use vent crawling.

**Suggested fields:**
- `EnterDelay` / `ExitDelay` (`TimeSpan`) for do-afters.
- `RequiredStandingState` (bool or enum) to force transitions.
- Optional restrictions (e.g., max body size, gear tags).

**Notes:** Mirrors role of `CrawlerComponent` but for vent-network traversal capability.

---

### 2) `VentCrawlableComponent` (network node marker)
**Purpose:** Added to vent/pipe entities that can act as crawl nodes.

**Suggested fields:**
- Directional connectivity bitmask/list (cardinals).
- `ContainerId` for occupants (e.g., `VentCrawlContainer`).
- Optional node type metadata (entry, junction, terminal).

**Notes:** This component defines the topological graph for crawl movement.

---

### 3) `VentCrawlingComponent` (runtime state)
**Purpose:** Added to crawling entity while active in vent network.

**Suggested fields:**
- `CurrentNode` (`EntityUid`) and `LastNode`.
- `TargetDirection` / requested direction.
- `InTransition` bool.
- Snapshot fields for restore:
  - prior `InputMover.CanMove`,
  - prior standing state,
  - prior collision mask changes (fixture map similar to `ChangedFixtures`).
- `ContainerId` / holder reference.

**Notes:** This component is the authoritative runtime state and is removed on clean exit.

## New Events

### 4) `VentEnterAttemptEvent` (cancellable)
Raised before enter starts. Used by systems to deny vent entry (buckled, stunned, oversized, blocked vent, etc.).

### 5) `VentEnterDoAfterEvent`
Completes the transition into vent crawling if uninterrupted.

### 6) `VentExitDoAfterEvent`
Completes exit and restores normal locomotion/collision state.

## Systems and Responsibilities

### A) `SharedVentCrawlingSystem` (new, shared)
Core orchestrator, responsible for:

1. **Entry flow**
   - Validate `VentCrawlerComponent` + nearby `VentCrawlableComponent`.
   - Raise `VentEnterAttemptEvent`.
   - Start `VentEnterDoAfterEvent`.
   - On success:
     - Add `VentCrawlingComponent`.
     - Insert entity into vent node container.
     - Suppress normal movement (`InputMover.CanMove = false`).
     - Transition to compact/down state and apply vent-specific collision filtering.

2. **Traversal flow**
   - Consume directional input (or explicit vent movement actions).
   - Resolve next connected node from `VentCrawlableComponent` connectivity.
   - Move occupant by container transfer node-to-node.
   - Keep transform/networked state synchronized for prediction/visibility.

3. **Exit flow**
   - Start `VentExitDoAfterEvent` at valid exit node.
   - On success: remove from container, place in world, remove `VentCrawlingComponent`, restore movement + standing + collision masks.

4. **Failure/cleanup flow**
   - On node delete/container invalidation/map shutdown: forced safe exit fallback.
   - Ensure all temporary mask/standing/movement overrides are reverted.

---

### B) `VentCrawlableGraphSystem` (optional helper)
If vent graphs are complex, isolate:
- connectivity queries,
- nearest entry lookup,
- node validation and path stepping.

This keeps `SharedVentCrawlingSystem` focused on state transitions and container operations.

## Exact Integration Points in Current Architecture

### 1) Movement suppression without physics-step edits

**Primary hook:** `InputMoverComponent.CanMove`.
- Set to `false` on vent-enter success.
- Restore previous value on vent-exit completion.

Why this fits:
- `SharedMoverController.HandleMobMovement()` already returns early when movement is disallowed.
- No changes required to solver stepping.

**Optional additional hook:** subscribe to `UpdateCanMoveEvent` and cancel while `VentCrawlingComponent` exists (defensive consistency with action blocker updates).

### 2) Shared mover integration (clean separation)

Add a vent-crawl prepass in mover flow that **does not alter stepping logic**:
- In `SharedMoverController.HandleMobMovement()`, near early gating branch, call a vent-crawl system method such as `TryHandleVentCrawlMovement(uid, mover, frameTime)`.
- If entity is vent-crawling, consume input for node traversal and return early from normal walking path.

This mirrors how tile movement is already handled as an early branch (`_tileMovement.TryTick(...)`) while leaving the rest of physics movement untouched.

### 3) Standing/collision transitions

**On enter:**
- Use `StandingStateSystem.Down()` (or equivalent forced compact state policy).
- Apply vent-specific mask edits in a reversible way (pattern after `CrawlUnderObjectsComponent.ChangedFixtures`).

**On exit:**
- Revert temporary fixture mask changes.
- Use `StandingStateSystem.Stand(..., force: true)` if policy requires automatic stand restore.

### 4) Mob push/collision exclusion

While `VentCrawlingComponent` is active:
- Cancel `AttemptMobCollideEvent` and `AttemptMobTargetCollideEvent`.
- Optionally ensure vent-crawling fixture masks exclude standard mob collision layers.

This prevents vent occupants from participating in corridor push mechanics.

### 5) Container traversal mechanics

Use robust container transfer for occupant movement:
- Occupant is inside vent-node container while crawling.
- Directional step = transfer to adjacent node container determined by `VentCrawlableComponent` connectivity.
- Exit = remove from container and place at node/world coordinates.

This avoids direct physics locomotion and cleanly models movement through sealed network entities.

## Suggested File/Type Additions

- `Content.Shared/_DV/VentCrawling/Components/VentCrawlerComponent.cs`
- `Content.Shared/_DV/VentCrawling/Components/VentCrawlableComponent.cs`
- `Content.Shared/_DV/VentCrawling/Components/VentCrawlingComponent.cs`
- `Content.Shared/_DV/VentCrawling/Events/VentEnterAttemptEvent.cs`
- `Content.Shared/_DV/VentCrawling/Events/VentEnterDoAfterEvent.cs`
- `Content.Shared/_DV/VentCrawling/Events/VentExitDoAfterEvent.cs`
- `Content.Shared/_DV/VentCrawling/Systems/SharedVentCrawlingSystem.cs`
- `Content.Server/_DV/VentCrawling/Systems/VentCrawlingSystem.cs` (server authority for container traversal)
- `Content.Client/_DV/VentCrawling/Systems/VentCrawlingSystem.cs` (prediction/UI feedback where needed)

## Ordering and Lifecycle Expectations

1. Player requests vent entry.
2. `VentEnterAttemptEvent` gates restrictions.
3. Enter do-after completes.
4. Runtime state activated (`VentCrawlingComponent`), movement suppressed, entity containerized.
5. Directional traversal moves entity container-to-container.
6. Exit requested, exit do-after completes.
7. Entity de-containerized, standing/collision/movement state restored.
8. Runtime component removed.

## Risk Controls

- Store and restore previous movement flags and fixture masks (never assume defaults).
- Force cleanup on interruption/deletion to avoid stuck `CanMove=false` or stale masks.
- Keep all vent traversal logic outside physics stepper internals.
- Keep mover integration as an early-return branch only.

## Minimal Initial Milestone

1. Implement components + enter/exit do-afters.
2. Single-step traversal between directly adjacent `VentCrawlable` nodes.
3. Integrate early-return branch in `SharedMoverController` for vent-crawlers.
4. Add tests for enter denial, traversal, interrupted exit, and state restoration.
