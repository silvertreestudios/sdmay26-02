using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>Represents the complete success or failure of parsing a level document.</summary>
    public sealed class DungeonLevelParseResult
    {
        /// <summary>Creates a parse result and snapshots its diagnostics.</summary>
        /// <param name="document">The complete validated document, or absence when parsing failed.</param>
        /// <param name="diagnostics">The deterministic diagnostics produced while reading and validating the source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is null.</exception>
        public DungeonLevelParseResult(DungeonLevelDocument document, IEnumerable<DungeonGenerationDiagnostic> diagnostics)
        {
            Document = document;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        }
        /// <summary>Gets the complete lossless document, or absence when invalid.</summary>
        public DungeonLevelDocument Document { get; }
        /// <summary>Gets an immutable snapshot of deterministic schema and semantic diagnostics.</summary>
        public IReadOnlyList<DungeonGenerationDiagnostic> Diagnostics { get; }
        /// <summary>Gets whether a complete document was parsed with no diagnostics.</summary>
        public bool IsSuccess => Document != null && Diagnostics.Count == 0;
    }

    /// <summary>Writes deterministic compact JSON through the project's Newtonsoft serializer.</summary>
    public static class DungeonLevelJsonSerializer
    {
        /// <summary>Serializes a complete document using the typed development schema.</summary>
        /// <param name="document">The document to serialize without dropping any contract field.</param>
        /// <returns>A compact JSON string whose property and collection order matches the supplied document.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A stair kind or encounter threat is undefined.</exception>
        public static string Serialize(DungeonLevelDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return DungeonLevelJsonModel.Serialize(document);
        }
    }

    /// <summary>
    /// Strict lossless parser for the current development contract. Unknown, duplicate, and
    /// mistyped properties are rejected rather than accepted and dropped.
    /// </summary>
    public static class DungeonLevelJsonParser
    {
        /// <summary>Parses and validates JSON without creating a partial document.</summary>
        /// <param name="json">The complete JSON source using the current development schema.</param>
        /// <returns>A complete lossless document or deterministic diagnostics with no partial document.</returns>
        public static DungeonLevelParseResult Parse(string json)
        {
            List<DungeonGenerationDiagnostic> errors = new();
            JObject root;
            try
            {
                root = JObject.Parse(json ?? string.Empty, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Ignore
                });
            }
            catch (JsonException exception)
            {
                return Invalid("json", "JSON could not be parsed: " + exception.Message);
            }

            ValidateProperties(
                root,
                "$",
                errors,
                "generation",
                "rows",
                "rooms",
                "doors",
                "stairs",
                "arrival",
                "objects",
                "encounterPlans",
                "runtimeState");
            JObject generation = root["generation"] as JObject;
            JArray rowsToken = root["rows"] as JArray;
            if (generation == null)
            {
                errors.Add(D("generation", "Generation metadata is required."));
            }

            DungeonGenerationMetadata metadata = ReadGeneration(generation, errors);
            List<string> rows = ReadStrings(rowsToken, "rows", errors);
            bool rowsAreRectangular = ValidateRows(rows, errors);
            List<DungeonRoom> rooms = ReadRooms(root["rooms"] as JArray, errors);
            List<DungeonDoor> doors = ReadDoors(root["doors"] as JArray, errors);
            List<DungeonStair> stairs = ReadStairs(root["stairs"] as JArray, errors);
            JObject arrival = root["arrival"] as JObject;
            DungeonCell start = ReadCell(arrival?["start"], "arrival.start", errors);
            if (arrival == null)
            {
                errors.Add(D("arrival", "Arrival must be an object."));
            }
            else
            {
                ValidateProperties(arrival, "arrival", errors, "start", "safeCells");
            }

            List<DungeonCell> safe = ReadCells(arrival?["safeCells"] as JArray, "arrival.safeCells", errors);
            List<DungeonObjectPlacement> objects = ReadObjects(root["objects"] as JArray, errors);
            List<DungeonEncounterPlan> encounters = ReadEncounters(root["encounterPlans"] as JArray, errors);
            DungeonRuntimeState runtime = ReadRuntime(root["runtimeState"], errors);
            if (rowsAreRectangular)
            {
                ValidateDocument(
                    metadata,
                    rows,
                    rooms,
                    doors,
                    stairs,
                    start,
                    safe,
                    objects,
                    encounters,
                    runtime,
                    errors);
            }

            if (errors.Count > 0)
            {
                return new DungeonLevelParseResult(null, errors);
            }

            DungeonLevelDocument document = new(
                metadata,
                rows,
                rooms,
                doors,
                stairs,
                start,
                safe,
                objects,
                encounters,
                runtime);
            return new DungeonLevelParseResult(document, errors);
        }

        private static DungeonGenerationMetadata ReadGeneration(JObject source, List<DungeonGenerationDiagnostic> errors)
        {
            if (source == null) return null;
            ValidateProperties(
                source,
                "generation",
                errors,
                "algorithm",
                "runSeed",
                "depth",
                "topologyAttempt");
            string algorithm = RequiredString(source, "algorithm", "generation", errors);
            int runSeed = RequiredInt(source, "runSeed", "generation", errors);
            int depth = RequiredInt(source, "depth", "generation", errors);
            int topologyAttempt = RequiredInt(source, "topologyAttempt", "generation", errors);
            if (depth < 0) errors.Add(D("generation.depth", "Depth must be zero or greater."));
            if (topologyAttempt < 0)
                errors.Add(D("generation.topologyAttempt", "Topology attempt must be zero or greater."));
            DungeonGenerationMetadata metadata = new(
                algorithm,
                runSeed,
                depth,
                topologyAttempt);
            if (DeterministicDungeonGenerator.OwnsContract(metadata) &&
                topologyAttempt >= DeterministicDungeonGenerator.MaximumAttempts)
            {
                errors.Add(D(
                    "generation.topologyAttempt",
                    "For the current Donjon generator, topology attempt must be from 0 through 31."));
            }
            return metadata;
        }

        private static List<DungeonRoom> ReadRooms(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "rooms", e, (o, p) =>
        {
            ValidateProperties(o, p, e, "id", "minX", "minZ", "maxX", "maxZ");
            return new DungeonRoom(RequiredInt(o, "id", p, e), RequiredInt(o, "minX", p, e), RequiredInt(o, "minZ", p, e), RequiredInt(o, "maxX", p, e), RequiredInt(o, "maxZ", p, e));
        });
        private static List<DungeonDoor> ReadDoors(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "doors", e, (o, p) =>
        {
            ValidateProperties(o, p, e, "id", "cell", "isOpen");
            return new DungeonDoor(RequiredString(o, "id", p, e), ReadCell(o["cell"], p + ".cell", e), RequiredBool(o, "isOpen", p, e));
        });
        private static List<DungeonStair> ReadStairs(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "stairs", e, (source, path) => ReadStair(source, path, e));
        private static List<DungeonObjectPlacement> ReadObjects(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "objects", e, (o, p) =>
        {
            ValidateProperties(o, p, e, "id", "assetId", "cell", "rotation", "yOffset", "state");
            return new DungeonObjectPlacement(
                RequiredString(o, "id", p, e),
                RequiredString(o, "assetId", p, e),
                ReadCell(o["cell"], p + ".cell", e),
                RequiredInt(o, "rotation", p, e),
                OptionalString(o, "state", p, e),
                OptionalFloat(o, "yOffset", p, e));
        });
        private static List<DungeonEncounterPlan> ReadEncounters(
            JArray array,
            List<DungeonGenerationDiagnostic> errors)
            => ReadObjects(array, "encounterPlans", errors, (source, path) =>
        {
            ValidateProperties(
                source,
                path,
                errors,
                "id",
                "roomId",
                "spawnCells",
                "creatureIds",
                "threat",
                "budget",
                "isResolved");
            return new DungeonEncounterPlan(
                RequiredString(source, "id", path, errors),
                RequiredInt(source, "roomId", path, errors),
                ReadThreat(RequiredString(source, "threat", path, errors), path + ".threat", errors),
                RequiredInt(source, "budget", path, errors),
                ReadCells(source["spawnCells"] as JArray, path + ".spawnCells", errors),
                ReadStrings(source["creatureIds"] as JArray, path + ".creatureIds", errors),
                RequiredBool(source, "isResolved", path, errors));
        });
        private static DungeonStair ReadStair(
            JObject source,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            ValidateProperties(source, path, errors, "id", "kind", "cell", "arrivalCell");
            string kind = RequiredString(source, "kind", path, errors);
            if (kind != "up" && kind != "down")
                errors.Add(D(path + ".kind", "Stair kind must be 'up' or 'down'."));

            return new DungeonStair(
                RequiredString(source, "id", path, errors),
                kind == "down" ? DungeonStairKind.Down : DungeonStairKind.Up,
                ReadCell(source["cell"], path + ".cell", errors),
                ReadCell(source["arrivalCell"], path + ".arrivalCell", errors));
        }

        private static DungeonRuntimeState ReadRuntime(
            JToken token,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (token == null)
                return null;
            if (token is not JObject source)
            {
                errors.Add(D("runtimeState", "Runtime state must be an object when provided."));
                return null;
            }

            ValidateProperties(
                source,
                "runtimeState",
                errors,
                "openDoorIds",
                "resolvedEncounterIds",
                "defeatedCreatureIds",
                "creatures");
            return new DungeonRuntimeState(
                ReadStrings(source["openDoorIds"] as JArray, "runtimeState.openDoorIds", errors),
                ReadStrings(source["resolvedEncounterIds"] as JArray, "runtimeState.resolvedEncounterIds", errors),
                ReadStrings(source["defeatedCreatureIds"] as JArray, "runtimeState.defeatedCreatureIds", errors),
                ReadCreatures(source["creatures"] as JArray, errors));
        }
        private static List<DungeonCreatureRuntimeState> ReadCreatures(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "runtimeState.creatures", e, (o, p) =>
        {
            ValidateProperties(o, p, e, "instanceId", "creatureId", "encounterId", "cell", "hitPoints", "state");
            return new DungeonCreatureRuntimeState(RequiredString(o, "instanceId", p, e), RequiredString(o, "creatureId", p, e), RequiredString(o, "encounterId", p, e), ReadCell(o["cell"], p + ".cell", e), RequiredInt(o, "hitPoints", p, e), OptionalString(o, "state", p, e));
        });
        private static List<T> ReadObjects<T>(
            JArray array,
            string field,
            List<DungeonGenerationDiagnostic> errors,
            Func<JObject, string, T> read)
        {
            List<T> result = new();
            if (array == null)
            {
                errors.Add(D(field, field + " must be an array."));
                return result;
            }

            for (int index = 0; index < array.Count; index++)
            {
                string path = field + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (array[index] is JObject source)
                    result.Add(read(source, path));
                else
                    errors.Add(D(path, "Entry must be an object."));
            }

            return result;
        }

        private static List<string> ReadStrings(
            JArray array,
            string field,
            List<DungeonGenerationDiagnostic> errors)
        {
            List<string> result = new();
            if (array == null)
            {
                errors.Add(D(field, field + " must be an array."));
                return result;
            }

            for (int index = 0; index < array.Count; index++)
            {
                string path = field + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (array[index].Type == JTokenType.String)
                {
                    string value = String(array[index]);
                    if (field != "rows" && string.IsNullOrWhiteSpace(value))
                    {
                        errors.Add(D(path, "Entry must be a non-empty string."));
                    }

                    result.Add(value);
                }
                else
                {
                    errors.Add(D(path, "Entry must be a string."));
                }
            }

            return result;
        }

        private static List<DungeonCell> ReadCells(
            JArray array,
            string field,
            List<DungeonGenerationDiagnostic> errors)
        {
            List<DungeonCell> result = new();
            if (array == null)
            {
                errors.Add(D(field, field + " must be an array."));
                return result;
            }

            for (int index = 0; index < array.Count; index++)
            {
                result.Add(ReadCell(
                    array[index],
                    field + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                    errors));
            }

            return result;
        }

        private static DungeonCell ReadCell(
            JToken token,
            string field,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (token is not JObject source)
            {
                errors.Add(D(field, "Cell must be an object with integer x and z."));
                return default;
            }

            ValidateProperties(source, field, errors, "x", "z");
            return new DungeonCell(
                RequiredInt(source, "x", field, errors),
                RequiredInt(source, "z", field, errors));
        }

        private static bool ValidateRows(
            List<string> rows,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (rows.Count == 0)
            {
                errors.Add(D("rows", "At least one row is required."));
                return false;
            }

            int width = rows[0].Length;
            bool rectangular = width > 0;
            if (width == 0)
                errors.Add(D("rows", "Rows must not be empty."));

            for (int z = 0; z < rows.Count; z++)
            {
                if (rows[z].Length != width)
                {
                    errors.Add(D(
                        "rows[" + z.ToString(CultureInfo.InvariantCulture) + "]",
                        "Row width must equal " + width.ToString(CultureInfo.InvariantCulture) + "."));
                    rectangular = false;
                }

                if (rows[z].Any(c => c != ' ' && c != '#' && c != '.' && c != 'D'))
                {
                    errors.Add(D(
                        "rows[" + z.ToString(CultureInfo.InvariantCulture) + "]",
                        "Rows may use only space, '#', '.', and 'D'."));
                }
            }

            return rectangular;
        }

        private static int RequiredInt(
            JObject source,
            string name,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            JToken token = source[name];
            int? value = Int(token);
            if (!value.HasValue)
            {
                string message = token?.Type == JTokenType.Integer
                    ? "Integer must be within the signed 32-bit range."
                    : "An integer is required.";
                errors.Add(D(path + "." + name, message));
            }

            return value ?? 0;
        }

        private static string RequiredString(
            JObject source,
            string name,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            string value = source[name]?.Type == JTokenType.String ? String(source[name]) : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(D(path + "." + name, "A non-empty string is required."));
            }

            return value ?? string.Empty;
        }

        private static string OptionalString(
            JObject source,
            string name,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (!source.TryGetValue(name, out JToken token))
                return null;
            if (token.Type == JTokenType.String)
                return String(token);

            errors.Add(D(path + "." + name, "The optional value must be a string when present."));
            return null;
        }
        private static float OptionalFloat(
            JObject source,
            string name,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (!source.TryGetValue(name, out JToken token))
                return 0f;
            if ((token.Type == JTokenType.Integer || token.Type == JTokenType.Float) &&
                token is not JValue { Value: System.Numerics.BigInteger } &&
                float.TryParse(
                    token.ToString(Formatting.None),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value) &&
                !float.IsNaN(value) &&
                !float.IsInfinity(value))
            {
                return value;
            }

            errors.Add(D(
                path + "." + name,
                "The optional value must be a finite number when present."));
            return 0f;
        }


        private static bool RequiredBool(
            JObject source,
            string name,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (source[name]?.Type != JTokenType.Boolean)
            {
                errors.Add(D(path + "." + name, "A boolean is required."));
                return false;
            }

            return source[name].Value<bool>();
        }

        private static DungeonEncounterThreat ReadThreat(
            string value,
            string path,
            List<DungeonGenerationDiagnostic> errors)
        {
            if (value == "trivial")
                return DungeonEncounterThreat.Trivial;
            if (value == "low")
                return DungeonEncounterThreat.Low;
            if (value == "moderate")
                return DungeonEncounterThreat.Moderate;

            errors.Add(D(path, "Encounter threat must be 'trivial', 'low', or 'moderate'."));
            return DungeonEncounterThreat.Trivial;
        }

        private static void ValidateProperties(
            JObject source,
            string path,
            List<DungeonGenerationDiagnostic> errors,
            params string[] names)
        {
            HashSet<string> allowed = new(names, StringComparer.Ordinal);
            foreach (JProperty property in source.Properties())
            {
                if (!allowed.Contains(property.Name))
                {
                    errors.Add(D(
                        path == "$" ? property.Name : path + "." + property.Name,
                        "Unknown property is not part of the current schema."));
                }
            }
        }
        private static void ValidateDocument(
            DungeonGenerationMetadata metadata,
            IReadOnlyList<string> rows,
            IReadOnlyList<DungeonRoom> rooms,
            IReadOnlyList<DungeonDoor> doors,
            IReadOnlyList<DungeonStair> stairs,
            DungeonCell start,
            IReadOnlyList<DungeonCell> safe,
            IReadOnlyList<DungeonObjectPlacement> objects,
            IReadOnlyList<DungeonEncounterPlan> encounters,
            DungeonRuntimeState runtime,
            List<DungeonGenerationDiagnostic> errors)
        {
            bool InBounds(DungeonCell c) => c.X >= 0 && c.Z >= 0 && c.X < rows[0].Length && c.Z < rows.Count;
            char Symbol(DungeonCell c) => rows[rows.Count - 1 - c.Z][c.X];
            bool Walkable(DungeonCell c) => InBounds(c) && (Symbol(c) == '.' || Symbol(c) == 'D');
            if (!Walkable(start))
            {
                errors.Add(D("arrival.start", "Start must reference a walkable cell."));
            }

            if (safe.Count == 0 || safe.Any(cell => !Walkable(cell)))
            {
                errors.Add(D(
                    "arrival.safeCells",
                    "At least one safe cell is required and every safe cell must be walkable."));
            }

            if (safe.Distinct().Count() != safe.Count)
            {
                errors.Add(D("arrival.safeCells", "Safe cells must be unique."));
            }

            bool ownsContract = DeterministicDungeonGenerator.OwnsContract(metadata);
            if (ownsContract &&
                (!DeterministicDungeonGenerator.IsSupportedDimension(rows[0].Length) ||
                 !DeterministicDungeonGenerator.IsSupportedDimension(rows.Count)))
            {
                errors.Add(D(
                    "rows",
                    "For the current Donjon generator, width and height must each be an odd integer from 15 through 101."));
            }
            if (ownsContract &&
                DeterministicDungeonGenerator.IsSupportedDimension(rows[0].Length) &&
                DeterministicDungeonGenerator.IsSupportedDimension(rows.Count) &&
                !DungeonTopologyValidator.HasProducibleLayoutMask(rows))
            {
                errors.Add(D(
                    "rows",
                    "For the current Donjon generator, serialized spaces must exactly match one supported Box, Cross, or Round generator layout mask."));
            }
            if (ownsContract &&
                safe.Count > 0 &&
                start != DeterministicDungeonGenerator.SelectStartCell(stairs, safe))
            {
                errors.Add(D(
                    "arrival.start",
                    "For the current Donjon generator, start must equal the deterministic stair-aware selection from stairs and ordered safeCells."));
            }
            bool invalidRooms =
                rooms.Select(room => room.Id).Distinct().Count() != rooms.Count ||
                rooms.Any(room =>
                    room.Id < 1 ||
                    room.MinimumX > room.MaximumX ||
                    room.MinimumZ > room.MaximumZ ||
                    !InBounds(new DungeonCell(room.MinimumX, room.MinimumZ)) ||
                    !InBounds(new DungeonCell(room.MaximumX, room.MaximumZ)));
            if (invalidRooms)
            {
                errors.Add(D(
                    "rooms",
                    "Room IDs must be unique positive integers with ordered in-bounds bounds."));
            }
            else
            {
                for (int left = 0; left < rooms.Count; left++)
                {
                    DungeonRoom room = rooms[left];
                    for (int right = left + 1; right < rooms.Count; right++)
                    {
                        DungeonRoom other = rooms[right];
                        if (room.MinimumX <= other.MaximumX &&
                            room.MaximumX >= other.MinimumX &&
                            room.MinimumZ <= other.MaximumZ &&
                            room.MaximumZ >= other.MinimumZ)
                        {
                            errors.Add(D("rooms", "Room bounds must not overlap."));
                        }
                    }

                    for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
                    {
                        for (int x = room.MinimumX; x <= room.MaximumX; x++)
                        {
                            if (Symbol(new DungeonCell(x, z)) != '.')
                            {
                                errors.Add(D(
                                    "rooms",
                                    "Every cell inside room bounds must contain '.'."));
                            }
                        }
                    }
                }
            }
            if (ownsContract && !invalidRooms &&
                !DungeonTopologyValidator.HasProducibleRoomRecords(
                    rows[0].Length,
                    rows.Count,
                    rooms))
            {
                errors.Add(D(
                    "rooms",
                    "For the current Donjon generator, room records must use ordered IDs starting at 1, odd-aligned bounds, and odd side lengths from 3 through the generator-supported map maximum."));
            }
            HashSet<DungeonCell> doorCells = new();
            bool invalidDoorCell = false;
            foreach (DungeonDoor door in doors)
            {
                if (!doorCells.Add(door.Cell) ||
                    !InBounds(door.Cell) ||
                    Symbol(door.Cell) != 'D')
                {
                    invalidDoorCell = true;
                }
            }

            bool duplicateDoorIds =
                doors.Select(door => door.Id).Distinct(StringComparer.Ordinal).Count() != doors.Count;
            if (duplicateDoorIds)
            {
                errors.Add(D("doors", "Door IDs must be unique."));
            }

            if (invalidDoorCell)
            {
                errors.Add(D(
                    "doors",
                    "Door cells must be unique, in bounds, and reference a 'D' row cell."));
            }

            HashSet<DungeonCell> rowDoorCells = new();
            for (int row = 0; row < rows.Count; row++)
            {
                for (int x = 0; x < rows[row].Length; x++)
                {
                    if (rows[row][x] == 'D')
                    {
                        rowDoorCells.Add(new DungeonCell(x, rows.Count - 1 - row));
                    }
                }
            }

            if (!rowDoorCells.SetEquals(doorCells))
            {
                errors.Add(D(
                    "doors",
                    "Every 'D' row cell must have exactly one door record and every record must map to one 'D' cell."));
            }

            if (ownsContract && Walkable(start) &&
                !DungeonTopologyValidator.AreAllWalkableCellsReachable(rows, start))
            {
                errors.Add(D(
                    "rows",
                    "For the current Donjon generator, every walkable cell must be reachable from arrival.start."));
            }
            if (ownsContract && !invalidRooms &&
                !DungeonTopologyValidator.HasValidRoomBoundaryCrossings(rows, rooms, doors))
            {
                errors.Add(D(
                    "doors",
                    "For the current Donjon generator, every walkable room-boundary crossing must be exactly one recorded 'D' door."));
            }
            if (ownsContract && !invalidRooms &&
                !DungeonTopologyValidator.HasValidDoors(rows, rooms, doors))
            {
                errors.Add(D(
                    "doors",
                    "For the current Donjon generator, every recorded door must have exactly two opposite walkable neighbors and valid room adjacency, and every room must have at least one valid recorded door."));
            }
            if (ownsContract &&
                !DungeonTopologyValidator.HasProducibleDoorRecords(doors))
            {
                errors.Add(D(
                    "doors",
                    "For the current Donjon generator, door records must use ordered door-0001-style IDs and generator door-sill parity with exactly one odd cell coordinate."));
            }
            bool invalidStairs =
                stairs.Select(stair => stair.Id).Distinct(StringComparer.Ordinal).Count() != stairs.Count ||
                stairs.Select(stair => stair.Kind).Distinct().Count() != stairs.Count ||
                stairs.Any(stair =>
                    !Walkable(stair.Cell) ||
                    !Walkable(stair.ArrivalCell) ||
                    Math.Abs(stair.Cell.X - stair.ArrivalCell.X) +
                    Math.Abs(stair.Cell.Z - stair.ArrivalCell.Z) != 1);
            if (invalidStairs)
            {
                errors.Add(D(
                    "stairs",
                    "Stair IDs and kinds must be unique, and each stair must have an adjacent walkable arrival cell."));
            }
            if (ownsContract &&
                !DungeonTopologyValidator.HasProducibleStairRecords(stairs))
            {
                errors.Add(D(
                    "stairs",
                    "For the current Donjon generator, stairs must be empty, one ordered stair-down/Down record, or ordered stair-down/Down then stair-up/Up records with distinct generator-aligned endpoint and arrival geometry."));
            }
            if (!invalidStairs && ownsContract && !invalidRooms && stairs.Any(stair =>
                         !DungeonTopologyValidator.MatchesStairEnd(
                             rows,
                             rooms,
                             stair.Cell,
                             stair.ArrivalCell)))
            {
                errors.Add(D(
                    "stairs",
                    "For the current Donjon generator, every stair must occupy a straight three-cell corridor end with all other surrounding endpoint cells blocked."));
            }
            if (ownsContract &&
                !DungeonTopologyValidator.HasProducibleSafeCells(
                    rows,
                    rooms,
                    stairs,
                    safe))
            {
                errors.Add(D(
                    "arrival.safeCells",
                    "For the current Donjon generator, safeCells must exactly preserve ordered stair arrivals, ordered room centers, and the conditional deterministic walkable fallback sequence."));
            }
            bool invalidObjects =
                objects.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != objects.Count ||
                objects.Any(item =>
                    !InBounds(item.Cell) ||
                    (item.Rotation != 0 &&
                     item.Rotation != 90 &&
                     item.Rotation != 180 &&
                     item.Rotation != 270));
            if (invalidObjects)
            {
                errors.Add(D(
                    "objects",
                    "Object IDs must be unique, cells must be in bounds, and rotations must be 0, 90, 180, or 270."));
            }
            HashSet<int> roomIds = new(rooms.Select(room => room.Id));
            bool duplicateEncounterIds =
                encounters.Select(plan => plan.Id).Distinct(StringComparer.Ordinal).Count() != encounters.Count;
            if (duplicateEncounterIds)
            {
                errors.Add(D("encounterPlans", "Encounter IDs must be unique."));
            }

            foreach (DungeonEncounterPlan plan in encounters)
            {
                DungeonRoom room = rooms.FirstOrDefault(candidate => candidate.Id == plan.RoomId);
                bool valid =
                    roomIds.Contains(plan.RoomId) &&
                    plan.Budget >= 0 &&
                    plan.SpawnCells.Count == plan.CreatureIds.Count &&
                    plan.SpawnCells.Distinct().Count() == plan.SpawnCells.Count &&
                    plan.SpawnCells.All(cell =>
                        Walkable(cell) &&
                        room != null &&
                        cell.X >= room.MinimumX &&
                        cell.X <= room.MaximumX &&
                        cell.Z >= room.MinimumZ &&
                        cell.Z <= room.MaximumZ);
                if (!valid)
                {
                    errors.Add(D(
                        "encounterPlans",
                        "Every encounter must reference a room, have a nonnegative budget, and pair each creature ID with one distinct walkable spawn cell inside that room."));
                }
            }
            if (runtime == null)
            {
                if (doors.Any(door => door.IsOpen))
                {
                    errors.Add(D("doors", "Door open flags must be false when runtime state is absent."));
                }

                if (encounters.Any(plan => plan.IsResolved))
                {
                    errors.Add(D("encounterPlans", "Encounter resolved flags must be false when runtime state is absent."));
                }
            }
            else
            {
                HashSet<string> doorIds = new(doors.Select(door => door.Id), StringComparer.Ordinal);
                HashSet<string> encounterIds = new(encounters.Select(plan => plan.Id), StringComparer.Ordinal);
                HashSet<string> openDoorIds = new(runtime.OpenDoorIds, StringComparer.Ordinal);
                HashSet<string> flaggedOpenDoorIds = new(
                    doors.Where(door => door.IsOpen).Select(door => door.Id),
                    StringComparer.Ordinal);
                if (openDoorIds.Count != runtime.OpenDoorIds.Count ||
                    runtime.OpenDoorIds.Any(id => !doorIds.Contains(id)))
                {
                    errors.Add(D("runtimeState.openDoorIds", "Open door IDs must be unique and reference generated doors."));
                }
                if (!openDoorIds.SetEquals(flaggedOpenDoorIds))
                {
                    errors.Add(D(
                        "runtimeState.openDoorIds",
                        "Open door IDs must exactly match doors whose isOpen flag is true."));
                }

                HashSet<string> resolvedEncounterIds = new(
                    runtime.ResolvedEncounterIds,
                    StringComparer.Ordinal);
                HashSet<string> flaggedResolvedEncounterIds = new(
                    encounters.Where(plan => plan.IsResolved).Select(plan => plan.Id),
                    StringComparer.Ordinal);
                if (resolvedEncounterIds.Count != runtime.ResolvedEncounterIds.Count ||
                    runtime.ResolvedEncounterIds.Any(id => !encounterIds.Contains(id)))
                {
                    errors.Add(D("runtimeState.resolvedEncounterIds", "Resolved encounter IDs must be unique and reference encounter plans."));
                }
                if (!resolvedEncounterIds.SetEquals(flaggedResolvedEncounterIds))
                {
                    errors.Add(D(
                        "runtimeState.resolvedEncounterIds",
                        "Resolved encounter IDs must exactly match encounter plans whose isResolved flag is true."));
                }

                HashSet<string> defeatedInstanceIds = new(
                    runtime.DefeatedCreatureIds,
                    StringComparer.Ordinal);
                if (defeatedInstanceIds.Count != runtime.DefeatedCreatureIds.Count)
                {
                    errors.Add(D("runtimeState.defeatedCreatureIds", "Defeated creature instance IDs must be unique."));
                }

                HashSet<string> liveInstanceIds = new(
                    runtime.Creatures.Select(creature => creature.InstanceId),
                    StringComparer.Ordinal);
                if (liveInstanceIds.Count != runtime.Creatures.Count)
                {
                    errors.Add(D("runtimeState.creatures", "Live creature instance IDs must be unique."));
                }
                if (runtime.Creatures.Select(creature => creature.Cell).Distinct().Count() !=
                    runtime.Creatures.Count)
                {
                    errors.Add(D(
                        "runtimeState.creatures",
                        "Live creature occupied cells must be unique."));
                }
                if (liveInstanceIds.Overlaps(defeatedInstanceIds))
                {
                    errors.Add(D(
                        "runtimeState.defeatedCreatureIds",
                        "Defeated creature instance IDs must be disjoint from live creature instance IDs."));
                }

                Dictionary<string, DungeonEncounterPlan> encounterById =
                    new(StringComparer.Ordinal);
                foreach (DungeonEncounterPlan plan in encounters)
                {
                    if (!encounterById.ContainsKey(plan.Id))
                        encounterById.Add(plan.Id, plan);
                }

                bool invalidLiveCreature = false;
                foreach (DungeonCreatureRuntimeState creature in runtime.Creatures)
                {
                    if (!encounterById.TryGetValue(creature.EncounterId, out DungeonEncounterPlan plan) ||
                        plan.IsResolved ||
                        !Walkable(creature.Cell) ||
                        !plan.CreatureIds.Contains(creature.CreatureId, StringComparer.Ordinal))
                    {
                        invalidLiveCreature = true;
                    }
                }

                bool exceedsPlannedMultiplicity = runtime.Creatures
                    .GroupBy(creature => (creature.EncounterId, creature.CreatureId))
                    .Any(group =>
                    {
                        if (!encounterById.TryGetValue(
                                group.Key.EncounterId,
                                out DungeonEncounterPlan plan))
                        {
                            return true;
                        }

                        int plannedCount = plan.CreatureIds.Count(
                            creatureId => string.Equals(
                                creatureId,
                                group.Key.CreatureId,
                                StringComparison.Ordinal));
                        return group.Count() > plannedCount;
                    });
                if (invalidLiveCreature || exceedsPlannedMultiplicity)
                {
                    errors.Add(D(
                        "runtimeState.creatures",
                        "Each live creature must occupy a walkable cell, reference an unresolved encounter, and match one available creature entry in that plan."));
                }
            }
        }
        private static int? Int(JToken token) => token?.Type == JTokenType.Integer && int.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        private static string String(JToken token) => token?.Value<string>() ?? string.Empty;
        private static DungeonGenerationDiagnostic D(string field, string message) => new(DungeonGenerationDiagnosticCode.InvalidDocument, field, message);
        private static DungeonLevelParseResult Invalid(string field, string message) => new(null, new[] { D(field, message) });
    }
}
