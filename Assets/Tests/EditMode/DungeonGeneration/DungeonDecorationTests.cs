using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using NUnit.Framework;

public sealed class DungeonDecorationTests
{
    [Test]
    public void Planner_PerformsTwoIndependentAttemptsAndUsesDistinctValidWallFaces()
    {
        string[] rows =
        {
            "#####",
            "#...#",
            "#...#",
            "#...#",
            "#####"
        };
        ScriptedDungeonRandom random = new(
            new[] { true, true },
            new[] { 0, 0, 1, 0 });

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                rows,
                new[] { new DungeonRoom(1, 1, 1, 3, 3) },
                random);

        Assert.That(random.PercentCalls, Is.EqualTo(2));
        Assert.That(random.IntegerCalls, Is.EqualTo(4));
        Assert.That(placements.Count, Is.EqualTo(2));
        Assert.That(placements.Select(placement => placement.Id), Is.EqualTo(new[]
        {
            "decoration-0001-01",
            "decoration-0001-02"
        }));
        Assert.That(placements[0].AssetId, Is.EqualTo(DungeonDecorationPlanner.BannerAssetId));
        Assert.That(placements[1].AssetId, Is.EqualTo(DungeonDecorationPlanner.TorchAssetId));
        Assert.That(placements.Select(placement => (placement.Cell, placement.Rotation)).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(placements.All(placement => HasWallAtRotation(rows, placement)), Is.True);
    }

    [Test]
    public void Planner_ConsumesBothChanceDrawsWhenNoWallFaceExists()
    {
        ScriptedDungeonRandom random = new(new[] { true, true }, Array.Empty<int>());

        IReadOnlyList<DungeonObjectPlacement> placements =
            DungeonDecorationPlanner.CreatePlacements(
                new[] { "...", "...", "..." },
                new[] { new DungeonRoom(1, 0, 0, 2, 2) },
                random);

        Assert.That(placements, Is.Empty);
        Assert.That(random.PercentCalls, Is.EqualTo(2));
        Assert.That(random.IntegerCalls, Is.Zero);
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
        Assert.That(first.Document.Rooms.All(room =>
            first.Document.Objects.Count(placement => placement.Id.StartsWith(
                $"decoration-{room.Id:D4}-", StringComparison.Ordinal)) <= 2), Is.True);
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
