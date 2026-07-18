using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Game.DungeonGeneration
{
    internal sealed class DungeonLevelJsonModel
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Ignore
        };

        [JsonProperty("generation", Order = 0)]
        public GenerationModel Generation { get; set; }

        [JsonProperty("rows", Order = 1)]
        public string[] Rows { get; set; }

        [JsonProperty("rooms", Order = 2)]
        public RoomModel[] Rooms { get; set; }

        [JsonProperty("doors", Order = 3)]
        public DoorModel[] Doors { get; set; }

        [JsonProperty("stairs", Order = 4)]
        public StairModel[] Stairs { get; set; }

        [JsonProperty("arrival", Order = 5)]
        public ArrivalModel Arrival { get; set; }

        [JsonProperty("objects", Order = 6)]
        public ObjectModel[] Objects { get; set; }

        [JsonProperty("encounterPlans", Order = 7)]
        public EncounterModel[] EncounterPlans { get; set; }

        [JsonProperty("runtimeState", Order = 8)]
        public RuntimeModel RuntimeState { get; set; }

        internal static string Serialize(DungeonLevelDocument document)
        {
            DungeonLevelJsonModel model = new()
            {
                Generation = new GenerationModel
                {
                    Algorithm = document.Generation.Algorithm,
                    RunSeed = document.Generation.RunSeed,
                    Depth = document.Generation.Depth,
                    TopologyAttempt = document.Generation.TopologyAttempt
                },
                Rows = document.Rows.ToArray(),
                Rooms = document.Rooms.Select(room => new RoomModel
                {
                    Id = room.Id,
                    MinimumX = room.MinimumX,
                    MinimumZ = room.MinimumZ,
                    MaximumX = room.MaximumX,
                    MaximumZ = room.MaximumZ
                }).ToArray(),
                Doors = document.Doors.Select(door => new DoorModel
                {
                    Id = door.Id,
                    Cell = CellModel.From(door.Cell),
                    IsOpen = door.IsOpen
                }).ToArray(),
                Stairs = document.Stairs.Select(stair => new StairModel
                {
                    Id = stair.Id,
                    Kind = StairKind(stair.Kind),
                    Cell = CellModel.From(stair.Cell),
                    ArrivalCell = CellModel.From(stair.ArrivalCell)
                }).ToArray(),
                Arrival = new ArrivalModel
                {
                    Start = CellModel.From(document.StartCell),
                    SafeCells = document.SafeCells.Select(CellModel.From).ToArray()
                },
                Objects = document.Objects.Select(item => new ObjectModel
                {
                    Id = item.Id,
                    AssetId = item.AssetId,
                    Cell = CellModel.From(item.Cell),
                    Rotation = item.Rotation,
                    YOffset = item.YOffset == 0f ? null : item.YOffset,
                    State = item.State
                }).ToArray(),
                EncounterPlans = document.EncounterPlans.Select(plan => new EncounterModel
                {
                    Id = plan.Id,
                    RoomId = plan.RoomId,
                    SpawnCells = plan.SpawnCells.Select(CellModel.From).ToArray(),
                    CreatureIds = plan.CreatureIds.ToArray(),
                    Threat = Threat(plan.Threat),
                    Budget = plan.Budget,
                    IsResolved = plan.IsResolved
                }).ToArray(),
                RuntimeState = RuntimeModel.From(document.RuntimeState)
            };
            return JsonConvert.SerializeObject(model, Formatting.None, Settings);
        }

        private static string StairKind(DungeonStairKind kind) => kind switch
        {
            DungeonStairKind.Up => "up",
            DungeonStairKind.Down => "down",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Stair kind is undefined.")
        };

        private static string Threat(DungeonEncounterThreat threat) => threat switch
        {
            DungeonEncounterThreat.Trivial => "trivial",
            DungeonEncounterThreat.Low => "low",
            DungeonEncounterThreat.Moderate => "moderate",
            _ => throw new ArgumentOutOfRangeException(nameof(threat), threat, "Encounter threat is undefined.")
        };

        internal sealed class GenerationModel
        {
            [JsonProperty("algorithm", Order = 0)] public string Algorithm { get; set; }
            [JsonProperty("runSeed", Order = 1)] public int RunSeed { get; set; }
            [JsonProperty("depth", Order = 2)] public int Depth { get; set; }
            [JsonProperty("topologyAttempt", Order = 3)] public int TopologyAttempt { get; set; }
        }

        internal sealed class CellModel
        {
            [JsonProperty("x", Order = 0)] public int X { get; set; }
            [JsonProperty("z", Order = 1)] public int Z { get; set; }
            internal static CellModel From(DungeonCell cell) => new() { X = cell.X, Z = cell.Z };
        }

        internal sealed class RoomModel
        {
            [JsonProperty("id", Order = 0)] public int Id { get; set; }
            [JsonProperty("minX", Order = 1)] public int MinimumX { get; set; }
            [JsonProperty("minZ", Order = 2)] public int MinimumZ { get; set; }
            [JsonProperty("maxX", Order = 3)] public int MaximumX { get; set; }
            [JsonProperty("maxZ", Order = 4)] public int MaximumZ { get; set; }
        }

        internal sealed class DoorModel
        {
            [JsonProperty("id", Order = 0)] public string Id { get; set; }
            [JsonProperty("cell", Order = 1)] public CellModel Cell { get; set; }
            [JsonProperty("isOpen", Order = 2)] public bool IsOpen { get; set; }
        }

        internal sealed class StairModel
        {
            [JsonProperty("id", Order = 0)] public string Id { get; set; }
            [JsonProperty("kind", Order = 1)] public string Kind { get; set; }
            [JsonProperty("cell", Order = 2)] public CellModel Cell { get; set; }
            [JsonProperty("arrivalCell", Order = 3)] public CellModel ArrivalCell { get; set; }
        }

        internal sealed class ArrivalModel
        {
            [JsonProperty("start", Order = 0)] public CellModel Start { get; set; }
            [JsonProperty("safeCells", Order = 1)] public CellModel[] SafeCells { get; set; }
        }

        internal sealed class ObjectModel
        {
            [JsonProperty("id", Order = 0)] public string Id { get; set; }
            [JsonProperty("assetId", Order = 1)] public string AssetId { get; set; }
            [JsonProperty("cell", Order = 2)] public CellModel Cell { get; set; }
            [JsonProperty("rotation", Order = 3)] public int Rotation { get; set; }
            [JsonProperty("yOffset", Order = 4)] public float? YOffset { get; set; }
            [JsonProperty("state", Order = 5)] public string State { get; set; }
        }

        internal sealed class EncounterModel
        {
            [JsonProperty("id", Order = 0)] public string Id { get; set; }
            [JsonProperty("roomId", Order = 1)] public int RoomId { get; set; }
            [JsonProperty("spawnCells", Order = 2)] public CellModel[] SpawnCells { get; set; }
            [JsonProperty("creatureIds", Order = 3)] public string[] CreatureIds { get; set; }
            [JsonProperty("threat", Order = 4)] public string Threat { get; set; }
            [JsonProperty("budget", Order = 5)] public int Budget { get; set; }
            [JsonProperty("isResolved", Order = 6)] public bool IsResolved { get; set; }
        }

        internal sealed class RuntimeModel
        {
            [JsonProperty("openDoorIds", Order = 0)] public string[] OpenDoorIds { get; set; }
            [JsonProperty("resolvedEncounterIds", Order = 1)] public string[] ResolvedEncounterIds { get; set; }
            [JsonProperty("defeatedCreatureIds", Order = 2)] public string[] DefeatedCreatureIds { get; set; }
            [JsonProperty("creatures", Order = 3)] public CreatureModel[] Creatures { get; set; }

            internal static RuntimeModel From(DungeonRuntimeState runtime)
            {
                if (runtime == null)
                    return null;
                return new RuntimeModel
                {
                    OpenDoorIds = runtime.OpenDoorIds.ToArray(),
                    ResolvedEncounterIds = runtime.ResolvedEncounterIds.ToArray(),
                    DefeatedCreatureIds = runtime.DefeatedCreatureIds.ToArray(),
                    Creatures = runtime.Creatures.Select(creature => new CreatureModel
                    {
                        InstanceId = creature.InstanceId,
                        CreatureId = creature.CreatureId,
                        EncounterId = creature.EncounterId,
                        Cell = CellModel.From(creature.Cell),
                        HitPoints = creature.HitPoints,
                        State = creature.State
                    }).ToArray()
                };
            }
        }

        internal sealed class CreatureModel
        {
            [JsonProperty("instanceId", Order = 0)] public string InstanceId { get; set; }
            [JsonProperty("creatureId", Order = 1)] public string CreatureId { get; set; }
            [JsonProperty("encounterId", Order = 2)] public string EncounterId { get; set; }
            [JsonProperty("cell", Order = 3)] public CellModel Cell { get; set; }
            [JsonProperty("hitPoints", Order = 4)] public int HitPoints { get; set; }
            [JsonProperty("state", Order = 5)] public string State { get; set; }
        }
    }
}
