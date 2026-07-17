using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    [TestCase(@"{""version"":9223372036854775808,""rows"":["".""]}", "version must equal 1")]
    [TestCase(@"{""version"":1,""rows"":["".""],""objects"":[{""assetId"":""prop"",""x"":9223372036854775808,""z"":0}]}", "integer x and z")]
    [TestCase(@"{""version"":1,""rows"":["".""],""objects"":[{""assetId"":""prop"",""x"":0,""z"":0,""rotation"":9223372036854775808}]}", "rotation must be")]
    [TestCase(@"{""version"":1,""rows"":["".""],""objects"":[{""assetId"":""prop"",""x"":0,""z"":0,""yOffset"":9223372036854775808}]}", "yOffset must be a finite number")]
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

    [Test]
    public void RotatedLineOfSightCollider_UsesUnrotatedLocalFootprint()
    {
        KayKitDungeonCatalog projectCatalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        GameObject model = projectCatalog.Entries.First(entry => entry.Model != null).Model;
        KayKitDungeonCatalog catalog = Catalog(new KayKitDungeonCatalogEntry(
            "non-square",
            model,
            null,
            new Vector2Int(2, 1),
            0,
            0f,
            false,
            true));
        catalog.ConfigureStructure(
            projectCatalog.DefaultMaterial,
            projectCatalog.FloorPrefab,
            projectCatalog.WallPrefab,
            projectCatalog.DoorwayPrefab);
        GameObject mapObject = Track(new GameObject("Collider Map"));
        Map map = mapObject.AddComponent<Map>();
        map.ConfigureJson(
            Track(new TextAsset(
                @"{""version"":1,""rows"":[""...."",""....""],""objects"":[{""assetId"":""non-square"",""x"":1,""z"":0,""rotation"":90}]}")),
            catalog);

        Assert.That(map.TryGenerate(out MapSourceValidationResult validation), Is.True,
            string.Join(Environment.NewLine, validation.Errors));
        Transform instance = mapObject.transform.Find("GeneratedMap/Objects/Object_000_non_square");
        Assert.That(instance, Is.Not.Null);
        BoxCollider collider = instance.GetComponent<BoxCollider>();

        Assert.That(instance.localEulerAngles.y, Is.EqualTo(90f).Within(0.01f));
        Assert.That(collider, Is.Not.Null);
        Assert.That(collider.size.x, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(collider.size.z, Is.EqualTo(0.9f).Within(0.001f));
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
    public void CatalogLookup_TracksInspectorAddRenameAndRemoveEdits()
    {
        KayKitDungeonCatalog catalog = Catalog(Entry("original", Vector2Int.one, false, false));
        Assert.That(catalog.TryGet("original", out _), Is.True);

        SerializedObject serialized = new(catalog);
        SerializedProperty entries = serialized.FindProperty("entries");
        entries.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue = "renamed";
        serialized.ApplyModifiedProperties();

        Assert.That(catalog.TryGet("original", out _), Is.False);
        Assert.That(catalog.TryGet("renamed", out _), Is.True);

        serialized.Update();
        entries = serialized.FindProperty("entries");
        entries.InsertArrayElementAtIndex(1);
        entries.GetArrayElementAtIndex(1).FindPropertyRelative("id").stringValue = "added";
        serialized.ApplyModifiedProperties();

        Assert.That(catalog.TryGet("added", out _), Is.True);

        serialized.Update();
        entries = serialized.FindProperty("entries");
        entries.DeleteArrayElementAtIndex(0);
        serialized.ApplyModifiedProperties();

        Assert.That(catalog.TryGet("renamed", out _), Is.False);
        Assert.That(catalog.TryGet("added", out _), Is.True);
    }

    [Test]
    public void JsonMode_RejectsNonUnitSpacingWithoutMutatingGeneratedContent()
    {
        GameObject mapObject = Track(new GameObject("JSON Spacing Contract"));
        Map map = mapObject.AddComponent<Map>();
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
            KayKitDungeonExampleTool.JsonPath);
        map.ConfigureJson(source, catalog);
        Assert.That(map.TryGenerate(out _), Is.True);
        Transform generated = mapObject.transform.Find("GeneratedMap");
        string[] before = Snapshot(generated);

        map.ConfigureJson(source, catalog, 2f);
        Assert.That(map.TryGenerate(out MapSourceValidationResult validation), Is.False);

        Assert.That(validation.Errors, Has.Some.Contains("requires tile spacing 1"));
        Assert.That(mapObject.transform.Find("GeneratedMap"), Is.SameAs(generated));
        Assert.That(Snapshot(generated), Is.EqualTo(before));
    }

    [Test]
    public void BitmapMode_ContinuesToAllowCustomSpacing()
    {
        GameObject mapObject = Track(new GameObject("Bitmap Spacing Compatibility"));
        Map map = mapObject.AddComponent<Map>();
        Texture2D image = Track(new Texture2D(1, 1));
        image.SetPixel(0, 0, Color.red);
        image.Apply();

        FieldInfo settingsField = typeof(Map).GetField(
            "Settings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(settingsField, Is.Not.Null);
        settingsField.SetValue(map, new TileSettings());
        SerializedObject serialized = new(map);
        serialized.FindProperty("ImageMap").objectReferenceValue = image;
        serialized.FindProperty("spacing").floatValue = 2f;
        SerializedProperty definitions = serialized.FindProperty("Settings.TileDefinitions");
        definitions.arraySize = 1;
        SerializedProperty definition = definitions.GetArrayElementAtIndex(0);
        definition.FindPropertyRelative("Color").colorValue = Color.red;
        definition.FindPropertyRelative("Tile").enumValueIndex = (int)TileType.Ground;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        MapSourceValidationResult validation = map.ValidateSource();

        Assert.That(validation.IsValid, Is.True, string.Join(Environment.NewLine, validation.Errors));
        Assert.That(map.Spacing, Is.EqualTo(2f));
    }

    [Test]
    public void BitmapGeneration_PreservesWorldGridUnderTransformedMapRoot()
    {
        GameObject mapObject = Track(new GameObject("Transformed Bitmap Map"));
        mapObject.transform.SetPositionAndRotation(
            new Vector3(14.3f, 2f, 11.5f),
            Quaternion.Euler(0f, 37f, 0f));
        mapObject.transform.localScale = new Vector3(1.5f, 2f, 0.75f);
        Map map = mapObject.AddComponent<Map>();
        Texture2D image = Track(new Texture2D(2, 1));
        image.SetPixels(new[] { Color.red, Color.red });
        image.Apply();

        FieldInfo settingsField = typeof(Map).GetField(
            "Settings",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(settingsField, Is.Not.Null);
        settingsField.SetValue(map, new TileSettings());
        SerializedObject serialized = new(map);
        serialized.FindProperty("ImageMap").objectReferenceValue = image;
        serialized.FindProperty("spacing").floatValue = 2f;
        SerializedProperty definitions = serialized.FindProperty("Settings.TileDefinitions");
        definitions.arraySize = 1;
        SerializedProperty definition = definitions.GetArrayElementAtIndex(0);
        definition.FindPropertyRelative("Color").colorValue = Color.red;
        definition.FindPropertyRelative("Tile").enumValueIndex = (int)TileType.Wall;
        definition.FindPropertyRelative("Prefab").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/MapPieces/Walls/Bricks/Wall_Brick.prefab");
        definition.FindPropertyRelative("Floor").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dirt.mat");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(map.TryGenerate(out MapSourceValidationResult firstResult), Is.True,
            string.Join(Environment.NewLine, firstResult.Errors));
        Transform generated = mapObject.transform.Find("GeneratedMap");
        Transform structure = generated.Find("Structure");
        Transform wall = structure.Find("Structure_001_000_Wall_Brick");
        Transform floor = structure.Find("Floor_001_000");

        Assert.That(generated.GetComponent<GeneratedMapRoot>(), Is.Not.Null);
        AssertWorldPosition(wall, new Vector3(2f, 0f, 0f));
        AssertWorldPosition(floor, new Vector3(2f, 0f, 0f));
        string[] first = Snapshot(generated);

        GameObject manual = new("Manual Infrastructure");
        manual.transform.SetParent(mapObject.transform, false);
        Assert.That(map.TryGenerate(out MapSourceValidationResult secondResult), Is.True,
            string.Join(Environment.NewLine, secondResult.Errors));

        generated = mapObject.transform.Find("GeneratedMap");
        Assert.That(Snapshot(generated), Is.EqualTo(first));
        AssertWorldPosition(
            generated.Find("Structure/Structure_001_000_Wall_Brick"),
            new Vector3(2f, 0f, 0f));
        Assert.That(manual, Is.Not.Null);

        map.ClearGeneratedContent();
        Assert.That(mapObject.transform.Find("GeneratedMap"), Is.Null);
        Assert.That(manual, Is.Not.Null);
    }

    [Test]
    public void RegenerationSceneGuard_SkipsPromptWhenNoSceneIsDirty()
    {
        bool result = ConfirmSceneTransition(false, () =>
            throw new AssertionException("The save prompt must not run for clean scenes."));

        Assert.That(result, Is.True);
    }

    [Test]
    public void RegenerationSceneGuard_AbortsWhenDirtySceneSaveIsCancelled()
    {
        int prompts = 0;

        bool result = ConfirmSceneTransition(true, () =>
        {
            prompts++;
            return false;
        });

        Assert.That(result, Is.False);
        Assert.That(prompts, Is.EqualTo(1));
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
            Assert.That(
                GridTargeting.CountClearRays(
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
    public void MovementOnlyObstacles_DoNotBlockSharedDiagonalCornerCheck()
    {
        Tile[,] tiles =
        {
            { new Tile(), null },
            { null, new Tile() }
        };
        bool[,] blockers =
        {
            { false, false },
            { false, false }
        };
        TileType[,] gridData =
        {
            { TileType.Ground, TileType.Obstacle },
            { TileType.Obstacle, TileType.Ground }
        };
        GridLineOfSightData.Register(tiles, blockers, gridData);
        try
        {
            Assert.That(
                GridTargeting.BlocksDiagonalCorner(
                    tiles,
                    Vector3Int.zero,
                    new Vector3Int(1, 0, 1)),
                Is.False);
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

    private static bool ConfirmSceneTransition(bool hasDirtyScenes, Func<bool> savePrompt)
    {
        MethodInfo method = typeof(KayKitDungeonExampleTool).GetMethod(
            "ConfirmSceneTransition",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(null, new object[] { hasDirtyScenes, savePrompt });
    }

    private static void AssertWorldPosition(Transform target, Vector3 expected)
    {
        Assert.That(target, Is.Not.Null);
        Assert.That(target.position.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(target.position.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(target.position.z, Is.EqualTo(expected.z).Within(0.0001f));
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
