using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using static Game.DungeonPersistence.Repository.DungeonSaveContract;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Classifies a dungeon save failure for logs and future UI presentation.</summary>
    public enum DungeonSaveDiagnosticCode
    {
        /// <summary>No autosave has been written yet.</summary>
        MissingSave,

        /// <summary>The autosave archive or JSON is malformed.</summary>
        CorruptSave,

        /// <summary>The autosave uses a schema or generator this build cannot read.</summary>
        IncompatibleVersion,

        /// <summary>Captured or restored gameplay state violates the save contract.</summary>
        InvalidSnapshot,

        /// <summary>The autosave could not be read or published.</summary>
        IoFailure,
    }

    /// <summary>Describes one structured dungeon persistence failure.</summary>
    public sealed class DungeonSaveDiagnostic
    {
        /// <summary>Creates a diagnostic at a stable save-contract path.</summary>
        public DungeonSaveDiagnostic(DungeonSaveDiagnosticCode code, string path, string message)
        {
            Code = code;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the machine-readable failure category.</summary>
        public DungeonSaveDiagnosticCode Code { get; }

        /// <summary>Gets the schema or repository path associated with the failure.</summary>
        public string Path { get; }

        /// <summary>Gets the human-readable failure explanation.</summary>
        public string Message { get; }
    }

    internal static class DungeonSaveSchema
    {
        internal const int Version = 1;
        internal const string ManifestPath = "manifest.json";
        internal const string FloorPath = "floor.json";
    }

    internal sealed class DungeonSaveResult<T>
    {
        private DungeonSaveResult(T value, IReadOnlyList<DungeonSaveDiagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
        }

        internal bool IsSuccess => Diagnostics.Count == 0;
        public T Value { get; }
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        internal static DungeonSaveResult<T> Success(T value) =>
            new(value, Array.Empty<DungeonSaveDiagnostic>());

        internal static DungeonSaveResult<T> Failure(
            DungeonSaveDiagnosticCode code,
            string path,
            string message
        ) => new(default, new[] { new DungeonSaveDiagnostic(code, path, message) });
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonFloorSaveReference
    {
        [JsonConstructor]
        internal DungeonFloorSaveReference(int documentVersion, string path)
        {
            if (documentVersion != DungeonSaveSchema.Version)
                throw new ArgumentOutOfRangeException(nameof(documentVersion));
            DocumentVersion = documentVersion;
            Path = RequireId(path, nameof(path));
            if (!string.Equals(Path, DungeonSaveSchema.FloorPath, StringComparison.Ordinal))
                throw new ArgumentException(
                    "The current floor path is not canonical.",
                    nameof(path)
                );
        }

        public int DocumentVersion { get; }
        public string Path { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonConditionSaveState
    {
        [JsonConstructor]
        internal DungeonConditionSaveState(string conditionId, string sourceKey)
        {
            ConditionId = RequireId(conditionId, nameof(conditionId));
            SourceKey = RequireId(sourceKey, nameof(sourceKey));
        }

        public string ConditionId { get; }
        public string SourceKey { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonTimedEffectSaveState
    {
        [JsonConstructor]
        internal DungeonTimedEffectSaveState(
            string kind,
            string sourceActorId,
            int remainingTurnStarts
        )
        {
            Kind = RequireId(kind, nameof(kind));
            if (
                Kind != "shield"
                && Kind != "guidance"
                && Kind != "guidance-immunity"
                && Kind != "bless"
                && Kind != "infuse-vitality"
            )
                throw new ArgumentException(
                    $"Timed effect kind '{Kind}' is not supported.",
                    nameof(kind)
                );
            SourceActorId = RequireId(sourceActorId, nameof(sourceActorId));
            if (remainingTurnStarts < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingTurnStarts));
            RemainingTurnStarts = remainingTurnStarts;
        }

        public string Kind { get; }
        public string SourceActorId { get; }
        public int RemainingTurnStarts { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonPreparedEffectSaveState
    {
        [JsonConstructor]
        internal DungeonPreparedEffectSaveState(string name, string slug, string sourceSlug)
        {
            Name = RequireId(name, nameof(name));
            Slug = RequireId(slug, nameof(slug));
            SourceSlug = RequireId(sourceSlug, nameof(sourceSlug));
        }

        public string Name { get; }
        public string Slug { get; }
        public string SourceSlug { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonEquipmentReference
    {
        [JsonConstructor]
        internal DungeonEquipmentReference(string definitionId, int occurrence)
        {
            DefinitionId = definitionId?.Trim() ?? string.Empty;
            Occurrence = occurrence;
            if (
                DefinitionId.Length == 0 && occurrence != -1
                || DefinitionId.Length > 0 && occurrence < 0
            )
            {
                throw new ArgumentException(
                    "An equipment reference must be either empty or identify a nonnegative occurrence."
                );
            }
        }

        public string DefinitionId { get; }
        public int Occurrence { get; }
        internal bool IsEmpty => DefinitionId.Length == 0;

        public static DungeonEquipmentReference Empty { get; } = new(string.Empty, -1);
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonAmmunitionSaveState
    {
        [JsonConstructor]
        internal DungeonAmmunitionSaveState(string definitionId, int quantity)
        {
            DefinitionId = RequireId(definitionId, nameof(definitionId));
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity;
        }

        public string DefinitionId { get; }
        public int Quantity { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonEquipmentSaveState
    {
        [JsonConstructor]
        internal DungeonEquipmentSaveState(
            DungeonEquipmentReference leftHand,
            DungeonEquipmentReference rightHand,
            DungeonEquipmentReference armor,
            IEnumerable<DungeonAmmunitionSaveState> ammunition,
            IEnumerable<string> unloadedWeaponIds
        )
        {
            LeftHand = leftHand ?? throw new ArgumentNullException(nameof(leftHand));
            RightHand = rightHand ?? throw new ArgumentNullException(nameof(rightHand));
            Armor = armor ?? throw new ArgumentNullException(nameof(armor));
            Ammunition = Copy(ammunition, nameof(ammunition));
            UnloadedWeaponIds = CopyIds(unloadedWeaponIds, nameof(unloadedWeaponIds));
            RequireUnique(
                Ammunition.Select(pool => pool.DefinitionId),
                nameof(ammunition),
                StringComparer.OrdinalIgnoreCase
            );
        }

        public DungeonEquipmentReference LeftHand { get; }
        public DungeonEquipmentReference RightHand { get; }
        public DungeonEquipmentReference Armor { get; }
        public IReadOnlyList<DungeonAmmunitionSaveState> Ammunition { get; }
        public IReadOnlyList<string> UnloadedWeaponIds { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonActorSaveState
    {
        [JsonConstructor]
        internal DungeonActorSaveState(
            int temporaryHitPoints,
            string temporaryHitPointSource,
            IEnumerable<string> temporaryHitPointImmunities,
            IEnumerable<DungeonConditionSaveState> conditions,
            IEnumerable<DungeonTimedEffectSaveState> timedEffects,
            IEnumerable<DungeonPreparedEffectSaveState> preparedEffects,
            DungeonEquipmentSaveState equipment
        )
        {
            if (temporaryHitPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(temporaryHitPoints));
            TemporaryHitPoints = temporaryHitPoints;
            TemporaryHitPointSource = temporaryHitPointSource?.Trim() ?? string.Empty;
            if (temporaryHitPoints == 0 && TemporaryHitPointSource.Length > 0)
            {
                throw new ArgumentException(
                    "Zero temporary Hit Points cannot retain an owning source.",
                    nameof(temporaryHitPointSource)
                );
            }
            TemporaryHitPointImmunities = CopyIds(
                temporaryHitPointImmunities,
                nameof(temporaryHitPointImmunities)
            );
            Conditions = Copy(conditions, nameof(conditions));
            TimedEffects = Copy(timedEffects, nameof(timedEffects));
            PreparedEffects = Copy(preparedEffects, nameof(preparedEffects));
            Equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            RequireUnique(
                Conditions.Select(item => item.ConditionId + "\n" + item.SourceKey),
                nameof(conditions),
                StringComparer.Ordinal
            );
            RequireUnique(
                PreparedEffects.Select(item => item.Slug + "\n" + item.SourceSlug),
                nameof(preparedEffects),
                StringComparer.OrdinalIgnoreCase
            );
        }

        public int TemporaryHitPoints { get; }
        public string TemporaryHitPointSource { get; }
        public IReadOnlyList<string> TemporaryHitPointImmunities { get; }
        public IReadOnlyList<DungeonConditionSaveState> Conditions { get; }
        public IReadOnlyList<DungeonTimedEffectSaveState> TimedEffects { get; }
        public IReadOnlyList<DungeonPreparedEffectSaveState> PreparedEffects { get; }
        public DungeonEquipmentSaveState Equipment { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonPartyMemberSaveState
    {
        [JsonConstructor]
        internal DungeonPartyMemberSaveState(
            string rosterSlotId,
            string creatureContentId,
            int cellX,
            int cellZ,
            int currentHitPoints,
            bool isDefeated,
            DungeonActorSaveState state
        )
        {
            RosterSlotId = RequireId(rosterSlotId, nameof(rosterSlotId));
            CreatureContentId = RequireId(creatureContentId, nameof(creatureContentId));
            if (currentHitPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(currentHitPoints));
            if (isDefeated != (currentHitPoints == 0))
                throw new ArgumentException("Defeat must exactly match zero current Hit Points.");
            CellX = cellX;
            CellZ = cellZ;
            CurrentHitPoints = currentHitPoints;
            IsDefeated = isDefeated;
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string RosterSlotId { get; }
        public string CreatureContentId { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int CurrentHitPoints { get; }
        public bool IsDefeated { get; }
        public DungeonActorSaveState State { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonPartySaveState
    {
        [JsonConstructor]
        internal DungeonPartySaveState(IEnumerable<DungeonPartyMemberSaveState> members)
        {
            Members = Copy(members, nameof(members));
            if (Members.Count == 0)
                throw new ArgumentException("A dungeon save requires at least one party member.");
            RequireUnique(
                Members.Select(member => member.RosterSlotId),
                nameof(members),
                StringComparer.Ordinal
            );
            RequireUnique(
                Members
                    .Where(member => !member.IsDefeated)
                    .Select(member => member.CellX + ":" + member.CellZ),
                nameof(members),
                StringComparer.Ordinal
            );
        }

        public IReadOnlyList<DungeonPartyMemberSaveState> Members { get; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonRunSaveManifest
    {
        [JsonConstructor]
        internal DungeonRunSaveManifest(
            int documentVersion,
            int startingSeed,
            string generatorVersion,
            int currentDepth,
            DungeonFloorSaveReference currentFloor,
            DungeonPartySaveState party
        )
        {
            if (documentVersion != DungeonSaveSchema.Version)
                throw new ArgumentOutOfRangeException(nameof(documentVersion));
            if (currentDepth < 0)
                throw new ArgumentOutOfRangeException(nameof(currentDepth));
            DocumentVersion = documentVersion;
            StartingSeed = startingSeed;
            GeneratorVersion = RequireId(generatorVersion, nameof(generatorVersion));
            CurrentDepth = currentDepth;
            CurrentFloor = currentFloor ?? throw new ArgumentNullException(nameof(currentFloor));
            Party = party ?? throw new ArgumentNullException(nameof(party));
        }

        public int DocumentVersion { get; }
        public int StartingSeed { get; }
        public string GeneratorVersion { get; }
        public int CurrentDepth { get; }
        public DungeonFloorSaveReference CurrentFloor { get; }
        public DungeonPartySaveState Party { get; }
    }

    internal sealed class DungeonRunSave
    {
        internal DungeonRunSave(DungeonRunSaveManifest manifest, DungeonLevelDocument floorDocument)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            FloorDocument = floorDocument ?? throw new ArgumentNullException(nameof(floorDocument));
            if (floorDocument.RuntimeState == null)
                throw new ArgumentException("A saved floor requires runtime state.");
            if (
                floorDocument.Generation.RunSeed != manifest.StartingSeed
                || floorDocument.Generation.Depth != manifest.CurrentDepth
                || !string.Equals(
                    floorDocument.Generation.Algorithm,
                    manifest.GeneratorVersion,
                    StringComparison.Ordinal
                )
            )
                throw new ArgumentException(
                    "Manifest generation metadata does not match the floor."
                );

            HashSet<string> livingCells = new(
                Manifest
                    .Party.Members.Where(member => !member.IsDefeated)
                    .Select(member => member.CellX + ":" + member.CellZ),
                StringComparer.Ordinal
            );
            foreach (DungeonPartyMemberSaveState member in Manifest.Party.Members)
            {
                if (!IsWalkable(FloorDocument.Rows, member.CellX, member.CellZ))
                    throw new ArgumentException(
                        $"Party member '{member.RosterSlotId}' is not on a walkable floor cell."
                    );
            }
            if (
                FloorDocument.RuntimeState.Creatures.Any(creature =>
                    livingCells.Contains(creature.Cell.X + ":" + creature.Cell.Z)
                )
            )
                throw new ArgumentException(
                    "A living party member and enemy cannot occupy the same floor cell."
                );
        }

        public DungeonRunSaveManifest Manifest { get; }
        public DungeonLevelDocument FloorDocument { get; }

        private static bool IsWalkable(IReadOnlyList<string> rows, int x, int z)
        {
            return rows.Count > 0
                && x >= 0
                && z >= 0
                && x < rows[0].Length
                && z < rows.Count
                && (rows[rows.Count - 1 - z][x] == '.' || rows[rows.Count - 1 - z][x] == 'D');
        }
    }

    internal static class DungeonSaveJson
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None,
        };

        internal static string SerializeManifest(DungeonRunSaveManifest manifest) =>
            JsonConvert.SerializeObject(manifest, Settings);

        internal static DungeonSaveResult<DungeonRunSaveManifest> ParseManifest(string json)
        {
            try
            {
                JToken root = RejectDuplicateProperties(json);
                if (root is not JObject manifestRoot)
                    throw new JsonSerializationException("Manifest must be an object.");
                JToken documentVersion = manifestRoot["documentVersion"];
                if (documentVersion?.Type != JTokenType.Integer)
                    throw new JsonSerializationException(
                        "Manifest documentVersion must be an integer."
                    );
                if (documentVersion.Value<int>() != DungeonSaveSchema.Version)
                    return DungeonSaveResult<DungeonRunSaveManifest>.Failure(
                        DungeonSaveDiagnosticCode.IncompatibleVersion,
                        "manifest.documentVersion",
                        "The autosave manifest version is not supported."
                    );
                DungeonRunSaveManifest manifest =
                    JsonConvert.DeserializeObject<DungeonRunSaveManifest>(json, Settings);
                if (manifest == null)
                    throw new JsonSerializationException("Manifest was null.");
                return DungeonSaveResult<DungeonRunSaveManifest>.Success(manifest);
            }
            catch (Exception exception)
                when (exception is JsonException
                    || exception is ArgumentException
                    || exception is InvalidOperationException
                )
            {
                return DungeonSaveResult<DungeonRunSaveManifest>.Failure(
                    DungeonSaveDiagnosticCode.CorruptSave,
                    DungeonSaveSchema.ManifestPath,
                    exception.Message
                );
            }
        }

        internal static string SerializeActor(DungeonActorSaveState state) =>
            JsonConvert.SerializeObject(state, Settings);

        internal static DungeonSaveResult<DungeonActorSaveState> ParseActor(string json)
        {
            try
            {
                _ = RejectDuplicateProperties(json);
                DungeonActorSaveState state = JsonConvert.DeserializeObject<DungeonActorSaveState>(
                    json,
                    Settings
                );
                if (state == null)
                    throw new JsonSerializationException("Actor state was null.");
                return DungeonSaveResult<DungeonActorSaveState>.Success(state);
            }
            catch (Exception exception)
                when (exception is JsonException
                    || exception is ArgumentException
                    || exception is InvalidOperationException
                )
            {
                return DungeonSaveResult<DungeonActorSaveState>.Failure(
                    DungeonSaveDiagnosticCode.CorruptSave,
                    "actor.state",
                    exception.Message
                );
            }
        }

        private static JToken RejectDuplicateProperties(string json)
        {
            return JToken.Parse(
                json ?? string.Empty,
                new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Ignore,
                }
            );
        }
    }

    internal static class DungeonSaveContract
    {
        internal static string RequireId(string value, string parameterName)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException("A stable identifier is required.", parameterName);
            return normalized;
        }

        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            T[] copied = values.ToArray();
            if (copied.Any(item => item == null))
                throw new ArgumentException("The collection cannot contain null.", parameterName);
            return Array.AsReadOnly(copied);
        }

        internal static IReadOnlyList<string> CopyIds(
            IEnumerable<string> values,
            string parameterName
        )
        {
            IReadOnlyList<string> copied = Copy(values, parameterName)
                .Select(value => RequireId(value, parameterName))
                .ToArray();
            RequireUnique(copied, parameterName, StringComparer.Ordinal);
            return copied;
        }

        internal static void RequireUnique(
            IEnumerable<string> values,
            string parameterName,
            StringComparer comparer
        )
        {
            string[] copied = values.ToArray();
            if (copied.Distinct(comparer).Count() != copied.Length)
                throw new ArgumentException("Stable identifiers must be unique.", parameterName);
        }
    }
}
