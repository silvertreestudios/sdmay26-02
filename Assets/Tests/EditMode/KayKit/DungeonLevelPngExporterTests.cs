using System;
using System.Collections.Generic;
using System.IO;
using Game.DungeonGeneration;
using Game.KayKit.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class DungeonLevelPngExporterTests
{
    private const int CellSize = 12;

    [Test]
    public void RenderPng_PreservesDimensionsAndHighestZAtTop()
    {
        DungeonLevelDocument document = FixtureDocument();
        Texture2D texture = Decode(DungeonLevelPngExporter.RenderPng(document, CellSize));
        try
        {
            Assert.That(texture.width, Is.EqualTo(document.Width * CellSize));
            Assert.That(texture.height, Is.EqualTo(document.Height * CellSize));

            Color32 highZWall = CellCenter(texture, 8, 37);
            Color32 lowZCorridor = CellCenter(texture, 8, 1);
            Assert.That(highZWall, Is.Not.EqualTo(lowZCorridor));
            Assert.That(
                highZWall,
                Is.EqualTo(CellCenter(texture, 0, 0)),
                "The highest-Z-first source row must appear at the top of the PNG.");
            Assert.That(
                lowZCorridor,
                Is.EqualTo(CellCenter(texture, 1, 37)),
                "The lowest serialized row must appear at the bottom of the PNG.");
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void RenderPng_UsesDistinctColorsForBaseSemanticsAndOverlays()
    {
        Texture2D texture = Decode(DungeonLevelPngExporter.RenderPng(FixtureJson(), CellSize));
        try
        {
            Color32[] semanticColors =
            {
                CellCenter(texture, 13, 25), // Masked void.
                CellCenter(texture, 0, 0),   // Wall.
                CellCenter(texture, 34, 30), // Room floor.
                CellCenter(texture, 1, 37),  // Corridor floor.
                CellCenter(texture, 30, 27), // Door.
                CellCenter(texture, 11, 25), // Up stair.
                CellCenter(texture, 3, 3),   // Down stair.
                Pixel(texture, 11, 24, 1, 1), // Start frame.
                CellCenter(texture, 33, 33)  // Object anchor diamond.
            };

            Assert.That(
                new HashSet<Color32>(semanticColors).Count,
                Is.EqualTo(semanticColors.Length),
                "Every documented base semantic and overlay must have a stable distinct color.");
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
            DungeonLevelPngExporter.RenderPng("{\"rows\":[]}", CellSize));
        Assert.That(invalidDocument.Message, Does.Contain("Generation metadata is required"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonLevelPngExporter.RenderPng(FixtureJson(), 6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DungeonLevelPngExporter.RenderPng(FixtureJson(), 128));
    }

    [Test]
    public void DefaultOutputPath_IsUnderAgentTempAndNeverAssets()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string expectedDirectory = Path.Combine(projectRoot, ".agent-temp");

        Assert.That(
            Path.GetDirectoryName(DungeonLevelPngExporter.DefaultOutputPath),
            Is.EqualTo(expectedDirectory).IgnoreCase);
        Assert.That(
            DungeonLevelPngExporter.DefaultOutputPath.StartsWith(
                Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            Is.False);
    }

    [Test]
    public void WriteFile_WritesArbitraryInputOutsideAssetsAndRejectsAssetsOutput()
    {
        string outputPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            ".agent-temp",
            "tests",
            "dungeon-level-png-exporter.png");
        try
        {
            string writtenPath = DungeonLevelPngExporter.WriteFile(
                DungeonLevelPngExporter.DefaultInputPath,
                outputPath,
                CellSize);

            Assert.That(writtenPath, Is.EqualTo(Path.GetFullPath(outputPath)).IgnoreCase);
            Assert.That(File.Exists(writtenPath), Is.True);
            Assert.That(File.ReadAllBytes(writtenPath), Is.EqualTo(
                DungeonLevelPngExporter.RenderPng(FixtureJson(), CellSize)));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }

        Assert.Throws<ArgumentException>(() => DungeonLevelPngExporter.WritePng(
            FixtureJson(),
            Path.Combine(Application.dataPath, "diagnostic.png"),
            CellSize));
    }

    private static DungeonLevelDocument FixtureDocument()
    {
        DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(FixtureJson());
        Assert.That(
            parsed.IsSuccess,
            Is.True,
            string.Join(Environment.NewLine, parsed.Diagnostics));
        return parsed.Document;
    }

    private static string FixtureJson() => File.ReadAllText(
        DungeonLevelPngExporter.DefaultInputPath);

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

    private static Color32 CellCenter(Texture2D texture, int x, int z) =>
        Pixel(texture, x, z, CellSize / 2, CellSize / 2);

    private static Color32 Pixel(
        Texture2D texture,
        int x,
        int z,
        int localX,
        int localY) => texture.GetPixel(x * CellSize + localX, z * CellSize + localY);
}
