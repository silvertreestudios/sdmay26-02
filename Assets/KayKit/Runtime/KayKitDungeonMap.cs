using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.DungeonGeneration;
using GridPrivate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.KayKit
{
    public sealed class KayKitDungeonObjectPlacement
    {
        public string AssetId { get; }
        public int X { get; }
        public int Z { get; }
        public float YOffset { get; }
        public int Rotation { get; }
        public Vector2Int Footprint { get; }
        public KayKitDungeonCatalogEntry CatalogEntry { get; }

        public KayKitDungeonObjectPlacement(
            string assetId,
            int x,
            int z,
            float yOffset,
            int rotation,
            Vector2Int footprint,
            KayKitDungeonCatalogEntry catalogEntry)
        {
            AssetId = assetId;
            X = x;
            Z = z;
            YOffset = yOffset;
            Rotation = rotation;
            Footprint = footprint;
            CatalogEntry = catalogEntry;
        }
    }

    public sealed class KayKitDungeonMapData
    {
        /// <summary>The original KayKit JSON version retained for v1 callers and fixtures.</summary>
        public const int SupportedVersion = 1;

        /// <summary>Gets an immutable ordered collection of every JSON version accepted by <see cref="KayKitDungeonMapParser"/>.</summary>
        public static IReadOnlyList<int> SupportedVersions { get; } =
            Array.AsReadOnly(new[] { SupportedVersion, DungeonLevelDocument.CurrentVersion });

        public int Version { get; }
        public int Width => GridData.GetLength(0);
        public int Height => GridData.GetLength(1);
        public TileType[,] GridData { get; }
        public bool[,] LineOfSightBlocks { get; }
        public IReadOnlyList<KayKitDungeonObjectPlacement> Objects { get; }
        /// <summary>Gets lossless version 2 data, or absence for a version 1 source.</summary>
        public DungeonLevelDocument LevelDocument { get; }

        /// <summary>Creates legacy map data without a version 2 document.</summary>
        /// <param name="version">The parsed source version.</param>
        /// <param name="gridData">The required projected tile grid.</param>
        /// <param name="lineOfSightBlocks">The required line-of-sight overlay matching the grid dimensions.</param>
        /// <param name="objects">The deterministic projected object placements.</param>
        public KayKitDungeonMapData(
            int version,
            TileType[,] gridData,
            bool[,] lineOfSightBlocks,
            IReadOnlyList<KayKitDungeonObjectPlacement> objects)
            : this(version, gridData, lineOfSightBlocks, objects, null)
        {
        }

        /// <summary>Creates map data while retaining the complete lossless version 2 source document.</summary>
        /// <param name="version">The parsed source version.</param>
        /// <param name="gridData">The required projected tile grid.</param>
        /// <param name="lineOfSightBlocks">The required line-of-sight overlay matching the grid dimensions.</param>
        /// <param name="objects">The deterministic projected object placements.</param>
        /// <param name="levelDocument">The complete version 2 document, or absence for a version 1 source.</param>
        public KayKitDungeonMapData(
            int version,
            TileType[,] gridData,
            bool[,] lineOfSightBlocks,
            IReadOnlyList<KayKitDungeonObjectPlacement> objects,
            DungeonLevelDocument levelDocument)
        {
            Version = version;
            GridData = gridData;
            LineOfSightBlocks = lineOfSightBlocks;
            Objects = objects;
            LevelDocument = levelDocument;
        }
    }

    public sealed class KayKitDungeonMapParseResult
    {
        public KayKitDungeonMapData Map { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Map != null && Errors.Count == 0;

        public KayKitDungeonMapParseResult(KayKitDungeonMapData map, IEnumerable<string> errors)
        {
            Map = map;
            Errors = errors.ToArray();
        }
    }

    public static class KayKitDungeonMapParser
    {
        private static readonly HashSet<int> ValidRotations = new() { 0, 90, 180, 270 };

        public static KayKitDungeonMapParseResult Parse(string json, KayKitDungeonCatalog catalog)
        {
            List<string> errors = new();
            if (string.IsNullOrWhiteSpace(json))
                return Invalid("JSON map source is empty.");
            if (catalog == null)
                return Invalid("JSON map source requires a KayKitDungeonCatalog.");
            if (catalog.DuplicateIds.Count > 0)
            {
                return new KayKitDungeonMapParseResult(
                    null,
                    catalog.DuplicateIds.Select(id =>
                        $"KayKit dungeon catalog contains duplicate id '{id}'."));
            }

            JObject root;
            DungeonLevelDocument levelDocument = null;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                return Invalid($"JSON map could not be parsed: {exception.Message}");
            }

            int? sourceVersion = ReadInteger(root["version"]);
            if (sourceVersion == DungeonLevelDocument.CurrentVersion)
            {
                DungeonLevelParseResult parsedV2 = DungeonLevelJsonParser.Parse(json);
                if (!parsedV2.IsSuccess)
                {
                    return new KayKitDungeonMapParseResult(
                        null,
                        parsedV2.Diagnostics.Select(diagnostic =>
                            $"JSON map v2 {diagnostic.Field}: {diagnostic.Message}"));
                }

                levelDocument = parsedV2.Document;
                root = RuntimeProjection(levelDocument);
            }

            int? version = ReadInteger(root["version"]);
            if (!version.HasValue || !KayKitDungeonMapData.SupportedVersions.Contains(version.Value))
            {
                string value = version?.ToString(CultureInfo.InvariantCulture) ?? "missing or non-integer";
                errors.Add($"JSON map version must be one of 1 or 2; found {value}.");
            }

            JArray rowsToken = root["rows"] as JArray;
            if (rowsToken == null || rowsToken.Count == 0)
                errors.Add("JSON map rows must be a non-empty array of strings.");

            List<string> rows = new();
            if (rowsToken != null)
            {
                for (int rowIndex = 0; rowIndex < rowsToken.Count; rowIndex++)
                {
                    if (rowsToken[rowIndex]?.Type != JTokenType.String)
                    {
                        errors.Add($"JSON map row {rowIndex} must be a string.");
                        continue;
                    }

                    rows.Add(rowsToken[rowIndex].Value<string>());
                }
            }

            int width = rows.Count > 0 ? rows[0].Length : 0;
            if (width == 0 && rows.Count > 0)
                errors.Add("JSON map rows must not be empty.");
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                if (rows[rowIndex].Length != width)
                {
                    errors.Add(
                        $"JSON map row {rowIndex} has width {rows[rowIndex].Length}; expected {width}.");
                }
            }

            TileType[,] grid = rows.Count > 0 && width > 0 ? new TileType[width, rows.Count] : null;
            bool[,] lineOfSightBlocks = grid == null ? null : new bool[width, rows.Count];
            if (grid != null)
                ParseRows(rows, width, grid, lineOfSightBlocks, errors);

            List<KayKitDungeonObjectPlacement> placements = new();
            JToken objectsToken = root["objects"];
            if (objectsToken != null && objectsToken.Type != JTokenType.Array)
                errors.Add("JSON map objects must be an array when provided.");
            else if (objectsToken is JArray objects)
                ParseObjects(objects, catalog, grid, lineOfSightBlocks, placements, errors);

            if (errors.Count > 0)
                return new KayKitDungeonMapParseResult(null, errors);

            IReadOnlyList<KayKitDungeonObjectPlacement> deterministicPlacements = placements
                .OrderBy(placement => placement.Z)
                .ThenBy(placement => placement.X)
                .ThenBy(placement => placement.AssetId, StringComparer.Ordinal)
                .ThenBy(placement => placement.Rotation)
                .ToArray();
            return new KayKitDungeonMapParseResult(
                new KayKitDungeonMapData(version.Value, grid, lineOfSightBlocks, deterministicPlacements, levelDocument),
                Array.Empty<string>());
        }

        private static JObject RuntimeProjection(DungeonLevelDocument document)
        {
            JArray objects = new();
            foreach (DungeonObjectPlacement placement in document.Objects)
            {
                objects.Add(new JObject
                {
                    ["assetId"] = placement.AssetId,
                    ["x"] = placement.Cell.X,
                    ["z"] = placement.Cell.Z,
                    ["rotation"] = placement.Rotation
                });
            }

            return new JObject
            {
                ["version"] = DungeonLevelDocument.CurrentVersion,
                ["rows"] = new JArray(document.Rows),
                ["objects"] = objects
            };
        }

        private static void ParseRows(
            IReadOnlyList<string> rows,
            int width,
            TileType[,] grid,
            bool[,] lineOfSightBlocks,
            ICollection<string> errors)
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string row = rows[rowIndex];
                if (row.Length != width)
                    continue;

                int z = rows.Count - 1 - rowIndex;
                for (int x = 0; x < width; x++)
                {
                    switch (row[x])
                    {
                        case '.':
                            grid[x, z] = TileType.Ground;
                            break;
                        case '#':
                            grid[x, z] = TileType.Wall;
                            lineOfSightBlocks[x, z] = true;
                            break;
                        case 'D':
                            grid[x, z] = TileType.Door;
                            break;
                        case ' ':
                            grid[x, z] = TileType.Empty;
                            lineOfSightBlocks[x, z] = true;
                            break;
                        default:
                            errors.Add(
                                $"JSON map row {rowIndex}, column {x} contains unknown symbol '{row[x]}'. " +
                                "Allowed symbols are '.', '#', 'D', and space.");
                            break;
                    }
                }
            }
        }

        private static void ParseObjects(
            JArray objects,
            KayKitDungeonCatalog catalog,
            TileType[,] grid,
            bool[,] lineOfSightBlocks,
            ICollection<KayKitDungeonObjectPlacement> placements,
            ICollection<string> errors)
        {
            HashSet<Vector2Int> occupiedBlockingCells = new();
            for (int index = 0; index < objects.Count; index++)
            {
                if (objects[index] is not JObject source)
                {
                    errors.Add($"JSON map object {index} must be an object.");
                    continue;
                }

                string assetId = source["assetId"]?.Type == JTokenType.String
                    ? source["assetId"].Value<string>()
                    : null;
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    errors.Add($"JSON map object {index} requires a non-empty assetId.");
                    continue;
                }

                if (!catalog.TryGet(assetId, out KayKitDungeonCatalogEntry entry))
                {
                    errors.Add($"JSON map object {index} references unknown assetId '{assetId}'.");
                    continue;
                }

                int? x = ReadInteger(source["x"]);
                int? z = ReadInteger(source["z"]);
                if (!x.HasValue || !z.HasValue)
                {
                    errors.Add($"JSON map object {index} requires integer x and z coordinates.");
                    continue;
                }

                int rotation = source["rotation"] == null
                    ? entry.DefaultRotation
                    : ReadInteger(source["rotation"]) ?? int.MinValue;
                if (!ValidRotations.Contains(rotation))
                {
                    errors.Add(
                        $"JSON map object {index} rotation must be 0, 90, 180, or 270; found " +
                        $"{source["rotation"]?.ToString(Formatting.None) ?? rotation.ToString(CultureInfo.InvariantCulture)}.");
                    continue;
                }

                float yOffset = entry.DefaultYOffset;
                if (source["yOffset"] != null && !TryReadFiniteFloat(source["yOffset"], out yOffset))
                {
                    errors.Add($"JSON map object {index} yOffset must be a finite number.");
                    continue;
                }

                Vector2Int sourceFootprint = entry.Footprint;
                if (sourceFootprint.x < 1 || sourceFootprint.y < 1)
                {
                    errors.Add(
                        $"Catalog entry '{assetId}' has invalid footprint {sourceFootprint.x}x{sourceFootprint.y}.");
                    continue;
                }

                Vector2Int footprint = rotation == 90 || rotation == 270
                    ? new Vector2Int(sourceFootprint.y, sourceFootprint.x)
                    : sourceFootprint;
                List<Vector2Int> footprintCells = FootprintCells(x.Value, z.Value, footprint).ToList();
                if (grid == null || footprintCells.Any(cell => !IsInBounds(grid, cell)))
                {
                    errors.Add(
                        $"JSON map object {index} ('{assetId}') footprint at ({x.Value}, {z.Value}) " +
                        $"with size {footprint.x}x{footprint.y} is out of bounds.");
                    continue;
                }

                if (entry.BlocksMovement)
                {
                    Vector2Int overlap = footprintCells.FirstOrDefault(occupiedBlockingCells.Contains);
                    if (footprintCells.Any(occupiedBlockingCells.Contains))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{assetId}') overlaps another blocking " +
                            $"footprint at ({overlap.x}, {overlap.y}).");
                        continue;
                    }

                    Vector2Int boundaryCell = footprintCells.FirstOrDefault(
                        cell => IsMapBoundary(grid, cell));
                    if (footprintCells.Any(cell => IsMapBoundary(grid, cell)))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{assetId}') may not overlap the map " +
                            $"boundary; cell ({boundaryCell.x}, {boundaryCell.y}) is on the boundary.");
                        continue;
                    }

                    Vector2Int invalidCell = footprintCells.FirstOrDefault(
                        cell => grid[cell.x, cell.y] != TileType.Ground);
                    if (footprintCells.Any(cell => grid[cell.x, cell.y] != TileType.Ground))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{assetId}') must lie entirely on Ground; " +
                            $"cell ({invalidCell.x}, {invalidCell.y}) is {grid[invalidCell.x, invalidCell.y]}.");
                        continue;
                    }

                    foreach (Vector2Int cell in footprintCells)
                    {
                        occupiedBlockingCells.Add(cell);
                        grid[cell.x, cell.y] = TileType.Obstacle;
                    }
                }

                if (entry.BlocksLineOfSight)
                {
                    foreach (Vector2Int cell in footprintCells)
                        lineOfSightBlocks[cell.x, cell.y] = true;
                }

                placements.Add(new KayKitDungeonObjectPlacement(
                    assetId,
                    x.Value,
                    z.Value,
                    yOffset,
                    rotation,
                    footprint,
                    entry));
            }
        }

        private static IEnumerable<Vector2Int> FootprintCells(int x, int z, Vector2Int footprint)
        {
            for (int offsetZ = 0; offsetZ < footprint.y; offsetZ++)
            {
                for (int offsetX = 0; offsetX < footprint.x; offsetX++)
                    yield return new Vector2Int(x + offsetX, z + offsetZ);
            }
        }

        private static bool IsInBounds(TileType[,] grid, Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 &&
                   cell.x < grid.GetLength(0) && cell.y < grid.GetLength(1);
        }

        private static bool IsMapBoundary(TileType[,] grid, Vector2Int cell)
        {
            return cell.x == 0 || cell.y == 0 ||
                   cell.x == grid.GetLength(0) - 1 || cell.y == grid.GetLength(1) - 1;
        }

        private static int? ReadInteger(JToken token)
        {
            if (token == null || token.Type != JTokenType.Integer)
                return null;
            return int.TryParse(
                token.ToString(Formatting.None),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : null;
        }

        private static bool TryReadFiniteFloat(JToken token, out float value)
        {
            value = 0f;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                return false;
            if (token is JValue { Value: System.Numerics.BigInteger })
                return false;
            return float.TryParse(
                       token.ToString(Formatting.None),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out value) &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static KayKitDungeonMapParseResult Invalid(string message)
        {
            return new KayKitDungeonMapParseResult(null, new[] { message });
        }
    }
}
