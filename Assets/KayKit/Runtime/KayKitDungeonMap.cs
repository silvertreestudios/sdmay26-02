using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using GridPrivate;
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
            KayKitDungeonCatalogEntry catalogEntry
        )
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
        public int Width => GridData.GetLength(0);
        public int Height => GridData.GetLength(1);
        public TileType[,] GridData { get; }
        public bool[,] LineOfSightBlocks { get; }
        public IReadOnlyList<KayKitDungeonObjectPlacement> Objects { get; }

        /// <summary>Gets the complete source document retained for downstream runtime systems.</summary>
        public DungeonLevelDocument LevelDocument { get; }

        /// <summary>Creates projected KayKit map data for one validated dungeon document.</summary>
        /// <param name="gridData">The required projected tile grid.</param>
        /// <param name="lineOfSightBlocks">The required line-of-sight overlay matching the grid dimensions.</param>
        /// <param name="objects">The deterministic projected object placements.</param>
        /// <param name="levelDocument">The complete source document.</param>
        public KayKitDungeonMapData(
            TileType[,] gridData,
            bool[,] lineOfSightBlocks,
            IReadOnlyList<KayKitDungeonObjectPlacement> objects,
            DungeonLevelDocument levelDocument
        )
        {
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
        public static KayKitDungeonMapParseResult Parse(string json, KayKitDungeonCatalog catalog)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Invalid("JSON map source is empty.");
            if (catalog == null)
                return Invalid("JSON map source requires a KayKitDungeonCatalog.");
            if (catalog.DuplicateIds.Count > 0)
            {
                return new KayKitDungeonMapParseResult(
                    null,
                    catalog.DuplicateIds.Select(id =>
                        $"KayKit dungeon catalog contains duplicate id '{id}'."
                    )
                );
            }

            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
            if (!parsed.IsSuccess)
            {
                return new KayKitDungeonMapParseResult(
                    null,
                    parsed.Diagnostics.Select(diagnostic =>
                        $"JSON map {diagnostic.Field}: {diagnostic.Message}"
                    )
                );
            }

            DungeonLevelDocument document = parsed.Document;
            int width = document.Width;
            TileType[,] grid = new TileType[width, document.Height];
            bool[,] lineOfSightBlocks = new bool[width, document.Height];
            List<string> errors = new();
            ParseRows(
                document.Rows,
                document.Doors.ToDictionary(door => door.Cell),
                width,
                grid,
                lineOfSightBlocks,
                errors
            );

            List<KayKitDungeonObjectPlacement> placements = new();
            ParseObjects(document.Objects, catalog, grid, lineOfSightBlocks, placements, errors);
            if (errors.Count > 0)
                return new KayKitDungeonMapParseResult(null, errors);

            IReadOnlyList<KayKitDungeonObjectPlacement> deterministicPlacements = placements
                .OrderBy(placement => placement.Z)
                .ThenBy(placement => placement.X)
                .ThenBy(placement => placement.AssetId, StringComparer.Ordinal)
                .ThenBy(placement => placement.Rotation)
                .ToArray();
            return new KayKitDungeonMapParseResult(
                new KayKitDungeonMapData(
                    grid,
                    lineOfSightBlocks,
                    deterministicPlacements,
                    document
                ),
                Array.Empty<string>()
            );
        }

        private static void ParseRows(
            IReadOnlyList<string> rows,
            IReadOnlyDictionary<DungeonCell, DungeonDoor> doors,
            int width,
            TileType[,] grid,
            bool[,] lineOfSightBlocks,
            ICollection<string> errors
        )
        {
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string row = rows[rowIndex];
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
                            DungeonDoor door = doors[new DungeonCell(x, z)];
                            grid[x, z] = door.IsOpen ? TileType.Door : TileType.ClosedDoor;
                            lineOfSightBlocks[x, z] = !door.IsOpen;
                            break;
                        case ' ':
                            grid[x, z] = TileType.Empty;
                            lineOfSightBlocks[x, z] = true;
                            break;
                        default:
                            errors.Add(
                                $"JSON map row {rowIndex}, column {x} contains unknown symbol '{row[x]}'. "
                                    + "Allowed symbols are '.', '#', 'D', and space."
                            );
                            break;
                    }
                }
            }
        }

        private static void ParseObjects(
            IReadOnlyList<DungeonObjectPlacement> objects,
            KayKitDungeonCatalog catalog,
            TileType[,] grid,
            bool[,] lineOfSightBlocks,
            ICollection<KayKitDungeonObjectPlacement> placements,
            ICollection<string> errors
        )
        {
            HashSet<Vector2Int> occupiedBlockingCells = new();
            for (int index = 0; index < objects.Count; index++)
            {
                DungeonObjectPlacement source = objects[index];
                if (!catalog.TryGet(source.AssetId, out KayKitDungeonCatalogEntry entry))
                {
                    errors.Add(
                        $"JSON map object {index} references unknown assetId '{source.AssetId}'."
                    );
                    continue;
                }

                Vector2Int sourceFootprint = entry.Footprint;
                if (sourceFootprint.x < 1 || sourceFootprint.y < 1)
                {
                    errors.Add(
                        $"Catalog entry '{source.AssetId}' has invalid footprint {sourceFootprint.x}x{sourceFootprint.y}."
                    );
                    continue;
                }

                Vector2Int footprint =
                    source.Rotation == 90 || source.Rotation == 270
                        ? new Vector2Int(sourceFootprint.y, sourceFootprint.x)
                        : sourceFootprint;
                List<Vector2Int> footprintCells = FootprintCells(
                        source.Cell.X,
                        source.Cell.Z,
                        footprint
                    )
                    .ToList();
                if (footprintCells.Any(cell => !IsInBounds(grid, cell)))
                {
                    errors.Add(
                        $"JSON map object {index} ('{source.AssetId}') footprint at ({source.Cell.X}, {source.Cell.Z}) "
                            + $"with size {footprint.x}x{footprint.y} is out of bounds."
                    );
                    continue;
                }

                if (entry.BlocksMovement)
                {
                    Vector2Int overlap = footprintCells.FirstOrDefault(
                        occupiedBlockingCells.Contains
                    );
                    if (footprintCells.Any(occupiedBlockingCells.Contains))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{source.AssetId}') overlaps another blocking "
                                + $"footprint at ({overlap.x}, {overlap.y})."
                        );
                        continue;
                    }

                    Vector2Int boundaryCell = footprintCells.FirstOrDefault(cell =>
                        IsMapBoundary(grid, cell)
                    );
                    if (footprintCells.Any(cell => IsMapBoundary(grid, cell)))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{source.AssetId}') may not overlap the map "
                                + $"boundary; cell ({boundaryCell.x}, {boundaryCell.y}) is on the boundary."
                        );
                        continue;
                    }

                    Vector2Int invalidCell = footprintCells.FirstOrDefault(cell =>
                        grid[cell.x, cell.y] != TileType.Ground
                    );
                    if (footprintCells.Any(cell => grid[cell.x, cell.y] != TileType.Ground))
                    {
                        errors.Add(
                            $"Blocking JSON map object {index} ('{source.AssetId}') must lie entirely on Ground; "
                                + $"cell ({invalidCell.x}, {invalidCell.y}) is {grid[invalidCell.x, invalidCell.y]}."
                        );
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

                placements.Add(
                    new KayKitDungeonObjectPlacement(
                        source.AssetId,
                        source.Cell.X,
                        source.Cell.Z,
                        source.YOffset,
                        source.Rotation,
                        footprint,
                        entry
                    )
                );
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
            return cell.x >= 0
                && cell.y >= 0
                && cell.x < grid.GetLength(0)
                && cell.y < grid.GetLength(1);
        }

        private static bool IsMapBoundary(TileType[,] grid, Vector2Int cell)
        {
            return cell.x == 0
                || cell.y == 0
                || cell.x == grid.GetLength(0) - 1
                || cell.y == grid.GetLength(1) - 1;
        }

        private static KayKitDungeonMapParseResult Invalid(string message)
        {
            return new KayKitDungeonMapParseResult(null, new[] { message });
        }
    }
}
