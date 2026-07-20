using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.DungeonGeneration;
using Game.KayKit.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DungeonLevelPngExporterTests
{
    private const int CellSize = 12;

    [Test]
    public void Fixture_MatchesRequestedPackedVariedPreset()
    {
        DungeonGenerationResult expected = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 156,
                Width = 31,
                Height = 31,
                Layout = DungeonLayout.Box,
                RoomLayout = DungeonRoomLayout.Packed,
                CorridorLayout = DungeonCorridorLayout.Straight,
                MinimumRoomSize = 5,
                MaximumRoomSize = 13,
                MinimumRoomCount = 3,
                StairCount = 2,
                DeadEndRemovalPercent = 100,
            }
        );

        Assert.That(
            expected.IsSuccess,
            Is.True,
            string.Join(Environment.NewLine, expected.Diagnostics)
        );
        Assert.That(
            FixtureJson(),
            Is.EqualTo(DungeonLevelJsonSerializer.Serialize(expected.Document))
        );
        Assert.That(expected.Document.Rooms, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(
            expected.Document.Rooms.All(room =>
            {
                int width = room.MaximumX - room.MinimumX + 1;
                int height = room.MaximumZ - room.MinimumZ + 1;
                return width is >= 5 and <= 13
                    && width % 2 == 1
                    && height is >= 5 and <= 13
                    && height % 2 == 1;
            }),
            Is.True
        );
        Assert.That(
            expected
                .Document.Rooms.Select(room =>
                {
                    int width = room.MaximumX - room.MinimumX + 1;
                    int height = room.MaximumZ - room.MinimumZ + 1;
                    return (Minimum: Math.Min(width, height), Maximum: Math.Max(width, height));
                })
                .Distinct()
                .Count(),
            Is.GreaterThan(1)
        );
    }

    [Test]
    public void RenderPng_PreservesDimensionsAndHighestZAtTop()
    {
        DungeonLevelDocument document = FixtureDocument();
        Texture2D texture = Decode(DungeonLevelPngExporter.RenderPng(document, CellSize));
        try
        {
            Assert.That(texture.width, Is.EqualTo(document.Width * CellSize));
            Assert.That(texture.height, Is.EqualTo(document.Height * CellSize));

            DungeonCell asymmetricWall = FindAsymmetricWall(document);
            Assert.That(
                CellCenter(texture, asymmetricWall.X, asymmetricWall.Z),
                Is.EqualTo(CellCenter(texture, 0, 0)),
                "The source row for a map cell must be rendered at that cell's Z coordinate, not its vertical mirror."
            );
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void RenderPng_UsesDistinctColorsForBaseSemanticsAndOverlays()
    {
        DungeonLevelDocument document = FixtureDocument();
        Texture2D texture = Decode(DungeonLevelPngExporter.RenderPng(document, CellSize));
        try
        {
            HashSet<DungeonCell> roomCells = RoomCells(document);
            Color32[] semanticColors =
            {
                CellCenter(texture, FindPlainCell(document, ' ', roomCells, false)),
                CellCenter(texture, FindPlainCell(document, '#', roomCells, false)),
                CellCenter(texture, FindPlainCell(document, '.', roomCells, true)),
                CellCenter(texture, FindPlainCell(document, '.', roomCells, false)),
                CellCenter(texture, document.Doors[0].Cell),
                CellCenter(
                    texture,
                    document.Stairs.Single(stair => stair.Kind == DungeonStairKind.Up).Cell
                ),
                CellCenter(
                    texture,
                    document.Stairs.Single(stair => stair.Kind == DungeonStairKind.Down).Cell
                ),
                Pixel(texture, document.StartCell.X, document.StartCell.Z, 1, 1),
                CellCenter(texture, document.Objects[0].Cell),
            };

            Assert.That(
                new HashSet<Color32>(semanticColors).Count,
                Is.EqualTo(semanticColors.Length),
                "Every documented base semantic and overlay must have a stable distinct color."
            );
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void RenderPng_IsByteStableForSameSerializedDocument()
    {
        string json = FixtureJson();

        byte[] first = DungeonLevelPngExporter.RenderPng(json, CellSize);
        byte[] second = DungeonLevelPngExporter.RenderPng(json, CellSize);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void RenderPng_RejectsInvalidJsonAndUnsupportedCellSize()
    {
        InvalidDataException invalidDocument = Assert.Throws<InvalidDataException>(() =>
            DungeonLevelPngExporter.RenderPng("{\"rows\":[]}", CellSize)
        );
        Assert.That(invalidDocument.Message, Does.Contain("Generation metadata is required"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonLevelPngExporter.RenderPng(FixtureJson(), 6)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonLevelPngExporter.RenderPng(FixtureJson(), 129)
        );

        DungeonGenerationResult large = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 167,
                Width = 101,
                Height = 101,
                MinimumRoomCount = 0,
                StairCount = 0,
            }
        );
        Assert.That(large.IsSuccess, Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonLevelPngExporter.RenderPng(large.Document, 128)
        );
    }

    [Test]
    public void DefaultOutputPath_IsUnderAgentTempAndNeverAssets()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string expectedDirectory = Path.Combine(projectRoot, ".agent-temp");

        Assert.That(
            Path.GetDirectoryName(DungeonLevelPngExporter.DefaultOutputPath),
            Is.EqualTo(expectedDirectory).IgnoreCase
        );
        Assert.That(
            DungeonLevelPngExporter.DefaultOutputPath.StartsWith(
                Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            ),
            Is.False
        );
    }

    [Test]
    public void WriteFile_WritesArbitraryInputOutsideAssetsAndRejectsAssetsOutput()
    {
        string outputPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            ".agent-temp",
            "tests",
            "dungeon-level-png-exporter.png"
        );
        try
        {
            string writtenPath = DungeonLevelPngExporter.WriteFile(
                DungeonLevelPngExporter.DefaultInputPath,
                outputPath,
                CellSize
            );

            Assert.That(writtenPath, Is.EqualTo(Path.GetFullPath(outputPath)).IgnoreCase);
            Assert.That(File.Exists(writtenPath), Is.True);
            Assert.That(
                File.ReadAllBytes(writtenPath),
                Is.EqualTo(DungeonLevelPngExporter.RenderPng(FixtureJson(), CellSize))
            );
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }

        Assert.Throws<ArgumentException>(() =>
            DungeonLevelPngExporter.WritePng(
                FixtureJson(),
                Path.Combine(Application.dataPath, "diagnostic.png"),
                CellSize
            )
        );
    }

    private static DungeonLevelDocument FixtureDocument()
    {
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(FixtureJson());
        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(Environment.NewLine, parsed.Diagnostics)
        );
        return parsed.Document;
    }

    private static string FixtureJson() =>
        File.ReadAllText(DungeonLevelPngExporter.DefaultInputPath);

    private static Texture2D Decode(byte[] png)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, false, true);
        if (!texture.LoadImage(png, false))
        {
            Object.DestroyImmediate(texture);
            throw new AssertionException("Generated bytes were not a decodable PNG.");
        }

        return texture;
    }

    private static DungeonCell FindAsymmetricWall(DungeonLevelDocument document)
    {
        for (int z = 0; z < document.Height; z++)
        for (int x = 0; x < document.Width; x++)
        {
            char expected = document.Rows[document.Height - 1 - z][x];
            char verticallyMirrored = document.Rows[z][x];
            if (expected == '#' && verticallyMirrored != '#')
                return new DungeonCell(x, z);
        }

        throw new AssertionException(
            "The fixture must contain a wall whose vertical mirror has a different semantic."
        );
    }

    private static HashSet<DungeonCell> RoomCells(DungeonLevelDocument document)
    {
        HashSet<DungeonCell> cells = new();
        foreach (DungeonRoom room in document.Rooms)
        {
            for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
            for (int x = room.MinimumX; x <= room.MaximumX; x++)
                cells.Add(new DungeonCell(x, z));
        }

        return cells;
    }

    private static DungeonCell FindPlainCell(
        DungeonLevelDocument document,
        char symbol,
        HashSet<DungeonCell> roomCells,
        bool requireRoom
    )
    {
        HashSet<DungeonCell> overlays = new(document.Stairs.Select(stair => stair.Cell));
        overlays.UnionWith(document.Objects.Select(item => item.Cell));
        overlays.Add(document.StartCell);

        for (int z = 0; z < document.Height; z++)
        for (int x = 0; x < document.Width; x++)
        {
            DungeonCell cell = new(x, z);
            if (
                document.Rows[document.Height - 1 - z][x] == symbol
                && roomCells.Contains(cell) == requireRoom
                && !overlays.Contains(cell)
            )
            {
                return cell;
            }
        }

        throw new AssertionException(
            $"The fixture must contain an unoverlaid '{symbol}' cell with room={requireRoom}."
        );
    }

    private static Color32 CellCenter(Texture2D texture, DungeonCell cell) =>
        CellCenter(texture, cell.X, cell.Z);

    private static Color32 CellCenter(Texture2D texture, int x, int z) =>
        Pixel(texture, x, z, CellSize / 2, CellSize / 2);

    private static Color32 Pixel(Texture2D texture, int x, int z, int localX, int localY) =>
        texture.GetPixel(x * CellSize + localX, z * CellSize + localY);
}
