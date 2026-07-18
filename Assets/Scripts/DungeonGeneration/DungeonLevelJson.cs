using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>Result of parsing a version 2 level document.</summary>
    public sealed class DungeonLevelParseResult
    {
        /// <summary>Creates a parse result.</summary>
        public DungeonLevelParseResult(DungeonLevelDocument document, IEnumerable<DungeonGenerationDiagnostic> diagnostics)
        { Document = document; Diagnostics = diagnostics.ToArray(); }
        /// <summary>Gets the lossless document, or null when invalid.</summary>
        public DungeonLevelDocument Document { get; }
        /// <summary>Gets deterministic validation diagnostics.</summary>
        public IReadOnlyList<DungeonGenerationDiagnostic> Diagnostics { get; }
        /// <summary>Gets whether a complete document was parsed.</summary>
        public bool IsSuccess => Document != null && Diagnostics.Count == 0;
    }

    /// <summary>Writes deterministic byte-for-byte version 2 JSON with an explicitly controlled property order.</summary>
    public static class DungeonLevelJsonSerializer
    {
        /// <summary>Serializes a complete document using invariant compact JSON.</summary>
        public static string Serialize(DungeonLevelDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            StringBuilder output = new(4096);
            output.Append("{\"version\":2,\"generation\":{");
            Property(output, "algorithm", document.Generation.Algorithm); output.Append(',');
            NumberProperty(output, "algorithmVersion", document.Generation.AlgorithmVersion); output.Append(',');
            Property(output, "runSeed", document.Generation.RunSeed.ToString(CultureInfo.InvariantCulture)); output.Append(',');
            NumberProperty(output, "depth", document.Generation.Depth); output.Append(',');
            NumberProperty(output, "topologyAttempt", document.Generation.TopologyAttempt); output.Append(',');
            Property(output, "depthState", document.Generation.DepthState); output.Append(',');
            Property(output, "topologyState", document.Generation.TopologyState); output.Append("},\"rows\":[");
            Strings(output, document.Rows); output.Append("],\"rooms\":[");
            for (int i = 0; i < document.Rooms.Count; i++)
            {
                if (i > 0) output.Append(','); DungeonRoom room = document.Rooms[i];
                output.Append("{\"id\":").Append(I(room.Id)).Append(",\"minX\":").Append(I(room.MinimumX))
                    .Append(",\"minZ\":").Append(I(room.MinimumZ)).Append(",\"maxX\":").Append(I(room.MaximumX))
                    .Append(",\"maxZ\":").Append(I(room.MaximumZ)).Append('}');
            }
            output.Append("],\"doors\":[");
            for (int i = 0; i < document.Doors.Count; i++)
            {
                if (i > 0) output.Append(','); DungeonDoor door = document.Doors[i];
                output.Append('{'); Property(output, "id", door.Id); output.Append(",\"cell\":"); Cell(output, door.Cell);
                output.Append(",\"isOpen\":").Append(door.IsOpen ? "true" : "false").Append('}');
            }
            output.Append("],\"stairs\":[");
            for (int i = 0; i < document.Stairs.Count; i++)
            {
                if (i > 0) output.Append(','); DungeonStair stair = document.Stairs[i]; output.Append('{');
                Property(output, "id", stair.Id); output.Append(','); Property(output, "kind", stair.Kind == DungeonStairKind.Up ? "up" : "down");
                output.Append(",\"cell\":"); Cell(output, stair.Cell); output.Append(",\"arrivalCell\":"); Cell(output, stair.ArrivalCell); output.Append('}');
            }
            output.Append("],\"arrival\":{\"start\":"); Cell(output, document.StartCell); output.Append(",\"safeCells\":[");
            Cells(output, document.SafeCells); output.Append("]},\"objects\":[");
            for (int i = 0; i < document.Objects.Count; i++)
            {
                if (i > 0) output.Append(','); DungeonObjectPlacement item = document.Objects[i]; output.Append('{');
                Property(output, "id", item.Id); output.Append(','); Property(output, "assetId", item.AssetId);
                output.Append(",\"cell\":"); Cell(output, item.Cell); output.Append(",\"rotation\":").Append(I(item.Rotation));
                if (item.State != null) { output.Append(','); Property(output, "state", item.State); } output.Append('}');
            }
            output.Append("],\"encounterPlans\":[");
            for (int i = 0; i < document.EncounterPlans.Count; i++)
            {
                if (i > 0) output.Append(','); DungeonEncounterPlan plan = document.EncounterPlans[i]; output.Append('{');
                Property(output, "id", plan.Id); output.Append(",\"roomId\":").Append(I(plan.RoomId)).Append(",\"spawnCells\":[");
                Cells(output, plan.SpawnCells); output.Append("],\"creatureIds\":["); Strings(output, plan.CreatureIds);
                output.Append("],\"isResolved\":").Append(plan.IsResolved ? "true" : "false").Append('}');
            }
            output.Append(']');
            if (document.RuntimeState != null)
            {
                output.Append(",\"runtimeState\":{\"openDoorIds\":["); Strings(output, document.RuntimeState.OpenDoorIds);
                output.Append("],\"resolvedEncounterIds\":["); Strings(output, document.RuntimeState.ResolvedEncounterIds);
                output.Append("],\"defeatedCreatureIds\":["); Strings(output, document.RuntimeState.DefeatedCreatureIds);
                output.Append("],\"creatures\":[");
                for (int i = 0; i < document.RuntimeState.Creatures.Count; i++)
                {
                    if (i > 0) output.Append(','); DungeonCreatureRuntimeState creature = document.RuntimeState.Creatures[i]; output.Append('{');
                    Property(output, "instanceId", creature.InstanceId); output.Append(','); Property(output, "creatureId", creature.CreatureId);
                    output.Append(','); Property(output, "encounterId", creature.EncounterId); output.Append(",\"cell\":"); Cell(output, creature.Cell);
                    output.Append(",\"hitPoints\":").Append(I(creature.HitPoints)); if (creature.State != null) { output.Append(','); Property(output, "state", creature.State); } output.Append('}');
                }
                output.Append("]}");
            }
            return output.Append('}').ToString();
        }

        private static void Property(StringBuilder output, string name, string value) => output.Append(JsonConvert.ToString(name)).Append(':').Append(JsonConvert.ToString(value));
        private static void NumberProperty(StringBuilder output, string name, int value) => output.Append(JsonConvert.ToString(name)).Append(':').Append(I(value));
        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static void Cell(StringBuilder output, DungeonCell cell) => output.Append("{\"x\":").Append(I(cell.X)).Append(",\"z\":").Append(I(cell.Z)).Append('}');
        private static void Cells(StringBuilder output, IReadOnlyList<DungeonCell> cells) { for (int i = 0; i < cells.Count; i++) { if (i > 0) output.Append(','); Cell(output, cells[i]); } }
        private static void Strings(StringBuilder output, IReadOnlyList<string> values) { for (int i = 0; i < values.Count; i++) { if (i > 0) output.Append(','); output.Append(JsonConvert.ToString(values[i])); } }
    }

    /// <summary>Strict lossless parser for the deterministic version 2 contract.</summary>
    public static class DungeonLevelJsonParser
    {
        /// <summary>Parses and validates version 2 JSON without creating a partial document.</summary>
        public static DungeonLevelParseResult Parse(string json)
        {
            List<DungeonGenerationDiagnostic> errors = new(); JObject root;
            try { root = JObject.Parse(json ?? string.Empty); }
            catch (JsonException exception) { return Invalid("json", "JSON could not be parsed: " + exception.Message); }
            if (Int(root["version"]) != 2) return Invalid("version", "Dungeon level version must equal 2.");
            JObject generation = root["generation"] as JObject; JArray rowsToken = root["rows"] as JArray;
            if (generation == null) errors.Add(D("generation", "Generation metadata is required."));
            DungeonGenerationMetadata metadata = ReadGeneration(generation, errors);
            List<string> rows = ReadStrings(rowsToken, "rows", errors); ValidateRows(rows, errors);
            List<DungeonRoom> rooms = ReadRooms(root["rooms"] as JArray, errors);
            List<DungeonDoor> doors = ReadDoors(root["doors"] as JArray, errors);
            List<DungeonStair> stairs = ReadStairs(root["stairs"] as JArray, errors);
            JObject arrival = root["arrival"] as JObject; DungeonCell start = ReadCell(arrival?["start"], "arrival.start", errors);
            List<DungeonCell> safe = ReadCells(arrival?["safeCells"] as JArray, "arrival.safeCells", errors);
            List<DungeonObjectPlacement> objects = ReadObjects(root["objects"] as JArray, errors);
            List<DungeonEncounterPlan> encounters = ReadEncounters(root["encounterPlans"] as JArray, errors);
            DungeonRuntimeState runtime = ReadRuntime(root["runtimeState"], errors);
            ValidateDocument(rows, rooms, doors, stairs, start, safe, objects, encounters, runtime, errors);
            if (errors.Count > 0) return new DungeonLevelParseResult(null, errors);
            return new DungeonLevelParseResult(new DungeonLevelDocument(metadata, rows, rooms, doors, stairs, start, safe, objects, encounters, runtime), errors);
        }

        private static DungeonGenerationMetadata ReadGeneration(JObject source, List<DungeonGenerationDiagnostic> errors)
        {
            if (source == null) return null;
            string algorithm = RequiredString(source, "algorithm", "generation", errors);
            int algorithmVersion = RequiredInt(source, "algorithmVersion", "generation", errors);
            string seedText = RequiredString(source, "runSeed", "generation", errors);
            if (!long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long runSeed))
                errors.Add(D("generation.runSeed", "Run seed must be a signed 64-bit integer encoded as a JSON string."));
            int depth = RequiredInt(source, "depth", "generation", errors);
            int topologyAttempt = RequiredInt(source, "topologyAttempt", "generation", errors);
            string depthState = RequiredString(source, "depthState", "generation", errors);
            string topologyState = RequiredString(source, "topologyState", "generation", errors);
            if (depth < 0) errors.Add(D("generation.depth", "Depth must be zero or greater."));
            if (topologyAttempt < 0 || topologyAttempt >= DeterministicDungeonGenerator.MaximumAttempts)
                errors.Add(D("generation.topologyAttempt", "Topology attempt must be from 0 through 31."));
            if (!IsState(depthState)) errors.Add(D("generation.depthState", "Depth state must be exactly 16 hexadecimal digits."));
            if (!IsState(topologyState)) errors.Add(D("generation.topologyState", "Topology state must be exactly 16 hexadecimal digits."));
            return new DungeonGenerationMetadata(algorithm, algorithmVersion, runSeed, depth, topologyAttempt, depthState, topologyState);
        }

        private static List<DungeonRoom> ReadRooms(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "rooms", e, (o, p) => new DungeonRoom(RequiredInt(o, "id", p, e), RequiredInt(o, "minX", p, e), RequiredInt(o, "minZ", p, e), RequiredInt(o, "maxX", p, e), RequiredInt(o, "maxZ", p, e)));
        private static List<DungeonDoor> ReadDoors(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "doors", e, (o, p) => new DungeonDoor(RequiredString(o, "id", p, e), ReadCell(o["cell"], p + ".cell", e), RequiredBool(o, "isOpen", p, e)));
        private static List<DungeonStair> ReadStairs(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "stairs", e, (source, path) => ReadStair(source, path, e));
        private static List<DungeonObjectPlacement> ReadObjects(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "objects", e, (o, p) => new DungeonObjectPlacement(RequiredString(o, "id", p, e), RequiredString(o, "assetId", p, e), ReadCell(o["cell"], p + ".cell", e), RequiredInt(o, "rotation", p, e), o["state"]?.Type == JTokenType.String ? String(o["state"]) : null));
        private static List<DungeonEncounterPlan> ReadEncounters(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "encounterPlans", e, (o, p) => new DungeonEncounterPlan(RequiredString(o, "id", p, e), RequiredInt(o, "roomId", p, e), ReadCells(o["spawnCells"] as JArray, p + ".spawnCells", e), ReadStrings(o["creatureIds"] as JArray, p + ".creatureIds", e), RequiredBool(o, "isResolved", p, e)));
        private static DungeonStair ReadStair(JObject source, string path, List<DungeonGenerationDiagnostic> errors) { string kind = RequiredString(source, "kind", path, errors); if (kind != "up" && kind != "down") errors.Add(D(path + ".kind", "Stair kind must be 'up' or 'down'.")); return new DungeonStair(RequiredString(source, "id", path, errors), kind == "down" ? DungeonStairKind.Down : DungeonStairKind.Up, ReadCell(source["cell"], path + ".cell", errors), ReadCell(source["arrivalCell"], path + ".arrivalCell", errors)); }
        private static DungeonRuntimeState ReadRuntime(JToken token, List<DungeonGenerationDiagnostic> e) { if (token == null) return null; if (token is not JObject o) { e.Add(D("runtimeState", "Runtime state must be an object.")); return null; } return new DungeonRuntimeState(ReadStrings(o["openDoorIds"] as JArray, "runtimeState.openDoorIds", e), ReadStrings(o["resolvedEncounterIds"] as JArray, "runtimeState.resolvedEncounterIds", e), ReadStrings(o["defeatedCreatureIds"] as JArray, "runtimeState.defeatedCreatureIds", e), ReadCreatures(o["creatures"] as JArray, e)); }
        private static List<DungeonCreatureRuntimeState> ReadCreatures(JArray array, List<DungeonGenerationDiagnostic> e) => ReadObjects(array, "runtimeState.creatures", e, (o, p) => new DungeonCreatureRuntimeState(RequiredString(o, "instanceId", p, e), RequiredString(o, "creatureId", p, e), RequiredString(o, "encounterId", p, e), ReadCell(o["cell"], p + ".cell", e), RequiredInt(o, "hitPoints", p, e), o["state"]?.Type == JTokenType.String ? String(o["state"]) : null));
        private static List<T> ReadObjects<T>(JArray array, string field, List<DungeonGenerationDiagnostic> e, Func<JObject, string, T> read) { List<T> result = new(); if (array == null) { e.Add(D(field, field + " must be an array.")); return result; } for (int i = 0; i < array.Count; i++) { string path = field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]"; if (array[i] is JObject o) result.Add(read(o, path)); else e.Add(D(path, "Entry must be an object.")); } return result; }
        private static List<string> ReadStrings(JArray array, string field, List<DungeonGenerationDiagnostic> e) { List<string> result = new(); if (array == null) { e.Add(D(field, field + " must be an array.")); return result; } for (int i = 0; i < array.Count; i++) { if (array[i].Type == JTokenType.String) result.Add(String(array[i])); else e.Add(D(field, "Every entry must be a string.")); } return result; }
        private static List<DungeonCell> ReadCells(JArray array, string field, List<DungeonGenerationDiagnostic> e) { List<DungeonCell> result = new(); if (array == null) { e.Add(D(field, field + " must be an array.")); return result; } for (int i = 0; i < array.Count; i++) result.Add(ReadCell(array[i], field + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", e)); return result; }
        private static DungeonCell ReadCell(JToken token, string field, List<DungeonGenerationDiagnostic> e) { if (token is not JObject o) { e.Add(D(field, "Cell must be an object with integer x and z.")); return default; } return new DungeonCell(RequiredInt(o, "x", field, e), RequiredInt(o, "z", field, e)); }
        private static void ValidateRows(List<string> rows, List<DungeonGenerationDiagnostic> e) { if (rows.Count == 0) { e.Add(D("rows", "At least one row is required.")); return; } int width = rows[0].Length; if (width == 0) e.Add(D("rows", "Rows must not be empty.")); for (int z = 0; z < rows.Count; z++) if (rows[z].Length != width || rows[z].Any(c => c != ' ' && c != '#' && c != '.' && c != 'D')) e.Add(D("rows", "Rows must have equal width and use only space, '#', '.', and 'D'.")); }
        private static int RequiredInt(JObject o, string name, string path, List<DungeonGenerationDiagnostic> e) { int? value = Int(o[name]); if (!value.HasValue) e.Add(D(path + "." + name, "An integer is required.")); return value ?? 0; }
        private static string RequiredString(JObject o, string name, string path, List<DungeonGenerationDiagnostic> e) { string value = o[name]?.Type == JTokenType.String ? String(o[name]) : null; if (string.IsNullOrEmpty(value)) e.Add(D(path + "." + name, "A non-empty string is required.")); return value ?? string.Empty; }
        private static bool RequiredBool(JObject o, string name, string path, List<DungeonGenerationDiagnostic> e) { if (o[name]?.Type != JTokenType.Boolean) { e.Add(D(path + "." + name, "A boolean is required.")); return false; } return o[name].Value<bool>(); }
        private static void ValidateDocument(IReadOnlyList<string> rows, IReadOnlyList<DungeonRoom> rooms, IReadOnlyList<DungeonDoor> doors, IReadOnlyList<DungeonStair> stairs, DungeonCell start, IReadOnlyList<DungeonCell> safe, IReadOnlyList<DungeonObjectPlacement> objects, IReadOnlyList<DungeonEncounterPlan> encounters, DungeonRuntimeState runtime, List<DungeonGenerationDiagnostic> errors)
        {
            if (rows.Count == 0) return;
            bool InBounds(DungeonCell c) => c.X >= 0 && c.Z >= 0 && c.X < rows[0].Length && c.Z < rows.Count;
            char Symbol(DungeonCell c) => rows[rows.Count - 1 - c.Z][c.X];
            bool Walkable(DungeonCell c) => InBounds(c) && (Symbol(c) == '.' || Symbol(c) == 'D');
            if (!Walkable(start)) errors.Add(D("arrival.start", "Start must reference a walkable cell."));
            if (safe.Count == 0 || safe.Any(cell => !Walkable(cell))) errors.Add(D("arrival.safeCells", "At least one safe cell is required and every safe cell must be walkable."));
            if (rooms.Select(room => room.Id).Distinct().Count() != rooms.Count || rooms.Any(room => room.Id < 1 || room.MinimumX > room.MaximumX || room.MinimumZ > room.MaximumZ || !InBounds(new DungeonCell(room.MinimumX, room.MinimumZ)) || !InBounds(new DungeonCell(room.MaximumX, room.MaximumZ)))) errors.Add(D("rooms", "Room IDs must be unique positive integers with ordered in-bounds bounds."));
            if (doors.Select(door => door.Id).Distinct(StringComparer.Ordinal).Count() != doors.Count || doors.Any(door => !InBounds(door.Cell) || Symbol(door.Cell) != 'D')) errors.Add(D("doors", "Door IDs must be unique and every door cell must contain 'D'."));
            if (stairs.Select(stair => stair.Id).Distinct(StringComparer.Ordinal).Count() != stairs.Count || stairs.Any(stair => !Walkable(stair.Cell) || !Walkable(stair.ArrivalCell))) errors.Add(D("stairs", "Stair IDs must be unique and stair/arrival cells must be walkable."));
            if (objects.Any(item => !InBounds(item.Cell))) errors.Add(D("objects", "Every object cell must be in bounds."));
            HashSet<int> roomIds = new(rooms.Select(room => room.Id));
            if (encounters.Any(plan => !roomIds.Contains(plan.RoomId) || plan.SpawnCells.Any(cell => !Walkable(cell)))) errors.Add(D("encounterPlans", "Every encounter must reference a room and use walkable spawn cells."));
            if (runtime != null)
            {
                HashSet<string> doorIds = new(doors.Select(door => door.Id), StringComparer.Ordinal);
                HashSet<string> encounterIds = new(encounters.Select(plan => plan.Id), StringComparer.Ordinal);
                if (runtime.OpenDoorIds.Any(id => !doorIds.Contains(id))) errors.Add(D("runtimeState.openDoorIds", "Every open door ID must reference a generated door."));
                if (runtime.ResolvedEncounterIds.Any(id => !encounterIds.Contains(id))) errors.Add(D("runtimeState.resolvedEncounterIds", "Every resolved encounter ID must reference an encounter plan."));
                if (runtime.Creatures.Select(creature => creature.InstanceId).Distinct(StringComparer.Ordinal).Count() != runtime.Creatures.Count || runtime.Creatures.Any(creature => !encounterIds.Contains(creature.EncounterId) || !Walkable(creature.Cell))) errors.Add(D("runtimeState.creatures", "Creature instance IDs must be unique and each creature must reference an encounter and walkable cell."));
            }
        }
        private static bool IsState(string value) => value?.Length == 16 && ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        private static int? Int(JToken token) => token?.Type == JTokenType.Integer && int.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        private static string String(JToken token) => token?.Value<string>() ?? string.Empty;
        private static DungeonGenerationDiagnostic D(string field, string message) => new(DungeonGenerationDiagnosticCode.InvalidDocument, field, message);
        private static DungeonLevelParseResult Invalid(string field, string message) => new(null, new[] { D(field, message) });
    }
}
