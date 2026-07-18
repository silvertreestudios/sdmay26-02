using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.DungeonGeneration;
using Game.KayKit;
using NUnit.Framework;
using UnityEngine;

public sealed class DungeonGenerationTests
{
    [Test]
    public void SplitMix64_PreservesSignedSeedBitsAndKnownSequence()
    {
        Assert.That(DungeonSeedSequence.ForDepth(-1, 0), Is.EqualTo(ulong.MaxValue));
        Assert.That(new SplitMix64DungeonRandom(0).NextUInt64(), Is.EqualTo(0xE220A8397B1DCDAFUL));
        Assert.That(DungeonSeedSequence.ForSubstream(4, 2, DungeonSeedSubstream.Topology),
            Is.Not.EqualTo(DungeonSeedSequence.ForSubstream(4, 2, DungeonSeedSubstream.Decoration)));
        Assert.That(DungeonSeedSequence.ForTopologyAttempt(4, 2, 0),
            Is.Not.EqualTo(DungeonSeedSequence.ForTopologyAttempt(4, 2, 1)));
    }

    [Test]
    public void GoldenDocument_IsByteStableAcrossGeneratorAndSerializerRuns()
    {
        DungeonGenerationRequest request = Request(-9223372036854775807L, 31, 31);
        string first = GenerateJson(request);
        string second = GenerateJson(request);

        Assert.That(second, Is.EqualTo(first));
        string hash;
        using (SHA256 sha256 = SHA256.Create())
            hash = BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(first))).Replace("-", string.Empty).ToLowerInvariant();
        TestContext.WriteLine("golden sha256=" + hash);
        Assert.That(hash, Is.EqualTo("dc369bcdf9d03c1a5950c95c2d0b57414dd03a43e83f83d4d051619f9afd968c"));
    }

    [Test]
    public void KayKitParser_AcceptsVersionTwoAndRetainsLosslessDocument()
    {
        string json = GenerateJson(Request(-17, 31, 31));
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        try
        {
            KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(json, catalog);

            Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.Map.Version, Is.EqualTo(2));
            Assert.That(result.Map.LevelDocument, Is.Not.Null);
            Assert.That(DungeonLevelJsonSerializer.Serialize(result.Map.LevelDocument), Is.EqualTo(json));
            Assert.That(KayKitDungeonMapData.SupportedVersion, Is.EqualTo(1));
            Assert.That(KayKitDungeonMapData.SupportedVersions, Is.EqualTo(new[] { 1, 2 }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void GeneratedTopology_SatisfiesStructuralPropertiesAcross256Seeds()
    {
        IDungeonGenerator generator = new DeterministicDungeonGenerator();
        for (int seed = 0; seed < 256; seed++)
        {
            DungeonGenerationRequest request = Request(seed - 128, 31, 31);
            request.Layout = (DungeonLayout)(seed % 3);
            request.RoomLayout = (DungeonRoomLayout)(seed % 2);
            request.CorridorLayout = (DungeonCorridorLayout)(seed % 3);
            DungeonGenerationResult result = generator.Generate(request);
            Assert.That(result.IsSuccess, Is.True, "seed " + seed + ": " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
            AssertDocumentInvariants(result.Document, seed);
        }
    }

    [Test]
    public void VersionTwoJson_RoundTripsEveryContractSectionWithoutLoss()
    {
        DungeonLevelDocument source = new(
            new DungeonGenerationMetadata("test", 7, long.MinValue, 3, 2, "8000000000000000", "0123456789abcdef"),
            new[] { "#D#", "...", "###" },
            new[] { new DungeonRoom(1, 0, 1, 2, 1) },
            new[] { new DungeonDoor("door-0001", new DungeonCell(1, 2), true) },
            new[] { new DungeonStair("stair-up", DungeonStairKind.Up, new DungeonCell(0, 1), new DungeonCell(1, 1)) },
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1), new DungeonCell(2, 1) },
            new[] { new DungeonObjectPlacement("object-1", "prop", new DungeonCell(2, 1), 270, "used") },
            new[] { new DungeonEncounterPlan("encounter-1", 1, new[] { new DungeonCell(0, 1) }, new[] { "creature-a", "creature-b" }, true) },
            new DungeonRuntimeState(new[] { "door-0001" }, new[] { "encounter-1" }, new[] { "creature-a#1" },
                new[] { new DungeonCreatureRuntimeState("creature-b#1", "creature-b", "encounter-1", new DungeonCell(2, 1), 7, "slowed") }));

        string json = DungeonLevelJsonSerializer.Serialize(source);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(parsed.IsSuccess, Is.True, string.Join(Environment.NewLine, parsed.Diagnostics.Select(d => d.Message)));
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        Assert.That(parsed.Document.Generation.RunSeed, Is.EqualTo(long.MinValue));
    }

    [Test]
    public void InvalidRequest_ReturnsActionableDiagnosticsAndNoDocument()
    {
        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(new DungeonGenerationRequest { Width = 20 });

        Assert.That(result.Document, Is.Null);
        Assert.That(result.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(d => d.Code == DungeonGenerationDiagnosticCode.InvalidRequest && d.Field == "Width"));
    }

    [Test]
    public void RejectedTopology_StopsAfter32AttemptsAndReturnsNoPartialDocument()
    {
        DungeonGenerationRequest request = Request(55, 15, 15);
        request.Layout = DungeonLayout.Cross;
        request.MaximumRoomSize = 5;
        request.MinimumRoomCount = 999;

        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);

        Assert.That(result.Document, Is.Null);
        Assert.That(result.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(d => d.Code == DungeonGenerationDiagnosticCode.RetryLimitExhausted));
        Assert.That(result.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(d => d.Attempt == 31));
    }

    [Test]
    public void GeneratorAssembly_HasNoUnitySceneMonoBehaviourOrRandomDependency()
    {
        Assembly assembly = typeof(DeterministicDungeonGenerator).Assembly;
        Assert.That(assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Has.None.StartsWith("UnityEngine"));
        Assert.That(assembly.GetTypes().SelectMany(type => new[] { type.FullName, type.BaseType?.FullName })
            .Any(value => value != null && value.Contains("UnityEngine.MonoBehaviour")), Is.False);
        Assert.That(assembly.GetTypes().SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Any(member => member.ToString().Contains("UnityEngine.Random")), Is.False);
        Assert.That(assembly.GetTypes().Any(type => type.FullName != null && type.FullName.Contains("UnityEngine.SceneManagement")), Is.False);
    }

    private static DungeonGenerationRequest Request(long seed, int width, int height) => new()
    {
        RunSeed = seed,
        Width = width,
        Height = height,
        Layout = DungeonLayout.Round,
        RoomLayout = DungeonRoomLayout.Scattered,
        CorridorLayout = DungeonCorridorLayout.Bent,
        MinimumRoomSize = 3,
        MaximumRoomSize = 7,
        MinimumRoomCount = 1,
        StairCount = 2,
        DeadEndRemovalPercent = 50
    };

    private static string GenerateJson(DungeonGenerationRequest request)
    {
        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);
        Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        return DungeonLevelJsonSerializer.Serialize(result.Document);
    }

    private static void AssertDocumentInvariants(DungeonLevelDocument document, int seed)
    {
        HashSet<DungeonCell> walkable = Walkable(document);
        Assert.That(walkable, Does.Contain(document.StartCell), "start seed " + seed);
        Assert.That(document.SafeCells.All(walkable.Contains), Is.True, "safe seed " + seed);
        Assert.That(document.Stairs.All(stair => walkable.Contains(stair.Cell) && walkable.Contains(stair.ArrivalCell)), Is.True, "stairs seed " + seed);

        Queue<DungeonCell> queue = new(); HashSet<DungeonCell> reached = new() { walkable.First() }; queue.Enqueue(walkable.First());
        while (queue.Count > 0)
        {
            DungeonCell cell = queue.Dequeue();
            foreach (DungeonCell next in Neighbors(cell).Where(walkable.Contains)) if (reached.Add(next)) queue.Enqueue(next);
        }
        Assert.That(reached.Count, Is.EqualTo(walkable.Count), "connectivity seed " + seed);

        for (int i = 0; i < document.Rooms.Count; i++)
        for (int j = i + 1; j < document.Rooms.Count; j++)
            Assert.That(Overlaps(document.Rooms[i], document.Rooms[j]), Is.False, "rooms seed " + seed);

        foreach (DungeonDoor door in document.Doors)
        {
            List<DungeonCell> open = Neighbors(door.Cell).Where(walkable.Contains).ToList();
            Assert.That(open.Count, Is.EqualTo(2), "door neighbor count seed " + seed);
            Assert.That(open[0].X == open[1].X || open[0].Z == open[1].Z, Is.True, "door axis seed " + seed);
        }
    }

    private static HashSet<DungeonCell> Walkable(DungeonLevelDocument document)
    {
        HashSet<DungeonCell> result = new();
        for (int row = 0; row < document.Rows.Count; row++)
        for (int x = 0; x < document.Rows[row].Length; x++)
            if (document.Rows[row][x] == '.' || document.Rows[row][x] == 'D') result.Add(new DungeonCell(x, document.Rows.Count - 1 - row));
        return result;
    }

    private static IEnumerable<DungeonCell> Neighbors(DungeonCell cell)
    {
        yield return new DungeonCell(cell.X + 1, cell.Z); yield return new DungeonCell(cell.X - 1, cell.Z);
        yield return new DungeonCell(cell.X, cell.Z + 1); yield return new DungeonCell(cell.X, cell.Z - 1);
    }

    private static bool Overlaps(DungeonRoom left, DungeonRoom right) =>
        left.MinimumX <= right.MaximumX && left.MaximumX >= right.MinimumX && left.MinimumZ <= right.MaximumZ && left.MaximumZ >= right.MinimumZ;
}
