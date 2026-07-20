using System;
using System.Collections.Generic;
using System.Globalization;
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
    public void SystemRandom_UsesRepeatableIsolatedSeedsAndStreams()
    {
        int seed = DungeonSeedSequence.ForTopologyAttempt(4, 2, 0);
        SystemDungeonRandom first = new(seed);
        SystemDungeonRandom second = new(seed);

        Assert.That(first.NextInt(1000), Is.EqualTo(second.NextInt(1000)));
        Assert.That(first.NextInt(1000), Is.EqualTo(second.NextInt(1000)));
        Assert.That(
            DungeonSeedSequence.ForDepth(4, 2),
            Is.Not.EqualTo(DungeonSeedSequence.ForDepth(4, 3))
        );
        Assert.That(
            DungeonSeedSequence.ForSubstream(4, 2, DungeonSeedSubstream.Topology),
            Is.Not.EqualTo(DungeonSeedSequence.ForSubstream(4, 2, DungeonSeedSubstream.Decoration))
        );
        Assert.That(
            DungeonSeedSequence.ForTopologyAttempt(4, 2, 0),
            Is.Not.EqualTo(DungeonSeedSequence.ForTopologyAttempt(4, 2, 1))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => first.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.NextPercent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonSeedSequence.ForSubstream(4, 2, (DungeonSeedSubstream)99)
        );
    }

    [Test]
    public void GoldenDocument_IsByteStableAcrossGeneratorAndSerializerRuns()
    {
        DungeonGenerationRequest request = Request(-2147483647, 31, 31);
        string first = GenerateJson(request);
        string second = GenerateJson(request);

        Assert.That(second, Is.EqualTo(first));
        string hash;
        using (SHA256 sha256 = SHA256.Create())
            hash = BitConverter
                .ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(first)))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        TestContext.WriteLine("golden sha256=" + hash);
        Assert.That(
            hash,
            Is.EqualTo("d3ebd003af1f20cde1d933eebdf15f3def300e87d8adc61bb3e88a8b0f5b3f6d")
        );
    }

    [Test]
    public void ConnectorSearch_UsesStableCoordinateOrderForReachableEqualLengthPaths()
    {
        DungeonCell lowOrigin = new(0, 0);
        DungeonCell highOrigin = new(0, 2);
        HashSet<DungeonCell> targets = new() { new DungeonCell(2, 0), new DungeonCell(2, 2) };
        HashSet<DungeonCell> traversable = new() { new DungeonCell(1, 0), new DungeonCell(1, 2) };

        bool forwardFound = DeterministicDungeonGenerator.TryFindConnectorPath(
            3,
            3,
            new[] { lowOrigin, highOrigin },
            targets.Contains,
            traversable.Contains,
            out IReadOnlyList<DungeonCell> forward
        );
        bool reverseFound = DeterministicDungeonGenerator.TryFindConnectorPath(
            3,
            3,
            new[] { highOrigin, lowOrigin },
            targets.Contains,
            traversable.Contains,
            out IReadOnlyList<DungeonCell> reverse
        );

        DungeonCell[] expected = { new DungeonCell(1, 0), new DungeonCell(2, 0) };
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
                new[] { ordinarySafeCell, downArrival }
            ),
            Is.EqualTo(ordinarySafeCell),
            "zero stairs use the first safe cell"
        );
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                new[] { down },
                new[] { downArrival, downCell, ordinarySafeCell }
            ),
            Is.EqualTo(ordinarySafeCell),
            "one Down stair avoids both its endpoint and arrival when possible"
        );
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(new[] { down }, new[] { downArrival }),
            Is.EqualTo(downArrival),
            "one Down stair falls back to the first safe cell when no alternative exists"
        );
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(
                new[] { down, up },
                new[] { downArrival, ordinarySafeCell, upArrival }
            ),
            Is.EqualTo(upArrival),
            "two stairs prefer the Up arrival while preserving down-before-up records"
        );
    }

    [Test]
    public void SafeCellSequence_AppendsFirstStableNonExitFallbackForLoneDownStair()
    {
        IReadOnlyList<string> rows = new[] { "#####", "#.###", "#####", "#...#", "#####" };
        DungeonStair down = new(
            "stair-down",
            DungeonStairKind.Down,
            new DungeonCell(1, 1),
            new DungeonCell(2, 1)
        );

        IReadOnlyList<DungeonCell> safe = DungeonTopologyValidator.BuildSafeCells(
            rows,
            Array.Empty<DungeonRoom>(),
            new[] { down }
        );

        DungeonCell[] expected = { down.ArrivalCell, new DungeonCell(3, 1) };
        Assert.That(safe, Is.EqualTo(expected));
        Assert.That(
            DungeonTopologyValidator.HasProducibleSafeCells(
                rows,
                Array.Empty<DungeonRoom>(),
                new[] { down },
                safe
            ),
            Is.True
        );
        Assert.That(
            DeterministicDungeonGenerator.SelectStartCell(new[] { down }, safe),
            Is.EqualTo(expected[1])
        );
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void GeneratedStart_FollowsDocumentedStairCountSemantics(int stairCount)
    {
        DungeonGenerationRequest request = Request(152, 31, 31);
        request.StairCount = stairCount;

        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);

        Assert.That(
            result.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(result.Document.Stairs.Count, Is.EqualTo(stairCount));
        Assert.That(
            result.Document.StartCell,
            Is.EqualTo(
                DeterministicDungeonGenerator.SelectStartCell(
                    result.Document.Stairs,
                    result.Document.SafeCells
                )
            )
        );
        if (stairCount == 2)
        {
            Assert.That(result.Document.Stairs[0].Kind, Is.EqualTo(DungeonStairKind.Down));
            Assert.That(result.Document.Stairs[1].Kind, Is.EqualTo(DungeonStairKind.Up));
            Assert.That(
                result.Document.StartCell,
                Is.EqualTo(result.Document.Stairs[1].ArrivalCell)
            );
        }
        else if (stairCount == 1)
        {
            Assert.That(result.Document.Stairs[0].Kind, Is.EqualTo(DungeonStairKind.Down));
            Assert.That(result.Document.StartCell, Is.Not.EqualTo(result.Document.Stairs[0].Cell));
            Assert.That(
                result.Document.StartCell,
                Is.Not.EqualTo(result.Document.Stairs[0].ArrivalCell)
            );
        }
        else
        {
            Assert.That(result.Document.StartCell, Is.EqualTo(result.Document.SafeCells[0]));
        }
    }

    [Test]
    public void RoomlessReviewerProbe_AddsNonExitSafeFallbackAndRoundTrips()
    {
        DungeonGenerationRequest request = Request(-5000, 15, 15);
        request.Layout = DungeonLayout.Box;
        request.RoomLayout = DungeonRoomLayout.Scattered;
        request.MaximumRoomSize = 11;
        request.MinimumRoomCount = 0;
        request.StairCount = 1;

        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);

        Assert.That(
            result.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(result.Document.Rooms, Is.Empty);
        HashSet<DungeonCell> walkable = Walkable(result.Document);
        Assert.That(walkable.Count, Is.GreaterThan(2));
        DungeonStair down = result.Document.Stairs.Single();
        HashSet<DungeonCell> downExitCells = new() { down.Cell, down.ArrivalCell };
        DungeonCell expectedFallback = walkable
            .Where(cell =>
                !downExitCells.Contains(cell)
                && result.Document.Rows[result.Document.Height - 1 - cell.Z][cell.X] == '.'
            )
            .OrderBy(cell => cell.Z)
            .ThenBy(cell => cell.X)
            .First();
        Assert.That(
            result.Document.SafeCells,
            Is.EqualTo(new[] { down.ArrivalCell, expectedFallback })
        );
        Assert.That(result.Document.StartCell, Is.EqualTo(expectedFallback));

        string json = DungeonLevelJsonSerializer.Serialize(result.Document);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [Test]
    public void CleanupReviewerProbe_PreservesFullStairRunwayAndRoundTrips()
    {
        DungeonGenerationRequest request = Request(1463, 15, 15);
        request.Depth = 2;
        request.Layout = DungeonLayout.Box;
        request.RoomLayout = DungeonRoomLayout.Scattered;
        request.CorridorLayout = DungeonCorridorLayout.Bent;
        request.MinimumRoomSize = 3;
        request.MaximumRoomSize = 9;
        request.MinimumRoomCount = 0;
        request.StairCount = 1;
        request.DeadEndRemovalPercent = 100;

        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);

        Assert.That(
            result.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(
            result.Document.Generation.TopologyAttempt,
            Is.Zero,
            "protecting the selected runway must preserve the original successful attempt"
        );
        DungeonStair stair = result.Document.Stairs.Single();
        Assert.That(
            DungeonTopologyValidator.MatchesStairEnd(
                result.Document.Rows,
                result.Document.Rooms,
                stair.Cell,
                stair.ArrivalCell
            ),
            Is.True,
            "successful generation must retain the strict parser's complete stair runway"
        );

        string json = DungeonLevelJsonSerializer.Serialize(result.Document);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void DungeonJson_RejectsStartTamperingForOwnedGeneratorStairCases(int stairCount)
    {
        DungeonGenerationRequest request = Request(152, 31, 31);
        request.StairCount = stairCount;
        JObject root = JObject.Parse(GenerateJson(request));
        DungeonLevelParseResult original = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );
        Assert.That(
            original.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                original.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );

        DungeonCell tamperedStart =
            stairCount == 0
                ? original.Document.SafeCells.First(cell => cell != original.Document.StartCell)
                : original
                    .Document.Stairs.Single(stair => stair.Kind == DungeonStairKind.Down)
                    .ArrivalCell;
        ((JObject)root["arrival"])["start"] = JsonCell(tamperedStart);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "arrival.start"
                && diagnostic.Message.Contains("current Donjon generator")
                && diagnostic.Message.Contains("stair-aware")
            )
        );
    }

    [TestCase("future-generator")]
    public void DungeonJson_DoesNotApplyDonjonStartSelectionToOtherGeneratorContracts(
        string algorithm
    )
    {
        JObject root = JObject.Parse(GenerateJson(Request(152, 31, 31)));
        JObject generation = (JObject)root["generation"];
        generation["algorithm"] = algorithm;
        JObject downStair = ((JArray)root["stairs"])
            .Cast<JObject>()
            .Single(stair => stair.Value<string>("kind") == "down");
        ((JObject)root["arrival"])["start"] = downStair["arrivalCell"].DeepClone();

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
    }

    [TestCase(int.MinValue, 0, 0)]
    [TestCase(-1, 17, 31)]
    [TestCase(int.MaxValue, int.MaxValue, 31)]
    public void DungeonJson_AcceptsOwnedGeneratorMetadataAtNumericBoundaries(
        int runSeed,
        int depth,
        int topologyAttempt
    )
    {
        string json = OwnedContractJson(runSeed, depth, topologyAttempt);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [Test]
    public void DungeonJson_RejectsRemovedGeneratorStateProperties()
    {
        JObject root = JObject.Parse(ContractJson());
        JObject generation = (JObject)root["generation"];
        generation["depthState"] = "removed";
        generation["topologyState"] = "removed";

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );
        string[] fields = parsed.Diagnostics.Select(diagnostic => diagnostic.Field).ToArray();

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(fields, Does.Contain("generation.depthState"));
        Assert.That(fields, Does.Contain("generation.topologyState"));
        Assert.That(
            parsed.Diagnostics,
            Has.All.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Message.Contains("current schema")
            )
        );
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void DungeonJson_RoundTripsIntegerRunSeeds(int runSeed)
    {
        DungeonGenerationMetadata metadata = new("future-generator", runSeed, 0, 0);
        string json = DungeonLevelJsonSerializer.Serialize(
            ContractDocumentWithGeneration(metadata)
        );

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(parsed.Document.Generation.RunSeed, Is.EqualTo(runSeed));
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [TestCase("+1")]
    [TestCase("001")]
    [TestCase(" 1 ")]
    [TestCase("-0")]
    [TestCase("")]
    [TestCase("1.0")]
    [TestCase("9223372036854775808")]
    [TestCase("--1")]
    public void DungeonJson_RejectsStringRunSeeds(string runSeed)
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)root["generation"])["runSeed"] = runSeed;

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "generation.runSeed"
                && diagnostic.Message.Contains("integer is required")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsOwnedAttemptAtMaximumBoundary()
    {
        JObject root = JObject.Parse(OwnedContractJson(int.MaxValue, int.MaxValue, 32));

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "generation.topologyAttempt"
                && diagnostic.Message.Contains("0 through 31")
            )
        );
    }

    [TestCase(15, 15, true)]
    [TestCase(101, 101, true)]
    [TestCase(13, 15, false)]
    [TestCase(14, 15, false)]
    [TestCase(102, 15, false)]
    [TestCase(103, 15, false)]
    [TestCase(15, 13, false)]
    [TestCase(15, 14, false)]
    [TestCase(15, 102, false)]
    [TestCase(15, 103, false)]
    public void DungeonJson_EnforcesOwnedGeneratorDimensionContract(
        int width,
        int height,
        bool expectedSuccess
    )
    {
        JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
        ResizeRows(root, width, height);
        string json = root.ToString(Formatting.None);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(parsed.IsSuccess, Is.EqualTo(expectedSuccess));
        if (expectedSuccess)
        {
            Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        }
        else
        {
            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "rows"
                    && diagnostic.Message.Contains("odd integer from 15 through 101")
                )
            );
        }
    }

    [Test]
    public void DungeonJson_RejectsOwnedRowsOutsideEverySupportedLayoutMask()
    {
        var cases = new[]
        {
            new
            {
                Name = "allowed wall changed to space",
                Cell = new DungeonCell(4, 10),
                Symbol = ' ',
            },
            new
            {
                Name = "masked space changed to wall",
                Cell = new DungeonCell(5, 5),
                Symbol = '#',
            },
        };

        foreach (var item in cases)
        {
            JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
            SetSymbol(root, item.Cell, item.Symbol);

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "rows"
                    && diagnostic.Message.Contains("spaces must exactly match")
                    && diagnostic.Message.Contains("Box, Cross, or Round")
                ),
                item.Name
            );
        }
    }

    [TestCase("future-generator", 32)]
    [TestCase("future-generator", int.MaxValue)]
    public void DungeonJson_OtherContractsRoundTripLargeTopologyAttempts(
        string algorithm,
        int topologyAttempt
    )
    {
        DungeonGenerationMetadata metadata = new(
            algorithm,
            int.MinValue,
            int.MaxValue,
            topologyAttempt
        );
        DungeonLevelDocument document = ContractDocumentWithGeneration(metadata);

        string json = DungeonLevelJsonSerializer.Serialize(document);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(parsed.Document.Generation.TopologyAttempt, Is.EqualTo(topologyAttempt));
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [Test]
    public void KayKitParser_AcceptsDungeonDocumentAndRetainsLosslessDocument()
    {
        string json = GenerateJson(Request(-17, 31, 31));
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        GameObject banner = new("Banner");
        GameObject torch = new("Torch");
        catalog.ReplaceEntries(
            new[]
            {
                new KayKitDungeonCatalogEntry(DungeonDecorationPlanner.BannerAssetId, banner),
                new KayKitDungeonCatalogEntry(DungeonDecorationPlanner.TorchAssetId, torch),
            }
        );
        try
        {
            KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(json, catalog);

            Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.Map.LevelDocument, Is.Not.Null);
            Assert.That(
                DungeonLevelJsonSerializer.Serialize(result.Map.LevelDocument),
                Is.EqualTo(json)
            );
            Assert.That(
                typeof(KayKitDungeonMapData).GetConstructor(
                    new[]
                    {
                        typeof(TileType[,]),
                        typeof(bool[,]),
                        typeof(IReadOnlyList<KayKitDungeonObjectPlacement>),
                        typeof(DungeonLevelDocument),
                    }
                ),
                Is.Not.Null
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(banner);
            UnityEngine.Object.DestroyImmediate(torch);
        }
    }

    [Test]
    public void KayKitParser_RejectsDuplicateGenerationProperties()
    {
        string json = GenerateJson(Request(152, 31, 31));
        string duplicateAlgorithm = json.Replace(
            "\"algorithm\":\"donjon-logical-system-random\"",
            "\"algorithm\":\"donjon-logical-system-random\",\"algorithm\":\"future-generator\""
        );
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        try
        {
            KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(
                duplicateAlgorithm,
                catalog
            );

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Map, Is.Null);
            Assert.That(
                result.Errors,
                Has.Some.Matches<string>(error =>
                    error.Contains("algorithm") && error.Contains("exists")
                )
            );
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
        bool observedPackedMaximumAtLastAnchor = false;
        bool observedScatteredLastAnchor = false;
        for (int seed = 0; seed < 256; seed++)
            foreach (int cleanupPercent in new[] { 0, 100 })
            {
                int width =
                    seed % 4 == 1 ? 31
                    : seed % 4 == 2 ? 21
                    : 31;
                int height =
                    seed % 4 == 1 ? 21
                    : seed % 4 == 2 ? 31
                    : 31;
                DungeonGenerationRequest request = Request(seed - 128, width, height);
                request.Layout = (DungeonLayout)(seed % 3);
                request.RoomLayout = (DungeonRoomLayout)(seed % 2);
                request.CorridorLayout = (DungeonCorridorLayout)(seed % 3);
                request.StairCount = seed % 3;
                request.DeadEndRemovalPercent = cleanupPercent;
                DungeonGenerationResult result = generator.Generate(request);
                string context = seed + " cleanup " + cleanupPercent;
                Assert.That(
                    result.IsSuccess,
                    Is.True,
                    "seed "
                        + context
                        + ": "
                        + string.Join(" | ", result.Diagnostics.Select(d => d.Message))
                );
                AssertDocumentInvariants(result.Document, context);
                bool TouchesLastAnchor(DungeonRoom room) =>
                    room.MaximumX == request.Width - 2 || room.MaximumZ == request.Height - 2;
                if (request.RoomLayout == DungeonRoomLayout.Packed)
                {
                    observedPackedMaximumAtLastAnchor |= result.Document.Rooms.Any(room =>
                        TouchesLastAnchor(room)
                        && (
                            room.MaximumX - room.MinimumX + 1 == request.MaximumRoomSize
                            || room.MaximumZ - room.MinimumZ + 1 == request.MaximumRoomSize
                        )
                    );
                }
                else
                {
                    observedScatteredLastAnchor |= result.Document.Rooms.Any(TouchesLastAnchor);
                }
                string json = DungeonLevelJsonSerializer.Serialize(result.Document);
                DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
                Assert.That(
                    parsed.IsSuccess,
                    Is.True,
                    "owned round-trip seed "
                        + context
                        + ": "
                        + string.Join(
                            " | ",
                            parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
                        )
                );
                Assert.That(
                    DungeonLevelJsonSerializer.Serialize(parsed.Document),
                    Is.EqualTo(json),
                    "owned lossless round-trip seed " + context
                );
            }
        Assert.That(
            observedPackedMaximumAtLastAnchor,
            Is.True,
            "packed sizing must include the largest fitting size at the last anchor"
        );
        Assert.That(
            observedScatteredLastAnchor,
            Is.True,
            "scattered placement must include the last valid anchor"
        );
    }

    [Test]
    public void DungeonJson_RoundTripsEveryContractSectionWithoutLoss()
    {
        DungeonLevelDocument source = ContractDocument();

        string json = DungeonLevelJsonSerializer.Serialize(source);
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(Environment.NewLine, parsed.Diagnostics.Select(d => d.Message))
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        Assert.That(parsed.Document.Generation.RunSeed, Is.EqualTo(int.MinValue));
        Assert.That(
            parsed.Document.EncounterPlans[0].Threat,
            Is.EqualTo(DungeonEncounterThreat.Low)
        );
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
            document.RuntimeState.Creatures,
        };
        Assert.That(snapshots.All(snapshot => !snapshot.GetType().IsArray), Is.True);

        Assert.Throws<NotSupportedException>(() => ((IList<string>)document.Rows)[0] = "###");
        Assert.Throws<NotSupportedException>(() =>
            ((IList<DungeonCell>)document.EncounterPlans[0].SpawnCells)[0] = new DungeonCell(1, 1)
        );
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)document.RuntimeState.OpenDoorIds)[0] = "other-door"
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(document), Is.EqualTo(originalJson));
    }

    [Test]
    public void DungeonJson_RaggedRowsReturnDiagnosticsWithoutSemanticIndexing()
    {
        JObject root = JObject.Parse(ContractJson());
        root["rows"] = new JArray("#D#", "..", "###");
        DungeonLevelParseResult parsed = null;

        Assert.DoesNotThrow(() =>
            parsed = DungeonLevelJsonParser.Parse(root.ToString(Formatting.None))
        );
        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "rows[1]" && diagnostic.Message.Contains("width")
            )
        );
    }

    [Test]
    public void DungeonJson_OutOfRangeIntegerReturnsSpecificDiagnostic()
    {
        JObject root = JObject.Parse(ContractJson());
        root["generation"]["depth"] = JToken.Parse("9223372036854775808");

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "generation.depth"
                && diagnostic.Message == "Integer must be within the signed 32-bit range."
            )
        );
    }

    [Test]
    public void KayKitParser_RejectsRaggedDungeonDocumentWithoutThrowing()
    {
        JObject root = JObject.Parse(ContractJson());
        root["rows"] = new JArray("#D#", "..", "###");
        KayKitDungeonCatalog catalog = ScriptableObject.CreateInstance<KayKitDungeonCatalog>();
        try
        {
            KayKitDungeonMapParseResult parsed = null;
            Assert.DoesNotThrow(() =>
                parsed = KayKitDungeonMapParser.Parse(root.ToString(Formatting.None), catalog)
            );
            Assert.That(parsed.IsValid, Is.False);
            Assert.That(parsed.Errors, Has.Some.Contains("rows[1]"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void DungeonJson_RequiresExactlyOneDoorRecordPerDoorCell()
    {
        JObject missing = JObject.Parse(ContractJson());
        missing["doors"] = new JArray();
        DungeonLevelParseResult missingResult = DungeonLevelJsonParser.Parse(
            missing.ToString(Formatting.None)
        );

        JObject duplicate = JObject.Parse(ContractJson());
        JObject duplicateDoor = (JObject)((JArray)duplicate["doors"])[0].DeepClone();
        duplicateDoor["id"] = "door-0002";
        ((JArray)duplicate["doors"]).Add(duplicateDoor);
        DungeonLevelParseResult duplicateResult = DungeonLevelJsonParser.Parse(
            duplicate.ToString(Formatting.None)
        );

        Assert.That(
            missingResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "doors" && diagnostic.Message.Contains("Every 'D'")
            )
        );
        Assert.That(
            duplicateResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "doors" && diagnostic.Message.Contains("unique")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsImpossibleOwnedRoomRecords()
    {
        var cases = new[]
        {
            new
            {
                Name = "width below minimum",
                MinX = 1,
                MinZ = 1,
                MaxX = 1,
                MaxZ = 3,
                Id = 1,
            },
            new
            {
                Name = "height below minimum",
                MinX = 1,
                MinZ = 1,
                MaxX = 3,
                MaxZ = 1,
                Id = 1,
            },
            new
            {
                Name = "even width",
                MinX = 1,
                MinZ = 1,
                MaxX = 2,
                MaxZ = 3,
                Id = 1,
            },
            new
            {
                Name = "even height",
                MinX = 1,
                MinZ = 1,
                MaxX = 3,
                MaxZ = 2,
                Id = 1,
            },
            new
            {
                Name = "even x alignment",
                MinX = 2,
                MinZ = 1,
                MaxX = 4,
                MaxZ = 3,
                Id = 1,
            },
            new
            {
                Name = "even z alignment",
                MinX = 1,
                MinZ = 2,
                MaxX = 3,
                MaxZ = 4,
                Id = 1,
            },
            new
            {
                Name = "width above map maximum",
                MinX = 1,
                MinZ = 1,
                MaxX = 13,
                MaxZ = 3,
                Id = 1,
            },
            new
            {
                Name = "height above map maximum",
                MinX = 1,
                MinZ = 1,
                MaxX = 3,
                MaxZ = 13,
                Id = 1,
            },
            new
            {
                Name = "nonsequential room id",
                MinX = 1,
                MinZ = 1,
                MaxX = 3,
                MaxZ = 3,
                Id = 2,
            },
        };

        foreach (var item in cases)
        {
            JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
            ConfigureOwnedSingleRoom(root, item.MinX, item.MinZ, item.MaxX, item.MaxZ, item.Id);

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "rooms"
                    && diagnostic.Message.Contains("ordered IDs starting at 1")
                    && diagnostic.Message.Contains("odd-aligned bounds")
                    && diagnostic.Message.Contains("odd side lengths")
                ),
                item.Name
            );
        }
    }

    [Test]
    public void DungeonJson_RejectsDisconnectedWalkableRegionsForOwnedGenerator()
    {
        JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
        SetSymbol(root, new DungeonCell(5, 5), '.');

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "rows"
                && diagnostic.Message.Contains("every walkable cell")
                && diagnostic.Message.Contains("arrival.start")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsUnrecordedRoomBoundaryCrossingsForOwnedGenerator()
    {
        JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
        SetSymbol(root, new DungeonCell(4, 2), '.');

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "doors"
                && diagnostic.Message.Contains("room-boundary crossing")
                && diagnostic.Message.Contains("exactly one recorded")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsDoorlessRoomsForOwnedGenerator()
    {
        JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
        SetSymbol(root, new DungeonCell(1, 4), '#');
        SetSymbol(root, new DungeonCell(1, 5), '#');
        root["doors"] = new JArray();

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "doors"
                && diagnostic.Message.Contains("every room")
                && diagnostic.Message.Contains("valid recorded door")
            )
        );
        Assert.That(
            parsed.Diagnostics,
            Has.None.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Message.Contains("room-boundary crossing")
                || diagnostic.Message.Contains("Every 'D' row cell")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsMalformedOwnedDoorGeometryAndRoomAdjacency()
    {
        var cases = new[]
        {
            new
            {
                Name = "one neighbor",
                Mutate = (Action<JObject>)(root => SetSymbol(root, new DungeonCell(1, 5), '#')),
            },
            new
            {
                Name = "three neighbors",
                Mutate = (Action<JObject>)(root => SetSymbol(root, new DungeonCell(0, 4), '.')),
            },
            new
            {
                Name = "non-opposite neighbors",
                Mutate = (Action<JObject>)(
                    root =>
                    {
                        SetSymbol(root, new DungeonCell(1, 5), '#');
                        SetSymbol(root, new DungeonCell(0, 4), '.');
                    }
                ),
            },
            new
            {
                Name = "no room adjacency",
                Mutate = (Action<JObject>)(
                    root =>
                    {
                        SetSymbol(root, new DungeonCell(1, 4), '#');
                        SetSymbol(root, new DungeonCell(1, 5), '#');
                        SetSymbol(root, new DungeonCell(4, 10), '.');
                        SetSymbol(root, new DungeonCell(5, 10), 'D');
                        SetSymbol(root, new DungeonCell(6, 10), '.');
                        ((JObject)((JArray)root["doors"])[0])["cell"] = JsonCell(
                            new DungeonCell(5, 10)
                        );
                    }
                ),
            },
        };

        foreach (var item in cases)
        {
            JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
            item.Mutate(root);

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "doors"
                    && diagnostic.Message.Contains("exactly two opposite walkable neighbors")
                    && diagnostic.Message.Contains("valid room adjacency")
                ),
                item.Name
            );
        }
    }

    [Test]
    public void DungeonJson_RejectsOwnedDoorRecordIdOrderAndSillParityDrift()
    {
        List<(string Name, JObject Root)> cases = new();

        JObject wrongId = JObject.Parse(OwnedContractJson(152, 0, 0));
        ((JObject)((JArray)wrongId["doors"])[0])["id"] = "custom-door";
        cases.Add(("non-generator ID", wrongId));

        JObject evenEvenSill = JObject.Parse(OwnedContractJson(152, 0, 0));
        SetSymbol(evenEvenSill, new DungeonCell(1, 4), '#');
        SetSymbol(evenEvenSill, new DungeonCell(1, 5), '#');
        SetSymbol(evenEvenSill, new DungeonCell(2, 4), 'D');
        SetSymbol(evenEvenSill, new DungeonCell(2, 5), '.');
        ((JObject)((JArray)evenEvenSill["doors"])[0])["cell"] = JsonCell(new DungeonCell(2, 4));
        cases.Add(("even/even sill", evenEvenSill));

        JObject reordered = JObject.Parse(GenerateJson(Request(152, 31, 31)));
        JArray generatedDoors = (JArray)reordered["doors"];
        Assert.That(generatedDoors.Count, Is.GreaterThan(1));
        reordered["doors"] = new JArray(generatedDoors.Reverse().Select(door => door.DeepClone()));
        cases.Add(("reordered records", reordered));

        foreach ((string name, JObject root) in cases)
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "doors"
                    && diagnostic.Message.Contains("ordered door-0001-style IDs")
                    && diagnostic.Message.Contains("door-sill parity")
                ),
                name
            );
        }
    }

    [Test]
    public void DungeonJson_RejectsImpossibleOwnedStairEndGeometry()
    {
        var cases = new[]
        {
            new
            {
                Name = "room floor endpoints",
                Mutate = (Action<JObject>)(
                    root =>
                    {
                        JObject room = (JObject)((JArray)root["rooms"])[0];
                        DungeonCell cell = new(room.Value<int>("minX"), room.Value<int>("minZ"));
                        DungeonCell arrival = new(cell.X + 1, cell.Z);
                        JObject down = ((JArray)root["stairs"])
                            .Cast<JObject>()
                            .Single(stair => stair.Value<string>("kind") == "down");
                        down["cell"] = JsonCell(cell);
                        down["arrivalCell"] = JsonCell(arrival);
                    }
                ),
            },
            new
            {
                Name = "open side neighbor",
                Mutate = (Action<JObject>)(
                    root =>
                    {
                        JObject down = ((JArray)root["stairs"])
                            .Cast<JObject>()
                            .Single(stair => stair.Value<string>("kind") == "down");
                        DungeonCell cell = ReadJsonCell(down["cell"]);
                        DungeonCell arrival = ReadJsonCell(down["arrivalCell"]);
                        DungeonCell direction = new(arrival.X - cell.X, arrival.Z - cell.Z);
                        SetSymbol(
                            root,
                            new DungeonCell(cell.X - direction.Z, cell.Z + direction.X),
                            '.'
                        );
                    }
                ),
            },
        };

        foreach (var item in cases)
        {
            JObject root = JObject.Parse(GenerateJson(Request(152, 31, 31)));
            item.Mutate(root);

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "stairs"
                    && diagnostic.Message.Contains("straight three-cell corridor end")
                    && diagnostic.Message.Contains("surrounding endpoint cells blocked")
                ),
                item.Name
            );
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void DungeonJson_AcceptsOwnedStairRecordSequences(int stairCount)
    {
        DungeonGenerationRequest request = Request(152 + stairCount, 31, 31);
        request.StairCount = stairCount;
        string json = GenerateJson(request);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [Test]
    public void DungeonJson_RejectsOwnedSafeCellMembershipAndOrderDrift()
    {
        JObject source = JObject.Parse(GenerateJson(RequestWithStairCount(152, 2)));
        JObject arrival = (JObject)source["arrival"];
        JArray originalSafe = (JArray)arrival["safeCells"];
        JObject down = ((JArray)source["stairs"])
            .Cast<JObject>()
            .Single(stair => stair.Value<string>("kind") == "down");
        DungeonCell downArrival = ReadJsonCell(down["arrivalCell"]);
        int downIndex = originalSafe.Select(ReadJsonCell).ToList().IndexOf(downArrival);
        Assert.That(downIndex, Is.GreaterThanOrEqualTo(0));

        JObject missingArrival = (JObject)source.DeepClone();
        ((JArray)((JObject)missingArrival["arrival"])["safeCells"]).RemoveAt(downIndex);

        JObject reordered = (JObject)source.DeepClone();
        JArray reorderedSafe = (JArray)((JObject)reordered["arrival"])["safeCells"];
        Assert.That(reorderedSafe.Count, Is.GreaterThan(1));
        JToken first = reorderedSafe[0].DeepClone();
        JToken second = reorderedSafe[1].DeepClone();
        reorderedSafe[0] = second;
        reorderedSafe[1] = first;

        JObject extra = (JObject)source.DeepClone();
        ((JArray)((JObject)extra["arrival"])["safeCells"]).Add(down["cell"].DeepClone());

        foreach (
            (string name, JObject root) in new[]
            {
                ("missing Down arrival", missingArrival),
                ("reordered safe cells", reordered),
                ("extra safe cell", extra),
            }
        )
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "arrival.safeCells"
                    && diagnostic.Message.Contains("ordered stair arrivals")
                    && diagnostic.Message.Contains("ordered room centers")
                ),
                name
            );
        }
    }

    [Test]
    public void DungeonJson_RejectsOwnedStairRecordDrift()
    {
        List<(string Name, JObject Root)> cases = new();

        JObject loneUp = JObject.Parse(GenerateJson(RequestWithStairCount(201, 1)));
        JObject loneStair = (JObject)((JArray)loneUp["stairs"])[0];
        loneStair["id"] = "stair-up";
        loneStair["kind"] = "up";
        ((JObject)loneUp["arrival"])["start"] = loneStair["arrivalCell"].DeepClone();
        cases.Add(("lone Up", loneUp));

        JObject reversed = JObject.Parse(GenerateJson(RequestWithStairCount(202, 2)));
        JArray reversedStairs = (JArray)reversed["stairs"];
        reversed["stairs"] = new JArray(
            reversedStairs[1].DeepClone(),
            reversedStairs[0].DeepClone()
        );
        cases.Add(("reversed Down/Up records", reversed));

        JObject wrongId = JObject.Parse(GenerateJson(RequestWithStairCount(203, 1)));
        ((JObject)((JArray)wrongId["stairs"])[0])["id"] = "custom-down";
        cases.Add(("non-generator ID", wrongId));

        JObject duplicateEndpoint = JObject.Parse(GenerateJson(RequestWithStairCount(204, 2)));
        JArray duplicateEndpointStairs = (JArray)duplicateEndpoint["stairs"];
        JObject duplicateEndpointDown = (JObject)duplicateEndpointStairs[0];
        JObject duplicateEndpointUp = (JObject)duplicateEndpointStairs[1];
        duplicateEndpointUp["cell"] = duplicateEndpointDown["cell"].DeepClone();
        duplicateEndpointUp["arrivalCell"] = duplicateEndpointDown["arrivalCell"].DeepClone();
        ((JObject)duplicateEndpoint["arrival"])["start"] = duplicateEndpointDown["arrivalCell"]
            .DeepClone();
        cases.Add(("duplicate endpoint", duplicateEndpoint));

        JObject duplicateArrival = JObject.Parse(GenerateJson(RequestWithStairCount(205, 2)));
        JArray duplicateArrivalStairs = (JArray)duplicateArrival["stairs"];
        JObject duplicateArrivalDown = (JObject)duplicateArrivalStairs[0];
        JObject duplicateArrivalUp = (JObject)duplicateArrivalStairs[1];
        duplicateArrivalUp["arrivalCell"] = duplicateArrivalDown["arrivalCell"].DeepClone();
        ((JObject)duplicateArrival["arrival"])["start"] = duplicateArrivalDown["arrivalCell"]
            .DeepClone();
        cases.Add(("duplicate arrival", duplicateArrival));

        JObject evenEndpoint = JObject.Parse(OwnedContractJson(152, 0, 0));
        SetSymbol(evenEndpoint, new DungeonCell(3, 5), '.');
        SetSymbol(evenEndpoint, new DungeonCell(4, 5), '.');
        evenEndpoint["stairs"] = new JArray(
            new JObject
            {
                ["id"] = "stair-down",
                ["kind"] = "down",
                ["cell"] = JsonCell(new DungeonCell(4, 5)),
                ["arrivalCell"] = JsonCell(new DungeonCell(3, 5)),
            }
        );
        cases.Add(("even endpoint coordinate", evenEndpoint));

        foreach ((string name, JObject root) in cases)
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
                root.ToString(Formatting.None)
            );

            Assert.That(
                parsed.Diagnostics,
                Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                    diagnostic.Field == "stairs"
                    && diagnostic.Message.Contains("empty, one ordered stair-down/Down record")
                    && diagnostic.Message.Contains(
                        "distinct generator-aligned endpoint and arrival geometry"
                    )
                ),
                name
            );
        }
    }

    [TestCase("future-generator")]
    public void DungeonJson_DoesNotApplyOwnedDimensionsRoomsOrStairsToOtherContracts(
        string algorithm
    )
    {
        JObject root = JObject.Parse(ContractJson());
        JObject generation = (JObject)root["generation"];
        generation["algorithm"] = algorithm;
        ((JArray)root["rows"])[0] = "###";
        root["doors"] = new JArray();
        ((JObject)root["runtimeState"])["openDoorIds"] = new JArray();
        ((JObject)((JArray)root["rooms"])[0])["id"] = 7;
        ((JObject)((JArray)root["encounterPlans"])[0])["roomId"] = 7;
        JObject duplicateStair = (JObject)((JArray)root["stairs"])[0].DeepClone();
        duplicateStair["id"] = "future-down";
        duplicateStair["kind"] = "down";
        ((JArray)root["stairs"]).Add(duplicateStair);
        string json = root.ToString(Formatting.None);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [Test]
    public void DungeonJson_DoesNotApplyOwnedTopologyRulesToOtherContracts()
    {
        JObject root = JObject.Parse(OwnedContractJson(152, 0, 0));
        ((JObject)root["generation"])["algorithm"] = "future-generator";
        SetSymbol(root, new DungeonCell(5, 5), '.');
        SetSymbol(root, new DungeonCell(4, 2), '.');
        SetSymbol(root, new DungeonCell(1, 5), '#');
        string json = root.ToString(Formatting.None);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [TestCase("future-generator")]
    public void DungeonJson_DoesNotApplyOwnedMaskSafeArrivalOrDoorRecordsToOtherContracts(
        string algorithm
    )
    {
        JObject root = JObject.Parse(ContractJson());
        JObject generation = (JObject)root["generation"];
        generation["algorithm"] = algorithm;
        SetSymbol(root, new DungeonCell(0, 0), ' ');
        SetSymbol(root, new DungeonCell(1, 2), '.');
        SetSymbol(root, new DungeonCell(2, 2), 'D');
        JObject door = (JObject)((JArray)root["doors"])[0];
        door["id"] = "future-door";
        door["cell"] = JsonCell(new DungeonCell(2, 2));
        ((JObject)root["runtimeState"])["openDoorIds"] = new JArray("future-door");
        ((JObject)root["arrival"])["safeCells"] = new JArray(JsonCell(new DungeonCell(2, 1)));
        string json = root.ToString(Formatting.None);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
    }

    [TestCase("future-generator")]
    public void DungeonJson_DoesNotApplyRectangularRoundOrSafeFallbackToOtherContracts(
        string algorithm
    )
    {
        JObject root = JObject.Parse(ContractJson());
        JObject generation = (JObject)root["generation"];
        generation["algorithm"] = algorithm;
        ResizeRows(root, 101, 15, (x, z) => IsMaskedByPinnedDonjonRound(101, 15, x, z));
        JObject stair = (JObject)((JArray)root["stairs"])[0];
        stair["id"] = "stair-down";
        stair["kind"] = "down";
        DungeonCell arrival = ReadJsonCell(stair["arrivalCell"]);
        JObject arrivalRecord = (JObject)root["arrival"];
        arrivalRecord["start"] = JsonCell(arrival);
        arrivalRecord["safeCells"] = new JArray(JsonCell(arrival));
        string json = root.ToString(Formatting.None);

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);

        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(diagnostic => diagnostic.Message)
            )
        );
        Assert.That(DungeonLevelJsonSerializer.Serialize(parsed.Document), Is.EqualTo(json));
        Assert.That(
            DungeonTopologyValidator.HasProducibleLayoutMask(parsed.Document.Rows),
            Is.False,
            "the old width-radius rectangle must remain valid only because this is a future contract"
        );
        Assert.That(parsed.Document.SafeCells, Is.EqualTo(new[] { arrival }));
        Assert.That(parsed.Document.StartCell, Is.EqualTo(arrival));
    }

    [Test]
    public void DungeonJson_RejectsWhitespaceOnlyRequiredAndCollectionStrings()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)root["generation"])["algorithm"] = "   ";
        JObject encounter = (JObject)((JArray)root["encounterPlans"])[0];
        encounter["id"] = " ";
        encounter["creatureIds"] = new JArray("\t");

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "generation.algorithm"
                && diagnostic.Message.Contains("non-empty")
            )
        );
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "encounterPlans[0].id"
                && diagnostic.Message.Contains("non-empty")
            )
        );
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "encounterPlans[0].creatureIds[0]"
                && diagnostic.Message.Contains("non-empty")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsDuplicateObjectAndEncounterIds()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JArray)root["objects"]).Add(((JArray)root["objects"])[0].DeepClone());
        ((JArray)root["encounterPlans"]).Add(((JArray)root["encounterPlans"])[0].DeepClone());

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "objects" && diagnostic.Message.Contains("unique")
            )
        );
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "encounterPlans" && diagnostic.Message.Contains("unique")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsEveryMistypedOptionalValue()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)((JArray)root["objects"])[0])["state"] = 4;
        ((JObject)((JArray)((JObject)root["runtimeState"])["creatures"])[0])["state"] = false;
        DungeonLevelParseResult states = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        JObject runtime = JObject.Parse(ContractJson());
        runtime["runtimeState"] = "not-an-object";
        DungeonLevelParseResult runtimeResult = DungeonLevelJsonParser.Parse(
            runtime.ToString(Formatting.None)
        );

        Assert.That(
            states.Diagnostics.Select(diagnostic => diagnostic.Field),
            Does.Contain("objects[0].state")
        );
        Assert.That(
            states.Diagnostics.Select(diagnostic => diagnostic.Field),
            Does.Contain("runtimeState.creatures[0].state")
        );
        Assert.That(
            runtimeResult.Diagnostics.Select(diagnostic => diagnostic.Field),
            Does.Contain("runtimeState")
        );
    }

    [Test]
    public void DungeonJson_RequiresRuntimeIdsToExactlyMirrorPersistedFlags()
    {
        JObject closedButListed = JObject.Parse(ContractJson());
        ((JObject)((JArray)closedButListed["doors"])[0])["isOpen"] = false;
        DungeonLevelParseResult closedButListedResult = DungeonLevelJsonParser.Parse(
            closedButListed.ToString(Formatting.None)
        );

        JObject openButMissing = JObject.Parse(ContractJson());
        ((JObject)openButMissing["runtimeState"])["openDoorIds"] = new JArray();
        DungeonLevelParseResult openButMissingResult = DungeonLevelJsonParser.Parse(
            openButMissing.ToString(Formatting.None)
        );

        JObject unresolvedButListed = JObject.Parse(ContractJson());
        ((JObject)unresolvedButListed["runtimeState"])["resolvedEncounterIds"] = new JArray(
            "encounter-1"
        );
        DungeonLevelParseResult unresolvedButListedResult = DungeonLevelJsonParser.Parse(
            unresolvedButListed.ToString(Formatting.None)
        );

        JObject resolvedButMissing = JObject.Parse(ContractJson());
        ((JObject)((JArray)resolvedButMissing["encounterPlans"])[0])["isResolved"] = true;
        DungeonLevelParseResult resolvedButMissingResult = DungeonLevelJsonParser.Parse(
            resolvedButMissing.ToString(Formatting.None)
        );

        Assert.That(
            closedButListedResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.openDoorIds"
                && diagnostic.Message.Contains("exactly match")
            )
        );
        Assert.That(
            openButMissingResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.openDoorIds"
                && diagnostic.Message.Contains("exactly match")
            )
        );
        Assert.That(
            unresolvedButListedResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.resolvedEncounterIds"
                && diagnostic.Message.Contains("exactly match")
            )
        );
        Assert.That(
            resolvedButMissingResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.resolvedEncounterIds"
                && diagnostic.Message.Contains("exactly match")
            )
        );
    }

    [Test]
    public void DungeonJson_RequiresPristineFlagsWhenRuntimeStateIsAbsent()
    {
        JObject openDoor = JObject.Parse(ContractJson());
        openDoor.Property("runtimeState").Remove();
        DungeonLevelParseResult openDoorResult = DungeonLevelJsonParser.Parse(
            openDoor.ToString(Formatting.None)
        );

        JObject resolvedEncounter = JObject.Parse(ContractJson());
        resolvedEncounter.Property("runtimeState").Remove();
        ((JObject)((JArray)resolvedEncounter["doors"])[0])["isOpen"] = false;
        ((JObject)((JArray)resolvedEncounter["encounterPlans"])[0])["isResolved"] = true;
        DungeonLevelParseResult resolvedEncounterResult = DungeonLevelJsonParser.Parse(
            resolvedEncounter.ToString(Formatting.None)
        );

        Assert.That(
            openDoorResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "doors"
                && diagnostic.Message.Contains("runtime state is absent")
            )
        );
        Assert.That(
            resolvedEncounterResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "encounterPlans"
                && diagnostic.Message.Contains("runtime state is absent")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsLiveCreatureContentOutsideItsEncounterPlan()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)((JArray)((JObject)root["runtimeState"])["creatures"])[0])["creatureId"] =
            "unplanned-creature";

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("match one available creature entry")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsNonpositiveHitPointsForLiveCreatures()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)((JArray)((JObject)root["runtimeState"])["creatures"])[0])["hitPoints"] = 0;

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("hit points must be positive")
            )
        );
    }

    [Test]
    public void DungeonJson_RequiresCompleteStateForEveryMaterializedEncounter()
    {
        JObject partial = JObject.Parse(ContractJson());
        ((JObject)partial["runtimeState"])["creatures"] = new JArray();
        DungeonLevelParseResult partialResult = DungeonLevelJsonParser.Parse(
            partial.ToString(Formatting.None)
        );

        JObject defeatedButUnresolved = JObject.Parse(ContractJson());
        ((JObject)defeatedButUnresolved["runtimeState"])["creatures"] = new JArray();
        ((JObject)defeatedButUnresolved["runtimeState"])["defeatedCreatureIds"] = new JArray(
            "encounter-1/creature-0000",
            "encounter-1/creature-0001"
        );
        DungeonLevelParseResult defeatedButUnresolvedResult = DungeonLevelJsonParser.Parse(
            defeatedButUnresolved.ToString(Formatting.None)
        );

        JObject resolvedWithoutCompleteDefeatedState = JObject.Parse(ContractJson());
        ((JObject)((JArray)resolvedWithoutCompleteDefeatedState["encounterPlans"])[0])[
            "isResolved"
        ] = true;
        ((JObject)resolvedWithoutCompleteDefeatedState["runtimeState"])["resolvedEncounterIds"] =
            new JArray("encounter-1");
        ((JObject)resolvedWithoutCompleteDefeatedState["runtimeState"])["creatures"] = new JArray();
        DungeonLevelParseResult resolvedWithoutCompleteDefeatedStateResult =
            DungeonLevelJsonParser.Parse(
                resolvedWithoutCompleteDefeatedState.ToString(Formatting.None)
            );

        Assert.That(
            partialResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState"
                && diagnostic.Message.Contains("account for every planned creature")
            )
        );
        Assert.That(
            defeatedButUnresolvedResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.resolvedEncounterIds"
                && diagnostic.Message.Contains("must be marked resolved")
            )
        );
        Assert.That(
            resolvedWithoutCompleteDefeatedStateResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState"
                && diagnostic.Message.Contains("persist every planned creature as defeated")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsLiveCreaturesForResolvedPlansAndExcessMultiplicity()
    {
        JObject resolved = JObject.Parse(ContractJson());
        ((JObject)((JArray)resolved["encounterPlans"])[0])["isResolved"] = true;
        ((JObject)resolved["runtimeState"])["resolvedEncounterIds"] = new JArray("encounter-1");
        DungeonLevelParseResult resolvedResult = DungeonLevelJsonParser.Parse(
            resolved.ToString(Formatting.None)
        );

        JObject excess = JObject.Parse(ContractJson());
        JObject duplicateCreature = (JObject)
            ((JArray)((JObject)excess["runtimeState"])["creatures"])[0].DeepClone();
        duplicateCreature["instanceId"] = "encounter-1/creature-0002";
        ((JArray)((JObject)excess["runtimeState"])["creatures"]).Add(duplicateCreature);
        DungeonLevelParseResult excessResult = DungeonLevelJsonParser.Parse(
            excess.ToString(Formatting.None)
        );

        Assert.That(
            resolvedResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("unresolved encounter")
            )
        );
        Assert.That(
            excessResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("available creature entry")
            )
        );
    }

    [Test]
    public void DungeonJson_RequiresDefeatedAndLiveInstanceIdsToBeDisjoint()
    {
        JObject root = JObject.Parse(ContractJson());
        ((JObject)root["runtimeState"])["defeatedCreatureIds"] = new JArray(
            "encounter-1/creature-0000",
            "encounter-1/creature-0001"
        );

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );

        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.defeatedCreatureIds"
                && diagnostic.Message.Contains("disjoint")
            )
        );
    }

    [Test]
    public void DungeonJson_RequiresUniqueLiveInstanceIdsAndOccupiedCells()
    {
        JObject duplicateInstance = JObject.Parse(ContractJson());
        JObject secondByInstance = (JObject)
            ((JArray)((JObject)duplicateInstance["runtimeState"])["creatures"])[0].DeepClone();
        secondByInstance["cell"] = JsonCell(new DungeonCell(0, 1));
        ((JArray)((JObject)duplicateInstance["runtimeState"])["creatures"]).Add(secondByInstance);
        DungeonLevelParseResult duplicateInstanceResult = DungeonLevelJsonParser.Parse(
            duplicateInstance.ToString(Formatting.None)
        );

        JObject duplicateCell = JObject.Parse(ContractJson());
        JObject secondByCell = (JObject)
            ((JArray)((JObject)duplicateCell["runtimeState"])["creatures"])[0].DeepClone();
        secondByCell["instanceId"] = "encounter-1/creature-0000";
        secondByCell["creatureId"] = "creature-a";
        ((JArray)((JObject)duplicateCell["runtimeState"])["creatures"]).Add(secondByCell);
        ((JObject)duplicateCell["runtimeState"])["defeatedCreatureIds"] = new JArray();
        DungeonLevelParseResult duplicateCellResult = DungeonLevelJsonParser.Parse(
            duplicateCell.ToString(Formatting.None)
        );

        Assert.That(
            duplicateInstanceResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("instance IDs must be unique")
            )
        );
        Assert.That(
            duplicateCellResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "runtimeState.creatures"
                && diagnostic.Message.Contains("occupied cells must be unique")
            )
        );
        Assert.That(
            duplicateInstanceResult.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Message.Contains("available creature entry")
            )
        );
        Assert.That(
            duplicateCellResult.Diagnostics,
            Has.None.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Message.Contains("available creature entry")
                || diagnostic.Message.Contains("disjoint")
            )
        );
    }

    [Test]
    public void DungeonJson_RejectsUnknownPropertiesAtEveryObjectLevel()
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

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(
            root.ToString(Formatting.None)
        );
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
    public void DungeonJson_RejectsDuplicateJsonProperties()
    {
        string duplicate = ContractJson()
            .Replace("\"algorithm\":\"test\"", "\"algorithm\":\"test\",\"algorithm\":\"test\"");

        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(duplicate);

        Assert.That(parsed.IsSuccess, Is.False);
        Assert.That(
            parsed.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(diagnostic =>
                diagnostic.Field == "json" && diagnostic.Message.Contains("algorithm")
            )
        );
    }

    [Test]
    public void InvalidRequest_ReturnsActionableDiagnosticsAndNoDocument()
    {
        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest { Width = 20 }
        );

        Assert.That(result.Document, Is.Null);
        Assert.That(
            result.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(d =>
                d.Code == DungeonGenerationDiagnosticCode.InvalidRequest && d.Field == "Width"
            )
        );
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
        Assert.That(
            result.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(d =>
                d.Code == DungeonGenerationDiagnosticCode.RetryLimitExhausted
            )
        );
        Assert.That(
            result.Diagnostics,
            Has.Some.Matches<DungeonGenerationDiagnostic>(d => d.Attempt == 31)
        );
    }

    [Test]
    public void GeneratorAssembly_HasNoUnitySceneMonoBehaviourOrRandomDependency()
    {
        Assembly assembly = typeof(DeterministicDungeonGenerator).Assembly;
        Assert.That(
            assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Has.None.StartsWith("UnityEngine")
        );
        Assert.That(
            assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Has.None.EqualTo("Unity.InputSystem")
        );
        Assert.That(
            assembly
                .GetTypes()
                .SelectMany(type => new[] { type.FullName, type.BaseType?.FullName })
                .Any(value => value != null && value.Contains("UnityEngine.MonoBehaviour")),
            Is.False
        );
        Assert.That(
            assembly
                .GetTypes()
                .SelectMany(type =>
                    type.GetMembers(
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.Static
                    )
                )
                .Any(member => member.ToString().Contains("UnityEngine.Random")),
            Is.False
        );
        Assert.That(
            assembly
                .GetTypes()
                .Any(type =>
                    type.FullName != null && type.FullName.Contains("UnityEngine.SceneManagement")
                ),
            Is.False
        );
        string assemblyDefinition = File.ReadAllText(
            "Assets/Scripts/DungeonGeneration/DungeonGeneration.asmdef"
        );
        Assert.That(assemblyDefinition, Does.Not.Contain("75469ad4d38634e559750d17036d5f7c"));
        Assert.That(assemblyDefinition, Does.Not.Contain("Unity.InputSystem"));
    }

    [Test]
    public void OwnedMasks_UsePinnedThreeByThreeAndSquareCircularScaling()
    {
        foreach (DungeonLayout layout in Enum.GetValues(typeof(DungeonLayout)))
        {
            DungeonGenerationRequest request = Request(90210 + (int)layout, 31, 31);
            request.Layout = layout;
            DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);
            Assert.That(
                result.IsSuccess,
                Is.True,
                layout + ": " + string.Join(" | ", result.Diagnostics.Select(d => d.Message))
            );

            for (int z = 0; z < request.Height; z++)
            for (int x = 0; x < request.Width; x++)
            {
                bool expectedMasked = IsMaskedByOwnedLayout(
                    layout,
                    request.Width,
                    request.Height,
                    x,
                    z
                );
                bool serializedAsMasked = result.Document.Rows[request.Height - 1 - z][x] == ' ';
                Assert.That(serializedAsMasked, Is.EqualTo(expectedMasked), $"{layout} ({x},{z})");
            }
        }
    }

    [Test]
    public void RoundMask_RectangularDimensionsAreRotationallySymmetric()
    {
        const int wideWidth = 101;
        const int wideHeight = 15;
        for (int z = 0; z < wideHeight; z++)
        for (int x = 0; x < wideWidth; x++)
        {
            Assert.That(
                DungeonTopologyValidator.IsMaskedByLayout(
                    DungeonLayout.Round,
                    wideWidth,
                    wideHeight,
                    x,
                    z
                ),
                Is.EqualTo(
                    DungeonTopologyValidator.IsMaskedByLayout(
                        DungeonLayout.Round,
                        wideHeight,
                        wideWidth,
                        z,
                        x
                    )
                ),
                $"rotated cell ({x},{z})"
            );
        }

        Assert.That(
            DungeonTopologyValidator.IsMaskedByLayout(
                DungeonLayout.Round,
                wideWidth,
                wideHeight,
                57,
                7
            ),
            Is.False,
            "the limiting radius includes its boundary"
        );
        Assert.That(
            DungeonTopologyValidator.IsMaskedByLayout(
                DungeonLayout.Round,
                wideWidth,
                wideHeight,
                58,
                7
            ),
            Is.True,
            "the limiting radius excludes the next column"
        );
    }

    [Test]
    public void RoundMask_SquareDimensionsRetainPinnedDonjonColumnRadius()
    {
        foreach (int size in new[] { 15, 31, 101 })
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                Assert.That(
                    DungeonTopologyValidator.IsMaskedByLayout(
                        DungeonLayout.Round,
                        size,
                        size,
                        x,
                        z
                    ),
                    Is.EqualTo(IsMaskedByPinnedDonjonRound(size, size, x, z)),
                    $"square {size} cell ({x},{z})"
                );
            }
    }

    [Test]
    public void DonjonStageModes_HaveAuditedGoldenDocuments()
    {
        var cases = new[]
        {
            new
            {
                Name = "box-packed-straight-clean100",
                Seed = 11,
                Layout = DungeonLayout.Box,
                Rooms = DungeonRoomLayout.Packed,
                Corridors = DungeonCorridorLayout.Straight,
                Cleanup = 100,
            },
            new
            {
                Name = "cross-scattered-labyrinth-clean0",
                Seed = -22,
                Layout = DungeonLayout.Cross,
                Rooms = DungeonRoomLayout.Scattered,
                Corridors = DungeonCorridorLayout.Labyrinth,
                Cleanup = 0,
            },
            new
            {
                Name = "round-packed-bent-clean50",
                Seed = 33,
                Layout = DungeonLayout.Round,
                Rooms = DungeonRoomLayout.Packed,
                Corridors = DungeonCorridorLayout.Bent,
                Cleanup = 50,
            },
        };
        string[] expected =
        {
            "dc6bed290a1ecbb06f57e2b333c3e663ae69964a001501b955a36c6f3f5b0f63",
            "2aa6e46900f3d79f63dadca45978d6fb87f49747354e8e923e7410cccf76422b",
            "68722b681612be2a6820018b09e71dcab164681a9e19aeb6f5013d37dc628506",
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

    private static DungeonLevelDocument ContractDocument() =>
        ContractDocumentWithGeneration(new DungeonGenerationMetadata("test", int.MinValue, 3, 2));

    private static DungeonLevelDocument ContractDocumentWithGeneration(
        DungeonGenerationMetadata generation
    ) =>
        new(
            generation,
            new[] { "#D#", "...", "###" },
            new[] { new DungeonRoom(1, 0, 1, 2, 1) },
            new[] { new DungeonDoor("door-0001", new DungeonCell(1, 2), true) },
            new[]
            {
                new DungeonStair(
                    "stair-up",
                    DungeonStairKind.Up,
                    new DungeonCell(0, 1),
                    new DungeonCell(1, 1)
                ),
            },
            new DungeonCell(1, 1),
            new[] { new DungeonCell(1, 1), new DungeonCell(2, 1) },
            new[]
            {
                new DungeonObjectPlacement("object-1", "prop", new DungeonCell(2, 1), 270, "used"),
            },
            new[]
            {
                new DungeonEncounterPlan(
                    "encounter-1",
                    1,
                    DungeonEncounterThreat.Low,
                    60,
                    new[] { new DungeonCell(0, 1), new DungeonCell(2, 1) },
                    new[] { "creature-a", "creature-b" },
                    false
                ),
            },
            new DungeonRuntimeState(
                new[] { "door-0001" },
                Array.Empty<string>(),
                new[] { "encounter-1/creature-0000" },
                new[]
                {
                    new DungeonCreatureRuntimeState(
                        "encounter-1/creature-0001",
                        "creature-b",
                        "encounter-1",
                        new DungeonCell(2, 1),
                        7,
                        "slowed"
                    ),
                }
            )
        );

    private static string ContractJson() =>
        DungeonLevelJsonSerializer.Serialize(ContractDocument());

    private static string OwnedContractJson(int runSeed, int depth, int topologyAttempt)
    {
        DungeonGenerationMetadata metadata = new(
            "donjon-logical-system-random",
            runSeed,
            depth,
            topologyAttempt
        );
        DungeonLevelDocument document = new(
            metadata,
            new[]
            {
                "###############",
                "###############",
                "###############",
                "###############",
                "###############",
                "#####     #####",
                "#####     #####",
                "#####     #####",
                "#####     #####",
                "#.###     #####",
                "#D#############",
                "#...###########",
                "#...###########",
                "#...###########",
                "###############",
            },
            new[] { new DungeonRoom(1, 1, 1, 3, 3) },
            new[] { new DungeonDoor("door-0001", new DungeonCell(1, 4)) },
            Array.Empty<DungeonStair>(),
            new DungeonCell(2, 2),
            new[] { new DungeonCell(2, 2) },
            Array.Empty<DungeonObjectPlacement>(),
            Array.Empty<DungeonEncounterPlan>()
        );
        return DungeonLevelJsonSerializer.Serialize(document);
    }

    private static DungeonGenerationRequest RequestWithStairCount(int seed, int stairCount)
    {
        DungeonGenerationRequest request = Request(seed, 31, 31);
        request.StairCount = stairCount;
        return request;
    }

    private static void ConfigureOwnedSingleRoom(
        JObject root,
        int minimumX,
        int minimumZ,
        int maximumX,
        int maximumZ,
        int id
    )
    {
        JArray rows = new();
        for (int z = 14; z >= 0; z--)
        {
            char[] row = Enumerable.Repeat('#', 15).ToArray();
            for (int x = 0; x < row.Length; x++)
            {
                if (IsMaskedByOwnedLayout(DungeonLayout.Box, 15, 15, x, z))
                    row[x] = ' ';
            }
            rows.Add(new string(row));
        }
        root["rows"] = rows;
        JObject room = (JObject)((JArray)root["rooms"])[0];
        room["id"] = id;
        room["minX"] = minimumX;
        room["minZ"] = minimumZ;
        room["maxX"] = maximumX;
        room["maxZ"] = maximumZ;
        for (int z = minimumZ; z <= maximumZ; z++)
        for (int x = minimumX; x <= maximumX; x++)
            SetSymbol(root, new DungeonCell(x, z), '.');

        DungeonCell doorCell;
        DungeonCell corridorCell;
        if (maximumZ + 2 < 15)
        {
            int doorX = Enumerable
                .Range(minimumX, maximumX - minimumX + 1)
                .First(x => (x & 1) == 1);
            doorCell = new DungeonCell(doorX, maximumZ + 1);
            corridorCell = new DungeonCell(doorX, maximumZ + 2);
        }
        else
        {
            int doorZ = Enumerable
                .Range(minimumZ, maximumZ - minimumZ + 1)
                .First(z => (z & 1) == 1);
            doorCell = new DungeonCell(maximumX + 1, doorZ);
            corridorCell = new DungeonCell(maximumX + 2, doorZ);
        }

        SetSymbol(root, doorCell, 'D');
        SetSymbol(root, corridorCell, '.');
        JObject door = (JObject)((JArray)root["doors"])[0];
        door["cell"] = JsonCell(doorCell);
        DungeonCell start = new((minimumX + maximumX) / 2, (minimumZ + maximumZ) / 2);
        JObject arrival = (JObject)root["arrival"];
        arrival["start"] = JsonCell(start);
        arrival["safeCells"] = new JArray(JsonCell(start));
    }

    private static void SetSymbol(JObject root, DungeonCell cell, char symbol)
    {
        JArray rows = (JArray)root["rows"];
        int rowIndex = rows.Count - 1 - cell.Z;
        char[] row = rows.Value<string>(rowIndex).ToCharArray();
        row[cell.X] = symbol;
        rows[rowIndex] = new string(row);
    }

    private static void ResizeRows(JObject root, int width, int height) =>
        ResizeRows(
            root,
            width,
            height,
            (x, z) => IsMaskedByOwnedLayout(DungeonLayout.Box, width, height, x, z)
        );

    private static void ResizeRows(
        JObject root,
        int width,
        int height,
        Func<int, int, bool> isMasked
    )
    {
        JArray source = (JArray)root["rows"];
        JArray resized = new();
        for (int z = height - 1; z >= 0; z--)
        {
            char[] row = Enumerable.Repeat('#', width).ToArray();
            for (int x = 0; x < width; x++)
            {
                if (isMasked(x, z))
                    row[x] = ' ';
            }
            if (z < source.Count)
            {
                string sourceRow = source.Value<string>(source.Count - 1 - z);
                int copiedWidth = Math.Min(width, sourceRow.Length);
                for (int x = 0; x < copiedWidth; x++)
                {
                    if (sourceRow[x] == '.' || sourceRow[x] == 'D')
                        row[x] = sourceRow[x];
                }
            }

            resized.Add(new string(row));
        }

        root["rows"] = resized;
    }

    private static DungeonCell ReadJsonCell(JToken token) =>
        new(token.Value<int>("x"), token.Value<int>("z"));

    private static JObject JsonCell(DungeonCell cell) => new() { ["x"] = cell.X, ["z"] = cell.Z };

    private static bool IsMaskedByOwnedLayout(
        DungeonLayout layout,
        int width,
        int height,
        int x,
        int z
    )
    {
        if (layout == DungeonLayout.Round)
        {
            int centerX = (width - 1) / 2;
            int centerZ = (height - 1) / 2;
            int radius = Math.Min(centerX, centerZ);
            long deltaX = x - centerX;
            long deltaZ = z - centerZ;
            return deltaX * deltaX + deltaZ * deltaZ > (long)radius * radius;
        }

        int[,] mask =
            layout == DungeonLayout.Box
                ? new[,]
                {
                    { 1, 1, 1 },
                    { 1, 0, 1 },
                    { 1, 1, 1 },
                }
                : new[,]
                {
                    { 0, 1, 0 },
                    { 1, 1, 1 },
                    { 0, 1, 0 },
                };
        return mask[z * 3 / height, x * 3 / width] == 0;
    }

    private static bool IsMaskedByPinnedDonjonRound(int width, int height, int x, int z)
    {
        int centerX = (width - 1) / 2;
        int centerZ = (height - 1) / 2;
        long deltaX = x - centerX;
        long deltaZ = z - centerZ;
        return deltaX * deltaX + deltaZ * deltaZ > (long)centerX * centerX;
    }

    private static DungeonGenerationRequest Request(int seed, int width, int height) =>
        new()
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
            DeadEndRemovalPercent = 50,
        };

    private static string GenerateJson(DungeonGenerationRequest request)
    {
        DungeonGenerationResult result = new DeterministicDungeonGenerator().Generate(request);
        Assert.That(
            result.IsSuccess,
            Is.True,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message))
        );
        return DungeonLevelJsonSerializer.Serialize(result.Document);
    }

    private static string Sha256(string value)
    {
        using SHA256 sha256 = SHA256.Create();
        return BitConverter
            .ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void AssertDocumentInvariants(DungeonLevelDocument document, string seed)
    {
        HashSet<DungeonCell> walkable = Walkable(document);
        Assert.That(
            DeterministicDungeonGenerator.IsSupportedDimension(document.Rows[0].Length),
            Is.True,
            "width seed " + seed
        );
        Assert.That(
            DeterministicDungeonGenerator.IsSupportedDimension(document.Rows.Count),
            Is.True,
            "height seed " + seed
        );
        Assert.That(walkable, Does.Contain(document.StartCell), "start seed " + seed);
        Assert.That(document.SafeCells.All(walkable.Contains), Is.True, "safe seed " + seed);
        Assert.That(
            DungeonTopologyValidator.HasProducibleLayoutMask(document.Rows),
            Is.True,
            "layout mask seed " + seed
        );
        Assert.That(
            DungeonTopologyValidator.HasProducibleSafeCells(
                document.Rows,
                document.Rooms,
                document.Stairs,
                document.SafeCells
            ),
            Is.True,
            "safe sequence seed " + seed
        );
        Assert.That(
            document.Stairs.Select(stair => stair.ArrivalCell),
            Is.EqualTo(document.SafeCells.Take(document.Stairs.Count)),
            "stair arrivals lead safe sequence seed " + seed
        );
        Assert.That(
            document.Stairs.All(stair =>
                walkable.Contains(stair.Cell) && walkable.Contains(stair.ArrivalCell)
            ),
            Is.True,
            "stairs seed " + seed
        );
        Assert.That(
            DungeonTopologyValidator.HasProducibleStairRecords(document.Stairs),
            Is.True,
            "stair records seed " + seed
        );
        if (document.Stairs.Count > 0)
        {
            Assert.That(document.Stairs[0].Id, Is.EqualTo("stair-down"), "down id seed " + seed);
            Assert.That(
                document.Stairs[0].Kind,
                Is.EqualTo(DungeonStairKind.Down),
                "down-first seed " + seed
            );
        }
        if (document.Stairs.Count == 1)
        {
            DungeonStair down = document.Stairs[0];
            HashSet<DungeonCell> downExitCells = new() { down.Cell, down.ArrivalCell };
            if (walkable.Any(cell => !downExitCells.Contains(cell)))
            {
                Assert.That(
                    document.SafeCells.Any(cell => !downExitCells.Contains(cell)),
                    Is.True,
                    "one-stair safe fallback seed " + seed
                );
                Assert.That(
                    downExitCells.Contains(document.StartCell),
                    Is.False,
                    "one-stair start avoids exit seed " + seed
                );
            }
        }
        if (document.Stairs.Count > 1)
        {
            Assert.That(document.Stairs[1].Id, Is.EqualTo("stair-up"), "up id seed " + seed);
            Assert.That(
                document.Stairs[1].Kind,
                Is.EqualTo(DungeonStairKind.Up),
                "up-second seed " + seed
            );
            Assert.That(
                document.StartCell,
                Is.EqualTo(document.Stairs[1].ArrivalCell),
                "up arrival start seed " + seed
            );
        }

        Queue<DungeonCell> queue = new();
        HashSet<DungeonCell> reached = new() { walkable.First() };
        queue.Enqueue(walkable.First());
        while (queue.Count > 0)
        {
            DungeonCell cell = queue.Dequeue();
            foreach (DungeonCell next in Neighbors(cell).Where(walkable.Contains))
                if (reached.Add(next))
                    queue.Enqueue(next);
        }
        Assert.That(reached.Count, Is.EqualTo(walkable.Count), "connectivity seed " + seed);

        for (int i = 0; i < document.Rooms.Count; i++)
        for (int j = i + 1; j < document.Rooms.Count; j++)
            Assert.That(
                Overlaps(document.Rooms[i], document.Rooms[j]),
                Is.False,
                "rooms seed " + seed
            );
        Assert.That(
            DungeonTopologyValidator.HasProducibleRoomRecords(
                document.Rows[0].Length,
                document.Rows.Count,
                document.Rooms
            ),
            Is.True,
            "room records seed " + seed
        );
        for (int index = 0; index < document.Rooms.Count; index++)
        {
            DungeonRoom room = document.Rooms[index];
            Assert.That(room.Id, Is.EqualTo(index + 1), "room id sequence seed " + seed);
            Assert.That(room.MinimumX % 2, Is.EqualTo(1), "room minX parity seed " + seed);
            Assert.That(room.MinimumZ % 2, Is.EqualTo(1), "room minZ parity seed " + seed);
            Assert.That(room.MaximumX % 2, Is.EqualTo(1), "room maxX parity seed " + seed);
            Assert.That(room.MaximumZ % 2, Is.EqualTo(1), "room maxZ parity seed " + seed);
        }

        foreach (DungeonDoor door in document.Doors)
        {
            List<DungeonCell> open = Neighbors(door.Cell).Where(walkable.Contains).ToList();
            Assert.That(open.Count, Is.EqualTo(2), "door neighbor count seed " + seed);
            Assert.That(
                open[0].X == open[1].X || open[0].Z == open[1].Z,
                Is.True,
                "door axis seed " + seed
            );
        }

        HashSet<DungeonCell> recordedDoorCells = new(document.Doors.Select(door => door.Cell));
        HashSet<DungeonCell> rowDoorCells = new();
        for (int row = 0; row < document.Rows.Count; row++)
        for (int x = 0; x < document.Rows[row].Length; x++)
            if (document.Rows[row][x] == 'D')
                rowDoorCells.Add(new DungeonCell(x, document.Rows.Count - 1 - row));
        Assert.That(
            recordedDoorCells.SetEquals(rowDoorCells),
            Is.True,
            "door records seed " + seed
        );
        Assert.That(
            document.Doors.Select(door => door.Id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(document.Doors.Count),
            "door ids seed " + seed
        );
        Assert.That(
            DungeonTopologyValidator.HasValidDoors(document.Rows, document.Rooms, document.Doors),
            Is.True,
            "valid door per room seed " + seed
        );
        Assert.That(
            DungeonTopologyValidator.HasProducibleDoorRecords(document.Doors),
            Is.True,
            "door records seed " + seed
        );

        foreach (DungeonRoom room in document.Rooms)
            for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
            for (int x = room.MinimumX; x <= room.MaximumX; x++)
                foreach (DungeonCell neighbor in Neighbors(new DungeonCell(x, z)))
                {
                    bool insideRoom =
                        neighbor.X >= room.MinimumX
                        && neighbor.X <= room.MaximumX
                        && neighbor.Z >= room.MinimumZ
                        && neighbor.Z <= room.MaximumZ;
                    if (!insideRoom && walkable.Contains(neighbor))
                        Assert.That(
                            recordedDoorCells,
                            Does.Contain(neighbor),
                            "room crossing seed " + seed
                        );
                }

        foreach (DungeonStair stair in document.Stairs)
        {
            Assert.That(
                DungeonTopologyValidator.MatchesStairEnd(
                    document.Rows,
                    document.Rooms,
                    stair.Cell,
                    stair.ArrivalCell
                ),
                Is.True,
                "shared stair template seed " + seed
            );
            DungeonCell delta = new(
                stair.ArrivalCell.X - stair.Cell.X,
                stair.ArrivalCell.Z - stair.Cell.Z
            );
            DungeonCell far = new(stair.ArrivalCell.X + delta.X, stair.ArrivalCell.Z + delta.Z);
            Assert.That(walkable, Does.Contain(far), "stair corridor template seed " + seed);
            for (int zOffset = -1; zOffset <= 1; zOffset++)
            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if ((xOffset == 0 && zOffset == 0) || (xOffset == delta.X && zOffset == delta.Z))
                    continue;
                DungeonCell neighbor = new(stair.Cell.X + xOffset, stair.Cell.Z + zOffset);
                Assert.That(
                    walkable.Contains(neighbor),
                    Is.False,
                    "stair wall template seed " + seed
                );
            }
        }
    }

    private static HashSet<DungeonCell> Walkable(DungeonLevelDocument document)
    {
        HashSet<DungeonCell> result = new();
        for (int row = 0; row < document.Rows.Count; row++)
        for (int x = 0; x < document.Rows[row].Length; x++)
            if (document.Rows[row][x] == '.' || document.Rows[row][x] == 'D')
                result.Add(new DungeonCell(x, document.Rows.Count - 1 - row));
        return result;
    }

    private static IEnumerable<DungeonCell> Neighbors(DungeonCell cell)
    {
        yield return new DungeonCell(cell.X + 1, cell.Z);
        yield return new DungeonCell(cell.X - 1, cell.Z);
        yield return new DungeonCell(cell.X, cell.Z + 1);
        yield return new DungeonCell(cell.X, cell.Z - 1);
    }

    private static bool Overlaps(DungeonRoom left, DungeonRoom right) =>
        left.MinimumX <= right.MaximumX
        && left.MaximumX >= right.MinimumX
        && left.MinimumZ <= right.MaximumZ
        && left.MaximumZ >= right.MinimumZ;
}
