using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class TargetingLineOfEffectSnapshotTests
    {
        [TestCase(AreaShape.Line, 16)]
        [TestCase(AreaShape.Burst, 4)]
        public void GridBlockerChangesRequireRecapture(AreaShape shape, int rays)
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            bool[,] blockers = new bool[3, 1];
            GridLineOfSightData.Register(tiles, blockers);
            try
            {
                TargetingSnapshot clear = Capture(tiles, shape);
                Vector3Int target = new(2, 0, 0);
                Assert.AreEqual(rays, GridTargeting.CountClearRays(clear, target));
                blockers[1, 0] = true;
                TargetingSnapshot blocked = Capture(tiles, shape);
                Assert.AreEqual(0, GridTargeting.CountClearRays(blocked, target));
                blockers[1, 0] = false;
                GridLineOfSightData.Unregister(tiles);
                Assert.AreEqual(rays, GridTargeting.CountClearRays(clear, target));
                Assert.AreEqual(0, GridTargeting.CountClearRays(blocked, target));
                Assert.AreEqual(rays, GridTargeting.CountClearRays(Capture(tiles, shape), target));
            }
            finally
            {
                GridLineOfSightData.Unregister(tiles);
            }
        }

        [TestCase(AreaShape.Line, 16)]
        [TestCase(AreaShape.Burst, 4)]
        public void ColliderRemovalDoesNotChangeCapturedEvaluation(AreaShape shape, int rays)
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.AddComponent<Game.KayKit.MapLineOfSightBlocker>();
            wall.transform.position = new Vector3(1.5f, 0.75f, 0.5f);
            wall.transform.localScale = new Vector3(0.2f, 2, 4);
            try
            {
                Physics.SyncTransforms();
                TargetingSnapshot blocked = Capture(tiles, shape);
                Vector3Int target = new(2, 0, 0);
                Assert.AreEqual(0, GridTargeting.CountClearRays(blocked, target));
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                Assert.AreEqual(0, GridTargeting.CountClearRays(blocked, target));
                Assert.AreEqual(rays, GridTargeting.CountClearRays(Capture(tiles, shape), target));
            }
            finally
            {
                if (wall != null)
                    Object.DestroyImmediate(wall);
            }
        }

        [Test]
        public void EndpointExemptionsAndTransparentMissingTilesPreserveExistingSemantics()
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { null },
                { new Tile() },
            };
            bool[,] blockers =
            {
                { true },
                { false },
                { true },
            };
            TileType[,] types =
            {
                { TileType.Obstacle },
                { TileType.Obstacle },
                { TileType.Obstacle },
            };
            GridLineOfSightData.Register(tiles, blockers, types);
            try
            {
                Vector3Int target = new(2, 0, 0);
                TargetingSnapshot line = Capture(tiles, AreaShape.Line);
                Assert.AreEqual(16, GridTargeting.CountClearRays(line, target));
                Assert.AreEqual(16, GridTargeting.CountClearRays(line, Vector3Int.zero));
                Assert.AreEqual(
                    0,
                    GridTargeting.CountClearRays(Capture(tiles, AreaShape.Burst), target)
                );
                Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                    GridTargeting.CountClearRays(line, new Vector3Int(-1, 0, 0))
                );
                Assert.Throws<System.ArgumentNullException>(() =>
                    GridTargeting.CountClearRays(null, target)
                );
            }
            finally
            {
                GridLineOfSightData.Unregister(tiles);
            }
        }

        [TestCase(AreaShape.Line)]
        [TestCase(AreaShape.Burst)]
        public void CombinedGridAndPhysicsRaysPreserveBaselineAcrossBoundaries(AreaShape shape)
        {
            Tile[,] tiles = new Tile[4, 4];
            bool[,] blockers = new bool[4, 4];
            for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                tiles[x, z] = new Tile();
            blockers[1, 0] = true;
            blockers[0, 1] = true;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.AddComponent<Game.KayKit.MapLineOfSightBlocker>();
            wall.transform.position = new Vector3(1.5f, 0.75f, 1.65f);
            wall.transform.localScale = new Vector3(0.1f, 1.5f, 0.8f);
            GridLineOfSightData.Register(tiles, blockers);
            try
            {
                Physics.SyncTransforms();
                TargetingSnapshot snapshot = Capture(tiles, shape);
                for (int x = 0; x < 4; x++)
                for (int z = 0; z < 4; z++)
                {
                    Vector3Int target = new(x, 0, z);
                    // Fixed baseline measured against the former live evaluator before removal.
                    int[,] baseline =
                    {
                        { 16, 16, 0, 0 },
                        { 16, 4, 0, 0 },
                        { 0, 0, 0, 0 },
                        { 0, 0, 0, 0 },
                    };
                    int expected = baseline[x, z] / (shape == AreaShape.Burst ? 4 : 1);
                    Assert.AreEqual(
                        expected,
                        GridTargeting.CountClearRays(snapshot, target),
                        target.ToString()
                    );
                }
            }
            finally
            {
                GridLineOfSightData.Unregister(tiles);
                Object.DestroyImmediate(wall);
            }
        }

        private static TargetingSnapshot Capture(Tile[,] tiles, AreaShape shape) =>
            TargetingSnapshot.Capture(
                new AreaTargetSource(Vector3Int.zero),
                tiles,
                new AreaTargetRequest { Shape = shape, RequiresLineOfEffect = false },
                new AreaPlacement { OriginCorner = Vector2Int.zero }
            );
    }
}
