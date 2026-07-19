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
        /// <summary>The default uniform scale used by one-cell floor tiles and props.</summary>
        public const float DungeonVisualScale = 0.50f;
        /// <summary>The uniform scale that fits structural wall modules to one grid unit.</summary>
        public const float WallVisualScale = 0.25f;
        /// <summary>The uniform scale used by generated stair visuals.</summary>
        public const float StairVisualScale = 0.25f;
        /// <summary>The uniform scale used by the generated wall-banner visual.</summary>
        public const float BannerVisualScale = 0.25f;
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
        /// <summary>The generated project-owned prefab used for a blocking closed door.</summary>
        public const string ClosedDoorPrefabPath = DungeonPrefabRoot + "/DungeonDoorClosed.prefab";
        /// <summary>The generated project-owned prefab used to mark a semantic stair endpoint.</summary>
        public const string StairPrefabPath = DungeonPrefabRoot + "/DungeonStair.prefab";
        public const string OpenDoorwayModelName = "wall_doorway_sides";

        private const float WallVisualHeight = 4f * WallVisualScale;

        private static readonly WrapperDescriptor ClosedDoorWrapper =
            new(
                "DungeonDoorClosed",
                "__generated_closed_door__",
                true,
                false,
                "wall_doorway",
                WallVisualScale);
        private static readonly WrapperDescriptor StairWrapper =
            new(
                "DungeonStair",
                "__generated_stair__",
                false,
                false,
                "stairs",
                StairVisualScale,
                -0.85f,
                Vector3.zero);
        private static readonly WrapperDescriptor BannerWrapper =
            new(
                "BannerRed",
                "banner_red",
                false,
                false,
                "banner_red",
                BannerVisualScale,
                0f,
                new Vector3(0f, -0.25f, -1f));
        private static readonly WrapperDescriptor TorchWrapper =
            new(
                "TorchMounted",
                "torch_mounted",
                false,
                false,
                "torch_mounted",
                DungeonVisualScale,
                0f,
                new Vector3(0f, 0.35f, -0.925f));

        private static readonly WrapperDescriptor[] Wrappers =
        {
            new("DungeonFloorSmall", "floor_tile_small", false, false),
            new("DungeonWallStraight", "wall", true, false, visualScale: WallVisualScale),
            new("DungeonWallCorner", "wall_corner", true, false, visualScale: WallVisualScale),
            new("DungeonWallTIntersection", "wall_Tsplit", true, false, visualScale: WallVisualScale),
            new("DungeonWallCrossing", "wall_crossing", true, false, visualScale: WallVisualScale),
            new("DungeonWallEndcap", "wall_endcap", true, false, visualScale: WallVisualScale),
            new("DungeonWallPillar", "wall_pillar", true, false, visualScale: WallVisualScale),
            new(
                "DungeonDoorwayOpen",
                "wall_doorway",
                false,
                true,
                OpenDoorwayModelName,
                WallVisualScale),
            ClosedDoorWrapper,
            StairWrapper,
            new("BarrelSmall", "barrel_small", false, false),
            new("Column", "column", false, false),
            new("CratesStacked", "crates_stacked", false, false),
            new("Chest", "chest", false, false),
            new("TableLong", "table_long", false, false),
            new("ShelfLarge", "shelf_large", false, false),
            new("RubbleHalf", "rubble_half", false, false),
            BannerWrapper,
            TorchWrapper
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

        /// <summary>
        /// Regenerates the blocking-door, stair, and wall-decoration wrappers used by generated floors.
        /// </summary>
        /// <param name="material">The existing generated dungeon material.</param>
        /// <remarks>
        /// Other authored-map prop wrappers are intentionally left untouched.
        /// </remarks>
        public static void RegenerateGeneratedFloorPrefabs(Material material)
        {
            EnsureFolder(DungeonPrefabRoot);
            CreateWrapper(ClosedDoorWrapper, material);
            CreateWrapper(StairWrapper, material);
            CreateWrapper(BannerWrapper, material);
            CreateWrapper(TorchWrapper, material);
        }

        /// <summary>Regenerates the uniformly scaled wall and doorway wrapper prefabs.</summary>
        /// <param name="material">The existing generated dungeon material.</param>
        public static void RegenerateWallPrefabs(Material material)
        {
            EnsureFolder(DungeonPrefabRoot);
            foreach (WrapperDescriptor descriptor in Wrappers.Where(
                         descriptor => descriptor.WallCollider || descriptor.OpenDoorway))
            {
                CreateWrapper(descriptor, material);
            }
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
            bool wrapperBlocksPlacement =
                wrapper != null &&
                wrapper.GetComponent<Collider>() != null &&
                wrapper.GetComponent<MapLineOfSightBlocker>() != null;
            bool blocksMovement = wrapperBlocksPlacement || IsOneOf(
                name,
                "barrel_small",
                "column",
                "crates_stacked",
                "chest",
                "table_long",
                "shelf_large");
            bool blocksLineOfSight = wrapperBlocksPlacement;

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

        /// <summary>Loads the generated blocking closed-door wrapper.</summary>
        /// <returns>The wrapper at <see cref="ClosedDoorPrefabPath"/>, or Unity's missing-object value.</returns>
        public static GameObject LoadClosedDoorPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(ClosedDoorPrefabPath);
        }

        /// <summary>Loads the generated nonblocking stair wrapper.</summary>
        /// <returns>The wrapper at <see cref="StairPrefabPath"/>, or Unity's missing-object value.</returns>
        public static GameObject LoadStairPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(StairPrefabPath);
        }

        private static void CreateWrapper(WrapperDescriptor descriptor, Material material)
        {
            GameObject model = LoadDungeonModel(descriptor.SourceModelName);
            GameObject root = new(descriptor.PrefabName);
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                if (!Mathf.Approximately(descriptor.ModelLocalZ, 0f))
                {
                    Vector3 sourcePosition = instance.transform.localPosition;
                    sourcePosition.z = descriptor.ModelLocalZ;
                    instance.transform.localPosition = sourcePosition;
                }
                instance.transform.localScale = Vector3.one * descriptor.VisualScale;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = Enumerable
                        .Repeat(material, renderer.sharedMaterials.Length)
                        .ToArray();
                }

                if (descriptor.WallCollider)
                {
                    BoxCollider collider = root.AddComponent<BoxCollider>();
                    collider.center = new Vector3(0f, WallVisualHeight * 0.5f, 0f);
                    collider.size = new Vector3(0.9f, WallVisualHeight, 0.25f);
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

                if (descriptor.PlacementOffset != Vector3.zero)
                {
                    DungeonPlacementOffset placementOffset =
                        root.AddComponent<DungeonPlacementOffset>();
                    placementOffset.Configure(descriptor.PlacementOffset);
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
            post.transform.localPosition = new Vector3(x, WallVisualHeight * 0.5f, 0f);
            BoxCollider collider = post.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.14f, WallVisualHeight, 0.28f);
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
            public string SourceModelName { get; }
            public bool WallCollider { get; }
            public bool OpenDoorway { get; }
            public float VisualScale { get; }
            public float ModelLocalZ { get; }
            public Vector3 PlacementOffset { get; }

            public WrapperDescriptor(
                string prefabName,
                string modelName,
                bool wallCollider,
                bool openDoorway,
                string sourceModelName = null,
                float visualScale = DungeonVisualScale)
                : this(
                    prefabName,
                    modelName,
                    wallCollider,
                    openDoorway,
                    sourceModelName,
                    visualScale,
                    0f,
                    Vector3.zero)
            {
            }

            public WrapperDescriptor(
                string prefabName,
                string modelName,
                bool wallCollider,
                bool openDoorway,
                string sourceModelName,
                float visualScale,
                float modelLocalZ,
                Vector3 placementOffset)
            {
                PrefabName = prefabName;
                ModelName = modelName;
                SourceModelName = sourceModelName ?? modelName;
                WallCollider = wallCollider;
                OpenDoorway = openDoorway;
                VisualScale = visualScale;
                ModelLocalZ = modelLocalZ;
                PlacementOffset = placementOffset;
            }
        }
    }
}
