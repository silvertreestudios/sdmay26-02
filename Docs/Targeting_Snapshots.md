# Targeting snapshot contract

`GridPublic.TargetingSnapshot` in `Assets/Scripts/Grid/TargetingSnapshot.cs` is a sealed,
immutable, query-scoped capture. Area membership, range highlighting, line of effect and candidate
selection use this one input. Unity object resolution is a separate lifetime-checked boundary.

## Construction and lifetime

Call `TargetingSnapshot.Capture(source, tiles, request, placement)` on Unity's main thread,
without yielding, after grid occupancy and transform projections have settled. If transforms
were edited since physics last synchronized, call `Physics.SyncTransforms()` before capture.
The low-level factory does not itself change physics synchronization policy. The production
`AreaTargetCapture.Capture` boundary synchronizes physics before calling it. Capture rejects null
containers, but accepts a cell-only `AreaTargetSource` and leaves request legality to evaluators.
It copies all request and placement fields, source position, x/z grid bounds, tile existence,
resolved grid obstruction, ordered occupant IDs, and exact map-collider ray outcomes.
No Tile, GameObject, Collider, mutable request, registry, delegate, or backing array escapes.

`Id` is a new Guid for each capture, NOT a world revision or content hash. The project has no
complete revision covering direct occupant-list writes, blocker-array writes, registry changes,
transforms, physics configuration and collider activation. Do not infer freshness from a frame
number or compare snapshot IDs to infer world equality. Until complete invalidation exists:

1. Capture a new snapshot for every preview evaluation (including changed aim or parameters).
2. At confirmation, capture and evaluate again before accepting the selection; do not combine
   old candidates with new obstruction or resolve a cached preview directly.
3. Return/tag results with the capture ID. Ignore asynchronous results whose ID is no longer the
   pending query's ID. Cancel pending work on selection exit or scene/encounter replacement.
4. If confirmation's membership/legality differs, update the preview and require confirmation
   again rather than silently casting against a changed target set.

An old snapshot remains a valid historical input indefinitely, but never proves an action is
currently legal. Refresh is an owner policy; the immutable object cannot detect world changes.

## Entity references

`SourceEntityId` is an optional Unity instance ID (absent for cell-only sources).
`OccupantsAt(cell)` returns immutable ordered integer IDs, preserving duplicate memberships and
allies; the current area evaluator does not filter by team or use multi-cell creature sizes.
Destroyed/null occupants are omitted at capture. Cell membership comes from the tile list,
not the occupant transform. Moving/destroying an occupant cannot mutate captured membership.

These identifiers are scene/session-local, not saved-game IDs or encounter combatant IDs.
`AreaTargetCapture` owns an ID-to-exact-original-GameObject mapping outside the snapshot.
Its `Resolve()` adapter validates each original object's lifetime; interactive confirmation
recaptures before resolving the accepted result.
Do not resolve IDs through object names, replacement occupants, or a cross-scene global lookup;
do not transfer mappings across scene lifetimes. Do not add GameObjects back to the snapshot.
When adapting a migrated rules action, resolve its existing encounter identity at that boundary
rather than treating these scene IDs as rules-store identifiers.

## Consumer interfaces and semantics

### Geometry APIs

`GridPrivate.AreaTargeting.CellsForPlacement(TargetingSnapshot)` returns a fresh
`List<Vector3Int>` with area membership for the captured query. No live Tile, transform, request,
occupant, registry or physics reads occur in this calculation. Repeated evaluation preserves cell
ordering; mutating a returned list cannot affect later evaluations. A null snapshot throws
`ArgumentNullException`. Nonpositive size, an out-of-range burst, an unknown shape, or an area
containing no present tiles yields an empty list, not a null result. Geometry does not filter
obstructions or creatures. Selection must use the same snapshot for that subsequent filtering.

`AreaTargeting.CellsInPlacementRange(TargetingSnapshot)` returns a fresh x-then-z ordered list
for range highlighting. It uses positive burst range, otherwise `max(size, 5)` feet. This helper
intentionally does not apply area legality: highlights can exist for a nonpositive area size.
It tests cell-to-cell distance, whereas placed burst legality tests cell-to-corner distance.
Do not use the highlight list as the authority for burst placement legality.

All query parameters are already copied into the query-scoped snapshot; these APIs accept no live
parameter objects and no override parameters that could disagree with captured physics rays.
Changing aim, size, width, range or source requires a new capture. `PlacementFromHover` remains a
live input-adaptation step before capture, not part of evaluating an existing snapshot.

The live `Evaluate(source, tiles, request, placement)` boundary captures through `AreaTargetCapture`,
evaluates the snapshot and resolves only original objects. Its null-boundary behavior is unchanged.
The range adapter captures physics even though geometry ignores it, a contract cost rather than a
second geometry snapshot type. Once an aim exists, the state reuses its placed capture for range
highlighting as well as membership, candidates and obstruction.

#### Preserved templates and unsupported cases

- Emanation uses alternating 5/10-foot diagonals and optionally includes the center.
- Cone excludes the source even when `IncludeCenter` is true, uses the same grid distance and
  includes the 45-degree angle edge with the existing 0.01-degree epsilon.
- Burst uses minimum distance to a cell's four corners, inclusively. This includes cells merely
  touching a qualifying corner. Only positive range constrains the burst origin. Its placement
  cell does not affect membership; the origin corner does.
- Line uses Euclidean forward projection, not alternating-diagonal distance, for its length
  cutoff (`size / 5 + 0.01`). Width is rounded up to whole cells, clamped to at least five feet,
  and tested against `(widthCells - 1) / 2 + 0.01`. Source and non-forward cells are excluded.
  Cells are sorted by grid distance; equal-distance ordering retains the existing sort behavior.
- Missing tiles and x/z bounds trim all templates; a missing tile does not stop geometry beyond
  it. Obstruction is deliberately a separate calculation.
- Only Burst, Cone, Emanation and Line are supported. Unknown shapes produce empty membership.
  Existing invalid direction values still fall back to east; this is not a new supported aim.
  Vertical cones, spheres, multi-level volumes, creature-size footprints, arbitrary-angle aim,
  and additional area shapes are not implemented. Do not infer 3D rules from retained y values.

Consume `Shape`, `SizeFeet`, `RangeFeet`, `LineWidthFeet`, `IncludeCenter`, `SourceCell`,
`OriginCorner`, `Direction`, `PlacementOriginCell`, `PlacementShape`, `Width`, `Depth`,
`IsInBounds(cell)` and `HasTile(cell)`. Geometry must not reconstruct live Tile arrays.
Retain existing `AreaTargeting` behavior: alternating diagonal grid distance, burst corner
origins, eight directions, current line projection/width thresholds and cone angle epsilon.
Burst cells currently have y=0; other templates use source y. Bounds and occupancy are x/z only.
Placement's shape is copied separately because existing result construction preserves it even
though the request shape controls the algorithm. This contract intentionally does not validate
or reinterpret unsupported/mismatched shapes.

### Line-of-effect worker

Consume `IsBlocking(cell)` for sampled grid rays and `PhysicsClearRayMask(target)` for map
colliders. Tile existence is NOT obstruction: registered transparent obstacle cells may lack
Tiles while allowing rays through. Out-of-bounds is blocking; requesting an out-of-bounds
physics mask throws because that target was not captured.

For each ray index below `PhysicsRayCount`, `RayStart(index)` and `RayEnd(target,index)` give
the exact planar endpoints. A burst has four point-to-target rays; all other shapes have sixteen
start-corner/target-corner pairs. Corner offsets are (-.4,-.4), (+.4,-.4), (-.4,+.4), (+.4,+.4)
around cell + .5, start outer loop and target inner loop. Preserve GridTargeting's eight samples
per cell and start/target exemption rules (burst has no exempt start cell). Intersect per-ray
grid-clear bits with physics-clear bits BEFORE counting; minimum counts are not equivalent.
Do not call MapLineOfSightBlocker, Physics, or GridLineOfSightData during evaluation.

Capture invokes the existing `MapLineOfSightBlocker.BlocksSegment` for all grid target cells,
at its existing fixed y=.75. This retains mesh/rotated collider precision, ray-start-inside,
layer masks, global trigger policy, parent marker behavior and full raycast-buffer fallback.
It deliberately does not approximate colliders as axis-aligned boxes or copy Unity components.
Physics bits alone are not final line-of-effect answers. Preserve the existing area cover policy
(including the current `< 16` comparison for burst rays) rather than changing rules in migration.
`RequiresLineOfEffect=false` still needs raw rays for cover reporting.

Cost is O(width * depth * ray-count) physics queries per capture, plus grid/occupancy copies.
This is a correctness-first finite-query capture, not a general physics-world snapshot. Aim,
source or shape changes require recapture; arbitrary new source points cannot reuse these bits.
Profile before optimizing. A later proven optimization may limit captured targets, but must make
uncaptured cells explicit and must not silently fall back to live physics.

### Selection APIs and ownership

`AreaTargeting.Evaluate(TargetingSnapshot)` returns an immutable `AreaSelectionSnapshot`: the
input snapshot, read-only ordered cells, and read-only `AreaSelectedEntity` values (entity ID,
captured cell, raw rays, effective line of effect, cover). Empty geometry means illegal placement.
Blocked candidates remain in the output with `IsAffected=false`, preserving existing reporting.
Historical `IsAffected` never consults live Unity objects. Duplicates and allies remain ordered
as captured; no new deduplication or team filtering is introduced.

`AreaTargetCapture.Capture(...).Resolve()` is the Unity adapter for callers needing the existing
mutable `AreaTargetResult`. It tags the DTO with `SnapshotId`, copies geometry, and maps captured
IDs to exact originals. Destroyed originals are omitted; moved originals retain their captured
cell. The adapter's existing `AreaAffectedCreature.IsAffected` also guards destroyed objects.
Mutating a DTO does not mutate its capture. Historical resolution is not revalidation.

`StateAreaTarget` retains pure pending output rather than a mutable Unity DTO. Every hover captures
anew. Emanation captures on entry; initial unplaced range highlighting is a separate query. Every
left-click captures again, compares source, query parameters, placement, ordered geometry and all
candidate outcomes (including raw rays), and accepts only an unchanged legal result. A changed
result is displayed and requires another click. An illegal result is retained as a pending aim so
it can become legal after another refresh. Accepted DTOs use the confirmation capture's ID, never
the preview ID. No yield or presentation callbacks intervene between confirmation capture and
object resolution. Exit/cancel clears pending output and removes listeners. Source destruction,
source-object replacement or grid-array replacement cancels confirmation rather than falling back
to an old cell or a new world's occupants. Cell-only sources remain explicitly supported.

`GridAPI`/`GridBase` route to that state without a second evaluation. `CreatureAuraArea` evaluates
through the same live capture boundary for each synchronous aura query. Existing spell and aura
adapters consume the resolved DTO and guard original object lifetime via `IsAffected`; they never
look up replacement occupants. No scene IDs become rules-store combatant IDs.

The obsolete area `GetCreatures`, live `GridTargeting.CountClearRays` overload, point-origin ray
overload, and live occupant helper were removed. `GridTargeting` retains distance, bounds and
diagonal-blocker helpers used by the distinct Strike pipeline. Strike's own evaluator and the
encounter rules targeting infrastructure are not migrated by this area-query contract.

### Remaining lifecycle limitations

- There is no asynchronous selection work or world-revision service; a capture is historical,
  not a lease on world state. Callers retaining results beyond confirmation must revalidate at
  their action boundary. Legacy direct `Cast(..., area)` APIs accept caller-supplied DTOs, not a
  durable confirmation capability. This migration does not make those APIs transactional.
- Grid occupancy is the source of candidate cells. A transform-only target move without updating
  occupancy is an unsettled projection, not a supported alternate position authority.
- Previews are refreshed by aim events and confirmation, not continuously while the mouse is
  stationary. Source/scene/grid replacement invalidates the pending selection; there is no global
  registry lookup or cross-scene reuse. Same-array changes are recaptured normally.
- Direction remains the selected octant if the source moves before confirmation; new geometry
  must be confirmed again. A new hover recalculates direction from the current source.
- Physics capture cost and planar/shape limitations above remain unchanged. No encounter module
  composition or authoritative rules state was added.

## Verification

`Assets/Tests/EditMode/TargetingSnapshotTests.cs` exercises copied parameters and transforms,
occupancy mutation/destruction, tile replacement, registered blocker mutation/removal,
transparent missing tiles, read-only collections, invalid input boundaries, unique captures,
and real collider removal with old/new four-ray and sixteen-ray snapshots.
`AreaGeometrySnapshotTests` covers all four shapes under live mutation, independent equivalent
captures, returned-list isolation, burst corner/range inclusion, grid clipping and holes, source
elevation, all eight line directions, line width/length edges, cone angle/distance boundaries,
range-highlight snapshot isolation and minimum range, unknown shapes and invalid query inputs.
Existing `Pf2eAreaTargetingTests` remain unchanged and exercise the live adapters for parity.
`AreaSelectionSnapshotTests` covers combined area membership/physics for all four shapes, LoE
bypass and cover, old/new captures after occupancy/grid/collider changes, destroyed-original
resolution, replacement rejection and immutable output. `AreaTargetingStateTests` covers unchanged
confirmation, changed candidates, source movement, candidate/source destruction and cancellation.
The mixed-obstruction ray test retains fixed expectations measured from the former live evaluator
before its removal, rather than comparing the snapshot implementation to itself.
