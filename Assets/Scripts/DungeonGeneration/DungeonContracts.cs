using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>Controls the playable outline applied before rooms and corridors are carved.</summary>
    public enum DungeonLayout
    {
        /// <summary>Uses the Donjon box mask, leaving a connected band around a blocked center.</summary>
        Box,
        /// <summary>Uses intersecting horizontal and vertical playable bands.</summary>
        Cross,
        /// <summary>Uses an ellipse fitted inside the requested rectangular dimensions.</summary>
        Round
    }

    /// <summary>Controls whether rooms are attempted throughout the map or in a sparse random sample.</summary>
    public enum DungeonRoomLayout
    {
        /// <summary>Attempts rooms at most eligible odd grid anchors.</summary>
        Packed,
        /// <summary>Attempts a bounded random sample of room anchors.</summary>
        Scattered
    }

    /// <summary>Controls the probability that a corridor continues in its current direction.</summary>
    public enum DungeonCorridorLayout
    {
        /// <summary>Applies no preference for continuing in the previous direction.</summary>
        Labyrinth,
        /// <summary>Continues straight on half of eligible direction selections.</summary>
        Bent,
        /// <summary>Always tries the previous direction first when it remains eligible.</summary>
        Straight
    }

    /// <summary>Identifies a stair's traversal direction.</summary>
    public enum DungeonStairKind
    {
        /// <summary>Traverses toward the previous depth.</summary>
        Up,
        /// <summary>Traverses toward the next depth.</summary>
        Down
    }

    /// <summary>Integer grid coordinate using KayKit's X and Z axes.</summary>
    public readonly struct DungeonCell : IEquatable<DungeonCell>
    {
        /// <summary>Creates a coordinate.</summary>
        public DungeonCell(int x, int z) { X = x; Z = z; }
        /// <summary>Gets the horizontal X coordinate.</summary>
        public int X { get; }
        /// <summary>Gets the horizontal Z coordinate, where serialized rows list the highest Z first.</summary>
        public int Z { get; }
        /// <inheritdoc/>
        public bool Equals(DungeonCell other) => X == other.X && Z == other.Z;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DungeonCell other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => unchecked((X * 397) ^ Z);
        /// <summary>Compares two coordinates.</summary>
        public static bool operator ==(DungeonCell left, DungeonCell right) => left.Equals(right);
        /// <summary>Compares two coordinates.</summary>
        public static bool operator !=(DungeonCell left, DungeonCell right) => !left.Equals(right);
    }

    /// <summary>Describes one deterministic dungeon level request.</summary>
    public sealed class DungeonGenerationRequest
    {
        /// <summary>Gets or sets the signed run seed whose exact two's-complement bits initialize depth zero.</summary>
        public long RunSeed { get; set; }
        /// <summary>Gets or sets the zero-based dungeon depth.</summary>
        public int Depth { get; set; }
        /// <summary>Gets or sets the odd map width, including the blocked boundary.</summary>
        public int Width { get; set; } = 39;
        /// <summary>Gets or sets the odd map height, including the blocked boundary.</summary>
        public int Height { get; set; } = 39;
        /// <summary>Gets or sets the initialization mask.</summary>
        public DungeonLayout Layout { get; set; } = DungeonLayout.Box;
        /// <summary>Gets or sets the room placement strategy.</summary>
        public DungeonRoomLayout RoomLayout { get; set; } = DungeonRoomLayout.Scattered;
        /// <summary>Gets or sets the corridor continuation bias.</summary>
        public DungeonCorridorLayout CorridorLayout { get; set; } = DungeonCorridorLayout.Bent;
        /// <summary>Gets or sets the minimum odd room side length.</summary>
        public int MinimumRoomSize { get; set; } = 3;
        /// <summary>Gets or sets the maximum odd room side length.</summary>
        public int MaximumRoomSize { get; set; } = 9;
        /// <summary>Gets or sets the minimum room count required for topology acceptance.</summary>
        public int MinimumRoomCount { get; set; } = 1;
        /// <summary>Gets or sets the requested stair count; zero, one, or two.</summary>
        public int StairCount { get; set; } = 2;
        /// <summary>Gets or sets the percentage of eligible corridor dead ends recursively removed.</summary>
        public int DeadEndRemovalPercent { get; set; } = 50;
    }

    /// <summary>Machine-readable category for generation failures.</summary>
    public enum DungeonGenerationDiagnosticCode
    {
        /// <summary>The request violates a documented input constraint.</summary>
        InvalidRequest,
        /// <summary>One deterministic attempt failed a topology invariant.</summary>
        TopologyRejected,
        /// <summary>All permitted deterministic attempts were rejected.</summary>
        RetryLimitExhausted,
        /// <summary>A version 2 JSON document violates its schema or semantic invariants.</summary>
        InvalidDocument
    }

    /// <summary>Actionable deterministic information about a failed request or topology attempt.</summary>
    public sealed class DungeonGenerationDiagnostic
    {
        /// <summary>Creates a diagnostic.</summary>
        public DungeonGenerationDiagnostic(DungeonGenerationDiagnosticCode code, string field, string message, int? attempt = null)
        {
            Code = code; Field = field ?? string.Empty; Message = message ?? string.Empty; Attempt = attempt;
        }
        /// <summary>Gets the stable category.</summary>
        public DungeonGenerationDiagnosticCode Code { get; }
        /// <summary>Gets the request field or generation stage associated with the failure.</summary>
        public string Field { get; }
        /// <summary>Gets an actionable invariant-focused explanation.</summary>
        public string Message { get; }
        /// <summary>Gets the zero-based rejected attempt when applicable.</summary>
        public int? Attempt { get; }
    }

    /// <summary>Result of generation; a failed result never exposes a partial document.</summary>
    public sealed class DungeonGenerationResult
    {
        /// <summary>Creates a result from a complete document or diagnostics.</summary>
        public DungeonGenerationResult(DungeonLevelDocument document, IEnumerable<DungeonGenerationDiagnostic> diagnostics)
        {
            Document = document; Diagnostics = (diagnostics ?? Array.Empty<DungeonGenerationDiagnostic>()).ToArray();
        }
        /// <summary>Gets the complete accepted document, or null on failure.</summary>
        public DungeonLevelDocument Document { get; }
        /// <summary>Gets deterministic diagnostics.</summary>
        public IReadOnlyList<DungeonGenerationDiagnostic> Diagnostics { get; }
        /// <summary>Gets whether generation produced a complete document.</summary>
        public bool IsSuccess => Document != null && Diagnostics.Count == 0;
    }

    /// <summary>Pure service contract for deterministic dungeon topology generation.</summary>
    public interface IDungeonGenerator
    {
        /// <summary>Generates a complete level or returns diagnostics without a partial map.</summary>
        DungeonGenerationResult Generate(DungeonGenerationRequest request);
    }

    /// <summary>Stable generation provenance written into every version 2 document.</summary>
    public sealed class DungeonGenerationMetadata
    {
        /// <summary>Creates generation metadata.</summary>
        public DungeonGenerationMetadata(string algorithm, int algorithmVersion, long runSeed, int depth, int topologyAttempt, string depthState, string topologyState)
        {
            Algorithm = algorithm; AlgorithmVersion = algorithmVersion; RunSeed = runSeed; Depth = depth;
            TopologyAttempt = topologyAttempt; DepthState = depthState; TopologyState = topologyState;
        }
        /// <summary>Gets the stable algorithm identifier.</summary>
        public string Algorithm { get; }
        /// <summary>Gets the algorithm contract version.</summary>
        public int AlgorithmVersion { get; }
        /// <summary>Gets the original signed run seed.</summary>
        public long RunSeed { get; }
        /// <summary>Gets the requested depth.</summary>
        public int Depth { get; }
        /// <summary>Gets the accepted zero-based topology attempt.</summary>
        public int TopologyAttempt { get; }
        /// <summary>Gets the depth state as fixed-width hexadecimal.</summary>
        public string DepthState { get; }
        /// <summary>Gets the accepted topology stream state as fixed-width hexadecimal.</summary>
        public string TopologyState { get; }
    }

    /// <summary>Stable inclusive room bounds and identifier.</summary>
    public sealed class DungeonRoom
    {
        /// <summary>Creates a room record.</summary>
        public DungeonRoom(int id, int minimumX, int minimumZ, int maximumX, int maximumZ)
        { Id = id; MinimumX = minimumX; MinimumZ = minimumZ; MaximumX = maximumX; MaximumZ = maximumZ; }
        /// <summary>Gets the stable one-based ID.</summary>
        public int Id { get; }
        /// <summary>Gets the inclusive minimum X.</summary>
        public int MinimumX { get; }
        /// <summary>Gets the inclusive minimum Z.</summary>
        public int MinimumZ { get; }
        /// <summary>Gets the inclusive maximum X.</summary>
        public int MaximumX { get; }
        /// <summary>Gets the inclusive maximum Z.</summary>
        public int MaximumZ { get; }
    }

    /// <summary>Unlocked mutable doorway generated at a room sill.</summary>
    public sealed class DungeonDoor
    {
        /// <summary>Creates a door record.</summary>
        public DungeonDoor(string id, DungeonCell cell, bool isOpen = false) { Id = id; Cell = cell; IsOpen = isOpen; }
        /// <summary>Gets the stable ID.</summary>
        public string Id { get; }
        /// <summary>Gets the door cell.</summary>
        public DungeonCell Cell { get; }
        /// <summary>Gets the optional runtime open state; generated doors start closed and unlocked.</summary>
        public bool IsOpen { get; }
    }

    /// <summary>Stair endpoint and its same-level arrival cell.</summary>
    public sealed class DungeonStair
    {
        /// <summary>Creates a stair record.</summary>
        public DungeonStair(string id, DungeonStairKind kind, DungeonCell cell, DungeonCell arrivalCell)
        { Id = id; Kind = kind; Cell = cell; ArrivalCell = arrivalCell; }
        /// <summary>Gets the stable ID.</summary>
        public string Id { get; }
        /// <summary>Gets the traversal direction.</summary>
        public DungeonStairKind Kind { get; }
        /// <summary>Gets the stair cell.</summary>
        public DungeonCell Cell { get; }
        /// <summary>Gets the walkable cell where a traversing creature arrives.</summary>
        public DungeonCell ArrivalCell { get; }
    }

    /// <summary>Deterministic prop placement reserved for the decoration child feature.</summary>
    public sealed class DungeonObjectPlacement
    {
        /// <summary>Creates an object placement.</summary>
        public DungeonObjectPlacement(string id, string assetId, DungeonCell cell, int rotation = 0, string state = null)
        { Id = id; AssetId = assetId; Cell = cell; Rotation = rotation; State = state; }
        /// <summary>Gets the stable placement ID.</summary>
        public string Id { get; }
        /// <summary>Gets the project catalog asset ID.</summary>
        public string AssetId { get; }
        /// <summary>Gets the anchor cell.</summary>
        public DungeonCell Cell { get; }
        /// <summary>Gets clockwise rotation in degrees.</summary>
        public int Rotation { get; }
        /// <summary>Gets optional mutable object state.</summary>
        public string State { get; }
    }

    /// <summary>Encounter plan reserved for deterministic composition and later spawn realization.</summary>
    public sealed class DungeonEncounterPlan
    {
        /// <summary>Creates an encounter plan.</summary>
        public DungeonEncounterPlan(string id, int roomId, IEnumerable<DungeonCell> spawnCells, IEnumerable<string> creatureIds, bool isResolved = false)
        { Id = id; RoomId = roomId; SpawnCells = spawnCells.ToArray(); CreatureIds = creatureIds.ToArray(); IsResolved = isResolved; }
        /// <summary>Gets the stable encounter ID.</summary>
        public string Id { get; }
        /// <summary>Gets the containing room ID.</summary>
        public int RoomId { get; }
        /// <summary>Gets ordered candidate spawn cells.</summary>
        public IReadOnlyList<DungeonCell> SpawnCells { get; }
        /// <summary>Gets ordered creature content IDs.</summary>
        public IReadOnlyList<string> CreatureIds { get; }
        /// <summary>Gets whether runtime play has resolved this encounter.</summary>
        public bool IsResolved { get; }
    }

    /// <summary>Optional mutable state kept separate from immutable generation facts.</summary>
    public sealed class DungeonRuntimeState
    {
        /// <summary>Creates runtime state from stable IDs.</summary>
        public DungeonRuntimeState(IEnumerable<string> openDoorIds, IEnumerable<string> resolvedEncounterIds, IEnumerable<string> defeatedCreatureIds, IEnumerable<DungeonCreatureRuntimeState> creatures = null)
        { OpenDoorIds = openDoorIds.ToArray(); ResolvedEncounterIds = resolvedEncounterIds.ToArray(); DefeatedCreatureIds = defeatedCreatureIds.ToArray(); Creatures = (creatures ?? Array.Empty<DungeonCreatureRuntimeState>()).ToArray(); }
        /// <summary>Gets open door IDs.</summary>
        public IReadOnlyList<string> OpenDoorIds { get; }
        /// <summary>Gets resolved encounter IDs.</summary>
        public IReadOnlyList<string> ResolvedEncounterIds { get; }
        /// <summary>Gets defeated or removed creature instance IDs.</summary>
        public IReadOnlyList<string> DefeatedCreatureIds { get; }
        /// <summary>Gets mutable state for creature instances that still exist on the level.</summary>
        public IReadOnlyList<DungeonCreatureRuntimeState> Creatures { get; }
    }

    /// <summary>Mutable state for one stable creature instance created from an encounter plan.</summary>
    public sealed class DungeonCreatureRuntimeState
    {
        /// <summary>Creates creature runtime state.</summary>
        public DungeonCreatureRuntimeState(string instanceId, string creatureId, string encounterId, DungeonCell cell, int hitPoints, string state)
        { InstanceId = instanceId; CreatureId = creatureId; EncounterId = encounterId; Cell = cell; HitPoints = hitPoints; State = state; }
        /// <summary>Gets the stable spawned instance ID.</summary>
        public string InstanceId { get; }
        /// <summary>Gets the immutable creature content ID.</summary>
        public string CreatureId { get; }
        /// <summary>Gets the encounter plan that created this instance.</summary>
        public string EncounterId { get; }
        /// <summary>Gets the creature's current cell.</summary>
        public DungeonCell Cell { get; }
        /// <summary>Gets current hit points; downstream rules own maximums and condition math.</summary>
        public int HitPoints { get; }
        /// <summary>Gets an optional deterministic state token for child persistence implementations.</summary>
        public string State { get; }
    }
}
