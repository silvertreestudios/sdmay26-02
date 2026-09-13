# Area targeting snapshots

Area targeting captures the scene once so geometry, occupants, line of effect, and cover all use
the same inputs. Evaluation can then finish without reading mutable tiles or querying physics
again. A snapshot describes the scene when it was captured; confirming an action requires a fresh
capture because movement, destruction, or obstruction may have changed the selection.

## Where to start

| Responsibility | Code |
| --- | --- |
| Copy query parameters, tile membership, obstruction, and physics results | [`TargetingSnapshot`](../Assets/Scripts/Grid/TargetingSnapshot.cs) |
| Calculate area geometry and occupant outcomes | [`AreaTargeting`](../Assets/Scripts/Grid/AreaTargeting.cs) |
| Store immutable outcomes and resolve original Unity objects | [`AreaSelectionSnapshot`, `AreaTargetCapture`](../Assets/Scripts/Grid/AreaSelectionSnapshot.cs) |
| Count rays clear of grid and map obstruction | [`GridTargeting`](../Assets/Scripts/Grid/GridTargeting.cs), [line-of-effect guide](Targeting_Line_Of_Effect.md) |
| Refresh previews and confirm selections | [`StateAreaTarget`](../Assets/Scripts/Grid/States/StateAreaTarget.cs) |
| Raise per-frame hover input | [`GridInput`](../Assets/Scripts/Grid/Input/GridInput.cs) |

For a synchronous query that needs Unity objects, call
`AreaTargeting.Evaluate(source, tiles, request, placement)`. It captures, evaluates, and returns an
`AreaTargetResult`, or null for missing inputs or a placement with no area cells. Spell and aura
callers can continue using this result type.

For a caller that needs to retain or compare immutable output, use
`AreaTargetCapture.Capture(...)` followed by `AreaTargeting.Evaluate(capture.Snapshot)`.
The resulting `AreaSelectionSnapshot` contains ordered cells and occupant outcomes. Use that same
snapshot for related range highlights. Call `capture.Resolve()` when Unity object references are
needed; this evaluates the saved snapshot and returns a new mutable result.

## Capture requirements and ownership

Capture on Unity's main thread, without yielding, after movement has updated grid occupancy and
transforms. `AreaTargetCapture.Capture` calls `Physics.SyncTransforms()` before copying the scene.
The lower-level `TargetingSnapshot.Capture` does not synchronize physics: its caller must do so if
transforms have changed since the last physics update. Both reject null arguments and support a
source defined by a cell without a GameObject.

`TargetingSnapshot` copies request and placement fields, source position, x/z bounds, tile presence,
resolved grid obstruction, ordered occupant IDs, and the result of each map-collider ray. It retains
no Tile, GameObject, Collider, mutable request, or callback. Collections cannot be changed through
the public API. Cells with no live occupants share `Array.Empty<int>()`; occupied cells receive
their own read-only ID storage.

Occupant membership comes from tile lists, not occupant transforms. Null and destroyed objects are
omitted at capture, while duplicates and allies retain their tile-list order. A later transform
change or destruction cannot change the captured IDs or cells. A transform-only move whose tile
membership has not been updated is an unsettled projection, not a second position authority.

`SourceEntityId` and occupant IDs are Unity instance IDs for the current scene/session. They are not
saved-game identifiers or encounter combatant IDs. `AreaTargetCapture` keeps a separate map to the
exact original GameObjects. `Resolve()` omits destroyed originals and retains captured cells for
moved originals. It does not substitute current occupants, search by name, or resolve across scene
lifetimes. Keep captures within their owning selection or synchronous query. When a rules action
needs encounter identity, translate through its existing Unity-to-encounter mapping.

`AreaSelectionSnapshot.Creatures` includes blocked occupants with `IsAffected=false`. That flag
describes the captured result and does not check object lifetime. The resolved Unity result's
`AreaAffectedCreature.IsAffected` additionally rejects destroyed objects. Mutating the resolved
result cannot alter its snapshot. Resolution checks lifetime only; it does not revalidate legality.

## Preview and confirmation

Each capture has a new `Guid` in `Id`, even if nothing changed. The grid has no complete revision
covering tile-list writes, registry changes, blockers, transforms, and physics settings. Comparing
IDs or frame numbers therefore cannot establish world equality or freshness.

`StateAreaTarget` owns the following selection flow:

1. For Burst, Cone, and Line, entry captures an initial range highlight and subscribes to
   `OnGridHover`. `GridInput.Update` raises this event on every valid grid frame, including frames
   where the pointer is stationary. Each event captures and evaluates a new preview. Emanation
   instead captures on entry and does not subscribe to hover input.
2. A placed preview uses one snapshot for range highlighting, area cells, occupants, and obstruction.
3. Each left-click captures the pending aim again. It compares source, request parameters,
   placement, ordered cells, and every occupant outcome, including clear-ray counts, with the
   pending preview. If the result differs, the state displays it and requires another click.
4. An unchanged legal result is resolved immediately from the confirmation capture, with no yield
   or presentation callback before object resolution. The returned `SnapshotId` is the confirmation
   ID. An illegal result remains pending so a later refresh can make it legal.
5. Exit or cancellation clears the pending data and removes listeners. Destruction or replacement
   of an object source, or replacement of the grid array, prevents confirmation. Changes within the
   same array are observed by the next capture. Explicit cell-only sources remain valid.

For aimed shapes, a stationary pointer therefore continues to incur full-grid capture work while
valid hover events arrive. Emanations refresh on entry and confirmation. If the source moves
between preview and click, confirmation retains the selected direction and requires another click
for changed geometry; the next hover recomputes direction from the current source position.

There is no asynchronous evaluation or world-revision service. A future asynchronous caller must
tag work with its capture ID, accept only the pending query's output, and cancel pending work when
the selection or scene ends. Callers retaining a confirmed result must still revalidate at their
action boundary. Direct `Cast(..., area)` APIs accept caller-supplied results and do not provide a
transaction that keeps the world unchanged until casting.

## Geometry contracts

`CellsForPlacement(snapshot)` returns a new list of area cells using only captured values.
Null input throws `ArgumentNullException`. A nonpositive size, out-of-range burst, unsupported shape,
or area with no present tiles returns an empty list. Geometry does not filter obstruction or
occupants; `Evaluate(snapshot)` performs that work using the same input. Repeated evaluation keeps
the same ordering, and changing a returned list does not affect later results.

`CellsInPlacementRange(snapshot)` returns present cells ordered by x then z. It uses a positive
burst range when specified, otherwise `max(size, 5)` feet. It may therefore highlight cells even
when the area size is invalid. Highlighting measures cell-to-cell distance; burst placement legality
measures source-cell-to-origin-corner distance. Use area evaluation to decide whether a placement
is legal. The live range overload creates a full capture, including physics results, so callers
with a placed snapshot should reuse it.

The geometry algorithms have these established behaviors:

| Shape | Membership |
| --- | --- |
| Emanation | Alternating 5/10-foot diagonals; includes the source only when `IncludeCenter` is true. |
| Cone | Same grid distance, excludes the source, and includes the 45-degree edge with a 0.01-degree epsilon. |
| Burst | Minimum distance to each cell's four corners, inclusive. A cell touching a qualifying corner is included. Only a positive range limits placement. The origin corner determines membership; the placement cell does not. |
| Line | Euclidean forward projection up to `size / 5 + 0.01` cells. Width rounds up to whole cells with a five-foot minimum; lateral distance is at most `(widthCells - 1) / 2 + 0.01`. Source and non-forward cells are excluded. Cells are sorted by grid distance, with ties following `List.Sort` behavior. |

Missing tiles and x/z bounds trim every template, but a missing tile does not stop geometry beyond
it. Burst cells have y=0; other shapes retain source y. Bounds and occupancy use x/z only.
Vertical volumes, multi-level areas, creature footprints, arbitrary-angle aim, and additional
shapes are unsupported. Unknown shapes produce no cells; invalid direction values fall back to east.

Request `Shape` chooses the algorithm. `PlacementShape` is copied separately for the returned
placement; capture does not validate mismatched shapes. Aim, source, size, width, or range changes
require a new capture. `PlacementFromHover` converts live input before capture and is not part of
snapshot evaluation.

## Obstruction and capture cost

Grid obstruction and tile presence are independent. A registered transparent obstacle cell can
lack a Tile and still let rays through. Out-of-bounds grid samples block rays. Requesting a physics
mask for a target outside the capture throws instead of consulting the live scene.

Capture calls `MapLineOfSightBlocker.BlocksSegment` at y=0.75 for every ray to every grid cell:
four rays for Burst and sixteen for other shapes. Using the real collider query preserves mesh and
rotated shapes, rays starting inside colliders, layers, trigger policy, parent markers, and the
full-buffer fallback. Collider bounds alone cannot represent those behaviors accurately.

The cost is proportional to `width * depth * ray-count`, plus grid and occupancy copies. Each
valid aimed-hover frame pays this cost, even for cells outside the selected area. Sharing empty
occupant storage avoids per-empty-cell collection allocations; it does not reduce raycasts.
There is no performance benchmark for this path. Any later optimization needs measurement and
must explicitly represent uncaptured cells rather than fall back to live physics during evaluation.

The [line-of-effect guide](Targeting_Line_Of_Effect.md) describes ray order, grid sampling, and cover.
Strike uses a separate evaluator and the shared distance/diagonal-blocker helpers. These area
snapshots do not change encounter module composition or authoritative rules state.

## Tests to consult

| Fixture | Contract covered |
| --- | --- |
| `TargetingSnapshotTests` | Copied parameters and topology, source movement, occupant lifetime, shared empty storage, occupied ordering and immutability, collider changes, and invalid inputs. |
| `AreaGeometrySnapshotTests` | Four shapes, range/corner/width/angle edges, holes, elevation, direction, stable ordering, and independent returned lists. |
| `TargetingLineOfEffectSnapshotTests` | Captured obstruction and collider changes, exemptions, transparent missing tiles, and fixed four/sixteen-ray baselines. |
| `AreaSelectionSnapshotTests` | Combined membership and rays, cover, line-of-effect bypass, changed occupants, and original-object resolution. |
| `AreaTargetingStateTests` (PlayMode) | Per-frame real input, unchanged confirmation, changed candidates/source, destruction, and cancellation. |
| `Pf2eAreaTargetingTests` | Existing geometry and live-adapter behavior. |

EditMode fixtures live under `Assets/Tests/EditMode`; the state fixture lives under
`Assets/Tests/PlayMode/TestsState`. Mixed-obstruction assertions use fixed counts measured from the
former live evaluator before its removal, giving an independent baseline for the snapshot evaluator.
