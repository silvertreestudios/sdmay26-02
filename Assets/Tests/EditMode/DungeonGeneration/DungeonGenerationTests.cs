using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.DungeonGeneration;
using Game.KayKit;
using GridPrivate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class DungeonGenerationTests
{
    [Test]
    public void SplitMix64_PreservesSignedSeedBitsAndKnownSequence()
    {
        Assert.That(DungeonSeedSequence.ForDepth(-1, 0), Is.EqualTo(ulong.MaxValue));
        Assert.That(DungeonSeedSequence.ForDepth(0, 1), Is.EqualTo(0xE220A8397B1DCDAFUL));
        Assert.That(DungeonSeedSequence.ForDepth(0, 2), Is.EqualTo(0x6E789E6AA1B965F4UL));
        Assert.That(DungeonSeedSequence.ForDepth(0, 3), Is.EqualTo(0x06C45D188009454FUL));
        Assert.That(DungeonSeedSequence.ForDepth(0, 17), Is.EqualTo(0x7D29825C75521255UL));
        Assert.That(DungeonSeedSequence.ForDepth(0, int.MaxValue), Is.EqualTo(0x8F230D036C8C0EDFUL));
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
        Assert.That(hash, Is.EqualTo("ff987538054fb5178b3c6f75cadb549373cdda1adf2337b14dcd498862109239"));
    }

    [Test]
    public void ConnectorSearch_UsesStableCoordinateOrderForReachableEqualLengthPaths()
    {
        DungeonCell lowOrigin = new(0, 0);
        DungeonCell highOrigin = new(0, 2);
        HashSet<DungeonCell> targets = new()
        {
            new DungeonCell(2, 0),
            new DungeonCell(2, 2)
        };
        HashSet<DungeonCell> traversable = new()
        {
            new DungeonCell(1, 0),
            new DungeonCell(1, 2)
        };

        bool forwardFound = DeterministicDungeonGenerator.TryFindConnectorPath(
            3,
            3,
            new[] { lowOrigin, highOrigin },
            targets.Contains,
            traversable.Contains,
            out IReadOnlyList<DungeonCell> forward);
        bool reverseFound = DeterministicDungeonGenerator.TryFindConnectorPath(
            3,
            3,
            new[] { highOrigin, lowOrigin },
            targets.Contains,
            traversable.Contains,
            out IReadOnlyList<DungeonCell> reverse);

        DungeonCell[] expected =
        {
            new DungeonCell(1, 0),
            new DungeonCell(2, 0)
        };
        Assert.That(forwardFound, Is.True);
        Assert.That(reverseFound, Is.True);
        Assert.That(forward, Is.EqualTo(expected));
        Assert.That(reverse, Is.EqualTo(expected));
    }

    [Test]
    public void StartSelection_UsesDocumentedZeroOneAndTwoStairFallbacks()
    {
        DungeonCell downCell = new(1, 1);
        DungeonCell downArrival = new(1, 2);
        DungeonCell ordinarySafeCell = new(5, 5);
        DungeonCell upArrival = new(8, 7);
        DungeonStair down = new("stair-down", DungeonStairKind.Down, downCell, downArrival);
        DungeonStair up = new("stair-up", DungeonStairKind.Up, new DungeonCell(8, 8), upArrival);

        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                Array.Empty<DungeonStair>(),
                new[] { ordinarySafeCell, downArrival }),
            Is.EqualTo(ordinarySafeCell),
            "zero stairs use the first safe cell");
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                new[] { down },
                new[] { downArrival, downCell, ordinarySafeCell }),
            Is.EqualTo(ordinarySafeCell),
            "one Down stair avoids both its endpoint and arrival when possible");
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                new[] { down },
                new[] { downArrival }),
            Is.EqualTo(downArrival),
            "one Down stair falls back to the first safe cell when no alternative exists");
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                new[] { down, up },
                new[] { downArrival, ordinarySafeCell, upArrival }),
            Is.EqualTo(upArrival),
            "two stairs prefer the Up arrival while preserving down-before-up records");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void GeneratedStart_FollowsDocumentedStairCountSemantics(int stairCount)
    {
        DungeonGenerationRequest request = Request(152, 31, 31);
        request.StairCount = stairCount;

        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);

        Assert.That(result.IsSuccess, Is.True,
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.That(result.Document.Stairs.Count, Is.EqualTo(stairCount));
        Assert.That(
            result.Document.StartCell,
            Is.EqualTo(DeterministicDungeonGenerator.SelectStartCell(
                result.Document.Stairs,
                result.Document.SafeCells)));
        if (stairCount == 2)
        {
            Assert.That(result.Document.Stairs[0].Kind, Is.EqualTo(DungeonStairKind.Down));
            Assert.That(result.Document.Stairs[1].Kind, Is.EqualTo(DungeonStairKind.Up));
            Assert.That(result.Document.StartCell, Is.EqualTo(result.Document.Stairs[1].ArrivalCell));
        }
        else if (stairCount == 1)
        {
            Assert.That(result.Document.Stairs[0].Kind, Is.EqualTo(DungeonStairKind.Down));
            Assert.That(result.Document.StartCell, Is.Not.EqualTo(result.Document.Stairs[0].Cell));
            Assert.That(result.Document.StartCell, Is.Not.EqualTo(result.Document.Stairs[0].ArrivalCell));
        }
        else
        {
            Assert.That(result.Document.StartCell, Is.EqualTo(result.Document.SafeCells[0]));
        }
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
            IList<int> supportedVersions = (IList<int>)KayKitDungeonMapData.SupportedVersions;
            Assert.Throws<NotSupportedException>(() => supportedVersions[0] = 99);
            Assert.That(typeof(KayKitDungeonMapData).GetConstructor(new[]
            {
                typeof(int),
                typeof(TileType[,]),
                typeof(bool[,]),
                typeof(IReadOnlyList<KayKitDungeonObjectPlacement>)
            }), Is.Not.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void KayKitParser_RejectsDuplicateVersionBeforeV2PayloadCanBeDiscarded()
    {
        string v2 = GenerateJson(Request(152, 31, 31));
        string duplicateVersion = v2.Replace(
            "\"version\":2",
            "\"version\":2,\"version\":1");
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        try
        {
            KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(
                duplicateVersion,
                catalog);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Map, Is.Null);
            Assert.That(result.Errors, Has.Some.EqualTo(
                "JSON map root property 'version' must not be repeated."));
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
        DungeonLevelDocument source = ContractDocument();

        string json = DungeonLevelJsonSerializer.Serialize(source);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(parsed.IsSuccess, Is.True, string.Join(Environment.NewLine, parsed.Diagnostics.Select(d => d.Message)));
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        Assert.That(parsed.Document.Generation.RunSeed, Is.EqualTo(long.MinValue));
        Assert.That(parsed.Document.EncounterPlans[0].Threat, Is.EqualTo(DungeonEncounterThreat.Low));
        Assert.That(parsed.Document.EncounterPlans[0].Budget, Is.EqualTo(60));
    }

    [Test]
    public void DocumentSnapshots_ProtectTopLevelAndNestedCollectionOwnership()
    {
        DungeonLevelDocument document = ContractDocument();
        string originalJson = DungeonLevelJsonSerializer.Serialize(document);

        object[] snapshots =
        {
            document.Rows,
            document.Rooms,
            document.Doors,
            document.Stairs,
            document.SafeCells,
            document.Objects,
            document.EncounterPlans,
            document.EncounterPlans[0].SpawnCells,
            document.EncounterPlans[0].CreatureIds,
            document.RuntimeState.OpenDoorIds,
            document.RuntimeState.ResolvedEncounterIds,
            document.RuntimeState.DefeatedCreatureIds,
            document.RuntimeState.Creatures
        };
        Assert.That(snapshots.All(snapshot => !snapshot.GetType().IsArray), Is.True);

        Assert.Throws<NotSupportedException>(
            () => ((IList<string>)document.Rows)[0] = "###");
        Assert.Throws<NotSupportedException>(
            () => ((IList<DungeonCell>)document.EncounterPlans[0].SpawnCells)[0] =
                new DungeonCell(1, 1));
        Assert.Throws<NotSupportedException>(
            () => ((IList<string>)document.RuntimeState.OpenDoorIds)[0] = "other-door");
        Assert.That(DungeonLevelJsonSerializer.Serialize(document), Is.EqualTo(originalJson));
    }

    [Test]
    public void VersionTwoJson_RaggedRowsReturnDiagnosticsWithoutSemanticIndexing()
    {
        JObject root = JObject.Parse(ContractJson());
        root["rows"] = new JArray("#D#", "..", "###");
        DungeonLevelParseResult parsed = null;

        Assert.DoesNotThrow(() => parsed = DungeonLevelJsonParser.Parse(root.ToString(Formatting.None)));
        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "rows[1]" && diagnostic.Message.Contains("width")));
    }

    [Test]
    public void KayKitParser_RejectsRaggedVersionTwoWithoutThrowing()
    {
        JObject root = JObject.Parse(ContractJson());
        root["rows"] = new JArray("#D#", "..", "###");
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        try
        {
            KayKitDungeonMapParseResult parsed = null;
            Assert.DoesNotThrow(() => parsed = KayKitDungeonMapParser.Parse(root.ToString(Formatting.None), catalog));
            Assert.That(parsed.IsValid, Is.False);
            Assert.That(parsed.Errors, Has.Some.Contains("rows[1]"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void VersionTwoJson_RequiresExactlyOneDoorRecordPerDoorCell()
    {
        JObject missing = JObject.Parse(ContractJson());
        missing["doors"] = new JArray();
        DungeonLevelParseResult missingResult = DungeonLevelJsonParser.Parse(missing.ToString(Formatting.None));

        JObject duplicate = JObject.Parse(ContractJson());
        JObject duplicateDoor = (JObject)((JArray)duplicate["doors"])[0].DeepClone();
        duplicateDoor["id"] = "door-0002";
        ((JArray)duplicate["doors"]).Add(duplicateDoor);
        DungeonLevelParseResult duplicateResult = DungeonLevelJsonParser.Parse(duplicate.ToString(Formatting.None));

        Assert.That(missingResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "doors" && diagnostic.Message.Contains("Every 'D'")));
        Assert.That(duplicateResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "doors" && diagnostic.Message.Contains("unique")));
    }

    [Test]
    public void VersionTwoJson_RejectsDuplicateObjectAndEncounterIds()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JArray)root["objects"]).Add(((JArray)root["objects"])[0].DeepClone());
        ((JArray)root["encounterPlans"]).Add(((JArray)root["encounterPlans"])[0].DeepClone());

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(root.ToString(Formatting.None));

        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "objects" && diagnostic.Message.Contains("unique")));
        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "encounterPlans" && diagnostic.Message.Contains("unique")));
    }

    [Test]
    public void VersionTwoJson_RejectsEveryMistypedOptionalValue()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)((JArray)root["objects"])[0])["state"] = 4;
        ((JObject)((JArray)((JObject)root["runtimeState"])["creatures"])[0])["state"] = false;
        DungeonLevelParseResult states = DungeonLevelJsonParser.Parse(root.ToString(Formatting.None));

        JObject runtime = JObject.Parse(ContractJson());
        runtime["runtimeState"] = "not-an-object";
        DungeonLevelParseResult runtimeResult = DungeonLevelJsonParser.Parse(runtime.ToString(Formatting.None));

        Assert.That(states.Diagnostics.Select(diagnostic => diagnostic.Field), Does.Contain("objects[0].state"));
        Assert.That(states.Diagnostics.Select(diagnostic => diagnostic.Field), Does.Contain("runtimeState.creatures[0].state"));
        Assert.That(runtimeResult.Diagnostics.Select(diagnostic => diagnostic.Field), Does.Contain("runtimeState"));
    }

    [Test]
    public void VersionTwoJson_RequiresRuntimeIdsToExactlyMirrorPersistedFlags()
    {
        JObject closedButListed = JObject.Parse(ContractJson());
        ((JObject)((JArray)closedButListed["doors"])[0])["isOpen"] = false;
        DungeonLevelParseResult closedButListedResult = DungeonLevelJsonParser.Parse(
            closedButListed.ToString(Formatting.None));

        JObject openButMissing = JObject.Parse(ContractJson());
        ((JObject)openButMissing["runtimeState"])["openDoorIds"] = new JArray();
        DungeonLevelParseResult openButMissingResult = DungeonLevelJsonParser.Parse(
            openButMissing.ToString(Formatting.None));

        JObject unresolvedButListed = JObject.Parse(ContractJson());
        ((JObject)unresolvedButListed["runtimeState"])["resolvedEncounterIds"] =
            new JArray("encounter-1");
        DungeonLevelParseResult unresolvedButListedResult = DungeonLevelJsonParser.Parse(
            unresolvedButListed.ToString(Formatting.None));

        JObject resolvedButMissing = JObject.Parse(ContractJson());
        ((JObject)((JArray)resolvedButMissing["encounterPlans"])[0])["isResolved"] = true;
        DungeonLevelParseResult resolvedButMissingResult = DungeonLevelJsonParser.Parse(
            resolvedButMissing.ToString(Formatting.None));

        Assert.That(closedButListedResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.openDoorIds" &&
                          diagnostic.Message.Contains("exactly match")));
        Assert.That(openButMissingResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.openDoorIds" &&
                          diagnostic.Message.Contains("exactly match")));
        Assert.That(unresolvedButListedResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.resolvedEncounterIds" &&
                          diagnostic.Message.Contains("exactly match")));
        Assert.That(resolvedButMissingResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.resolvedEncounterIds" &&
                          diagnostic.Message.Contains("exactly match")));
    }

    [Test]
    public void VersionTwoJson_RequiresPristineFlagsWhenRuntimeStateIsAbsent()
    {
        JObject openDoor = JObject.Parse(ContractJson());
        openDoor.Property("runtimeState").Remove();
        DungeonLevelParseResult openDoorResult = DungeonLevelJsonParser.Parse(
            openDoor.ToString(Formatting.None));

        JObject resolvedEncounter = JObject.Parse(ContractJson());
        resolvedEncounter.Property("runtimeState").Remove();
        ((JObject)((JArray)resolvedEncounter["doors"])[0])["isOpen"] = false;
        ((JObject)((JArray)resolvedEncounter["encounterPlans"])[0])["isResolved"] = true;
        DungeonLevelParseResult resolvedEncounterResult = DungeonLevelJsonParser.Parse(
            resolvedEncounter.ToString(Formatting.None));

        Assert.That(openDoorResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "doors" &&
                          diagnostic.Message.Contains("runtime state is absent")));
        Assert.That(resolvedEncounterResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "encounterPlans" &&
                          diagnostic.Message.Contains("runtime state is absent")));
    }

    [Test]
    public void VersionTwoJson_RejectsLiveCreatureContentOutsideItsEncounterPlan()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)((JArray)((JObject)root["runtimeState"])["creatures"])[0])["creatureId"] =
            "unplanned-creature";

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None));

        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.creatures" &&
                          diagnostic.Message.Contains("match one available creature entry")));
    }

    [Test]
    public void VersionTwoJson_RejectsLiveCreaturesForResolvedPlansAndExcessMultiplicity()
    {
        JObject resolved = JObject.Parse(ContractJson());
        ((JObject)((JArray)resolved["encounterPlans"])[0])["isResolved"] = true;
        ((JObject)resolved["runtimeState"])["resolvedEncounterIds"] =
            new JArray("encounter-1");
        DungeonLevelParseResult resolvedResult = DungeonLevelJsonParser.Parse(
            resolved.ToString(Formatting.None));

        JObject excess = JObject.Parse(ContractJson());
        JObject duplicateCreature = (JObject)((JArray)((JObject)excess["runtimeState"])["creatures"])[0]
            .DeepClone();
        duplicateCreature["instanceId"] = "creature-b#2";
        ((JArray)((JObject)excess["runtimeState"])["creatures"]).Add(duplicateCreature);
        DungeonLevelParseResult excessResult = DungeonLevelJsonParser.Parse(
            excess.ToString(Formatting.None));

        Assert.That(resolvedResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.creatures" &&
                          diagnostic.Message.Contains("unresolved encounter")));
        Assert.That(excessResult.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.creatures" &&
                          diagnostic.Message.Contains("available creature entry")));
    }

    [Test]
    public void VersionTwoJson_RequiresDefeatedAndLiveInstanceIdsToBeDisjoint()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)root["runtimeState"])["defeatedCreatureIds"] =
            new JArray("creature-a#1", "creature-b#1");

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None));

        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(
            diagnostic => diagnostic.Field == "runtimeState.defeatedCreatureIds" &&
                          diagnostic.Message.Contains("disjoint")));
    }

    [Test]
    public void VersionTwoJson_RejectsUnknownPropertiesAtEveryObjectLevel()
    {
        JObject root = JObject.Parse(ContractJson());
        root["unknownRoot"] = true;
        ((JObject)root["generation"])["unknownGeneration"] = true;
        ((JObject)((JArray)root["rooms"])[0])["unknownRoom"] = true;
        JObject door = (JObject)((JArray)root["doors"])[0];
        door["unknownDoor"] = true;
        ((JObject)door["cell"])["unknownCell"] = true;
        ((JObject)((JArray)root["stairs"])[0])["unknownStair"] = true;
        ((JObject)root["arrival"])["unknownArrival"] = true;
        ((JObject)((JArray)root["objects"])[0])["unknownObject"] = true;
        ((JObject)((JArray)root["encounterPlans"])[0])["unknownEncounter"] = true;
        JObject runtime = (JObject)root["runtimeState"];
        runtime["unknownRuntime"] = true;
        ((JObject)((JArray)runtime["creatures"])[0])["unknownCreature"] = true;

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(root.ToString(Formatting.None));
        string[] fields = parsed.Diagnostics.Select(diagnostic => diagnostic.Field).ToArray();

        Assert.That(fields, Does.Contain("unknownRoot"));
        Assert.That(fields, Does.Contain("generation.unknownGeneration"));
        Assert.That(fields, Does.Contain("rooms[0].unknownRoom"));
        Assert.That(fields, Does.Contain("doors[0].unknownDoor"));
        Assert.That(fields, Does.Contain("doors[0].cell.unknownCell"));
        Assert.That(fields, Does.Contain("stairs[0].unknownStair"));
        Assert.That(fields, Does.Contain("arrival.unknownArrival"));
        Assert.That(fields, Does.Contain("objects[0].unknownObject"));
        Assert.That(fields, Does.Contain("encounterPlans[0].unknownEncounter"));
        Assert.That(fields, Does.Contain("runtimeState.unknownRuntime"));
        Assert.That(fields, Does.Contain("runtimeState.creatures[0].unknownCreature"));
    }

    [Test]
    public void VersionTwoJson_RejectsDuplicateJsonProperties()
    {
        string duplicate = ContractJson().Replace("\"version\":2", "\"version\":2,\"version\":2");

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(duplicate);

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(parsed.Diagnostics, Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
            diagnostic.Field == "json" && diagnostic.Message.Contains("version")));
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
        Assert.That(assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Has.None.EqualTo("Unity.InputSystem"));
        Assert.That(assembly.GetTypes().SelectMany(type => new[] { type.FullName, type.BaseType?.FullName })
            .Any(value => value != null && value.Contains("UnityEngine.MonoBehaviour")), Is.False);
        Assert.That(assembly.GetTypes().SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Any(member => member.ToString().Contains("UnityEngine.Random")), Is.False);
        Assert.That(assembly.GetTypes().Any(type => type.FullName != null && type.FullName.Contains("UnityEngine.SceneManagement")), Is.False);
        string assemblyDefinition = File.ReadAllText("Assets/Scripts/DungeonGeneration/DungeonGeneration.asmdef");
        Assert.That(assemblyDefinition, Does.Not.Contain("75469ad4d38634e559750d17036d5f7c"));
        Assert.That(assemblyDefinition, Does.Not.Contain("Unity.InputSystem"));
    }

    [Test]
    public void DonjonMasks_UsePinnedThreeByThreeAndCircularScaling()
    {
        foreach (DungeonLayout layout in Enum.GetValues(typeof(DungeonLayout)))
        {
            DungeonGenerationRequest request = Request(90210 + (int)layout, 31, 31);
            request.Layout = layout;
            DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);
            Assert.That(result.IsSuccess, Is.True, layout + ": " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

            for (int z = 0; z < request.Height; z++)
            for (int x = 0; x < request.Width; x++)
            {
                bool expectedMasked = IsMaskedByDonjon(layout, request.Width, request.Height, x, z);
                bool serializedAsMasked = result.Document.Rows[request.Height - 1 - z][x] == ' ';
                Assert.That(serializedAsMasked, Is.EqualTo(expectedMasked), $"{layout} ({x},{z})");
            }
        }
    }

    [Test]
    public void DonjonStageModes_HaveAuditedGoldenDocuments()
    {
        var cases = new[]
        {
            new { Name = "box-packed-straight-clean100", Seed = 11L, Layout = DungeonLayout.Box, Rooms = DungeonRoomLayout.Packed, Corridors = DungeonCorridorLayout.Straight, Cleanup = 100 },
            new { Name = "cross-scattered-labyrinth-clean0", Seed = -22L, Layout = DungeonLayout.Cross, Rooms = DungeonRoomLayout.Scattered, Corridors = DungeonCorridorLayout.Labyrinth, Cleanup = 0 },
            new { Name = "round-packed-bent-clean50", Seed = 33L, Layout = DungeonLayout.Round, Rooms = DungeonRoomLayout.Packed, Corridors = DungeonCorridorLayout.Bent, Cleanup = 50 }
        };
        string[] expected =
        {
            "5cf543f8f165f035908a9f981ed7ae5e3b52c918b03f2ce8c45ac1661856ca7e",
            "c0c3eccf0f75919f62d1507eaa7f710b86f338b8b7f4e52deb82f3d601b0beb2",
            "fc8e3f28a3d550adc54f88a19b98696ee75994a166c3faebb6be765927b89cab"
        };
        List<string> actual = new();
        foreach (var item in cases)
        {
            DungeonGenerationRequest request = Request(item.Seed, 31, 31);
            request.Layout = item.Layout;
            request.RoomLayout = item.Rooms;
            request.CorridorLayout = item.Corridors;
            request.DeadEndRemovalPercent = item.Cleanup;
            string hash = Sha256(GenerateJson(request));
            TestContext.WriteLine(item.Name + "=" + hash);
            actual.Add(hash);
        }

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static DungeonLevelDocument ContractDocument() => new(
        new DungeonGenerationMetadata("test", 7, long.MinValue, 3, 2, "8000000000000000", "0123456789abcdef"),
        new[] { "#D#", "...", "###" },
        new[] { new DungeonRoom(1, 0, 1, 2, 1) },
        new[] { new DungeonDoor("door-0001", new DungeonCell(1, 2), true) },
        new[] { new DungeonStair("stair-up", DungeonStairKind.Up, new DungeonCell(0, 1), new DungeonCell(1, 1)) },
        new DungeonCell(1, 1),
        new[] { new DungeonCell(1, 1), new DungeonCell(2, 1) },
        new[] { new DungeonObjectPlacement("object-1", "prop", new DungeonCell(2, 1), 270, "used") },
        new[]
        {
            new DungeonEncounterPlan(
                "encounter-1",
                1,
                DungeonEncounterThreat.Low,
                60,
                new[] { new DungeonCell(0, 1), new DungeonCell(2, 1) },
                new[] { "creature-a", "creature-b" },
                false)
        },
        new DungeonRuntimeState(
            new[] { "door-0001" },
            Array.Empty<string>(),
            new[] { "creature-a#1" },
            new[]
            {
                new DungeonCreatureRuntimeState(
                    "creature-b#1",
                    "creature-b",
                    "encounter-1",
                    new DungeonCell(2, 1),
                    7,
                    "slowed")
            }));

    private static string ContractJson() => DungeonLevelJsonSerializer.Serialize(ContractDocument());

    private static bool IsMaskedByDonjon(
        DungeonLayout layout,
        int width,
        int height,
        int x,
        int z)
    {
        if (layout == DungeonLayout.Round)
        {
            int centerX = (width - 1) / 2;
            int centerZ = (height - 1) / 2;
            long deltaX = x - centerX;
            long deltaZ = z - centerZ;
            return deltaX * deltaX + deltaZ * deltaZ > (long)centerX * centerX;
        }

        int[,] mask = layout == DungeonLayout.Box
            ? new[,] { { 1, 1, 1 }, { 1, 0, 1 }, { 1, 1, 1 } }
            : new[,] { { 0, 1, 0 }, { 1, 1, 1 }, { 0, 1, 0 } };
        return mask[z * 3 / height, x * 3 / width] == 0;
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

    private static string Sha256(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void AssertDocumentInvariants(DungeonLevelDocument document, int seed)
    {
        HashSet<DungeonCell> walkable = Walkable(document);
        Assert.That(walkable, Does.Contain(document.StartCell), "start seed " + seed);
        Assert.That(document.SafeCells.All(walkable.Contains), Is.True, "safe seed " + seed);
        Assert.That(document.Stairs.All(stair => walkable.Contains(stair.Cell) && walkable.Contains(stair.ArrivalCell)), Is.True, "stairs seed " + seed);
        if (document.Stairs.Count > 0)
            Assert.That(document.Stairs[0].Kind, Is.EqualTo(DungeonStairKind.Down), "down-first seed " + seed);
        if (document.Stairs.Count > 1)
        {
            Assert.That(document.Stairs[1].Kind, Is.EqualTo(DungeonStairKind.Up), "up-second seed " + seed);
            Assert.That(document.StartCell, Is.EqualTo(document.Stairs[1].ArrivalCell), "up arrival start seed " + seed);
        }

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

        HashSet<DungeonCell> recordedDoorCells = new(document.Doors.Select(door => door.Cell));
        HashSet<DungeonCell> rowDoorCells = new();
        for (int row = 0; row < document.Rows.Count; row++)
        for (int x = 0; x < document.Rows[row].Length; x++)
            if (document.Rows[row][x] == 'D') rowDoorCells.Add(new DungeonCell(x, document.Rows.Count - 1 - row));
        Assert.That(recordedDoorCells.SetEquals(rowDoorCells), Is.True, "door records seed " + seed);
        Assert.That(document.Doors.Select(door => door.Id).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(document.Doors.Count), "door ids seed " + seed);

        foreach (DungeonRoom room in document.Rooms)
        for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
        for (int x = room.MinimumX; x <= room.MaximumX; x++)
        foreach (DungeonCell neighbor in Neighbors(new DungeonCell(x, z)))
        {
            bool insideRoom = neighbor.X >= room.MinimumX && neighbor.X <= room.MaximumX &&
                              neighbor.Z >= room.MinimumZ && neighbor.Z <= room.MaximumZ;
            if (!insideRoom && walkable.Contains(neighbor))
                Assert.That(recordedDoorCells, Does.Contain(neighbor), "room crossing seed " + seed);
        }

        foreach (DungeonStair stair in document.Stairs)
        {
            DungeonCell delta = new(stair.ArrivalCell.X - stair.Cell.X, stair.ArrivalCell.Z - stair.Cell.Z);
            DungeonCell far = new(stair.ArrivalCell.X + delta.X, stair.ArrivalCell.Z + delta.Z);
            Assert.That(walkable, Does.Contain(far), "stair corridor template seed " + seed);
            for (int zOffset = -1; zOffset <= 1; zOffset++)
            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if ((xOffset == 0 && zOffset == 0) || (xOffset == delta.X && zOffset == delta.Z))
                    continue;
                DungeonCell neighbor = new(stair.Cell.X + xOffset, stair.Cell.Z + zOffset);
                Assert.That(walkable.Contains(neighbor), Is.False, "stair wall template seed " + seed);
            }
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
