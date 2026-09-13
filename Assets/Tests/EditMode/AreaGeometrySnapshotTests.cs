using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class AreaGeometrySnapshotTests
    {
        [Test]
        public void GeometryUsesCapturedTilesSourceAndQueryAfterLiveMutation()
        {
            Tile[,] tiles = BuildTiles(5, 3);
            GameObject actor = new("geometry source");
            try
            {
                actor.transform.position = new Vector3(0, 7, 1);
                AreaTargetSource source = new(actor);
                AreaTargetRequest request = new() { Shape = AreaShape.Line, SizeFeet = 15 };
                AreaPlacement placement = new() { Direction = AreaDirection.East };
                TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                    source,
                    tiles,
                    request,
                    placement
                );
                TargetingSnapshot equivalent = TargetingSnapshot.Capture(
                    source,
                    tiles,
                    request,
                    placement
                );
                Vector3Int[] expected = { new(1, 7, 1), new(2, 7, 1), new(3, 7, 1) };
                CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
                CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(equivalent));
                actor.transform.position = new Vector3(4, 0, 1);
                request.SizeFeet = 5;
                placement.Direction = AreaDirection.West;
                tiles[2, 1] = null;
                CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
                CollectionAssert.AreEqual(
                    new[] { new Vector3Int(3, 0, 1) },
                    AreaTargeting.CellsForPlacement(
                        TargetingSnapshot.Capture(source, tiles, request, placement)
                    )
                );
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        [Test]
        public void PlacementRangeUsesCapturedParametersAndPresence()
        {
            Tile[,] tiles = BuildTiles(5, 5);
            AreaTargetSource source = new(new Vector3Int(0, 9, 0));
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Burst,
                SizeFeet = 5,
                RangeFeet = 10,
            };
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                source,
                tiles,
                request,
                new AreaPlacement()
            );
            Vector3Int[] expected =
            {
                new(0, 9, 0),
                new(0, 9, 1),
                new(0, 9, 2),
                new(1, 9, 0),
                new(1, 9, 1),
                new(1, 9, 2),
                new(2, 9, 0),
                new(2, 9, 1),
            };
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsInPlacementRange(snapshot));
            tiles[1, 1] = null;
            source.Cell = new Vector3Int(4, 0, 4);
            request.RangeFeet = 5;
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsInPlacementRange(snapshot));
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector3Int(3, 0, 3),
                    new Vector3Int(3, 0, 4),
                    new Vector3Int(4, 0, 3),
                    new Vector3Int(4, 0, 4),
                },
                AreaTargeting.CellsInPlacementRange(
                    TargetingSnapshot.Capture(source, tiles, request, new AreaPlacement())
                )
            );
        }

        [TestCase(AreaShape.Burst)]
        [TestCase(AreaShape.Cone)]
        [TestCase(AreaShape.Emanation)]
        [TestCase(AreaShape.Line)]
        public void EveryShapeRetainsCapturedGeometryAndOwnsItsResultList(AreaShape shape)
        {
            Tile[,] tiles = BuildTiles(7, 7);
            AreaTargetSource source = new(new Vector3Int(3, 6, 3));
            AreaTargetRequest request = new()
            {
                Shape = shape,
                SizeFeet = 10,
                IncludeCenter = true,
            };
            AreaPlacement placement = new()
            {
                OriginCorner = new Vector2Int(3, 3),
                Direction = AreaDirection.North,
            };
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                source,
                tiles,
                request,
                placement
            );
            var expected = AreaTargeting.CellsForPlacement(snapshot);
            Assert.IsNotEmpty(expected);
            foreach (Vector3Int cell in expected)
                Assert.AreEqual(shape == AreaShape.Burst ? 0 : 6, cell.y);
            source.Cell = Vector3Int.zero;
            request.Shape = (AreaShape)999;
            request.SizeFeet = 0;
            request.IncludeCenter = false;
            placement.OriginCorner = new Vector2Int(99, 99);
            placement.Direction = AreaDirection.South;
            System.Array.Clear(tiles, 0, tiles.Length);
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
            AreaTargeting.CellsForPlacement(snapshot).Clear();
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
            Assert.IsEmpty(
                AreaTargeting.CellsForPlacement(
                    TargetingSnapshot.Capture(source, tiles, request, placement)
                )
            );
        }

        [Test]
        public void BurstIncludesCornerDistanceBoundaryAndIgnoresPlacementCellElevation()
        {
            Tile[,] tiles = BuildTiles(6, 6);
            AreaTargetSource source = new(new Vector3Int(0, 8, 0));
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Burst,
                SizeFeet = 5,
                RangeFeet = 5,
            };
            AreaPlacement placement = new()
            {
                OriginCorner = new Vector2Int(2, 2),
                OriginCell = new Vector3Int(99, 99, 99),
            };
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                source,
                tiles,
                request,
                placement
            );
            var expected = new System.Collections.Generic.List<Vector3Int>();
            // Nearest cell-corner distance includes the surrounding four-by-four square at 5 ft.
            for (int x = 0; x <= 3; x++)
            for (int z = 0; z <= 3; z++)
                expected.Add(new Vector3Int(x, 0, z));
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
            placement.OriginCorner = new Vector2Int(3, 3);
            Assert.IsEmpty(
                AreaTargeting.CellsForPlacement(
                    TargetingSnapshot.Capture(source, tiles, request, placement)
                )
            );
            request.RangeFeet = 0;
            Assert.IsNotEmpty(
                AreaTargeting.CellsForPlacement(
                    TargetingSnapshot.Capture(source, tiles, request, placement)
                )
            );
        }

        [Test]
        public void MissingTilesAndGridEdgesTrimGeometryWithoutStoppingIt()
        {
            Tile[,] tiles = BuildTiles(4, 2);
            tiles[1, 0] = null;
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(new Vector3Int(0, -2, 0)),
                tiles,
                new AreaTargetRequest { Shape = AreaShape.Line, SizeFeet = 30 },
                new AreaPlacement { Direction = AreaDirection.East }
            );
            CollectionAssert.AreEqual(
                new[] { new Vector3Int(2, -2, 0), new Vector3Int(3, -2, 0) },
                AreaTargeting.CellsForPlacement(snapshot)
            );
        }

        [TestCase(AreaDirection.East, 1, 0)]
        [TestCase(AreaDirection.NorthEast, 1, 1)]
        [TestCase(AreaDirection.North, 0, 1)]
        [TestCase(AreaDirection.NorthWest, -1, 1)]
        [TestCase(AreaDirection.West, -1, 0)]
        [TestCase(AreaDirection.SouthWest, -1, -1)]
        [TestCase(AreaDirection.South, 0, -1)]
        [TestCase(AreaDirection.SouthEast, 1, -1)]
        public void LinesPreserveProjectionLengthAndAllEightDirections(
            AreaDirection direction,
            int dx,
            int dz
        )
        {
            Vector3Int start = new(4, 3, 4);
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(start),
                BuildTiles(9, 9),
                new AreaTargetRequest
                {
                    Shape = AreaShape.Line,
                    SizeFeet = 15,
                    LineWidthFeet = -5,
                },
                new AreaPlacement { Direction = direction }
            );
            Vector3Int step = new(dx, 0, dz);
            var expected = new System.Collections.Generic.List<Vector3Int>
            {
                start + step,
                start + step * 2,
            };
            if (dx == 0 || dz == 0)
                expected.Add(start + step * 3);
            CollectionAssert.AreEqual(expected, AreaTargeting.CellsForPlacement(snapshot));
        }

        [TestCase(5, 2)]
        [TestCase(6, 2)]
        [TestCase(10, 2)]
        [TestCase(11, 6)]
        [TestCase(15, 6)]
        public void LineWidthRoundsUpToCellsAndIncludesPerpendicularBoundary(
            int widthFeet,
            int count
        )
        {
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(new Vector3Int(2, 0, 2)),
                BuildTiles(7, 5),
                new AreaTargetRequest
                {
                    Shape = AreaShape.Line,
                    SizeFeet = 10,
                    LineWidthFeet = widthFeet,
                },
                new AreaPlacement { Direction = AreaDirection.East }
            );
            var cells = AreaTargeting.CellsForPlacement(snapshot);
            Assert.AreEqual(count, cells.Count);
            Assert.Contains(new Vector3Int(4, 0, 2), cells);
            Assert.IsFalse(cells.Contains(new Vector3Int(5, 0, 2)));
            if (widthFeet > 10)
            {
                Assert.Contains(new Vector3Int(4, 0, 1), cells);
                Assert.Contains(new Vector3Int(4, 0, 3), cells);
            }
        }

        [Test]
        public void DiagonalConeIncludesBothAngleEdgesAndAlternatingDistanceBoundary()
        {
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(new Vector3Int(2, 5, 2)),
                BuildTiles(7, 7),
                new AreaTargetRequest
                {
                    Shape = AreaShape.Cone,
                    SizeFeet = 10,
                    IncludeCenter = true,
                },
                new AreaPlacement { Direction = AreaDirection.NorthEast }
            );
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector3Int(2, 5, 3),
                    new Vector3Int(2, 5, 4),
                    new Vector3Int(3, 5, 2),
                    new Vector3Int(4, 5, 2),
                    new Vector3Int(3, 5, 3),
                    new Vector3Int(3, 5, 4),
                    new Vector3Int(4, 5, 3),
                },
                AreaTargeting.CellsForPlacement(snapshot)
            );
        }

        [TestCase(AreaShape.Emanation, 0)]
        [TestCase(AreaShape.Burst, -5)]
        [TestCase((AreaShape)999, 5)]
        public void InvalidGeometryReturnsEmptyRatherThanInventingAnArea(
            AreaShape shape,
            int sizeFeet
        )
        {
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(Vector3Int.zero),
                BuildTiles(2, 2),
                new AreaTargetRequest { Shape = shape, SizeFeet = sizeFeet },
                new AreaPlacement()
            );
            Assert.IsEmpty(AreaTargeting.CellsForPlacement(snapshot));
        }

        [TestCase(AreaShape.Burst, 0, 0)]
        [TestCase(AreaShape.Emanation, -5, 100)]
        public void HighlightRetainsMinimumFiveFeetInsteadOfApplyingGeometryLegality(
            AreaShape shape,
            int sizeFeet,
            int rangeFeet
        )
        {
            TargetingSnapshot snapshot = TargetingSnapshot.Capture(
                new AreaTargetSource(Vector3Int.zero),
                BuildTiles(3, 3),
                new AreaTargetRequest
                {
                    Shape = shape,
                    SizeFeet = sizeFeet,
                    RangeFeet = rangeFeet,
                },
                new AreaPlacement()
            );
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector3Int(0, 0, 0),
                    new Vector3Int(0, 0, 1),
                    new Vector3Int(1, 0, 0),
                    new Vector3Int(1, 0, 1),
                },
                AreaTargeting.CellsInPlacementRange(snapshot)
            );
        }

        [Test]
        public void SnapshotEntryPointsRejectAbsentCapture()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                AreaTargeting.CellsForPlacement(null)
            );
            Assert.Throws<System.ArgumentNullException>(() =>
                AreaTargeting.CellsInPlacementRange(null)
            );
        }

        private static Tile[,] BuildTiles(int width, int depth)
        {
            Tile[,] tiles = new Tile[width, depth];
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                tiles[x, z] = new Tile();
            return tiles;
        }
    }
}
