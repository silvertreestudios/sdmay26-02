# Evaluating captured line of effect

[`GridTargeting.CountClearRays(snapshot, target)`](../Assets/Scripts/Grid/GridTargeting.cs)
counts the rays that reach a cell through both grid obstruction and map colliders. Area selection
calls it once per occupied area cell, using the same `TargetingSnapshot` as geometry and occupant
membership. It performs no live grid-registry reads or physics queries.

Use this method for the raw ray count. `AreaTargeting.Evaluate(snapshot)` combines that count with
the request's line-of-effect requirement to produce occupant eligibility and cover. See the
[snapshot guide](Targeting_Snapshots.md) for capture requirements, ownership, and confirmation.

## Ray layout and evaluation

Burst captures have four rays from the selected origin corner to the target's four corners.
Other shapes have sixteen rays: each source-cell corner is paired with every target-cell corner.
Corners are offsets `(-.4,-.4)`, `(+.4,-.4)`, `(-.4,+.4)`, and `(+.4,+.4)` from cell center
`(x + .5, z + .5)`, in that order. `RayStart(index)` and `RayEnd(target, index)` expose these
planar endpoints; source corners form the outer loop and target corners the inner loop.

Capture stores a bit for each ray that is clear of map colliders. Evaluation tests each set bit
against captured grid obstruction, sampling eight times per cell of planar distance. A ray counts
only if both tests pass. Taking the smaller of two separate clear-ray totals would be incorrect:
the grid and physics tests might allow different rays.

Grid sampling exempts the target cell and, for non-burst shapes, the source cell. Source exemption
uses full cell equality, including elevation. Bursts originate at a corner and have no source-cell
exemption. Registered transparent cells can pass rays even if their Tile is missing. Coincident
endpoints are clear. Null snapshots and out-of-capture targets throw rather than consulting the
current world.

## Eligibility and cover

When `RequiresLineOfEffect` is true, zero clear rays means the occupant is blocked. When false, the
occupant is eligible regardless of obstruction, but the raw ray count is still returned for cover.
`AreaSelectedEntity.Cover` reports standard cover for one through fifteen clear rays and no cover
otherwise. This existing threshold also applies to bursts, even though bursts have only four rays.
The snapshot refactor preserves that policy; it does not introduce a new PF2e cover calculation.

Strike retains its own evaluator and uses shared distance and diagonal-blocker helpers. Area query
callers use the snapshot overload; the former Tile-based and point-origin overloads are removed.

## Tests to consult

`Assets/Tests/EditMode/TargetingLineOfEffectSnapshotTests.cs` covers blocker mutation and removal,
collider destruction, stable old captures, transparent missing tiles, source/target exemptions,
coincident cells, invalid targets, and mixed grid/collider layouts. Its fixed four/sixteen-ray
counts were measured against the former live evaluator before removal.

`AreaSelectionSnapshotTests` exercises the same rays through area selection, including bypass and
cover. These tests separate immutable ray evaluation from the Unity capture boundary so changes
to either can be diagnosed without relying only on end-to-end scene behavior.
