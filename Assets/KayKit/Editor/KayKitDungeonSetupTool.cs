using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.KayKit;
using GridPrivate;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    public static class KayKitDungeonSetupTool
    {
        public const string DungeonPrefabRoot = KayKitSetupTool.PrefabRoot + "/Dungeon";
        public const string FloorPrefabPath = DungeonPrefabRoot + "/DungeonFloorSmall.prefab";
        public const string WallStraightPrefabPath = DungeonPrefabRoot + "/DungeonWallStraight.prefab";
        public const string WallCornerPrefabPath = DungeonPrefabRoot + "/DungeonWallCorner.prefab";
        public const string WallTPrefabPath = DungeonPrefabRoot + "/DungeonWallTIntersection.prefab";
        public const string WallCrossPrefabPath = DungeonPrefabRoot + "/DungeonWallCrossing.prefab";
        public const string WallEndcapPrefabPath = DungeonPrefabRoot + "/DungeonWallEndcap.prefab";
        public const string WallPillarPrefabPath = DungeonPrefabRoot + "/DungeonWallPillar.prefab";
        public const string WallResolverPrefabPath = DungeonPrefabRoot + "/DungeonWallResolver.prefab";
        public const string DoorwayPrefabPath = DungeonPrefabRoot + "/DungeonDoorwayOpen.prefab";

        private static readonly WrapperDescriptor[] Wrappers =
        {
            new("DungeonFloorSmall", "floor_tile_small", false, false),
            new("DungeonWallStraight", "wall", true, false),
            new("DungeonWallCorner", "wall_corner", true, false),
            new("DungeonWallTIntersection", "wall_Tsplit", true, false),
            new("DungeonWallCrossing", "wall_crossing", true, false),
            new("DungeonWallEndcap", "wall_endcap", true, false),
            new("DungeonWallPillar", "wall_pillar", true, false),
            new("DungeonDoorwayOpen", "wall_doorway", false, true),
            new("BarrelSmall", "barrel_small", false, false),
            new("Column", "column", false, false),
            new("CratesStacked", "crates_stacked", false, false),
            new("Chest", "chest", false, false),
            new("TableLong", "table_long", false, false),
            new("ShelfLarge", "shelf_large", false, false),
            new("RubbleHalf", "rubble_half", false, false),
            new("BannerRed", "banner_red", false, false),
            new("TorchMounted", "torch_mounted", false, false)
        };

        private static readonly Dictionary<string, string> WrapperPathByModel =
            Wrappers.ToDictionary(
                descriptor => descriptor.ModelName,
                descriptor => $"{DungeonPrefabRoot}/{descriptor.PrefabName}.prefab",
                StringComparer.OrdinalIgnoreCase);

        public static void RegeneratePrefabs(Material material)
        {
            EnsureFolder(DungeonPrefabRoot);
            foreach (WrapperDescriptor descriptor in Wrappers)
                CreateWrapper(descriptor, material);
            CreateWallResolver();
        }

        public static KayKitDungeonCatalogEntry CreateCatalogEntry(string id, GameObject model)
        {
            string name = model.name;
            GameObject wrapper = null;
            if (WrapperPathByModel.TryGetValue(name, out string wrapperPath))
                wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);

            Vector2Int footprint = string.Equals(name, "table_long", StringComparison.OrdinalIgnoreCase)
                ? new Vector2Int(2, 1)
                : Vector2Int.one;
            bool blocksMovement = IsOneOf(
                name,
                "barrel_small",
                "column",
                "crates_stacked",
                "chest",
                "table_long",
                "shelf_large");
            bool blocksLineOfSight = IsOneOf(
                name,
                "barrel_small",
                "column",
                "crates_stacked",
                "shelf_large");

            return new KayKitDungeonCatalogEntry(
                id,
                model,
                wrapper,
                footprint,
                0,
                0f,
                blocksMovement,
                blocksLineOfSight);
        }

        public static GameObject LoadFloorPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(FloorPrefabPath);
        }

        public static GameObject LoadWallResolverPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(WallResolverPrefabPath);
        }

        public static GameObject LoadDoorwayPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(DoorwayPrefabPath);
        }

        private static void CreateWrapper(WrapperDescriptor descriptor, Material material)
        {
            GameObject model = LoadDungeonModel(descriptor.ModelName);
            GameObject root = new(descriptor.PrefabName);
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable
                        .Repeat(material, renderer.sharedMaterials.Length)
                        .ToArray();
                }

                if (descriptor.WallCollider)
                {
                    BoxCollider collider = root.AddComponent<BoxCollider>();
                    collider.center = new Vector3(0f, 1f, 0f);
                    collider.size = new Vector3(0.9f, 2f, 0.25f);
                    root.AddComponent<MapLineOfSightBlocker>();
                }

                if (descriptor.OpenDoorway)
                {
                    root.AddComponent<OpenDoorway>();
                    AddDoorPost(root.transform, "LeftDoorPostCollider", -0.43f);
                    AddDoorPost(root.transform, "RightDoorPostCollider", 0.43f);
                }

                if (string.Equals(descriptor.ModelName, "torch_mounted", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject lightObject = new("TorchLight");
                    lightObject.transform.SetParent(root.transform, false);
                    lightObject.transform.localPosition = new Vector3(0f, 1.5f, 0.2f);
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 4f;
                    light.intensity = 1.2f;
                    light.color = new Color(1f, 0.55f, 0.2f);
                }

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    $"{DungeonPrefabRoot}/{descriptor.PrefabName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateWallResolver()
        {
            if (HasValidWallResolver())
                return;

            GameObject root = new("DungeonWallResolver");
            try
            {
                Transform straight = InstantiateNested(WallStraightPrefabPath, root.transform);
                Transform endcap = InstantiateNested(WallEndcapPrefabPath, root.transform);
                Transform corner = InstantiateNested(WallCornerPrefabPath, root.transform);
                Transform crossing = InstantiateNested(WallCrossPrefabPath, root.transform);
                Transform pillar = InstantiateNested(WallPillarPrefabPath, root.transform);
                Transform tIntersection = InstantiateNested(WallTPrefabPath, root.transform);

                Wall resolver = root.AddComponent<Wall>();
                SerializedObject serialized = new(resolver);
                serialized.FindProperty("wall").objectReferenceValue = straight;
                serialized.FindProperty("cap").objectReferenceValue = endcap;
                serialized.FindProperty("corner").objectReferenceValue = corner;
                serialized.FindProperty("crossIntersection").objectReferenceValue = crossing;
                serialized.FindProperty("pillar").objectReferenceValue = pillar;
                serialized.FindProperty("tIntersection").objectReferenceValue = tIntersection;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                foreach (Transform child in root.transform)
                    child.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, WallResolverPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static bool HasValidWallResolver()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallResolverPrefabPath);
            Wall resolver = prefab == null ? null : prefab.GetComponent<Wall>();
            if (resolver == null || prefab.transform.childCount != 6)
                return false;

            SerializedObject serialized = new(resolver);
            string[] fields =
            {
                "wall",
                "cap",
                "corner",
                "crossIntersection",
                "pillar",
                "tIntersection"
            };
            foreach (string field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null || property.objectReferenceValue == null)
                    return false;
            }

            foreach (Transform child in prefab.transform)
            {
                if (child.gameObject.activeSelf)
                    return false;
            }

            return true;
        }

        private static Transform InstantiateNested(string path, Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException($"Missing generated Dungeon wrapper: {path}");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, false);
            instance.name = prefab.name;
            return instance.transform;
        }

        private static GameObject LoadDungeonModel(string modelName)
        {
            string path = AssetDatabase.FindAssets(string.Empty, new[] { KayKitPathUtility.DungeonRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(candidate =>
                    string.Equals(Path.GetExtension(candidate), ".fbx", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        Path.GetFileNameWithoutExtension(candidate),
                        modelName,
                        StringComparison.OrdinalIgnoreCase));
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
                throw new InvalidOperationException($"Missing Dungeon model '{modelName}'.");
            return model;
        }

        private static void AddDoorPost(Transform parent, string name, float x)
        {
            GameObject post = new(name);
            post.transform.SetParent(parent, false);
            post.transform.localPosition = new Vector3(x, 1f, 0f);
            BoxCollider collider = post.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.14f, 2f, 0.28f);
        }

        private static bool IsOneOf(string value, params string[] candidates)
        {
            return candidates.Any(candidate =>
                string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace((char)92, '/');
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private sealed class WrapperDescriptor
        {
            public string PrefabName { get; }
            public string ModelName { get; }
            public bool WallCollider { get; }
            public bool OpenDoorway { get; }

            public WrapperDescriptor(
                string prefabName,
                string modelName,
                bool wallCollider,
                bool openDoorway)
            {
                PrefabName = prefabName;
                ModelName = modelName;
                WallCollider = wallCollider;
                OpenDoorway = openDoorway;
            }
        }
    }
}
