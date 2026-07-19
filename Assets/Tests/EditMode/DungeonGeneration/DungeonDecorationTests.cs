using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using NUnit.Framework;

public sealed class DungeonDecorationTests
{
    [Test]
    public void Planner_PlacesOneCenteredSconcePerShortestCornerSafeWallRun()
    {
        string[] rows =
        {
            "#######",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            "#######"
        };
        ScriptedDungeonRandom random = new(new[] { false }, Array.Empty<int>());

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                new[] { new DungeonRoom(1, 1, 1, 5, 5) },
                Array.Empty<DungeonCell>(),
                random);

        Assert.That(random.PercentCalls, Is.EqualTo(1));
        Assert.That(random.IntegerCalls, Is.Zero);
        Assert.That(placements.Select(placement => placement.Id), Is.EqualTo(new[]
        {
            "sconce-0001",
            "sconce-0002",
            "sconce-0003",
            "sconce-0004"
        }));
        Assert.That(placements.Select(placement => (placement.Cell, placement.Rotation)),
            Is.EquivalentTo(new[]
            {
                (new DungeonCell(3, 1), 0),
                (new DungeonCell(1, 3), 90),
                (new DungeonCell(3, 5), 180),
                (new DungeonCell(5, 3), 270)
            }));
        Assert.That(placements.All(placement =>
            placement.AssetId == DungeonDecorationPlanner.TorchAssetId), Is.True);
        Assert.That(placements.All(placement => HasWallAtRotation(rows, placement)), Is.True);
        Assert.That(placements.All(placement => HasTorchCornerClearance(rows, placement)), Is.True);
    }

    [Test]
    public void Planner_SkipsWallRunsWithoutCornerSafeTorchPositions()
    {
        string[] rows =
        {
            "#####",
            "#...#",
            "#...#",
            "#...#",
            "#####"
        };

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                Array.Empty<DungeonRoom>(),
                Array.Empty<DungeonCell>(),
                new ScriptedDungeonRandom(Array.Empty<bool>(), Array.Empty<int>()));

        Assert.That(placements, Is.Empty);
    }

    [Test]
    public void Planner_KeepsBannerRandomnessSeparateFromRequiredSconces()
    {
        string[] rows =
        {
            "#######",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            "#.....#",
            "#######"
        };
        ScriptedDungeonRandom random = new(new[] { true }, new[] { 0 });

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                new[] { new DungeonRoom(1, 1, 1, 5, 5) },
                Array.Empty<DungeonCell>(),
                random);

        Assert.That(random.PercentCalls, Is.EqualTo(1));
        Assert.That(random.IntegerCalls, Is.EqualTo(1));
        Assert.That(placements.Count(placement =>
            placement.AssetId == DungeonDecorationPlanner.TorchAssetId), Is.EqualTo(4));
        Assert.That(placements.Count(placement =>
            placement.AssetId == DungeonDecorationPlanner.BannerAssetId), Is.EqualTo(1));
        Assert.That(placements.Last().Id, Is.EqualTo("decoration-0001-01"));
        Assert.That(placements.Select(placement => (placement.Cell, placement.Rotation)).Distinct().Count(),
            Is.EqualTo(placements.Count));
        Assert.That(placements.All(placement => HasWallAtRotation(rows, placement)), Is.True);
    }

    [Test]
    public void Planner_ConsumesBannerChanceWhenNoWallFaceExists()
    {
        ScriptedDungeonRandom random = new(new[] { true }, Array.Empty<int>());

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                new[] { "...", "...", "..." },
                new[] { new DungeonRoom(1, 0, 0, 2, 2) },
                Array.Empty<DungeonCell>(),
                random);

        Assert.That(placements, Is.Empty);
        Assert.That(random.PercentCalls, Is.EqualTo(1));
        Assert.That(random.IntegerCalls, Is.Zero);
    }

    [Test]
    public void Planner_SplitsWallRunsAtDoors()
    {
        string[] rows =
        {
            "#########",
            "#...D...#",
            "#########"
        };

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                Array.Empty<DungeonRoom>(),
                Array.Empty<DungeonCell>(),
                new ScriptedDungeonRandom(Array.Empty<bool>(), Array.Empty<int>()));

        Assert.That(placements
                .Where(placement => placement.Rotation == 180)
                .Select(placement => placement.Cell),
            Is.EquivalentTo(new[] { new DungeonCell(3, 1), new DungeonCell(5, 1) }));
        Assert.That(placements.All(placement => placement.Cell != new DungeonCell(4, 1)), Is.True);
    }

    [Test]
    public void Planner_SpacesLongWallRunsAtEightCellIntervals()
    {
        string[] rows =
        {
            "###################",
            "#.................#",
            "###################"
        };

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                Array.Empty<DungeonRoom>(),
                Array.Empty<DungeonCell>(),
                new ScriptedDungeonRandom(Array.Empty<bool>(), Array.Empty<int>()));

        DungeonObjectPlacement[] southWall = placements
            .Where(placement => placement.Rotation == 0)
            .OrderBy(placement => placement.Cell.X)
            .ToArray();
        Assert.That(southWall.Select(placement => placement.Cell),
            Is.EqualTo(new[] { new DungeonCell(5, 1), new DungeonCell(13, 1) }));
        Assert.That(southWall[1].Cell.X - southWall[0].Cell.X,
            Is.EqualTo(DungeonDecorationPlanner.TorchSpacingCells));
    }

    [Test]
    public void Planner_SplitsWallRunsAtReservedTraversalCells()
    {
        string[] rows =
        {
            "#########",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#.......#",
            "#########"
        };
        DungeonCell reserved = new(4, 1);

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                new[] { new DungeonRoom(1, 1, 1, 7, 7) },
                new[] { reserved },
                new ScriptedDungeonRandom(new[] { false }, Array.Empty<int>()));

        Assert.That(placements.All(placement => placement.Cell != reserved), Is.True);
        Assert.That(placements
                .Where(placement => placement.Rotation == 0)
                .Select(placement => placement.Cell),
            Is.EquivalentTo(new[] { new DungeonCell(3, 1), new DungeonCell(5, 1) }));
    }

    [Test]
    public void GeneratedDecorations_AreStableWallMountedAndUseOnlyApprovedAssets()
    {
        DeterministicDungeonGenerator generator = new();
        DungeonGenerationRequest request = new()
        {
            RunSeed = 156,
            Depth = 2,
            Width = 39,
            Height = 39,
            MinimumRoomCount = 3
        };

        DungeonGenerationResult first = generator.Generate(request);
        DungeonGenerationResult second = generator.Generate(request);

        Assert.That(first.IsSuccess, Is.True, Diagnostics(first));
        Assert.That(second.IsSuccess, Is.True, Diagnostics(second));
        Assert.That(DungeonLevelJsonSerializer.Serialize(second.Document),
            Is.EqualTo(DungeonLevelJsonSerializer.Serialize(first.Document)));
        Assert.That(first.Document.Objects, Is.Not.Empty);
        Assert.That(first.Document.Objects.All(placement =>
            placement.AssetId == DungeonDecorationPlanner.BannerAssetId ||
            placement.AssetId == DungeonDecorationPlanner.TorchAssetId), Is.True);
        Assert.That(first.Document.Objects.All(placement =>
            HasWallAtRotation(first.Document.Rows, placement)), Is.True);
        Assert.That(first.Document.Objects.GroupBy(placement => (placement.Cell, placement.Rotation))
            .All(group => group.Count() == 1), Is.True);
        DungeonObjectPlacement[] sconces = first.Document.Objects
            .Where(placement => placement.AssetId == DungeonDecorationPlanner.TorchAssetId)
            .ToArray();
        Assert.That(sconces, Is.Not.Empty);
        Assert.That(sconces.Select(placement => placement.Id), Is.EqualTo(
            Enumerable.Range(1, sconces.Length).Select(index => $"sconce-{index:D4}")));
        Assert.That(sconces.All(placement =>
            HasTorchCornerClearance(first.Document.Rows, placement)), Is.True);
        HashSet<DungeonCell> reserved = new(first.Document.Stairs.SelectMany(stair =>
            new[] { stair.Cell, stair.ArrivalCell }))
        {
            first.Document.StartCell
        };
        Assert.That(first.Document.Objects.All(placement => !reserved.Contains(placement.Cell)), Is.True);
    }

    private static bool HasWallAtRotation(
        IReadOnlyList<string> rows,
        DungeonObjectPlacement placement)
    {
        (int offsetX, int offsetZ) = placement.Rotation switch
        {
            0 => (0, -1),
            90 => (-1, 0),
            180 => (0, 1),
            270 => (1, 0),
            _ => (int.MaxValue, int.MaxValue)
        };
        int x = placement.Cell.X + offsetX;
        int z = placement.Cell.Z + offsetZ;
        if (z < 0 || z >= rows.Count || x < 0 || x >= rows[0].Length)
            return false;
        return rows[rows.Count - 1 - z][x] == '#';
    }

    private static bool HasTorchCornerClearance(
        IReadOnlyList<string> rows,
        DungeonObjectPlacement placement)
    {
        int alongX = placement.Rotation == 0 || placement.Rotation == 180 ? 1 : 0;
        int alongZ = alongX == 1 ? 0 : 1;
        for (int direction = -1; direction <= 1; direction += 2)
        {
            for (int distance = 1;
                 distance < DungeonDecorationPlanner.MinimumTorchCornerDistanceCells;
                 distance++)
            {
                int x = placement.Cell.X + direction * alongX * distance;
                int z = placement.Cell.Z + direction * alongZ * distance;
                if (z < 0 || z >= rows.Count || x < 0 || x >= rows[0].Length)
                    continue;
                if (rows[rows.Count - 1 - z][x] == '#')
                    return false;
            }
        }

        return true;
    }

    private static string Diagnostics(DungeonGenerationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    private sealed class ScriptedDungeonRandom : IDungeonRandom
    {
        private readonly Queue<bool> percentages;
        private readonly Queue<int> integers;

        internal ScriptedDungeonRandom(IEnumerable<bool> percentages, IEnumerable<int> integers)
        {
            this.percentages = new Queue<bool>(percentages);
            this.integers = new Queue<int>(integers);
        }

        internal int PercentCalls { get; private set; }
        internal int IntegerCalls { get; private set; }

        public int NextInt(int exclusiveMaximum)
        {
            IntegerCalls++;
            int value = integers.Dequeue();
            Assert.That(value, Is.GreaterThanOrEqualTo(0).And.LessThan(exclusiveMaximum));
            return value;
        }

        public bool NextPercent(int percentage)
        {
            PercentCalls++;
            Assert.That(percentage, Is.EqualTo(35));
            return percentages.Dequeue();
        }
    }
}
