using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class TargetingSnapshotTests
    {
        [Test]
        public void CellsWithoutLiveOccupantsShareEmptyStorageAcrossCaptures()
        {
            GameObject destroyed = new("destroyed occupant");
            Tile[,] tiles =
            {
                { new Tile(), new Tile(), new Tile(), null },
            };
            tiles[0, 1].Occupants.Add(null);
            tiles[0, 2].Occupants.Add(destroyed);
            Object.DestroyImmediate(destroyed);

            TargetingSnapshot first = TargetingSnapshot.Capture(
                new AreaTargetSource(Vector3Int.zero),
                tiles,
                new AreaTargetRequest(),
                new AreaPlacement()
            );
            TargetingSnapshot second = TargetingSnapshot.Capture(
                new AreaTargetSource(Vector3Int.zero),
                tiles,
                new AreaTargetRequest(),
                new AreaPlacement()
            );

            for (int z = 0; z < tiles.GetLength(1); z++)
            {
                Vector3Int cell = new(0, 0, z);
                Assert.AreSame(System.Array.Empty<int>(), first.OccupantsAt(cell));
                Assert.AreSame(first.OccupantsAt(cell), second.OccupantsAt(cell));
            }
        }

        [Test]
        public void OccupiedStoragePreservesLiveOrderDuplicatesAndReadOnlyIsolation()
        {
            GameObject first = new("first occupant");
            GameObject second = new("second occupant");
            GameObject destroyed = new("destroyed occupant");
            Tile[,] tiles =
            {
                { new Tile() },
            };
            try
            {
                tiles[0, 0].Occupants.AddRange(new[] { first, null, destroyed, second, first });
                Object.DestroyImmediate(destroyed);
                TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    new AreaTargetRequest(),
                    new AreaPlacement()
                );
                int[] expected =
                {
                    first.GetInstanceID(),
                    second.GetInstanceID(),
                    first.GetInstanceID(),
                };
                var captured = snapshot.OccupantsAt(Vector3Int.zero);
                CollectionAssert.AreEqual(expected, captured);
                Assert.Throws<System.NotSupportedException>(() =>
                    ((System.Collections.Generic.IList<int>)captured)[0] = 0
                );

                tiles[0, 0].Occupants.Clear();
                Object.DestroyImmediate(first);
                TargetingSnapshot refreshed = TargetingSnapshot.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    new AreaTargetRequest(),
                    new AreaPlacement()
                );
                CollectionAssert.AreEqual(expected, captured);
                Assert.AreSame(System.Array.Empty<int>(), refreshed.OccupantsAt(Vector3Int.zero));
            }
            finally
            {
                if (first != null)
                    Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                if (destroyed != null)
                    Object.DestroyImmediate(destroyed);
            }
        }

        [Test]
        public void TransparentMissingTilesRemainDistinctFromGeometryAndCollectionsAreReadOnly()
        {
            Tile[,] tiles = new Tile[1, 1];
            GridLineOfSightData.Register(
                tiles,
                new bool[1, 1],
                new[,]
                {
                    { TileType.Obstacle },
                }
            );
            try
            {
                TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    new AreaTargetRequest { Shape = AreaShape.Line },
                    new AreaPlacement()
                );
                GridLineOfSightData.Unregister(tiles);
                Assert.IsFalse(snapshot.HasTile(Vector3Int.zero));
                Assert.IsFalse(snapshot.IsBlocking(Vector3Int.zero));
                Assert.IsNull(snapshot.SourceEntityId);
                Assert.AreEqual(1, snapshot.Width);
                Assert.AreEqual(1, snapshot.Depth);
                Assert.Throws<System.NotSupportedException>(() =>
                    (
                        (System.Collections.Generic.IList<int>)snapshot.OccupantsAt(Vector3Int.zero)
                    ).Add(7)
                );
                Assert.IsEmpty(snapshot.OccupantsAt(Vector3Int.one));
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    snapshot.PhysicsClearRayMask(Vector3Int.one)
                );
                Assert.Throws<System.ArgumentOutOfRangeException>(() => snapshot.RayStart(-1));
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    snapshot.RayEnd(Vector3Int.zero, 16)
                );
                Assert.AreEqual(new Vector2(0.1f, 0.1f).x, snapshot.RayStart(0).x, 0.00001f);
                Assert.AreEqual(new Vector2(0.9f, 0.9f), snapshot.RayEnd(Vector3Int.zero, 15));
                Assert.Throws<System.ArgumentNullException>(() =>
                    TargetingSnapshot.Capture(
                        null,
                        tiles,
                        new AreaTargetRequest(),
                        new AreaPlacement()
                    )
                );
                Assert.Throws<System.ArgumentNullException>(() =>
                    TargetingSnapshot.Capture(
                        new AreaTargetSource(),
                        null,
                        new AreaTargetRequest(),
                        new AreaPlacement()
                    )
                );
                Assert.Throws<System.ArgumentNullException>(() =>
                    TargetingSnapshot.Capture(
                        new AreaTargetSource(),
                        tiles,
                        null,
                        new AreaPlacement()
                    )
                );
                Assert.Throws<System.ArgumentNullException>(() =>
                    TargetingSnapshot.Capture(
                        new AreaTargetSource(),
                        tiles,
                        new AreaTargetRequest(),
                        null
                    )
                );
            }
            finally
            {
                GridLineOfSightData.Unregister(tiles);
            }
        }

        [TestCase(AreaShape.Line, 16)]
        [TestCase(AreaShape.Burst, 4)]
        public void CaptureFreezesPhysicsRaysAndRefreshObservesRemovedBlocker(
            AreaShape shape,
            int rays
        )
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.AddComponent<Game.KayKit.MapLineOfSightBlocker>();
            wall.transform.position = new Vector3(1.5f, 0.75f, 0.5f);
            wall.transform.localScale = new Vector3(0.2f, 2, 4);
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            AreaTargetRequest request = new() { Shape = shape, SizeFeet = 15 };
            AreaPlacement placement = new() { Shape = shape, OriginCorner = Vector2Int.zero };
            try
            {
                Physics.SyncTransforms();
                TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    request,
                    placement
                );
                Vector3Int target = new(2, 0, 0);
                Assert.AreEqual(rays, snapshot.PhysicsRayCount);
                Assert.AreEqual(0, snapshot.PhysicsClearRayMask(target));
                wall.SetActive(false);
                Physics.SyncTransforms();
                TargetingSnapshot refreshed = TargetingSnapshot.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    request,
                    placement
                );
                Assert.AreEqual(0, snapshot.PhysicsClearRayMask(target));
                Assert.AreEqual((1 << rays) - 1, refreshed.PhysicsClearRayMask(target));
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void CaptureFreezesSourceParametersTopologyBlockersAndOccupants()
        {
            GameObject actor = new("snapshot actor");
            GameObject occupant = new("snapshot occupant");
            Tile[,] tiles =
            {
                { new Tile(), new Tile() },
            };
            bool[,] blockers =
            {
                { false, false },
            };
            GridLineOfSightData.Register(tiles, blockers);
            try
            {
                actor.transform.position = new Vector3(0, 2, 0);
                tiles[0, 1].Occupants.Add(occupant);
                int occupantId = occupant.GetInstanceID();
                AreaTargetRequest request = new()
                {
                    Shape = AreaShape.Line,
                    SizeFeet = 15,
                    RangeFeet = 30,
                    LineWidthFeet = 10,
                    IncludeCenter = true,
                    RequiresLineOfEffect = false,
                };
                AreaPlacement placement = new()
                {
                    Shape = AreaShape.Line,
                    OriginCell = new Vector3Int(0, 2, 0),
                    OriginCorner = new Vector2Int(1, 1),
                    Direction = AreaDirection.North,
                };
                TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                    new AreaTargetSource(actor),
                    tiles,
                    request,
                    placement
                );
                actor.transform.position = Vector3.one;
                request.SizeFeet = 99;
                request.RangeFeet = 99;
                request.LineWidthFeet = 99;
                request.IncludeCenter = false;
                request.RequiresLineOfEffect = true;
                request.Shape = AreaShape.Burst;
                placement.Direction = AreaDirection.South;
                placement.OriginCell = Vector3Int.one;
                placement.OriginCorner = Vector2Int.zero;
                placement.Shape = AreaShape.Cone;
                blockers[0, 1] = true;
                tiles[0, 1].Occupants.Clear();
                tiles[0, 0] = null;
                Object.DestroyImmediate(occupant);

                Assert.AreEqual(new Vector3Int(0, 2, 0), snapshot.SourceCell);
                Assert.AreEqual(actor.GetInstanceID(), snapshot.SourceEntityId);
                Assert.AreEqual(AreaShape.Line, snapshot.Shape);
                Assert.AreEqual(15, snapshot.SizeFeet);
                Assert.AreEqual(30, snapshot.RangeFeet);
                Assert.AreEqual(10, snapshot.LineWidthFeet);
                Assert.IsTrue(snapshot.IncludeCenter);
                Assert.IsFalse(snapshot.RequiresLineOfEffect);
                Assert.AreEqual(AreaDirection.North, snapshot.Direction);
                Assert.AreEqual(new Vector3Int(0, 2, 0), snapshot.PlacementOriginCell);
                Assert.AreEqual(new Vector2Int(1, 1), snapshot.OriginCorner);
                Assert.AreEqual(AreaShape.Line, snapshot.PlacementShape);
                Assert.IsTrue(snapshot.HasTile(Vector3Int.zero));
                Assert.IsFalse(snapshot.IsBlocking(new Vector3Int(0, 0, 1)));
                CollectionAssert.AreEqual(
                    new[] { occupantId },
                    snapshot.OccupantsAt(new Vector3Int(0, 0, 1))
                );
                Assert.IsTrue(snapshot.IsBlocking(new Vector3Int(-1, 0, 0)));
                Assert.IsFalse(snapshot.HasTile(new Vector3Int(1, 0, 0)));
                TargetingSnapshot refreshed = TargetingSnapshot.Capture(
                    new AreaTargetSource(actor),
                    tiles,
                    request,
                    placement
                );
                Assert.AreNotEqual(snapshot.Id, refreshed.Id);
                Assert.IsTrue(refreshed.IsBlocking(new Vector3Int(0, 0, 1)));
                Assert.IsEmpty(refreshed.OccupantsAt(new Vector3Int(0, 0, 1)));
                Assert.IsFalse(refreshed.HasTile(Vector3Int.zero));
            }
            finally
            {
                GridLineOfSightData.Unregister(tiles);
                Object.DestroyImmediate(actor);
                if (occupant != null)
                    Object.DestroyImmediate(occupant);
            }
        }
    }
}
