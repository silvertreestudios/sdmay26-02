using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class DungeonEncounterBuilderTests
{
    private static readonly DungeonEncounterCandidate[] LevelMinusOneCandidates =
    {
        new("goblin-warrior", -1),
        new("kobold-warrior", -1),
        new("skeleton-guard", -1),
        new("zombie-shambler", -1)
    };

    [TestCase(1, DungeonEncounterThreat.Trivial, 10)]
    [TestCase(1, DungeonEncounterThreat.Low, 0)]
    [TestCase(1, DungeonEncounterThreat.Moderate, 20)]
    [TestCase(2, DungeonEncounterThreat.Trivial, 20)]
    [TestCase(2, DungeonEncounterThreat.Low, 20)]
    [TestCase(2, DungeonEncounterThreat.Moderate, 40)]
    [TestCase(4, DungeonEncounterThreat.Trivial, 40)]
    [TestCase(4, DungeonEncounterThreat.Low, 60)]
    [TestCase(4, DungeonEncounterThreat.Moderate, 80)]
    [TestCase(6, DungeonEncounterThreat.Trivial, 60)]
    [TestCase(6, DungeonEncounterThreat.Low, 100)]
    [TestCase(6, DungeonEncounterThreat.Moderate, 120)]
    public void Budget_UsesPf2ePartySizeAdjustments(
        int partySize,
        DungeonEncounterThreat threat,
        int expected)
    {
        Assert.That(DungeonEncounterRules.GetBudget(partySize, threat), Is.EqualTo(expected));
    }

    [TestCase(-4, 10)]
    [TestCase(-3, 15)]
    [TestCase(-2, 20)]
    [TestCase(-1, 30)]
    [TestCase(0, 40)]
    [TestCase(1, 60)]
    [TestCase(2, 80)]
    [TestCase(3, 120)]
    [TestCase(4, 160)]
    public void CreatureXp_UsesOfficialRelativeLevelTable(int difference, int expected)
    {
        Assert.That(
            DungeonEncounterRules.TryGetCreatureXp(7, 7 + difference, out int xp),
            Is.True);
        Assert.That(xp, Is.EqualTo(expected));
    }

    [TestCase(-5)]
    [TestCase(5)]
    public void CreatureXp_ExcludesCandidatesOutsideSupportedTable(int difference)
    {
        Assert.That(
            DungeonEncounterRules.TryGetCreatureXp(7, 7 + difference, out int xp),
            Is.False);
        Assert.That(xp, Is.Zero);
    }

    [TestCase(DungeonEncounterThreat.Trivial, 1)]
    [TestCase(DungeonEncounterThreat.Low, 1)]
    [TestCase(DungeonEncounterThreat.Moderate, 2)]
    public void TwoLevelOneCharacters_ReceiveRequiredLevelMinusOneCounts(
        DungeonEncounterThreat threat,
        int expectedCount)
    {
        DungeonEncounterBuildResult result = new DungeonEncounterBuilder().Build(
            1,
            2,
            threat,
            LevelMinusOneCandidates,
            10,
            new SystemDungeonRandom(155));

        Assert.That(result.CreatureIds, Has.Count.EqualTo(expectedCount));
        Assert.That(result.SpentXp, Is.EqualTo(expectedCount * 20));
        Assert.That(result.SpentXp, Is.LessThanOrEqualTo(result.Budget));
    }

    [Test]
    public void Builder_MaximizesXpBeforeCountAndThenPrefersPartySizedCount()
    {
        DungeonEncounterBuildResult maximum = new DungeonEncounterBuilder().Build(
            5,
            4,
            DungeonEncounterThreat.Low,
            new[]
            {
                new DungeonEncounterCandidate("same", 5),
                new DungeonEncounterCandidate("minus-one", 4),
                new DungeonEncounterCandidate("minus-two", 3)
            },
            2,
            new SystemDungeonRandom(1));
        Assert.That(maximum.SpentXp, Is.EqualTo(60));
        Assert.That(maximum.CreatureIds, Is.EquivalentTo(new[] { "same", "minus-two" }));

        DungeonEncounterBuildResult nearest = new DungeonEncounterBuilder().Build(
            5,
            4,
            DungeonEncounterThreat.Low,
            new[]
            {
                new DungeonEncounterCandidate("minus-one", 4),
                new DungeonEncounterCandidate("minus-two", 3)
            },
            4,
            new SystemDungeonRandom(2));
        Assert.That(nearest.SpentXp, Is.EqualTo(60));
        Assert.That(nearest.CreatureIds, Has.Count.EqualTo(3));
        Assert.That(nearest.CreatureIds, Has.All.EqualTo("minus-two"));
    }

    [Test]
    public void Builder_IsCandidateOrderIndependentAndAllowsRepeats()
    {
        DungeonEncounterCandidate[] forward = LevelMinusOneCandidates.ToArray();
        DungeonEncounterCandidate[] reverse = forward.Reverse().ToArray();
        DungeonEncounterBuildResult first = new DungeonEncounterBuilder().Build(
            1, 4, DungeonEncounterThreat.Moderate, forward, 4, new SystemDungeonRandom(99));
        DungeonEncounterBuildResult second = new DungeonEncounterBuilder().Build(
            1, 4, DungeonEncounterThreat.Moderate, reverse, 4, new SystemDungeonRandom(99));

        Assert.That(second.CreatureIds, Is.EqualTo(first.CreatureIds));
        Assert.That(first.CreatureIds, Has.Count.EqualTo(4));
    }

    [Test]
    public void Builder_HandlesZeroCapacityEmptyAndUnsatisfiableCandidates()
    {
        DungeonEncounterBuilder builder = new();
        Assert.That(builder.Build(
            1, 4, DungeonEncounterThreat.Moderate, LevelMinusOneCandidates, 0,
            new SystemDungeonRandom(1)).CreatureIds, Is.Empty);
        Assert.That(builder.Build(
            1, 4, DungeonEncounterThreat.Moderate,
            Array.Empty<DungeonEncounterCandidate>(), 8,
            new SystemDungeonRandom(1)).CreatureIds, Is.Empty);
        Assert.That(builder.Build(
            1, 4, DungeonEncounterThreat.Moderate,
            new[] { new DungeonEncounterCandidate("too-high", 6) }, 8,
            new SystemDungeonRandom(1)).CreatureIds, Is.Empty);
    }

    [TestCase(0, 49, DungeonEncounterThreat.Trivial)]
    [TestCase(0, 50, DungeonEncounterThreat.Low)]
    [TestCase(0, 84, DungeonEncounterThreat.Low)]
    [TestCase(0, 85, DungeonEncounterThreat.Moderate)]
    [TestCase(1, 44, DungeonEncounterThreat.Trivial)]
    [TestCase(1, 45, DungeonEncounterThreat.Low)]
    [TestCase(7, 14, DungeonEncounterThreat.Trivial)]
    [TestCase(7, 15, DungeonEncounterThreat.Low)]
    [TestCase(8, 9, DungeonEncounterThreat.Trivial)]
    [TestCase(8, 10, DungeonEncounterThreat.Low)]
    [TestCase(8, 44, DungeonEncounterThreat.Low)]
    [TestCase(8, 45, DungeonEncounterThreat.Moderate)]
    [TestCase(20, 45, DungeonEncounterThreat.Moderate)]
    public void ThreatDistribution_UsesPinnedDepthBoundaries(
        int depth,
        int draw,
        DungeonEncounterThreat expected)
    {
        Assert.That(
            DungeonEncounterRules.SelectThreat(depth, new ScriptedRandom(draw)),
            Is.EqualTo(expected));
    }

    [Test]
    public void Catalog_IsStrictDataDrivenAndPointsToExistingContent()
    {
        TextAsset asset = Resources.Load<TextAsset>("DataFiles/dungeon/encounter-enemies");
        Assert.That(asset, Is.Not.Null);
        IReadOnlyList<DungeonEncounterCandidate> candidates =
            DungeonEncounterCatalogJson.Parse(asset.text);

        Assert.That(candidates.Select(candidate => candidate.Id), Is.EqualTo(new[]
        {
            "goblin-warrior",
            "kobold-warrior",
            "skeleton-guard",
            "zombie-shambler"
        }));
        foreach (DungeonEncounterCandidate candidate in candidates)
        {
            Assert.That(Resources.Load<TextAsset>(candidate.ResourcePath), Is.Not.Null, candidate.Id);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(candidate.PrefabPath),
                Is.Not.Null,
                candidate.Id);
        }

        string duplicate = asset.text.Replace(
            "\"kobold-warrior\"",
            "\"goblin-warrior\"");
        Assert.Throws<FormatException>(() => DungeonEncounterCatalogJson.Parse(duplicate));
        Assert.Throws<FormatException>(() => DungeonEncounterCatalogJson.Parse(
            "{\"schema\":\"sdmay26-02/dungeon-encounter-catalog\",\"enemies\":[],\"extra\":true}"));
        Assert.Throws<FormatException>(() => DungeonEncounterCatalogJson.Parse(
            "{\"schema\":\"sdmay26-02/dungeon-encounter-catalog\",\"enemies\":[{" +
            "\"id\":123,\"level\":-1,\"resourcePath\":true,\"prefabPath\":\"Assets/enemy.prefab\"}]}"));
    }

    [Test]
    public void Planner_ExcludesArrivalRoomAndProducesByteStableCurrentSchema()
    {
        DungeonLevelDocument source = TwoRoomDocument(0, false);
        DungeonEncounterPlanner planner = new();
        DungeonLevelDocument first = planner.Plan(source, 1, 4, LevelMinusOneCandidates);
        DungeonLevelDocument second = planner.Plan(source, 1, 4, LevelMinusOneCandidates.Reverse().ToArray());

        Assert.That(first.EncounterPlans, Has.Count.EqualTo(1));
        Assert.That(first.EncounterPlans[0].RoomId, Is.EqualTo(2));
        Assert.That(first.EncounterPlans[0].CreatureIds, Is.Not.Empty);
        Assert.That(first.EncounterPlans[0].SpawnCells, Is.Unique);
        Assert.That(
            DungeonLevelJsonSerializer.Serialize(second),
            Is.EqualTo(DungeonLevelJsonSerializer.Serialize(first)));
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            DungeonLevelJsonSerializer.Serialize(first));
        Assert.That(parsed.IsSuccess, Is.True,
            string.Join(Environment.NewLine, parsed.Diagnostics.Select(item => item.Message)));
    }

    [Test]
    public void Planner_ExcludesDeeperUpStairArrivalRoomAndKeepsEmptyPlans()
    {
        DungeonLevelDocument source = TwoRoomDocument(1, true);
        DungeonLevelDocument planned = new DungeonEncounterPlanner().Plan(
            source,
            1,
            4,
            Array.Empty<DungeonEncounterCandidate>());

        Assert.That(planned.EncounterPlans, Has.Count.EqualTo(1));
        Assert.That(planned.EncounterPlans[0].RoomId, Is.EqualTo(1));
        Assert.That(planned.EncounterPlans[0].CreatureIds, Is.Empty);
        Assert.That(planned.EncounterPlans[0].SpawnCells, Is.Empty);
    }

    [Test]
    public void Planner_ExcludesExistingObjectAnchorsFromSpawnCells()
    {
        DungeonLevelDocument topology = TwoRoomDocument(0, false);
        DungeonObjectPlacement placement = new(
            "decoration-0002-01",
            "dungeon/assets/fbx(unity)/banner_red",
            new DungeonCell(5, 1));
        DungeonLevelDocument decorated = new(
            topology.Generation,
            topology.Rows,
            topology.Rooms,
            topology.Doors,
            topology.Stairs,
            topology.StartCell,
            topology.SafeCells,
            new[] { placement },
            Array.Empty<DungeonEncounterPlan>());

        DungeonLevelDocument planned = new DungeonEncounterPlanner().Plan(
            decorated,
            1,
            4,
            LevelMinusOneCandidates);

        Assert.That(planned.Objects, Is.EqualTo(decorated.Objects));
        Assert.That(planned.EncounterPlans.SelectMany(plan => plan.SpawnCells),
            Has.None.EqualTo(placement.Cell));
    }

    [Test]
    public void CurrentSchema_RejectsReservedSpawnsAndMultiplePlansForOneRoom()
    {
        DungeonGenerationRequest request = new()
        {
            RunSeed = 155,
            Depth = 0,
            Width = 31,
            Height = 31,
            MinimumRoomCount = 2,
            StairCount = 0
        };
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(request);
        Assert.That(generated.IsSuccess, Is.True);
        DungeonLevelDocument planned = new DungeonEncounterPlanner().Plan(
            generated.Document,
            1,
            2,
            LevelMinusOneCandidates);
        JObject root = JObject.Parse(DungeonLevelJsonSerializer.Serialize(planned));
        JArray plans = (JArray)root["encounterPlans"];
        Assert.That(plans, Is.Not.Empty);

        JObject reservedSpawn = (JObject)root.DeepClone();
        JObject firstPlan = (JObject)((JArray)reservedSpawn["encounterPlans"])[0];
        DungeonCell start = planned.StartCell;
        ((JArray)firstPlan["spawnCells"])[0] = new JObject
        {
            ["x"] = start.X,
            ["z"] = start.Z
        };
        DungeonLevelParseResult reservedResult = DungeonLevelJsonParser.Parse(
            reservedSpawn.ToString());
        Assert.That(reservedResult.IsSuccess, Is.False);
        Assert.That(reservedResult.Diagnostics.Any(diagnostic =>
            diagnostic.Field == "encounterPlans" &&
            diagnostic.Message.Contains("safe-arrival")), Is.True);

        JObject duplicateRoom = (JObject)root.DeepClone();
        JArray duplicatePlans = (JArray)duplicateRoom["encounterPlans"];
        JObject duplicate = (JObject)duplicatePlans[0].DeepClone();
        duplicate["id"] = duplicate.Value<string>("id") + "-duplicate";
        duplicatePlans.Add(duplicate);
        DungeonLevelParseResult duplicateResult = DungeonLevelJsonParser.Parse(
            duplicateRoom.ToString());
        Assert.That(duplicateResult.IsSuccess, Is.False);
        Assert.That(duplicateResult.Diagnostics.Any(diagnostic =>
            diagnostic.Field == "encounterPlans" &&
            diagnostic.Message.Contains("at most one")), Is.True);
    }

    [Test]
    public void Planner_PropertiesHoldAcross256GeneratedSeeds()
    {
        for (int seed = 0; seed < 256; seed++)
        {
            DungeonGenerationRequest request = new()
            {
                RunSeed = seed,
                Depth = 0,
                Width = 31,
                Height = 31,
                MinimumRoomCount = 1,
                StairCount = 0
            };
            DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(request);
            Assert.That(generated.IsSuccess, Is.True, $"seed {seed}");
            DungeonLevelDocument planned = new DungeonEncounterPlanner().Plan(
                generated.Document,
                1,
                2,
                LevelMinusOneCandidates);

            DungeonRoom excluded = generated.Document.Rooms.Single(room =>
                Contains(room, generated.Document.StartCell));
            Assert.That(planned.EncounterPlans, Has.Count.EqualTo(planned.Rooms.Count - 1), $"seed {seed}");
            Assert.That(planned.EncounterPlans.Any(plan => plan.RoomId == excluded.Id), Is.False, $"seed {seed}");
            HashSet<DungeonCell> reserved = new(planned.SafeCells);
            reserved.Add(planned.StartCell);
            foreach (DungeonDoor door in planned.Doors)
                reserved.Add(door.Cell);
            foreach (DungeonStair stair in planned.Stairs)
            {
                reserved.Add(stair.Cell);
                reserved.Add(stair.ArrivalCell);
            }

            foreach (DungeonEncounterPlan plan in planned.EncounterPlans)
            {
                DungeonRoom room = planned.Rooms.Single(candidate => candidate.Id == plan.RoomId);
                Assert.That(plan.SpawnCells, Is.Unique, $"seed {seed} room {room.Id}");
                Assert.That(plan.SpawnCells.Count, Is.EqualTo(plan.CreatureIds.Count));
                Assert.That(plan.CreatureIds.All(id => LevelMinusOneCandidates.Any(
                    candidate => candidate.Id == id)), Is.True);
                Assert.That(plan.CreatureIds.Count * 20, Is.LessThanOrEqualTo(plan.Budget));
                Assert.That(plan.SpawnCells.All(cell =>
                    Contains(room, cell) &&
                    !reserved.Contains(cell) &&
                    IsWalkable(planned, cell)), Is.True, $"seed {seed} room {room.Id}");
            }

            string json = DungeonLevelJsonSerializer.Serialize(planned);
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
            Assert.That(parsed.IsSuccess, Is.True,
                $"seed {seed}: {string.Join("; ", parsed.Diagnostics.Select(item => item.Message))}");
            Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        }
    }

    private static DungeonLevelDocument TwoRoomDocument(int depth, bool includeUpStair)
    {
        DungeonStair[] stairs = includeUpStair
            ? new[]
            {
                new DungeonStair(
                    "stair-up",
                    DungeonStairKind.Up,
                    new DungeonCell(4, 2),
                    new DungeonCell(5, 2))
            }
            : Array.Empty<DungeonStair>();
        return new DungeonLevelDocument(
            new DungeonGenerationMetadata("test", 155, depth, 0),
            new[]
            {
                "#########",
                "#...#...#",
                "#...D...#",
                "#...#...#",
                "#########"
            },
            new[]
            {
                new DungeonRoom(1, 1, 1, 3, 3),
                new DungeonRoom(2, 5, 1, 7, 3)
            },
            new[] { new DungeonDoor("door", new DungeonCell(4, 2)) },
            stairs,
            new DungeonCell(2, 2),
            new[] { new DungeonCell(2, 2), new DungeonCell(6, 2) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>());
    }

    private static bool Contains(DungeonRoom room, DungeonCell cell) =>
        cell.X >= room.MinimumX && cell.X <= room.MaximumX &&
        cell.Z >= room.MinimumZ && cell.Z <= room.MaximumZ;

    private static bool IsWalkable(DungeonLevelDocument document, DungeonCell cell)
    {
        char symbol = document.Rows[document.Height - 1 - cell.Z][cell.X];
        return symbol == '.' || symbol == 'D';
    }

    private sealed class ScriptedRandom : IDungeonRandom
    {
        private readonly Queue<int> values;

        internal ScriptedRandom(params int[] values)
        {
            this.values = new Queue<int>(values);
        }

        public int NextInt(int exclusiveMaximum)
        {
            int value = values.Dequeue();
            Assert.That(value, Is.GreaterThanOrEqualTo(0).And.LessThan(exclusiveMaximum));
            return value;
        }

        public bool NextPercent(int percentage) => NextInt(100) < percentage;
    }
}
