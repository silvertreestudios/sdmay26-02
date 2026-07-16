using System;
using System.Collections.Generic;
using System.Linq;
using Game.KayKit;
using Game.KayKit.Editor;
using GridPrivate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class KayKitDungeonMapTests
{
    private readonly List<UnityEngine.Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        GridLineOfSightData.Unregister(null);
        foreach (UnityEngine.Object target in cleanup.Where(target => target != null).Reverse())
            UnityEngine.Object.DestroyImmediate(target);
        cleanup.Clear();
    }

    [Test]
    public void JsonVersionOne_UsesSpecifiedSymbolsAndHighestZOrientation()
    {
        KayKitDungeonCatalog catalog = Catalog();
        KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(
            @"{""version"":1,""rows"":[""#D "",""...""],""objects"":[]}",
            catalog);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
        Assert.That(result.Map.Width, Is.EqualTo(3));
        Assert.That(result.Map.Height, Is.EqualTo(2));
        Assert.That(result.Map.GridData[0, 1], Is.EqualTo(TileType.Wall));
        Assert.That(result.Map.GridData[1, 1], Is.EqualTo(TileType.Door));
        Assert.That(result.Map.GridData[2, 1], Is.EqualTo(TileType.Empty));
        Assert.That(result.Map.GridData[0, 0], Is.EqualTo(TileType.Ground));
        Assert.That(result.Map.LineOfSightBlocks[0, 1], Is.True);
        Assert.That(result.Map.LineOfSightBlocks[1, 1], Is.False);
    }

    [Test]
    public void JsonObjectDefaults_AreZeroAndCatalogEntryIsResolved()
    {
        KayKitDungeonCatalogEntry entry = Entry(
            "dungeon/assets/fbx(unity)/barrel_small",
            Vector2Int.one,
            false,
            false);
        KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(
            @"{""version"":1,""rows"":[""...""],""objects"":[{""assetId"":""dungeon/assets/fbx(unity)/barrel_small"",""x"":1,""z"":0}]}",
            Catalog(entry));

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
        Assert.That(result.Map.Objects.Count, Is.EqualTo(1));
        Assert.That(result.Map.Objects[0].Rotation, Is.Zero);
        Assert.That(result.Map.Objects[0].YOffset, Is.Zero);
        Assert.That(result.Map.Objects[0].CatalogEntry, Is.SameAs(entry));
    }

    [TestCase(@"{""version"":2,""rows"":["".""]}", "version must equal 1")]
    [TestCase(@"{""version"":1,""rows"":[""X""]}", "unknown symbol")]
    [TestCase(@"{""version"":1,""rows"":["".."","".""]}", "expected 2")]
    [TestCase(@"{""version"":1,""rows"":["".""],""objects"":[{""assetId"":""missing"",""x"":0,""z"":0}]}", "unknown assetId")]
    [TestCase(@"{""version"":1,""rows"":["".""],""objects"":[{""assetId"":""prop"",""x"":0,""z"":0,""rotation"":45}]}", "rotation must be")]
    public void InvalidJson_FailsWithActionableMessage(string json, string expected)
    {
        KayKitDungeonCatalog catalog = Catalog(Entry("prop", Vector2Int.one, false, false));
        KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(json, catalog);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(error =>
            error.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0), Is.True);
    }

    [Test]
    public void RotatedBlockingFootprint_OverlaysObstacleCells()
    {
        KayKitDungeonCatalog catalog = Catalog(
            Entry("blocking", new Vector2Int(2, 1), true, false));
        KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(
            @"{""version"":1,""rows"":[""..."",""..."",""...""],""objects"":[{""assetId"":""blocking"",""x"":1,""z"":0,""rotation"":90}]}",
            catalog);

        Assert.That(result.IsValid, Is.True, string.Join(Environment.NewLine, result.Errors));
        Assert.That(result.Map.Objects[0].Footprint, Is.EqualTo(new Vector2Int(1, 2)));
        Assert.That(result.Map.GridData[1, 0], Is.EqualTo(TileType.Obstacle));
        Assert.That(result.Map.GridData[1, 1], Is.EqualTo(TileType.Obstacle));
        Assert.That(result.Map.LineOfSightBlocks[1, 0], Is.False);
    }

    [TestCase(
        @"{""version"":1,""rows"":["".."",""..""],""objects"":[{""assetId"":""blocking"",""x"":2,""z"":1}]}",
        "out of bounds")]
    [TestCase(
        @"{""version"":1,""rows"":["".D""],""objects"":[{""assetId"":""blocking"",""x"":1,""z"":0}]}",
        "entirely on Ground")]
    [TestCase(
        @"{""version"":1,""rows"":[""..""],""objects"":[{""assetId"":""blocking"",""x"":0,""z"":0},{""assetId"":""blocking"",""x"":0,""z"":0}]}",
        "overlaps another blocking")]
    public void InvalidBlockingFootprints_FailBeforeProducingMap(string json, string expected)
    {
        KayKitDungeonCatalog catalog = Catalog(
            Entry("blocking", Vector2Int.one, true, true));
        KayKitDungeonMapParseResult result = KayKitDungeonMapParser.Parse(json, catalog);

        Assert.That(result.Map, Is.Null);
        Assert.That(result.Errors.Any(error => error.Contains(expected)), Is.True);
    }

    [Test]
    public void Catalog_IsUniqueCompleteAndConfiguredForEveryDungeonModel()
    {
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        string[] modelPaths = AssetDatabase.FindAssets(
                string.Empty,
                new[] { KayKitPathUtility.DungeonRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(
                System.IO.Path.GetExtension(path),
                ".fbx",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.That(catalog, Is.Not.Null);
        Assert.That(catalog.Entries, Has.Count.EqualTo(modelPaths.Length));
        Assert.That(catalog.Entries.Select(entry => entry.Id).Distinct().Count(),
            Is.EqualTo(modelPaths.Length));
        Assert.That(catalog.Entries.All(entry =>
            entry.Id == entry.Id.ToLowerInvariant() && entry.Id.Contains('/')), Is.True);
        Assert.That(catalog.Entries.All(entry =>
            entry.Model != null && entry.Footprint.x > 0 && entry.Footprint.y > 0), Is.True);
        Assert.That(catalog.FloorPrefab, Is.Not.Null);
        Assert.That(catalog.WallPrefab, Is.Not.Null);
        Assert.That(catalog.DoorwayPrefab, Is.Not.Null);
        Assert.That(catalog.DefaultMaterial, Is.Not.Null);
        Assert.That(catalog.Entries.Any(entry => entry.Id.EndsWith("/stairs")), Is.True);
    }

    [Test]
    public void Generation_IsDeterministicAndClearPreservesManualInfrastructure()
    {
        GameObject mapObject = Track(new GameObject("Test Map"));
        Map map = mapObject.AddComponent<Map>();
        GameObject manual = new("Manual Camera");
        manual.transform.SetParent(mapObject.transform, false);
        manual.AddComponent<Camera>();
        TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
            KayKitDungeonExampleTool.JsonPath);
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        map.ConfigureJson(source, catalog);

        Assert.That(map.TryGenerate(out MapSourceValidationResult firstResult), Is.True,
            string.Join(Environment.NewLine, firstResult.Errors));
        string[] first = Snapshot(mapObject.transform.Find("GeneratedMap"));
        Assert.That(map.TryGenerate(out MapSourceValidationResult secondResult), Is.True,
            string.Join(Environment.NewLine, secondResult.Errors));
        string[] second = Snapshot(mapObject.transform.Find("GeneratedMap"));

        Assert.That(second, Is.EqualTo(first));
        Assert.That(manual, Is.Not.Null);
        map.ClearGeneratedContent();
        Assert.That(mapObject.transform.Find("GeneratedMap"), Is.Null);
        Assert.That(manual, Is.Not.Null);
    }

    [Test]
    public void InvalidGeneration_DoesNotModifyExistingGeneratedContent()
    {
        GameObject mapObject = Track(new GameObject("Test Map"));
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        map.ConfigureJson(
            AssetDatabase.LoadAssetAtPath<TextAsset>(KayKitDungeonExampleTool.JsonPath),
            catalog);
        Assert.That(map.TryGenerate(out _), Is.True);
        Transform generated = mapObject.transform.Find("GeneratedMap");
        string[] before = Snapshot(generated);

        TextAsset invalid = Track(new TextAsset(@"{""version"":99,""rows"":["".""]}"));
        map.ConfigureJson(invalid, catalog);
        Assert.That(map.TryGenerate(out MapSourceValidationResult validation), Is.False);

        Assert.That(validation.Errors, Is.Not.Empty);
        Assert.That(mapObject.transform.Find("GeneratedMap"), Is.SameAs(generated));
        Assert.That(Snapshot(generated), Is.EqualTo(before));
    }

    [Test]
    public void Obstacle_IsNotWalkableAndDoesNotConnectStructuralWalls()
    {
        Assert.That(GridBase.IsWalkableTile(TileType.Ground), Is.True);
        Assert.That(GridBase.IsWalkableTile(TileType.Door), Is.True);
        Assert.That(GridBase.IsWalkableTile(TileType.Obstacle), Is.False);

        TileType[,] grid =
        {
            { TileType.Wall },
            { TileType.Obstacle }
        };
        WallResolution isolated = WallStructuralResolver.Resolve(Vector3Int.zero, grid);
        Assert.That(isolated.Variant, Is.EqualTo(WallVariant.Pillar));

        grid[1, 0] = TileType.Door;
        WallResolution connected = WallStructuralResolver.Resolve(Vector3Int.zero, grid);
        Assert.That(connected.Variant, Is.EqualTo(WallVariant.Endcap));
    }

    [Test]
    public void MovementOnlyObstacle_RemainsTransparentToLineOfSight()
    {
        Tile[,] tiles = { { new Tile() }, { null }, { new Tile() } };
        bool[,] blockers = { { false }, { false }, { false } };
        TileType[,] gridData =
        {
            { TileType.Ground },
            { TileType.Obstacle },
            { TileType.Ground }
        };
        GridLineOfSightData.Register(tiles, blockers, gridData);
        try
        {
            Assert.That(
                StrikeTargeting.CountClearRays(
                    tiles,
                    Vector3Int.zero,
                    new Vector3Int(2, 0, 0)),
                Is.EqualTo(16));
        }
        finally
        {
            GridLineOfSightData.Unregister(tiles);
        }
    }

    [Test]
    public void ColliderBackedBlocker_AffectsCurrentLineOfSightChecks()
    {
        Tile[,] tiles = { { new Tile() }, { new Tile() }, { new Tile() } };
        bool[,] blockers = { { false }, { false }, { false } };
        GridLineOfSightData.Register(tiles, blockers);
        GameObject blocker = Track(new GameObject("Line Of Sight Blocker"));
        blocker.transform.position = new Vector3(1f, 0f, 0f);
        BoxCollider collider = blocker.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.75f, 0f);
        collider.size = new Vector3(0.9f, 1.5f, 0.9f);
        blocker.AddComponent<MapLineOfSightBlocker>();
        Physics.SyncTransforms();
        try
        {
            Assert.That(
                StrikeTargeting.CountClearRays(
                    tiles,
                    Vector3Int.zero,
                    new Vector3Int(2, 0, 0)),
                Is.Zero);
        }
        finally
        {
            GridLineOfSightData.Unregister(tiles);
        }
    }

    [Test]
    public void NewMapComponents_DefaultToBitmapMode()
    {
        GameObject mapObject = Track(new GameObject("Bitmap Compatibility"));
        Map map = mapObject.AddComponent<Map>();

        Assert.That(map.SourceMode, Is.EqualTo(MapSourceMode.Bitmap));
    }

    private KayKitDungeonCatalog Catalog(params KayKitDungeonCatalogEntry[] entries)
    {
        KayKitDungeonCatalog catalog = Track(
            ScriptableObject.CreateInstance<KayKitDungeonCatalog>());
        catalog.ReplaceEntries(entries);
        return catalog;
    }

    private KayKitDungeonCatalogEntry Entry(
        string id,
        Vector2Int footprint,
        bool blocksMovement,
        bool blocksLineOfSight)
    {
        GameObject model = Track(new GameObject(id));
        return new KayKitDungeonCatalogEntry(
            id,
            model,
            null,
            footprint,
            0,
            0f,
            blocksMovement,
            blocksLineOfSight);
    }

    private T Track<T>(T target) where T : UnityEngine.Object
    {
        cleanup.Add(target);
        return target;
    }

    private static string[] Snapshot(Transform root)
    {
        List<string> values = new();
        Visit(root, root.name, values);
        return values.ToArray();
    }

    private static void Visit(Transform current, string path, ICollection<string> values)
    {
        Vector3 position = current.localPosition;
        Vector3 rotation = current.localEulerAngles;
        values.Add(
            $"{path}|{current.gameObject.activeSelf}|" +
            $"{position.x:F4},{position.y:F4},{position.z:F4}|" +
            $"{rotation.x:F4},{rotation.y:F4},{rotation.z:F4}");
        foreach (Transform child in current)
            Visit(child, path + "/" + child.name, values);
    }
}
