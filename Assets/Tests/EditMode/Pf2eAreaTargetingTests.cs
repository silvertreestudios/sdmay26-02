using System.Collections.Generic;
using System.Linq;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class Pf2eAreaTargetingTests
    {
        [Test]
        public void EmanationUsesAlternatingDiagonalDistanceAndIncludeCenterPolicy()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            GameObject actor = CreateToken("actor", 3, 3);
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Emanation,
                SizeFeet = 10,
                IncludeCenter = false
            };
            AreaPlacement placement = new() { Shape = AreaShape.Emanation, OriginCell = new Vector3Int(3, 0, 3) };

            AreaTargetResult result = AreaTargeting.Evaluate(actor, tiles, request, placement);

            CollectionAssert.AreEquivalent(new[]
            {
                Cell(1, 2), Cell(1, 3), Cell(1, 4),
                Cell(2, 1), Cell(2, 2), Cell(2, 3), Cell(2, 4), Cell(2, 5),
                Cell(3, 1), Cell(3, 2),             Cell(3, 4), Cell(3, 5),
                Cell(4, 1), Cell(4, 2), Cell(4, 3), Cell(4, 4), Cell(4, 5),
                Cell(5, 2), Cell(5, 3), Cell(5, 4)
            }, result.Cells);
            Assert.IsFalse(result.Cells.Contains(Cell(3, 3)));

            request.IncludeCenter = true;
            result = AreaTargeting.Evaluate(actor, tiles, request, placement);
            Assert.Contains(Cell(3, 3), result.Cells);

            Object.DestroyImmediate(actor);
        }

        [Test]
        public void LineDefaultsToFiveFootWidthAndSupportsDiagonalDistance()
        {
            Tile[,] tiles = BuildTiles(10, 10);
            GameObject actor = CreateToken("actor", 1, 1);
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Line,
                SizeFeet = 30
            };

            AreaTargetResult east = AreaTargeting.Evaluate(actor, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Line,
                Direction = AreaDirection.East
            });
            CollectionAssert.AreEqual(new[] { Cell(2, 1), Cell(3, 1), Cell(4, 1), Cell(5, 1), Cell(6, 1), Cell(7, 1) }, east.Cells);

            AreaTargetResult diagonal = AreaTargeting.Evaluate(actor, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Line,
                Direction = AreaDirection.NorthEast
            });
            CollectionAssert.AreEqual(new[] { Cell(2, 2), Cell(3, 3), Cell(4, 4), Cell(5, 5) }, diagonal.Cells);

            Object.DestroyImmediate(actor);
        }

        [Test]
        public void AreaTargetingSupportsSourceCellWithoutActorObject()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            AreaTargetSource source = new(Cell(2, 2));
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Line,
                SizeFeet = 15
            };

            AreaTargetResult result = AreaTargeting.Evaluate(source, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Line,
                Direction = AreaDirection.East
            });

            Assert.IsNotNull(result);
            CollectionAssert.AreEqual(new[] { Cell(3, 2), Cell(4, 2), Cell(5, 2) }, result.Cells);
            Assert.AreEqual(Cell(2, 2), result.Placement.OriginCell);
        }

        [Test]
        public void ConeUsesAimedQuarterCircleAndExcludesCasterCell()
        {
            Tile[,] tiles = BuildTiles(8, 8);
            GameObject actor = CreateToken("actor", 3, 3);
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Cone,
                SizeFeet = 15
            };

            AreaTargetResult result = AreaTargeting.Evaluate(actor, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Cone,
                Direction = AreaDirection.East
            });

            CollectionAssert.AreEquivalent(new[]
            {
                Cell(4, 2), Cell(4, 3), Cell(4, 4),
                Cell(5, 1), Cell(5, 2), Cell(5, 3), Cell(5, 4), Cell(5, 5),
                Cell(6, 2), Cell(6, 3), Cell(6, 4)
            }, result.Cells);
            Assert.IsFalse(result.Cells.Contains(Cell(3, 3)));

            Object.DestroyImmediate(actor);
        }

        [Test]
        public void BurstOriginMustBeInRangeAndAffectedCellsAreTrimmedToGrid()
        {
            Tile[,] tiles = BuildTiles(5, 5);
            GameObject actor = CreateToken("actor", 0, 0);
            AreaTargetRequest request = new()
            {
                Shape = AreaShape.Burst,
                SizeFeet = 10,
                RangeFeet = 10
            };

            AreaTargetResult legal = AreaTargeting.Evaluate(actor, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Burst,
                OriginCorner = new Vector2Int(1, 1)
            });
            Assert.IsNotNull(legal);
            Assert.Contains(Cell(0, 0), legal.Cells);
            Assert.Contains(Cell(2, 2), legal.Cells);
            Assert.IsTrue(legal.Cells.All(cell => cell.x >= 0 && cell.z >= 0 && cell.x < 5 && cell.z < 5));

            AreaTargetResult tooFar = AreaTargeting.Evaluate(actor, tiles, request, new AreaPlacement
            {
                Shape = AreaShape.Burst,
                OriginCorner = new Vector2Int(5, 5)
            });
            Assert.IsNull(tooFar);

            Object.DestroyImmediate(actor);
        }

        [Test]
        public void AreaResultReportsOccupantsAlliesLineOfEffectAndCover()
        {
            Tile[,] tiles = BuildTiles(5, 3);
            GameObject actor = CreateToken("actor", 0, 1);
            GameObject ally = CreateToken("ally", 1, 1);
            GameObject blocked = CreateToken("blocked", 4, 1);
            tiles[1, 1].Occupants.Add(ally);
            tiles[4, 1].Occupants.Add(blocked);
            tiles[2, 1] = null;

            AreaTargetRequest line = new()
            {
                Shape = AreaShape.Line,
                SizeFeet = 20
            };
            AreaTargetResult result = AreaTargeting.Evaluate(actor, tiles, line, new AreaPlacement
            {
                Shape = AreaShape.Line,
                Direction = AreaDirection.East
            });

            AreaAffectedCreature allyResult = result.Creatures.Single(creature => creature.Creature == ally);
            AreaAffectedCreature blockedResult = result.Creatures.Single(creature => creature.Creature == blocked);
            Assert.AreEqual(StrikeLineOfEffect.Clear, allyResult.LineOfEffect);
            Assert.AreEqual(StrikeLineOfEffect.Blocked, blockedResult.LineOfEffect);
            Assert.IsTrue(allyResult.IsAffected);
            Assert.IsFalse(blockedResult.IsAffected);

            tiles[2, 1] = new Tile();
            tiles[1, 2] = null;
            GameObject covered = CreateToken("covered", 2, 2);
            tiles[2, 2].Occupants.Add(covered);
            AreaTargetResult cone = AreaTargeting.Evaluate(actor, tiles, new AreaTargetRequest
            {
                Shape = AreaShape.Cone,
                SizeFeet = 15
            }, new AreaPlacement
            {
                Shape = AreaShape.Cone,
                Direction = AreaDirection.NorthEast
            });

            AreaAffectedCreature coveredResult = cone.Creatures.Single(creature => creature.Creature == covered);
            Assert.AreEqual(StrikeLineOfEffect.Clear, coveredResult.LineOfEffect);
            Assert.AreEqual(StrikeCover.Standard, coveredResult.Cover);

            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(ally);
            Object.DestroyImmediate(blocked);
            Object.DestroyImmediate(covered);
        }

        private static Tile[,] BuildTiles(int width, int height)
        {
            Tile[,] tiles = new Tile[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                    tiles[x, z] = new Tile();
            }
            return tiles;
        }

        private static GameObject CreateToken(string name, int x, int z)
        {
            GameObject token = new(name);
            token.transform.position = new Vector3(x, 0, z);
            return token;
        }

        private static Vector3Int Cell(int x, int z)
        {
            return new Vector3Int(x, 0, z);
        }
    }
}
