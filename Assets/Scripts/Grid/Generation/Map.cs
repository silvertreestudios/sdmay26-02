using System;
using System.Collections.Generic;
using System.Linq;
using Game.KayKit;
using GridPrivate;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public enum MapSourceMode
{
    Bitmap,
    Json
}

public sealed class MapSourceValidationResult
{
    public IReadOnlyList<string> Errors { get; }
    public bool IsValid => Errors.Count == 0;
    public KayKitDungeonMapData JsonMap { get; }

    public MapSourceValidationResult(IEnumerable<string> errors, KayKitDungeonMapData jsonMap = null)
    {
        Errors = errors.ToArray();
        JsonMap = jsonMap;
    }
}

[ExecuteAlways]
public class Map : MonoBehaviour
{
    public const float JsonTileSpacing = 1f;

    [SerializeField] protected Texture2D ImageMap;
    [SerializeField] protected float spacing = 1f;
    [SerializeField] private TileSettings Settings;
    [SerializeField] private MapSourceMode sourceMode = MapSourceMode.Bitmap;
    [SerializeField] private TextAsset jsonSource;
    [SerializeField] private KayKitDungeonCatalog dungeonCatalog;

    protected TileType[,] GridData { get; set; }
    protected bool[,] LineOfSightBlocks { get; set; }

    public MapSourceMode SourceMode => sourceMode;
    public TextAsset JsonSource => jsonSource;
    public KayKitDungeonCatalog DungeonCatalog => dungeonCatalog;
    public float Spacing => spacing;

    private void OnValidate()
    {
#if UNITY_EDITOR
        InvalidateCache();
        if (PrefabUtility.IsPartOfPrefabAsset(this) || sourceMode != MapSourceMode.Bitmap)
            return;

        EditorApplication.delayCall += () =>
        {
            if (this != null && !Application.isPlaying)
                Generate();
        };
#endif
    }

    public void ConfigureJson(
        TextAsset source,
        KayKitDungeonCatalog catalog,
        float tileSpacing = JsonTileSpacing)
    {
        sourceMode = MapSourceMode.Json;
        jsonSource = source;
        dungeonCatalog = catalog;
        spacing = tileSpacing;
        InvalidateCache();
    }

    public MapSourceValidationResult ValidateSource()
    {
        return sourceMode == MapSourceMode.Json ? ValidateJson() : ValidateBitmap();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (TryGenerate(out MapSourceValidationResult validation))
            return;

        foreach (string error in validation.Errors)
            Debug.LogError($"Map generation failed: {error}", this);
    }

    public bool TryGenerate(out MapSourceValidationResult validation)
    {
        InvalidateCache();
        validation = ValidateSource();
        if (!validation.IsValid)
            return false;

        bool migrateLegacyBitmap = sourceMode == MapSourceMode.Bitmap &&
                                   GetComponentsInChildren<GeneratedMapRoot>(true).Length == 0 &&
                                   transform.childCount > 0;
        if (migrateLegacyBitmap)
            ClearLegacyBitmapGeneratedContent();
        else
            ClearGeneratedContent();

        GameObject generatedMap = new("GeneratedMap");
        generatedMap.AddComponent<GeneratedMapRoot>();
        generatedMap.transform.SetParent(transform, false);
        Transform structure = CreateContainer("Structure", generatedMap.transform);
        Transform objects = CreateContainer("Objects", generatedMap.transform);

        if (sourceMode == MapSourceMode.Json)
        {
            GridData = validation.JsonMap.GridData;
            LineOfSightBlocks = validation.JsonMap.LineOfSightBlocks;
            GenerateJson(validation.JsonMap, structure, objects);
        }
        else
        {
            Settings.ResetCache();
            PopulateBitmapData();
            GenerateBitmap(structure);
        }

        return true;
    }

    public TileType[,] GetMapData()
    {
        EnsureData();
        return GridData;
    }

    public bool[,] GetLineOfSightBlocks()
    {
        EnsureData();
        return LineOfSightBlocks;
    }

    [ContextMenu("Clear Generated Content")]
    public void ClearGeneratedContent()
    {
        GeneratedMapRoot[] roots = GetComponentsInChildren<GeneratedMapRoot>(true);
        foreach (GeneratedMapRoot root in roots)
        {
            if (root != null && root.transform.parent == transform)
                DestroyOwned(root.gameObject);
        }

        InvalidateCache();
    }

    public void ClearLegacyBitmapGeneratedContent()
    {
        if (sourceMode != MapSourceMode.Bitmap)
            throw new InvalidOperationException("Legacy generated content can only be cleared while the source is Bitmap.");

        while (transform.childCount > 0)
            DestroyOwned(transform.GetChild(0).gameObject);
        InvalidateCache();
    }

    private MapSourceValidationResult ValidateBitmap()
    {
        List<string> errors = new();
        if (ImageMap == null)
            errors.Add("Bitmap mode requires an Image Map texture.");
        if (Settings == null)
            errors.Add("Bitmap mode requires Tile Settings.");
        if (errors.Count > 0)
            return new MapSourceValidationResult(errors);

        try
        {
            Settings.ResetCache();
            HashSet<Color32> unknownColors = new();
            for (int x = 0; x < ImageMap.width; x++)
            {
                for (int z = 0; z < ImageMap.height; z++)
                {
                    Color32 pixel = ImageMap.GetPixel(x, z);
                    if (!Settings.TryGetTileInfo(pixel, out _))
                    {
                        pixel.a = 255;
                        unknownColors.Add(pixel);
                    }
                }
            }

            foreach (Color32 color in unknownColors.OrderBy(color => color.r)
                         .ThenBy(color => color.g).ThenBy(color => color.b))
            {
                errors.Add($"Bitmap contains undefined palette color RGB({color.r}, {color.g}, {color.b}).");
            }
        }
        catch (Exception exception)
        {
            errors.Add($"Bitmap tile settings are invalid: {exception.Message}");
        }

        return new MapSourceValidationResult(errors);
    }

    private MapSourceValidationResult ValidateJson()
    {
        if (jsonSource == null)
            return new MapSourceValidationResult(new[] { "JSON mode requires a TextAsset source." });

        KayKitDungeonMapParseResult parsed = KayKitDungeonMapParser.Parse(jsonSource.text, dungeonCatalog);
        List<string> errors = new(parsed.Errors);
        if (spacing != JsonTileSpacing)
        {
            errors.Add(
                $"JSON mode requires tile spacing {JsonTileSpacing}; found {spacing}. " +
                "Bitmap mode continues to support custom spacing.");
        }
        if (parsed.Map != null)
        {
            if (ContainsAny(parsed.Map.GridData, TileType.Ground, TileType.Door, TileType.Obstacle) &&
                dungeonCatalog.FloorPrefab == null)
            {
                errors.Add("KayKitDungeonCatalog is missing its project-owned floor wrapper prefab.");
            }
            if (ContainsAny(parsed.Map.GridData, TileType.Wall) && dungeonCatalog.WallPrefab == null)
                errors.Add("KayKitDungeonCatalog is missing its project-owned wall resolver prefab.");
            if (ContainsAny(parsed.Map.GridData, TileType.Door) && dungeonCatalog.DoorwayPrefab == null)
                errors.Add("KayKitDungeonCatalog is missing its project-owned open doorway prefab.");

            foreach (KayKitDungeonObjectPlacement placement in parsed.Map.Objects)
            {
                if (placement.CatalogEntry.PlacementPrefab == null)
                    errors.Add($"Catalog entry '{placement.AssetId}' has no model or wrapper prefab.");
            }
        }

        return new MapSourceValidationResult(errors, errors.Count == 0 ? parsed.Map : null);
    }

    private void EnsureData()
    {
        if (GridData != null && LineOfSightBlocks != null)
            return;

        if (sourceMode == MapSourceMode.Json)
        {
            MapSourceValidationResult validation = ValidateJson();
            if (!validation.IsValid)
            {
                foreach (string error in validation.Errors)
                    Debug.LogError($"Map data is invalid: {error}", this);
                return;
            }

            GridData = validation.JsonMap.GridData;
            LineOfSightBlocks = validation.JsonMap.LineOfSightBlocks;
            return;
        }

        if (ValidateBitmap().IsValid)
            PopulateBitmapData();
    }

    private void PopulateBitmapData()
    {
        GridData = new TileType[ImageMap.width, ImageMap.height];
        LineOfSightBlocks = new bool[ImageMap.width, ImageMap.height];
        for (int x = 0; x < ImageMap.width; x++)
        {
            for (int z = 0; z < ImageMap.height; z++)
            {
                TileType tile = Settings.GetTileType(ImageMap.GetPixel(x, z));
                GridData[x, z] = tile;
                LineOfSightBlocks[x, z] = tile != TileType.Ground && tile != TileType.Door;
            }
        }
    }

    private void GenerateBitmap(Transform structure)
    {
        for (int x = 0; x < ImageMap.width; x++)
        {
            for (int z = 0; z < ImageMap.height; z++)
            {
                Color pixel = ImageMap.GetPixel(x, z);
                var (_, prefab, floor) = Settings.GetTileInfo(pixel);
                Vector3 position = new(x * spacing, 0f, z * spacing);
                if (prefab != null)
                {
                    GameObject instance = InstantiatePrefab(prefab, structure);
                    instance.name = $"Structure_{x:D3}_{z:D3}_{prefab.name}";
                    instance.transform.SetPositionAndRotation(position, prefab.transform.rotation);
                    instance.GetComponent<IOnGridGeneration>()?.OnGeneration(
                        new Vector3Int(x, 0, z),
                        GridData);
                }

                if (floor != null)
                {
                    GameObject quad = Quad(position, structure);
                    quad.name = $"Floor_{x:D3}_{z:D3}";
                    quad.GetComponent<MeshRenderer>().sharedMaterial = floor;
                }
            }
        }
    }

    private void GenerateJson(
        KayKitDungeonMapData map,
        Transform structure,
        Transform objectContainer)
    {
        for (int z = 0; z < map.Height; z++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                TileType tile = map.GridData[x, z];
                Vector3 position = new(x * spacing, 0f, z * spacing);
                if (tile == TileType.Ground || tile == TileType.Door || tile == TileType.Obstacle)
                {
                    GameObject floor = InstantiatePrefab(dungeonCatalog.FloorPrefab, structure);
                    floor.name = $"Floor_{x:D3}_{z:D3}";
                    floor.transform.localPosition = position;
                }

                GameObject structuralPrefab = tile == TileType.Wall
                    ? dungeonCatalog.WallPrefab
                    : tile == TileType.Door
                        ? dungeonCatalog.DoorwayPrefab
                        : null;
                if (structuralPrefab == null)
                    continue;

                GameObject structural = InstantiatePrefab(structuralPrefab, structure);
                structural.name = $"{tile}_{x:D3}_{z:D3}";
                structural.transform.localPosition = position;
                structural.GetComponent<IOnGridGeneration>()?.OnGeneration(
                    new Vector3Int(x, 0, z),
                    map.GridData);
            }
        }

        for (int index = 0; index < map.Objects.Count; index++)
        {
            KayKitDungeonObjectPlacement placement = map.Objects[index];
            GameObject instance = InstantiatePrefab(placement.CatalogEntry.PlacementPrefab, objectContainer);
            instance.name = $"Object_{index:D3}_{StableName(placement.AssetId)}";
            float centerX = placement.X + (placement.Footprint.x - 1) * 0.5f;
            float centerZ = placement.Z + (placement.Footprint.y - 1) * 0.5f;
            instance.transform.localPosition =
                new Vector3(centerX * spacing, placement.YOffset, centerZ * spacing);
            instance.transform.localRotation = Quaternion.Euler(0f, placement.Rotation, 0f);
            ApplyDefaultMaterial(instance);

            if (placement.CatalogEntry.BlocksLineOfSight)
                AddLineOfSightCollider(instance, placement.CatalogEntry.Footprint);
        }
    }

    private void ApplyDefaultMaterial(GameObject instance)
    {
        if (dungeonCatalog.DefaultMaterial == null)
            return;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                if (materials[index] == null || materials[index].name == "Default-Material")
                    materials[index] = dungeonCatalog.DefaultMaterial;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private void AddLineOfSightCollider(GameObject instance, Vector2Int footprint)
    {
        BoxCollider collider = instance.GetComponent<BoxCollider>();
        if (collider == null)
            collider = instance.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 0.75f, 0f);
        collider.size = new Vector3(
            Mathf.Max(0.8f, footprint.x * spacing * 0.9f),
            1.5f,
            Mathf.Max(0.8f, footprint.y * spacing * 0.9f));
        collider.isTrigger = false;
        if (instance.GetComponent<MapLineOfSightBlocker>() == null)
            instance.AddComponent<MapLineOfSightBlocker>();
    }

    private static bool ContainsAny(TileType[,] grid, params TileType[] types)
    {
        HashSet<TileType> expected = new(types);
        foreach (TileType tile in grid)
        {
            if (expected.Contains(tile))
                return true;
        }
        return false;
    }

    private static Transform CreateContainer(string name, Transform parent)
    {
        GameObject container = new(name);
        container.transform.SetParent(parent, false);
        return container.transform;
    }

    private static string StableName(string assetId)
    {
        char[] value = assetId.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(value);
    }

    private static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
        return Instantiate(prefab, parent);
    }

    private static void DestroyOwned(GameObject target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(target);
            return;
        }
#endif
        Destroy(target);
    }

    private static GameObject Quad(Vector3 position, Transform parent)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(parent, false);
        quad.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
        return quad;
    }

    private void InvalidateCache()
    {
        GridData = null;
        LineOfSightBlocks = null;
    }
}
