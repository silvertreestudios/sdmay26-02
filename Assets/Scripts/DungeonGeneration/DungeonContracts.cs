using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("EditModeAssembly")]

namespace Game.DungeonGeneration
{
    /// <summary>Controls the playable outline applied before rooms and corridors are carved.</summary>
    public enum DungeonLayout
    {
        /// <summary>Uses Donjon's 3-by-3 box mask, leaving a blocked central third.</summary>
        Box,
        /// <summary>Uses Donjon's 3-by-3 cross mask, leaving the four corner thirds blocked.</summary>
        Cross,
        /// <summary>Uses Donjon's circular mask centered in the requested cell grid.</summary>
        Round
    }

    /// <summary>Controls whether rooms are attempted at packed grid anchors or at scattered random positions.</summary>
    public enum DungeonRoomLayout
    {
        /// <summary>Attempts one room at each eligible coarse-grid anchor in stable row-major order.</summary>
        Packed,
        /// <summary>Attempts the Donjon area-derived number of randomly positioned rooms.</summary>
        Scattered
    }

    /// <summary>Controls the probability that recursive corridor tunneling retries its prior direction first.</summary>
    public enum DungeonCorridorLayout
    {
        /// <summary>Applies no preference for continuing in the previous direction.</summary>
        Labyrinth,
        /// <summary>Prepends the previous direction on half of recursive direction selections.</summary>
        Bent,
        /// <summary>Always prepends the previous direction after the first tunnel step.</summary>
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

    /// <summary>Identifies the PF2e threat category recorded by a deterministic encounter plan.</summary>
    public enum DungeonEncounterThreat
    {
        /// <summary>A trivial-threat encounter.</summary>
        Trivial,
        /// <summary>A low-threat encounter.</summary>
        Low,
        /// <summary>A moderate-threat encounter.</summary>
        Moderate
    }

    /// <summary>Represents an integer grid coordinate using KayKit's horizontal X and Z axes.</summary>
    public readonly struct DungeonCell : IEquatable<DungeonCell>
    {
        /// <summary>Creates a coordinate without applying map bounds.</summary>
        /// <param name="x">The horizontal X coordinate.</param>
        /// <param name="z">The horizontal Z coordinate.</param>
        public DungeonCell(int x, int z)
        {
            X = x;
            Z = z;
        }

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

        /// <summary>Tests two coordinates for value equality.</summary>
        /// <param name="left">The first coordinate.</param>
        /// <param name="right">The second coordinate.</param>
        /// <returns><see langword="true"/> when both axes are equal.</returns>
        public static bool operator ==(DungeonCell left, DungeonCell right) => left.Equals(right);

        /// <summary>Tests two coordinates for value inequality.</summary>
        /// <param name="left">The first coordinate.</param>
        /// <param name="right">The second coordinate.</param>
        /// <returns><see langword="true"/> when either axis differs.</returns>
        public static bool operator !=(DungeonCell left, DungeonCell right) => !left.Equals(right);
    }

    /// <summary>Describes one deterministic dungeon level request and its topology constraints.</summary>
    public sealed class DungeonGenerationRequest
    {
        /// <summary>Gets or sets the signed run seed whose exact two's-complement bits initialize depth zero.</summary>
        public long RunSeed { get; set; }

        /// <summary>Gets or sets the zero-based dungeon depth; generation rejects negative values.</summary>
        public int Depth { get; set; }

        /// <summary>Gets or sets the odd map width, including its outermost cell columns.</summary>
        public int Width { get; set; } = 39;

        /// <summary>Gets or sets the odd map height, including its outermost cell rows.</summary>
        public int Height { get; set; } = 39;

        /// <summary>Gets or sets the initialization mask.</summary>
        public DungeonLayout Layout { get; set; } = DungeonLayout.Box;

        /// <summary>Gets or sets the room placement strategy.</summary>
        public DungeonRoomLayout RoomLayout { get; set; } = DungeonRoomLayout.Scattered;

        /// <summary>Gets or sets the recursive corridor continuation bias.</summary>
        public DungeonCorridorLayout CorridorLayout { get; set; } = DungeonCorridorLayout.Bent;

        /// <summary>Gets or sets the minimum odd room side length in cells.</summary>
        public int MinimumRoomSize { get; set; } = 3;

        /// <summary>Gets or sets the maximum odd room side length in cells.</summary>
        public int MaximumRoomSize { get; set; } = 9;

        /// <summary>Gets or sets the minimum room count required before a topology attempt can be accepted.</summary>
        public int MinimumRoomCount { get; set; } = 1;

        /// <summary>Gets or sets the requested stair count; supported values are zero, one, and two.</summary>
        public int StairCount { get; set; } = 2;

        /// <summary>Gets or sets the percentage of eligible corridor anchors whose dead ends are recursively removed.</summary>
        public int DeadEndRemovalPercent { get; set; } = 50;
    }

    /// <summary>Provides a machine-readable category for generation and version 2 validation failures.</summary>
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

    /// <summary>Provides actionable deterministic information about a failed request, topology attempt, or document.</summary>
    public sealed class DungeonGenerationDiagnostic
    {
        /// <summary>Creates a diagnostic whose strings are normalized to non-null values.</summary>
        /// <param name="code">The stable failure category.</param>
        /// <param name="field">The request field, JSON path, or generation stage associated with the failure.</param>
        /// <param name="message">An invariant-focused explanation suitable for logs or user-facing error presentation.</param>
        /// <param name="attempt">The zero-based rejected topology attempt, or absence when no attempt applies.</param>
        public DungeonGenerationDiagnostic(
            DungeonGenerationDiagnosticCode code,
            string field,
            string message,
            int? attempt = null)
        {
            Code = code;
            Field = field ?? string.Empty;
            Message = message ?? string.Empty;
            Attempt = attempt;
        }

        /// <summary>Gets the stable category.</summary>
        public DungeonGenerationDiagnosticCode Code { get; }

        /// <summary>Gets the request field, JSON path, or generation stage associated with the failure.</summary>
        public string Field { get; }

        /// <summary>Gets an actionable invariant-focused explanation.</summary>
        public string Message { get; }

        /// <summary>Gets the zero-based rejected attempt when applicable.</summary>
        public int? Attempt { get; }
    }

    /// <summary>Represents generation success or failure; a failed result never exposes a partial document.</summary>
    public sealed class DungeonGenerationResult
    {
        /// <summary>Creates a result from a complete document or deterministic diagnostics.</summary>
        /// <param name="document">The accepted document, or absence on failure.</param>
        /// <param name="diagnostics">Diagnostics to copy; a null sequence is treated as empty.</param>
        public DungeonGenerationResult(
            DungeonLevelDocument document,
            IEnumerable<DungeonGenerationDiagnostic> diagnostics)
        {
            Document = document;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? Array.Empty<DungeonGenerationDiagnostic>()).ToArray());
        }

        /// <summary>Gets the complete accepted document, or absence on failure.</summary>
        public DungeonLevelDocument Document { get; }

        /// <summary>Gets an immutable snapshot of deterministic diagnostics.</summary>
        public IReadOnlyList<DungeonGenerationDiagnostic> Diagnostics { get; }

        /// <summary>Gets whether generation produced a complete document with no diagnostics.</summary>
        public bool IsSuccess => Document != null && Diagnostics.Count == 0;
    }

    /// <summary>Defines a pure service contract for deterministic dungeon topology generation.</summary>
    public interface IDungeonGenerator
    {
        /// <summary>Generates a complete level or returns diagnostics without a partial map.</summary>
        /// <param name="request">The explicit seed, topology options, and acceptance constraints.</param>
        /// <returns>A successful immutable document or a failed result containing actionable diagnostics.</returns>
        DungeonGenerationResult Generate(DungeonGenerationRequest request);
    }

    /// <summary>Records stable generation provenance in every version 2 document.</summary>
    public sealed class DungeonGenerationMetadata
    {
        /// <summary>Creates the provenance captured for one accepted topology attempt.</summary>
        /// <param name="algorithm">The stable algorithm identifier.</param>
        /// <param name="algorithmVersion">The positive algorithm contract version.</param>
        /// <param name="runSeed">The original signed run seed.</param>
        /// <param name="depth">The nonnegative requested depth.</param>
        /// <param name="topologyAttempt">The accepted zero-based retry attempt.</param>
        /// <param name="depthState">The fixed-width hexadecimal depth output.</param>
        /// <param name="topologyState">The fixed-width hexadecimal topology stream state.</param>
        public DungeonGenerationMetadata(
            string algorithm,
            int algorithmVersion,
            long runSeed,
            int depth,
            int topologyAttempt,
            string depthState,
            string topologyState)
        {
            Algorithm = algorithm;
            AlgorithmVersion = algorithmVersion;
            RunSeed = runSeed;
            Depth = depth;
            TopologyAttempt = topologyAttempt;
            DepthState = depthState;
            TopologyState = topologyState;
        }

        /// <summary>Gets the stable algorithm identifier.</summary>
        public string Algorithm { get; }

        /// <summary>Gets the algorithm contract version.</summary>
        public int AlgorithmVersion { get; }

        /// <summary>Gets the original signed run seed.</summary>
        public long RunSeed { get; }

        /// <summary>Gets the requested nonnegative depth.</summary>
        public int Depth { get; }

        /// <summary>Gets the accepted zero-based topology attempt.</summary>
        public int TopologyAttempt { get; }

        /// <summary>Gets the depth output as exactly 16 hexadecimal digits.</summary>
        public string DepthState { get; }

        /// <summary>Gets the accepted topology stream state as exactly 16 hexadecimal digits.</summary>
        public string TopologyState { get; }
    }

    /// <summary>Records one room's stable positive identifier and inclusive bounds.</summary>
    public sealed class DungeonRoom
    {
        /// <summary>Creates a room record without interpreting cells outside the containing document.</summary>
        /// <param name="id">The stable positive room identifier.</param>
        /// <param name="minimumX">The inclusive minimum X coordinate.</param>
        /// <param name="minimumZ">The inclusive minimum Z coordinate.</param>
        /// <param name="maximumX">The inclusive maximum X coordinate.</param>
        /// <param name="maximumZ">The inclusive maximum Z coordinate.</param>
        public DungeonRoom(int id, int minimumX, int minimumZ, int maximumX, int maximumZ)
        {
            Id = id;
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
        }

        /// <summary>Gets the stable positive ID.</summary>
        public int Id { get; }

        /// <summary>Gets the inclusive minimum X coordinate.</summary>
        public int MinimumX { get; }

        /// <summary>Gets the inclusive minimum Z coordinate.</summary>
        public int MinimumZ { get; }

        /// <summary>Gets the inclusive maximum X coordinate.</summary>
        public int MaximumX { get; }

        /// <summary>Gets the inclusive maximum Z coordinate.</summary>
        public int MaximumZ { get; }
    }

    /// <summary>Records one stable unlocked doorway generated at a room sill.</summary>
    public sealed class DungeonDoor
    {
        /// <summary>Creates a door record.</summary>
        /// <param name="id">The stable non-empty door identifier.</param>
        /// <param name="cell">The unique cell represented by a <c>D</c> row symbol.</param>
        /// <param name="isOpen">Whether persisted runtime state currently considers the door open.</param>
        public DungeonDoor(string id, DungeonCell cell, bool isOpen = false)
        {
            Id = id;
            Cell = cell;
            IsOpen = isOpen;
        }

        /// <summary>Gets the stable ID.</summary>
        public string Id { get; }

        /// <summary>Gets the unique door cell.</summary>
        public DungeonCell Cell { get; }

        /// <summary>
        /// Gets the persisted open state. It is mirrored exactly by
        /// <see cref="DungeonRuntimeState.OpenDoorIds"/> when runtime state exists and must be
        /// <see langword="false"/> for a pristine document without runtime state.
        /// </summary>
        public bool IsOpen { get; }
    }

    /// <summary>Records a stair endpoint and its walkable same-level arrival cell.</summary>
    public sealed class DungeonStair
    {
        /// <summary>Creates a stair record.</summary>
        /// <param name="id">The stable non-empty stair identifier.</param>
        /// <param name="kind">The depth traversal direction.</param>
        /// <param name="cell">The walkable stair endpoint.</param>
        /// <param name="arrivalCell">The adjacent walkable cell used for party arrival.</param>
        public DungeonStair(string id, DungeonStairKind kind, DungeonCell cell, DungeonCell arrivalCell)
        {
            Id = id;
            Kind = kind;
            Cell = cell;
            ArrivalCell = arrivalCell;
        }

        /// <summary>Gets the stable ID.</summary>
        public string Id { get; }

        /// <summary>Gets the traversal direction.</summary>
        public DungeonStairKind Kind { get; }

        /// <summary>Gets the walkable stair endpoint.</summary>
        public DungeonCell Cell { get; }

        /// <summary>Gets the adjacent walkable cell where a traversing creature arrives.</summary>
        public DungeonCell ArrivalCell { get; }
    }

    /// <summary>Records a deterministic prop placement reserved for the decoration feature.</summary>
    public sealed class DungeonObjectPlacement
    {
        /// <summary>Creates an object placement.</summary>
        /// <param name="id">The stable non-empty placement identifier.</param>
        /// <param name="assetId">The project catalog asset identifier.</param>
        /// <param name="cell">The in-bounds anchor cell.</param>
        /// <param name="rotation">Clockwise rotation in degrees.</param>
        /// <param name="state">An optional losslessly preserved state token.</param>
        public DungeonObjectPlacement(
            string id,
            string assetId,
            DungeonCell cell,
            int rotation = 0,
            string state = null)
        {
            Id = id;
            AssetId = assetId;
            Cell = cell;
            Rotation = rotation;
            State = state;
        }

        /// <summary>Gets the stable placement ID.</summary>
        public string Id { get; }

        /// <summary>Gets the project catalog asset ID.</summary>
        public string AssetId { get; }

        /// <summary>Gets the anchor cell.</summary>
        public DungeonCell Cell { get; }

        /// <summary>Gets clockwise rotation in degrees.</summary>
        public int Rotation { get; }

        /// <summary>Gets the optional losslessly preserved mutable state token.</summary>
        public string State { get; }
    }

    /// <summary>Records a deterministic room encounter for later spawn realization.</summary>
    public sealed class DungeonEncounterPlan
    {
        /// <summary>Creates an encounter plan and copies its ordered collections.</summary>
        /// <param name="id">The stable non-empty encounter identifier.</param>
        /// <param name="roomId">The positive containing room identifier.</param>
        /// <param name="threat">The PF2e threat category used to derive the budget.</param>
        /// <param name="budget">The nonnegative adjusted XP budget used during composition.</param>
        /// <param name="spawnCells">Distinct ordered walkable cells, one per creature ID.</param>
        /// <param name="creatureIds">Ordered creature content IDs, one per spawn cell.</param>
        /// <param name="isResolved">Whether runtime play has permanently resolved the encounter.</param>
        /// <exception cref="ArgumentNullException"><paramref name="spawnCells"/> or <paramref name="creatureIds"/> is null.</exception>
        public DungeonEncounterPlan(
            string id,
            int roomId,
            DungeonEncounterThreat threat,
            int budget,
            IEnumerable<DungeonCell> spawnCells,
            IEnumerable<string> creatureIds,
            bool isResolved = false)
        {
            Id = id;
            RoomId = roomId;
            Threat = threat;
            Budget = budget;
            SpawnCells = Array.AsReadOnly(
                (spawnCells ?? throw new ArgumentNullException(nameof(spawnCells))).ToArray());
            CreatureIds = Array.AsReadOnly(
                (creatureIds ?? throw new ArgumentNullException(nameof(creatureIds))).ToArray());
            IsResolved = isResolved;
        }

        /// <summary>Gets the stable encounter ID.</summary>
        public string Id { get; }

        /// <summary>Gets the containing room ID.</summary>
        public int RoomId { get; }

        /// <summary>Gets the PF2e threat category used to derive <see cref="Budget"/>.</summary>
        public DungeonEncounterThreat Threat { get; }

        /// <summary>Gets the adjusted nonnegative XP budget available to the composition.</summary>
        public int Budget { get; }

        /// <summary>Gets ordered distinct candidate spawn cells, one per <see cref="CreatureIds"/> entry.</summary>
        public IReadOnlyList<DungeonCell> SpawnCells { get; }

        /// <summary>Gets ordered creature content IDs, one per <see cref="SpawnCells"/> entry.</summary>
        public IReadOnlyList<string> CreatureIds { get; }

        /// <summary>
        /// Gets whether runtime play has permanently resolved this encounter. It is mirrored exactly
        /// by <see cref="DungeonRuntimeState.ResolvedEncounterIds"/> when runtime state exists and
        /// must be <see langword="false"/> for a pristine document without runtime state.
        /// </summary>
        public bool IsResolved { get; }
    }

    /// <summary>Stores optional mutable state separately from immutable generation facts.</summary>
    public sealed class DungeonRuntimeState
    {
        /// <summary>Creates runtime state by copying every stable-ID and creature sequence.</summary>
        /// <param name="openDoorIds">Stable IDs that exactly mirror doors whose persisted <see cref="DungeonDoor.IsOpen"/> flag is set.</param>
        /// <param name="resolvedEncounterIds">Stable IDs that exactly mirror plans whose persisted <see cref="DungeonEncounterPlan.IsResolved"/> flag is set.</param>
        /// <param name="defeatedCreatureIds">Unique stable IDs for defeated or removed instances; these IDs must not also identify live creatures.</param>
        /// <param name="creatures">State for unique live instances belonging to unresolved encounter plans; null means an empty sequence.</param>
        /// <exception cref="ArgumentNullException">A required ID sequence is null.</exception>
        public DungeonRuntimeState(
            IEnumerable<string> openDoorIds,
            IEnumerable<string> resolvedEncounterIds,
            IEnumerable<string> defeatedCreatureIds,
            IEnumerable<DungeonCreatureRuntimeState> creatures = null)
        {
            OpenDoorIds = Array.AsReadOnly(
                (openDoorIds ?? throw new ArgumentNullException(nameof(openDoorIds))).ToArray());
            ResolvedEncounterIds = Array.AsReadOnly(
                (resolvedEncounterIds ?? throw new ArgumentNullException(nameof(resolvedEncounterIds))).ToArray());
            DefeatedCreatureIds = Array.AsReadOnly(
                (defeatedCreatureIds ?? throw new ArgumentNullException(nameof(defeatedCreatureIds))).ToArray());
            Creatures = Array.AsReadOnly(
                (creatures ?? Array.Empty<DungeonCreatureRuntimeState>()).ToArray());
        }

        /// <summary>Gets stable IDs that exactly mirror doors whose persisted open flag is set.</summary>
        public IReadOnlyList<string> OpenDoorIds { get; }

        /// <summary>Gets stable IDs that exactly mirror encounter plans whose persisted resolved flag is set.</summary>
        public IReadOnlyList<string> ResolvedEncounterIds { get; }

        /// <summary>Gets stable IDs for defeated or removed instances, disjoint from live instance IDs.</summary>
        public IReadOnlyList<string> DefeatedCreatureIds { get; }

        /// <summary>Gets mutable state for unique live instances belonging to unresolved encounter plans.</summary>
        public IReadOnlyList<DungeonCreatureRuntimeState> Creatures { get; }
    }

    /// <summary>Stores mutable state for one stable creature instance created from an encounter plan.</summary>
    public sealed class DungeonCreatureRuntimeState
    {
        /// <summary>Creates a creature runtime-state record.</summary>
        /// <param name="instanceId">The stable spawned-instance identifier.</param>
        /// <param name="creatureId">The immutable creature content identifier, which must be present in the referenced plan.</param>
        /// <param name="encounterId">The stable unresolved encounter plan that created the instance.</param>
        /// <param name="cell">The current walkable cell.</param>
        /// <param name="hitPoints">Current hit points; downstream rules own valid maximums and condition math.</param>
        /// <param name="state">An optional losslessly preserved child-persistence state token.</param>
        public DungeonCreatureRuntimeState(
            string instanceId,
            string creatureId,
            string encounterId,
            DungeonCell cell,
            int hitPoints,
            string state)
        {
            InstanceId = instanceId;
            CreatureId = creatureId;
            EncounterId = encounterId;
            Cell = cell;
            HitPoints = hitPoints;
            State = state;
        }

        /// <summary>Gets the stable spawned-instance ID.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the immutable creature content ID present in the referenced encounter plan.</summary>
        public string CreatureId { get; }

        /// <summary>Gets the stable unresolved encounter plan that created this live instance.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the creature's current walkable cell.</summary>
        public DungeonCell Cell { get; }

        /// <summary>Gets current hit points; downstream rules own maximums and condition math.</summary>
        public int HitPoints { get; }

        /// <summary>Gets the optional losslessly preserved child-persistence state token.</summary>
        public string State { get; }
    }
}
