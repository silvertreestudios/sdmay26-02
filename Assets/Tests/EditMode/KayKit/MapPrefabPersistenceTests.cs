using System.Linq;
using System.Reflection;
using Game.KayKit;
using GridPrivate;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MapPrefabPersistenceTests
{
    private const string TempParent = "Assets/_agent-temp";
    private const string TempRoot = TempParent + "/issue-109-round-8-map-prefab-tests";
    private const string PrefabPath = TempRoot + "/Map.prefab";
    private const string ScenePath = TempRoot + "/MapPersistence.unity";
    private const string TexturePath = TempRoot + "/Bitmap.asset";

    private bool createdTempParent;

    [SetUp]
    public void SetUp()
    {
        createdTempParent = !AssetDatabase.IsValidFolder(TempParent);
        if (createdTempParent)
            AssetDatabase.CreateFolder("Assets", "_agent-temp");
        if (AssetDatabase.IsValidFolder(TempRoot))
            AssetDatabase.DeleteAsset(TempRoot);
        AssetDatabase.CreateFolder(TempParent, "issue-109-round-8-map-prefab-tests");
    }

    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        AssetDatabase.DeleteAsset(TempRoot);
        if (createdTempParent && AssetDatabase.IsValidFolder(TempParent))
            AssetDatabase.DeleteAsset(TempParent);
        AssetDatabase.Refresh();
    }

    [Test]
    public void PrefabInstance_SourceModeBookkeepingSurvivesSceneReload()
    {
        CreateBitmapMapPrefab(false);
        Scene scene = CreateSceneWithPrefabInstance();
        Map map = FindMap(scene);

        SerializedObject serialized = new(map);
        serialized.FindProperty("spacing").floatValue = 2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        serialized.Update();
        serialized.FindProperty("sourceMode").enumValueIndex = (int)MapSourceMode.Json;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        serialized.Update();
        serialized.FindProperty("spacing").floatValue = Map.JsonTileSpacing;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssertOverride(map, "legacyBitmapSpacing");
        AssertOverride(map, "previousSourceMode");
        SaveAndReload(ref scene);
        map = FindMap(scene);
        serialized = new SerializedObject(map);
        Assert.That(map.SourceMode, Is.EqualTo(MapSourceMode.Json));
        Assert.That(serialized.FindProperty("legacyBitmapSpacing").floatValue, Is.EqualTo(2f));
        Assert.That(
            serialized.FindProperty("previousSourceMode").enumValueIndex,
            Is.EqualTo((int)MapSourceMode.Json));

        serialized.FindProperty("sourceMode").enumValueIndex = (int)MapSourceMode.Bitmap;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(map.Spacing, Is.EqualTo(2f));
        AssertOverride(map, "previousSourceMode");

        SaveAndReload(ref scene);
        map = FindMap(scene);
        serialized = new SerializedObject(map);
        Assert.That(map.SourceMode, Is.EqualTo(MapSourceMode.Bitmap));
        Assert.That(map.Spacing, Is.EqualTo(2f));
        Assert.That(
            serialized.FindProperty("previousSourceMode").enumValueIndex,
            Is.EqualTo((int)MapSourceMode.Bitmap));
    }

    [Test]
    public void PrefabInstance_MigrationCompletionSurvivesReloadAndPreservesManualLookalike()
    {
        CreateBitmapMapPrefab(true);
        Scene scene = CreateSceneWithPrefabInstance();
        Map map = FindMap(scene);
        GameObject generated = new("GeneratedMap");
        generated.AddComponent<GeneratedMapRoot>();
        generated.transform.SetParent(map.transform, false);

        map.ClearGeneratedContent();

        Assert.That(generated == null, Is.True);
        AssertOverride(map, "legacyBitmapMigrationVersion");
        SaveAndReload(ref scene);
        map = FindMap(scene);
        Assert.That(MigrationVersion(map), Is.GreaterThan(0));

        Material legacyFloor = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dirt.mat");
        Vector3 expectedWorldPosition = Vector3.zero;
        GameObject manualLookalike = GameObject.CreatePrimitive(PrimitiveType.Quad);
        manualLookalike.name = "Quad";
        manualLookalike.GetComponent<MeshRenderer>().sharedMaterial = legacyFloor;
        manualLookalike.transform.SetParent(map.transform, true);
        manualLookalike.transform.position = expectedWorldPosition;

        Assert.That(manualLookalike.name, Is.EqualTo("Quad"));
        Assert.That(manualLookalike.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(legacyFloor));
        Assert.That(manualLookalike.transform.position, Is.EqualTo(expectedWorldPosition));

        map.ClearGeneratedContent();

        Assert.That(manualLookalike, Is.Not.Null);
        Assert.That(manualLookalike.transform.parent, Is.SameAs(map.transform));
        SaveAndReload(ref scene);
        map = FindMap(scene);
        Transform reloadedLookalike = map.transform.Find("Quad");
        Assert.That(reloadedLookalike, Is.Not.Null);
        Assert.That(reloadedLookalike.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(legacyFloor));
        Assert.That(reloadedLookalike.position, Is.EqualTo(expectedWorldPosition));
    }

    private static void AssertOverride(Map map, string propertyPath)
    {
        PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(map);
        Assert.That(
            modifications != null && modifications.Any(modification =>
                modification.propertyPath == propertyPath),
            Is.True,
            $"Expected prefab override for {propertyPath}.");
    }

    private static int MigrationVersion(Map map)
    {
        SerializedObject serialized = new(map);
        return serialized.FindProperty("legacyBitmapMigrationVersion").intValue;
    }

    private static Map FindMap(Scene scene)
    {
        Map map = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Map>(true))
            .Single();
        Assert.That(PrefabUtility.IsPartOfPrefabInstance(map), Is.True);
        return map;
    }

    private static void SaveAndReload(ref Scene scene)
    {
        int previousMapId = FindMap(scene).GetInstanceID();
        Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Assert.That(FindMap(scene).GetInstanceID(), Is.Not.EqualTo(previousMapId));
    }

    private static Scene CreateSceneWithPrefabInstance()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Assert.That(instance, Is.Not.Null);
        Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
        return scene;
    }

    private static void CreateBitmapMapPrefab(bool migrationPending)
    {
        Texture2D image = new(1, 1);
        image.SetPixel(0, 0, Color.red);
        image.Apply();
        AssetDatabase.CreateAsset(image, TexturePath);

        GameObject root = new("Map Prefab");
        try
        {
            Map map = root.AddComponent<Map>();
            FieldInfo settingsField = typeof(Map).GetField(
                "Settings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(settingsField, Is.Not.Null);
            settingsField.SetValue(map, new TileSettings());

            SerializedObject serialized = new(map);
            serialized.FindProperty("ImageMap").objectReferenceValue = image;
            SerializedProperty definitions = serialized.FindProperty("Settings.TileDefinitions");
            definitions.arraySize = 1;
            SerializedProperty definition = definitions.GetArrayElementAtIndex(0);
            definition.FindPropertyRelative("Color").colorValue = Color.red;
            definition.FindPropertyRelative("Tile").enumValueIndex = (int)TileType.Ground;
            definition.FindPropertyRelative("Floor").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dirt.mat");
            if (migrationPending)
                serialized.FindProperty("legacyBitmapMigrationVersion").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(PrefabUtility.SaveAsPrefabAsset(root, PrefabPath), Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
