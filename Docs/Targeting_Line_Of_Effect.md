# Snapshot line-of-effect evaluation

`GridPrivate.GridTargeting.CountClearRays(TargetingSnapshot, Vector3Int)` evaluates
both burst and cell-origin captures. Use the same snapshot as geometry and occupant
collection; do not recapture inside the occupant loop. See
[the capture contract](Targeting_Snapshots.md) for lifetime and confirmation policy.

The evaluator samples captured grid obstruction eight times per cell and intersects
clear rays with the exact captured physics mask before counting. It never reads the
live grid registry or performs collision queries. It preserves target-cell exemption,
non-burst source-cell exemption (including existing elevation equality), burst's lack
of source exemption, and transparent missing tiles. Out-of-capture target cells throw
rather than consulting the current world. Coincident endpoints remain clear.

The count is raw even when `RequiresLineOfEffect` is false. The area consumer retains
its existing policy: zero rays means blocked unless the request bypasses line of
effect; a positive count below sixteen reports standard cover (including four-ray
bursts). This migration does not reinterpret visibility as effect eligibility or
change the existing MapLineOfSightBlocker capture semantics.

## Selection integration

`AreaTargeting.Evaluate(snapshot)` calls `CountClearRays(snapshot, cell)` once per
occupied area cell. Geometry, occupant IDs and ray filtering share that exact capture.
The former Tile-based and point-origin ray overloads were removed after migrating all
callers. The mixed-grid/collider regression retains fixed ray counts measured against
the former live evaluator before removal; it does not compare the new method to itself.
Strike retains its separate evaluator and the shared diagonal-blocker helper.

## Verification

`TargetingLineOfEffectSnapshotTests` covers grid blocker mutation/unregistration,
collider destruction, old/new capture stability, transparent missing tiles, blocked
source/target exemptions, coincident cells, invalid targets, and four/sixteen-ray
parity across a mixed grid/collider layout. `AreaSelectionSnapshotTests` exercises
those rays through the production selection path, including LoE bypass and cover.
No scene or presentation assets changed.
