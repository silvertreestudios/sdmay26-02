using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.DungeonGeneration;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.KayKit.Editor
{
    /// <summary>
    /// Renders validated dungeon documents as deterministic, top-down PNG diagnostics without
    /// instantiating scene objects or consulting the KayKit catalog. Serialized rows keep their
    /// highest-Z-first orientation in the image: positive Z points toward the top edge.
    /// </summary>
    /// <remarks>
    /// The stable palette uses charcoal for masked void, slate for walls, blue for room floors,
    /// gray for corridor floors, and gold for doors. Green and violet insets mark up and down
    /// stairs, a cyan frame marks the start cell, and a magenta diamond marks each object anchor.
    /// Rendering owns its temporary <see cref="Texture2D"/> and destroys it before returning PNG
    /// bytes, so callers never need to manage a Unity object.
    /// </remarks>
    public static class DungeonLevelPngExporter
    {
        /// <summary>The default number of pixels used for both dimensions of one dungeon cell.</summary>
        public const int DefaultCellSize = 12;

        /// <summary>
        /// Environment variable read by <see cref="ExportBatch"/> to override the fixture JSON input.
        /// </summary>
        public const string BatchInputEnvironmentVariable = "PF2E_DUNGEON_JSON_PATH";

        /// <summary>
        /// Environment variable read by <see cref="ExportBatch"/> to override the PNG output path.
        /// </summary>
        public const string BatchOutputEnvironmentVariable = "PF2E_DUNGEON_PNG_PATH";

        private const int MinimumCellSize = 7;
        private const int MaximumCellSize = 128;
        private const int MaximumImageDimension = 4096;
        private const int MaximumPixelCount = MaximumImageDimension * MaximumImageDimension;
        private const string DefaultFileName = "procedural-dungeon-2d-diagnostic.png";

        private static readonly Color32 GridColor = new(12, 16, 24, 255);
        private static readonly Color32 MaskedVoidColor = new(24, 27, 33, 255);
        private static readonly Color32 WallColor = new(72, 82, 96, 255);
        private static readonly Color32 RoomFloorColor = new(72, 128, 176, 255);
        private static readonly Color32 CorridorFloorColor = new(139, 148, 159, 255);
        private static readonly Color32 DoorColor = new(224, 164, 54, 255);
        private static readonly Color32 UpStairColor = new(74, 190, 112, 255);
        private static readonly Color32 DownStairColor = new(151, 96, 204, 255);
        private static readonly Color32 StartColor = new(55, 225, 238, 255);
        private static readonly Color32 ObjectAnchorColor = new(239, 71, 145, 255);

        /// <summary>Gets the absolute fixture JSON path used when batch input is not overridden.</summary>
        public static string DefaultInputPath => Path.GetFullPath(
            Path.Combine(ProjectRootPath, ProceduralDungeonSceneTool.FixturePath));

        /// <summary>
        /// Gets the absolute default PNG path under the checkout-root <c>.agent-temp</c> directory.
        /// The diagnostic is deliberately kept outside <c>Assets</c> so Unity never imports it.
        /// </summary>
        public static string DefaultOutputPath => Path.Combine(
            ProjectRootPath,
            ".agent-temp",
            DefaultFileName);

        private static string ProjectRootPath => Path.GetFullPath(
            Path.Combine(Application.dataPath, ".."));

        /// <summary>Exports the checked-in procedural fixture through the Unity Tools menu.</summary>
        [MenuItem("Tools/KayKit/Export Procedural Dungeon 2D Diagnostic")]
        public static void ExportFixtureFromMenu()
        {
            string outputPath = ExportFixture();
            Debug.Log(DescribeExport(outputPath));
        }

        /// <summary>
        /// Batchmode-safe <c>-executeMethod</c> entrypoint. By default it renders the checked-in
        /// procedural fixture beneath <c>.agent-temp</c>. Set
        /// <see cref="BatchInputEnvironmentVariable"/> and
        /// <see cref="BatchOutputEnvironmentVariable"/> to inspect an arbitrary JSON file.
        /// </summary>
        public static void ExportBatch()
        {
            string inputPath = Environment.GetEnvironmentVariable(BatchInputEnvironmentVariable);
            string outputPath = Environment.GetEnvironmentVariable(BatchOutputEnvironmentVariable);
            string writtenPath = WriteFile(
                string.IsNullOrWhiteSpace(inputPath) ? DefaultInputPath : inputPath,
                string.IsNullOrWhiteSpace(outputPath) ? DefaultOutputPath : outputPath);
            Debug.Log(DescribeExport(writtenPath));
        }

        /// <summary>Writes the checked-in procedural fixture to <see cref="DefaultOutputPath"/>.</summary>
        /// <returns>The absolute path of the written PNG.</returns>
        public static string ExportFixture() => WriteFile(DefaultInputPath, DefaultOutputPath);

        /// <summary>Renders a complete document to deterministic PNG bytes.</summary>
        /// <param name="document">The document to validate and render.</param>
        /// <param name="cellSize">The square pixel size of each dungeon cell.</param>
        /// <returns>A complete PNG file. No Unity object ownership escapes this method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="cellSize"/> cannot preserve the diagnostic overlays or is excessively large.
        /// </exception>
        /// <exception cref="InvalidDataException">The document violates the serialized dungeon contract.</exception>
        public static byte[] RenderPng(
            DungeonLevelDocument document,
            int cellSize = DefaultCellSize)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            DungeonLevelParseResult validation = DungeonLevelJsonParser.Parse(
                DungeonLevelJsonSerializer.Serialize(document));
            if (!validation.IsSuccess)
                throw InvalidDocument(validation.Diagnostics);

            return RenderValidatedDocument(validation.Document, cellSize);
        }

        /// <summary>Parses, validates, and renders serialized dungeon JSON to deterministic PNG bytes.</summary>
        /// <param name="serializedJson">The complete current-schema dungeon document.</param>
        /// <param name="cellSize">The square pixel size of each dungeon cell.</param>
        /// <returns>A complete PNG file. No Unity object ownership escapes this method.</returns>
        /// <exception cref="InvalidDataException">The JSON cannot be parsed or violates the dungeon contract.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="cellSize"/> cannot preserve the diagnostic overlays or is excessively large.
        /// </exception>
        public static byte[] RenderPng(
            string serializedJson,
            int cellSize = DefaultCellSize)
        {
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(serializedJson);
            if (!parsed.IsSuccess)
                throw InvalidDocument(parsed.Diagnostics);

            return RenderValidatedDocument(parsed.Document, cellSize);
        }

        /// <summary>Validates serialized dungeon JSON and writes its deterministic PNG diagnostic.</summary>
        /// <param name="serializedJson">The complete current-schema dungeon document.</param>
        /// <param name="outputPath">
        /// Destination outside the Unity <c>Assets</c> directory. Relative paths resolve from the
        /// current process directory.
        /// </param>
        /// <param name="cellSize">The square pixel size of each dungeon cell.</param>
        /// <returns>The absolute path of the written PNG.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="outputPath"/> is empty or resolves inside the Unity <c>Assets</c> directory.
        /// </exception>
        /// <exception cref="InvalidDataException">The JSON cannot be parsed or violates the dungeon contract.</exception>
        public static string WritePng(
            string serializedJson,
            string outputPath,
            int cellSize = DefaultCellSize)
        {
            string absoluteOutputPath = ValidateOutputPath(outputPath);
            byte[] png = RenderPng(serializedJson, cellSize);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath) ??
                                      throw new InvalidOperationException("Output path has no parent directory."));
            File.WriteAllBytes(absoluteOutputPath, png);
            return absoluteOutputPath;
        }

        /// <summary>Reads a serialized dungeon document and writes its deterministic PNG diagnostic.</summary>
        /// <param name="inputPath">Path to a current-schema dungeon JSON file.</param>
        /// <param name="outputPath">Destination outside the Unity <c>Assets</c> directory.</param>
        /// <param name="cellSize">The square pixel size of each dungeon cell.</param>
        /// <returns>The absolute path of the written PNG.</returns>
        /// <exception cref="ArgumentException">Either path is empty or the output resolves under <c>Assets</c>.</exception>
        /// <exception cref="FileNotFoundException"><paramref name="inputPath"/> does not identify a file.</exception>
        /// <exception cref="InvalidDataException">The source file violates the dungeon contract.</exception>
        public static string WriteFile(
            string inputPath,
            string outputPath,
            int cellSize = DefaultCellSize)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("Input path must not be empty.", nameof(inputPath));

            string absoluteInputPath = Path.GetFullPath(inputPath);
            if (!File.Exists(absoluteInputPath))
            {
                throw new FileNotFoundException(
                    "Dungeon JSON input file was not found.",
                    absoluteInputPath);
            }

            return WritePng(File.ReadAllText(absoluteInputPath), outputPath, cellSize);
        }

        private static byte[] RenderValidatedDocument(
            DungeonLevelDocument document,
            int cellSize)
        {
            ValidateCellSize(cellSize);
            int pixelWidth;
            int pixelHeight;
            int pixelCount;
            try
            {
                pixelWidth = checked(document.Width * cellSize);
                pixelHeight = checked(document.Height * cellSize);
                pixelCount = checked(pixelWidth * pixelHeight);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    "Document dimensions and cell size exceed supported image dimensions.");
            }
            if (pixelWidth > MaximumImageDimension ||
                pixelHeight > MaximumImageDimension ||
                pixelCount > MaximumPixelCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    $"Diagnostic images may not exceed {MaximumImageDimension} pixels per side " +
                    $"or {MaximumPixelCount} total pixels.");
            }

            Color32[] pixels = Enumerable.Repeat(
                    GridColor,
                    pixelCount)
                .ToArray();
            HashSet<DungeonCell> roomCells = BuildRoomCells(document.Rooms);
            Dictionary<DungeonCell, DungeonStairKind> stairs = document.Stairs.ToDictionary(
                stair => stair.Cell,
                stair => stair.Kind);
            HashSet<DungeonCell> objectAnchors = new(document.Objects.Select(item => item.Cell));

            for (int rowIndex = 0; rowIndex < document.Height; rowIndex++)
            {
                int z = document.Height - 1 - rowIndex;
                for (int x = 0; x < document.Width; x++)
                {
                    DungeonCell cell = new(x, z);
                    int pixelX = x * cellSize;
                    int pixelY = z * cellSize;
                    Color32 baseColor = BaseColor(document.Rows[rowIndex][x], cell, roomCells);
                    FillRectangle(
                        pixels,
                        pixelWidth,
                        pixelX + 1,
                        pixelY + 1,
                        cellSize - 2,
                        cellSize - 2,
                        baseColor);

                    if (stairs.TryGetValue(cell, out DungeonStairKind stairKind))
                    {
                        int stairInset = Math.Max(2, cellSize / 4);
                        FillRectangle(
                            pixels,
                            pixelWidth,
                            pixelX + stairInset,
                            pixelY + stairInset,
                            cellSize - stairInset * 2,
                            cellSize - stairInset * 2,
                            stairKind == DungeonStairKind.Up ? UpStairColor : DownStairColor);
                    }

                    if (cell == document.StartCell)
                        DrawFrame(pixels, pixelWidth, pixelX, pixelY, cellSize, StartColor);

                    if (objectAnchors.Contains(cell))
                        DrawDiamond(pixels, pixelWidth, pixelX, pixelY, cellSize, ObjectAnchorColor);
                }
            }

            Texture2D texture = new(pixelWidth, pixelHeight, TextureFormat.RGBA32, false, true)
            {
                name = "Dungeon 2D Diagnostic",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static HashSet<DungeonCell> BuildRoomCells(IReadOnlyList<DungeonRoom> rooms)
        {
            HashSet<DungeonCell> cells = new();
            foreach (DungeonRoom room in rooms)
            {
                for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
                for (int x = room.MinimumX; x <= room.MaximumX; x++)
                    cells.Add(new DungeonCell(x, z));
            }

            return cells;
        }

        private static Color32 BaseColor(
            char symbol,
            DungeonCell cell,
            HashSet<DungeonCell> roomCells)
        {
            return symbol switch
            {
                ' ' => MaskedVoidColor,
                '#' => WallColor,
                'D' => DoorColor,
                '.' when roomCells.Contains(cell) => RoomFloorColor,
                '.' => CorridorFloorColor,
                _ => throw new InvalidDataException($"Unsupported dungeon symbol '{symbol}'.")
            };
        }

        private static void FillRectangle(
            Color32[] pixels,
            int pixelWidth,
            int x,
            int y,
            int width,
            int height,
            Color32 color)
        {
            for (int localY = 0; localY < height; localY++)
            for (int localX = 0; localX < width; localX++)
                pixels[(y + localY) * pixelWidth + x + localX] = color;
        }

        private static void DrawFrame(
            Color32[] pixels,
            int pixelWidth,
            int pixelX,
            int pixelY,
            int cellSize,
            Color32 color)
        {
            int minimum = 1;
            int maximum = cellSize - 2;
            for (int offset = minimum; offset <= maximum; offset++)
            {
                pixels[(pixelY + minimum) * pixelWidth + pixelX + offset] = color;
                pixels[(pixelY + maximum) * pixelWidth + pixelX + offset] = color;
                pixels[(pixelY + offset) * pixelWidth + pixelX + minimum] = color;
                pixels[(pixelY + offset) * pixelWidth + pixelX + maximum] = color;
            }
        }

        private static void DrawDiamond(
            Color32[] pixels,
            int pixelWidth,
            int pixelX,
            int pixelY,
            int cellSize,
            Color32 color)
        {
            int center = cellSize / 2;
            int radius = Math.Max(1, cellSize / 4);
            for (int yOffset = -radius; yOffset <= radius; yOffset++)
            for (int xOffset = -radius; xOffset <= radius; xOffset++)
            {
                if (Math.Abs(xOffset) + Math.Abs(yOffset) <= radius)
                {
                    pixels[(pixelY + center + yOffset) * pixelWidth +
                           pixelX + center + xOffset] = color;
                }
            }
        }

        private static void ValidateCellSize(int cellSize)
        {
            if (cellSize < MinimumCellSize || cellSize > MaximumCellSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    $"Cell size must be from {MinimumCellSize} through {MaximumCellSize} pixels.");
            }
        }

        private static string ValidateOutputPath(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path must not be empty.", nameof(outputPath));

            string absolutePath = Path.GetFullPath(outputPath);
            string assetsRoot = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string assetsPrefix = assetsRoot + Path.DirectorySeparatorChar;
            if (string.Equals(absolutePath, assetsRoot, StringComparison.OrdinalIgnoreCase) ||
                absolutePath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Dungeon diagnostic PNGs must be written outside the Unity Assets directory.",
                    nameof(outputPath));
            }

            return absolutePath;
        }

        private static InvalidDataException InvalidDocument(
            IReadOnlyList<DungeonGenerationDiagnostic> diagnostics)
        {
            string details = string.Join(
                Environment.NewLine,
                diagnostics.Select(diagnostic =>
                    $"{diagnostic.Field}: {diagnostic.Message}"));
            return new InvalidDataException(
                "Dungeon JSON did not satisfy the validated document contract." +
                Environment.NewLine +
                details);
        }

        private static string DescribeExport(string outputPath) =>
            $"Wrote dungeon 2D diagnostic to {outputPath}. " +
            "Palette: void charcoal, wall slate, room blue, corridor gray, door gold, " +
            "up stair green, down stair violet, start cyan frame, object magenta diamond.";
    }
}
