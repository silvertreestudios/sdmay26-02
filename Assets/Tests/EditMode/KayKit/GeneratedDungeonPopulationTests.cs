using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using Game.KayKit;
using Game.KayKit.Editor;
using GridPrivate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GeneratedDungeonPopulationTests
{
    private readonly List<Object> cleanup = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object target in cleanup.Where(target => target != null).Reverse<Object>())
            Object.DestroyImmediate(target);
        cleanup.Clear();
    }

    [Test]
    public void RuntimeJson_RepopulationProducesStableOwnedHierarchyAndSemantics()
    {
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        Assert.That(catalog, Is.Not.Null);
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest
            {
                RunSeed = 156,
                Width = 39,
                Height = 39,
                MinimumRoomCount = 3
            });
        Assert.That(generated.IsSuccess, Is.True, Diagnostics(generated));
        string json = DungeonLevelJsonSerializer.Serialize(generated.Document);
        GameObject mapObject = Track(new GameObject("Runtime JSON Map"));
        Map map = mapObject.AddComponent<Map>();

        Assert.That(map.TryPopulateJson(json, catalog, out MapSourceValidationResult first), Is.True,
            string.Join(Environment.NewLine, first.Errors));
        string[] firstSnapshot = Snapshot(mapObject.transform.Find("GeneratedMap"));
        AssertGeneratedSemantics(mapObject, generated.Document);

        Assert.That(map.TryPopulateJson(json, catalog, out MapSourceValidationResult second), Is.True,
            string.Join(Environment.NewLine, second.Errors));

        Assert.That(map.UsesRuntimeJsonSource, Is.True);
        Assert.That(Snapshot(mapObject.transform.Find("GeneratedMap")), Is.EqualTo(firstSnapshot));
        AssertGeneratedSemantics(mapObject, generated.Document);
    }

    [Test]
    public void InvalidRuntimeJson_PreservesPriorOwnedHierarchy()
    {
        KayKitDungeonCatalog catalog = AssetDatabase.LoadAssetAtPath<KayKitDungeonCatalog>(
            KayKitSetupTool.DungeonCatalogPath);
        DungeonGenerationResult generated = new DeterministicDungeonGenerator().Generate(
            new DungeonGenerationRequest { RunSeed = 156, Width = 39, Height = 39 });
        string json = DungeonLevelJsonSerializer.Serialize(generated.Document);
        GameObject mapObject = Track(new GameObject("Runtime JSON Map"));
        Map map = mapObject.AddComponent<Map>();
        Assert.That(map.TryPopulateJson(json, catalog, out _), Is.True);
        Transform owned = mapObject.transform.Find("GeneratedMap");
        string[] before = Snapshot(owned);

        Assert.That(map.TryPopulateJson("not json", catalog, out MapSourceValidationResult invalid), Is.False);

        Assert.That(invalid.Errors, Is.Not.Empty);
        Assert.That(mapObject.transform.Find("GeneratedMap"), Is.SameAs(owned));
        Assert.That(Snapshot(owned), Is.EqualTo(before));
    }

    private static void AssertGeneratedSemantics(GameObject mapObject, DungeonLevelDocument document)
    {
        DungeonDoorController[] doors = mapObject.GetComponentsInChildren<DungeonDoorController>(true);
        DungeonStairMarker[] stairs = mapObject.GetComponentsInChildren<DungeonStairMarker>(true);
        Assert.That(doors.Select(door => door.StableId),
            Is.EquivalentTo(document.Doors.Select(door => door.Id)));
        Assert.That(doors.All(door => !door.IsOpen), Is.True);
        Assert.That(stairs.Select(stair => stair.StableId),
            Is.EquivalentTo(document.Stairs.Select(stair => stair.Id)));
        foreach (DungeonStair expected in document.Stairs)
        {
            DungeonStairMarker actual = stairs.Single(stair => stair.StableId == expected.Id);
            Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
            Assert.That(actual.Cell, Is.EqualTo(expected.Cell));
            Assert.That(actual.ArrivalCell, Is.EqualTo(expected.ArrivalCell));
        }
    }

    private static string[] Snapshot(Transform root)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .Select(transform => string.Join("|",
                RelativePath(root, transform),
                transform.gameObject.activeSelf,
                FormatVector3(transform.position),
                FormatVector3(transform.rotation.eulerAngles),
                FormatVector3(transform.lossyScale)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatVector3(Vector3 value)
    {
        return FormattableString.Invariant($"({value.x:F4}, {value.y:F4}, {value.z:F4})");
    }

    private static string RelativePath(Transform root, Transform current)
    {
        if (current == root)
            return root.name;
        Stack<string> names = new();
        while (current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return root.name + "/" + string.Join("/", names);
    }

    private static string Diagnostics(DungeonGenerationResult result)
    {
        return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    private T Track<T>(T target) where T : Object
    {
        cleanup.Add(target);
        return target;
    }
}
