using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Classifies a dungeon save failure for logs and future UI presentation.</summary>
    public enum DungeonSaveDiagnosticCode
    {
        /// <summary>No autosave has been written yet.</summary>
        MissingSave,

        /// <summary>The autosave JSON is malformed or incomplete.</summary>
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
        internal const int ManifestVersion = 2;
        internal const int FloorDocumentVersion = 1;

        internal static string FloorPath(int depth) =>
            "floors/" + depth.ToString(CultureInfo.InvariantCulture) + ".json";
    }

    internal sealed class DungeonSaveResult<T>
    {
        private DungeonSaveResult(T value, IReadOnlyList<DungeonSaveDiagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
        }

        internal bool IsSuccess => Diagnostics.Count == 0;
        internal T Value { get; }
        internal IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        internal static DungeonSaveResult<T> Success(T value) =>
            new(value, Array.Empty<DungeonSaveDiagnostic>());

        internal static DungeonSaveResult<T> Failure(
            DungeonSaveDiagnosticCode code,
            string path,
            string message
        ) => new(default, new[] { new DungeonSaveDiagnostic(code, path, message) });
    }

    [Serializable]
    internal sealed class DungeonConditionSaveState
    {
        public string ConditionId;
        public string SourceKey;
    }

    [Serializable]
    internal sealed class DungeonTimedEffectSaveState
    {
        public string Kind;
        public string SourceActorId;
        public int RemainingTurnStarts;
    }

    [Serializable]
    internal sealed class DungeonPreparedEffectSaveState
    {
        public string Name;
        public string Slug;
        public string SourceSlug;
    }

    [Serializable]
    internal sealed class DungeonEquipmentSaveState
    {
        public string LeftHandId;
        public string RightHandId;
        public string ArmorId;
        public AmmoCount[] Ammunition;
        public string[] UnloadedWeaponIds;
    }

    [Serializable]
    internal sealed class DungeonActorSaveState
    {
        public int TemporaryHitPoints;
        public string TemporaryHitPointSource;
        public string[] TemporaryHitPointImmunities;
        public bool RageWasActive;
        public DungeonConditionSaveState[] Conditions;
        public DungeonTimedEffectSaveState[] TimedEffects;
        public DungeonPreparedEffectSaveState[] PreparedEffects;
        public DungeonEquipmentSaveState Equipment;
    }

    [Serializable]
    internal sealed class DungeonPartyMemberSaveState
    {
        public string RosterSlotId;
        public string CreatureContentId;
        public int CellX;
        public int CellZ;
        public int CurrentHitPoints;
        public bool IsDefeated;
        public DungeonActorSaveState State;
    }

    [Serializable]
    internal sealed class DungeonFloorSaveReference
    {
        public int Depth;
        public int DocumentVersion;
        public string Path;
    }

    [Serializable]
    internal sealed class DungeonFloorSavePayload
    {
        public string Path;
        public string FloorJson;
    }

    [Serializable]
    internal sealed class DungeonRunSaveManifest
    {
        public int DocumentVersion;
        public int StartingSeed;
        public string GeneratorVersion;
        public int CurrentDepth;
        public DungeonFloorSaveReference[] Floors;
        public DungeonPartyMemberSaveState[] Party;
    }

    /// <summary>
    /// Stores one immutable, current-schema dungeon run snapshot containing every visited floor.
    /// </summary>
    /// <remarks>
    /// Construction validates the complete run as one unit. Loading never migrates, repairs,
    /// regenerates, salvages, or partially accepts an autosave.
    /// </remarks>
    internal sealed class DungeonRunSave
    {
        private readonly DungeonRunSaveManifest manifest;
        private readonly DungeonFloorSavePayload[] floorPayloads;
        private readonly IReadOnlyDictionary<int, DungeonLevelDocument> floorsByDepth;

        internal DungeonRunSave(
            DungeonRunSaveManifest manifest,
            IEnumerable<DungeonFloorSavePayload> floorPayloads
        )
        {
            this.manifest = CloneManifest(
                manifest ?? throw new ArgumentNullException(nameof(manifest))
            );
            this.floorPayloads = (
                floorPayloads ?? throw new ArgumentNullException(nameof(floorPayloads))
            )
                .Select(ClonePayload)
                .ToArray();
            floorsByDepth = ValidateRun(this.manifest, this.floorPayloads);
        }

        internal DungeonRunSaveManifest Manifest => CloneManifest(manifest);

        internal IReadOnlyList<DungeonFloorSavePayload> FloorPayloads =>
            Array.AsReadOnly(floorPayloads.Select(ClonePayload).ToArray());

        internal static DungeonRunSave CreateNew(
            IEnumerable<DungeonPartyMemberSaveState> party,
            DungeonLevelDocument floor
        )
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));
            string path = DungeonSaveSchema.FloorPath(floor.Generation.Depth);
            return new DungeonRunSave(
                new DungeonRunSaveManifest
                {
                    DocumentVersion = DungeonSaveSchema.ManifestVersion,
                    StartingSeed = floor.Generation.RunSeed,
                    GeneratorVersion = floor.Generation.Algorithm,
                    CurrentDepth = floor.Generation.Depth,
                    Floors = new[] { CreateReference(floor.Generation.Depth) },
                    Party = CopyParty(party),
                },
                new[] { CreatePayload(path, floor) }
            );
        }

        internal DungeonLevelDocument GetFloor(int depth)
        {
            if (!floorsByDepth.TryGetValue(depth, out DungeonLevelDocument floor))
                throw new ArgumentOutOfRangeException(
                    nameof(depth),
                    depth,
                    "The requested dungeon floor has not been visited."
                );
            return floor;
        }

        /// <summary>Returns whether the complete run envelope contains a visited depth.</summary>
        internal bool HasFloor(int depth) => floorsByDepth.ContainsKey(depth);

        internal DungeonRunSave WithCurrentCheckpoint(
            IEnumerable<DungeonPartyMemberSaveState> party,
            DungeonLevelDocument floor
        )
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));
            if (floor.Generation.Depth != manifest.CurrentDepth)
                throw new ArgumentException(
                    "A current-floor checkpoint must match the selected depth.",
                    nameof(floor)
                );

            DungeonRunSaveManifest candidateManifest = CloneManifest(manifest);
            candidateManifest.Party = CopyParty(party);
            string currentPath = DungeonSaveSchema.FloorPath(manifest.CurrentDepth);
            DungeonFloorSavePayload[] candidatePayloads = floorPayloads
                .Select(payload =>
                    string.Equals(payload.Path, currentPath, StringComparison.Ordinal)
                        ? CreatePayload(currentPath, floor)
                        : ClonePayload(payload)
                )
                .ToArray();
            return new DungeonRunSave(candidateManifest, candidatePayloads);
        }

        internal DungeonRunSave WithSelectedFloor(
            int depth,
            IEnumerable<DungeonPartyMemberSaveState> party
        )
        {
            if (!floorsByDepth.ContainsKey(depth))
                throw new ArgumentOutOfRangeException(
                    nameof(depth),
                    depth,
                    "The selected dungeon floor has not been visited."
                );
            DungeonRunSaveManifest candidateManifest = CloneManifest(manifest);
            candidateManifest.CurrentDepth = depth;
            candidateManifest.Party = CopyParty(party);
            return new DungeonRunSave(candidateManifest, floorPayloads);
        }

        internal DungeonRunSave WithAddedAndSelectedFloor(
            IEnumerable<DungeonPartyMemberSaveState> party,
            DungeonLevelDocument floor
        )
        {
            if (floor == null)
                throw new ArgumentNullException(nameof(floor));
            int depth = floor.Generation.Depth;
            if (floorsByDepth.ContainsKey(depth))
                throw new ArgumentException(
                    $"Dungeon depth {depth} has already been visited.",
                    nameof(floor)
                );

            DungeonRunSaveManifest candidateManifest = CloneManifest(manifest);
            candidateManifest.CurrentDepth = depth;
            candidateManifest.Party = CopyParty(party);
            candidateManifest.Floors = candidateManifest
                .Floors.Append(CreateReference(depth))
                .OrderBy(reference => reference.Depth)
                .ToArray();
            DungeonFloorSavePayload added = CreatePayload(
                DungeonSaveSchema.FloorPath(depth),
                floor
            );
            DungeonFloorSavePayload[] candidatePayloads = floorPayloads
                .Append(added)
                .OrderBy(payload => ParseCanonicalDepth(payload.Path))
                .ToArray();
            return new DungeonRunSave(candidateManifest, candidatePayloads);
        }

        private static IReadOnlyDictionary<int, DungeonLevelDocument> ValidateRun(
            DungeonRunSaveManifest manifest,
            IReadOnlyList<DungeonFloorSavePayload> payloads
        )
        {
            DungeonSaveJson.ValidateManifest(manifest);
            if (payloads.Count != manifest.Floors.Length)
                throw new ArgumentException(
                    "Every indexed floor requires exactly one matching payload."
                );

            Dictionary<int, DungeonLevelDocument> documents = new();
            HashSet<string> referencePaths = new(StringComparer.Ordinal);
            HashSet<string> payloadPaths = new(StringComparer.Ordinal);
            int previousDepth = -1;
            for (int index = 0; index < manifest.Floors.Length; index++)
            {
                DungeonFloorSaveReference reference = manifest.Floors[index];
                if (reference == null || reference.Depth < 0 || reference.Depth <= previousDepth)
                    throw new ArgumentException(
                        "Saved floor depths must be nonnegative, unique, and strictly ordered."
                    );
                previousDepth = reference.Depth;
                string canonicalPath = DungeonSaveSchema.FloorPath(reference.Depth);
                if (
                    reference.DocumentVersion != DungeonSaveSchema.FloorDocumentVersion
                    || !string.Equals(reference.Path, canonicalPath, StringComparison.Ordinal)
                    || !referencePaths.Add(reference.Path)
                )
                    throw new ArgumentException(
                        $"Saved floor reference '{reference.Path}' is incomplete or incompatible."
                    );

                DungeonFloorSavePayload payload = payloads[index];
                if (
                    payload == null
                    || string.IsNullOrWhiteSpace(payload.FloorJson)
                    || !string.Equals(payload.Path, reference.Path, StringComparison.Ordinal)
                    || !payloadPaths.Add(payload.Path)
                )
                    throw new ArgumentException(
                        $"Saved floor payload at index {index} does not match its reference."
                    );

                DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(payload.FloorJson);
                if (!parsed.IsSuccess)
                    throw new ArgumentException(
                        $"Saved floor '{payload.Path}' is invalid: "
                            + string.Join(" ", parsed.Diagnostics.Select(item => item.Message))
                    );
                DungeonLevelDocument floor = parsed.Document;
                if (floor.RuntimeState == null)
                    throw new ArgumentException(
                        $"Saved floor '{payload.Path}' requires runtime state."
                    );
                if (
                    floor.Generation.RunSeed != manifest.StartingSeed
                    || floor.Generation.Depth != reference.Depth
                    || !string.Equals(
                        floor.Generation.Algorithm,
                        manifest.GeneratorVersion,
                        StringComparison.Ordinal
                    )
                )
                    throw new ArgumentException(
                        $"Manifest generation metadata does not match saved floor '{payload.Path}'."
                    );
                List<DungeonActorSaveState> enemyStates = new();
                foreach (DungeonCreatureRuntimeState creature in floor.RuntimeState.Creatures)
                {
                    DungeonSaveResult<DungeonActorSaveState> actor = DungeonSaveJson.ParseActor(
                        creature.State
                    );
                    if (!actor.IsSuccess)
                        throw new ArgumentException(
                            $"Living creature '{creature.InstanceId}' on '{payload.Path}' has invalid actor state: "
                                + actor.Diagnostics[0].Message
                        );
                    enemyStates.Add(actor.Value);
                }
                ValidateActorGraph(manifest.Party, floor, enemyStates, payload.Path);
                documents.Add(reference.Depth, floor);
            }

            if (!documents.TryGetValue(manifest.CurrentDepth, out DungeonLevelDocument current))
                throw new ArgumentException("The selected dungeon depth is not indexed.");
            ValidateCurrentFloorParty(manifest.Party, current);
            return documents;
        }

        private static void ValidateActorGraph(
            IReadOnlyList<DungeonPartyMemberSaveState> party,
            DungeonLevelDocument floor,
            IEnumerable<DungeonActorSaveState> enemyStates,
            string floorPath
        )
        {
            HashSet<string> actorIds = new(StringComparer.Ordinal);
            foreach (DungeonPartyMemberSaveState member in party)
            {
                if (!actorIds.Add(member.RosterSlotId))
                    throw new ArgumentException(
                        $"Actor identity '{member.RosterSlotId}' is duplicated on '{floorPath}'."
                    );
            }
            foreach (DungeonCreatureRuntimeState creature in floor.RuntimeState.Creatures)
            {
                if (!actorIds.Add(creature.InstanceId))
                    throw new ArgumentException(
                        $"Actor identity '{creature.InstanceId}' is duplicated on '{floorPath}'."
                    );
            }
            foreach (string defeatedId in floor.RuntimeState.DefeatedCreatureIds)
            {
                if (!actorIds.Add(defeatedId))
                    throw new ArgumentException(
                        $"Actor identity '{defeatedId}' is duplicated on '{floorPath}'."
                    );
            }

            IEnumerable<DungeonActorSaveState> allStates = party
                .Select(member => member.State)
                .Concat(enemyStates);
            foreach (
                DungeonTimedEffectSaveState effect in allStates.SelectMany(state =>
                    state.TimedEffects
                )
            )
            {
                if (!actorIds.Contains(effect.SourceActorId))
                    throw new ArgumentException(
                        $"Timed effect source actor '{effect.SourceActorId}' is unavailable on '{floorPath}'."
                    );
            }
        }

        private static void ValidateCurrentFloorParty(
            IReadOnlyList<DungeonPartyMemberSaveState> party,
            DungeonLevelDocument floor
        )
        {
            HashSet<string> livingCells = new(
                party
                    .Where(member => !member.IsDefeated)
                    .Select(member => member.CellX + ":" + member.CellZ),
                StringComparer.Ordinal
            );
            foreach (DungeonPartyMemberSaveState member in party)
            {
                if (!IsWalkable(floor.Rows, member.CellX, member.CellZ))
                    throw new ArgumentException(
                        $"Party member '{member.RosterSlotId}' is not on a walkable current-floor "
                            + $"cell ({member.CellX}, {member.CellZ})."
                    );
            }
            if (
                floor.RuntimeState.Creatures.Any(creature =>
                    livingCells.Contains(creature.Cell.X + ":" + creature.Cell.Z)
                )
            )
                throw new ArgumentException(
                    "A living party member and enemy cannot occupy the same current-floor cell."
                );
        }

        private static DungeonRunSaveManifest CloneManifest(DungeonRunSaveManifest source) =>
            JsonUtility.FromJson<DungeonRunSaveManifest>(JsonUtility.ToJson(source));

        private static DungeonFloorSavePayload ClonePayload(DungeonFloorSavePayload source)
        {
            if (source == null)
                return null;
            return new DungeonFloorSavePayload { Path = source.Path, FloorJson = source.FloorJson };
        }

        private static DungeonPartyMemberSaveState[] CopyParty(
            IEnumerable<DungeonPartyMemberSaveState> party
        )
        {
            if (party == null)
                throw new ArgumentNullException(nameof(party));
            return party.ToArray();
        }

        private static DungeonFloorSaveReference CreateReference(int depth) =>
            new()
            {
                Depth = depth,
                DocumentVersion = DungeonSaveSchema.FloorDocumentVersion,
                Path = DungeonSaveSchema.FloorPath(depth),
            };

        private static DungeonFloorSavePayload CreatePayload(
            string path,
            DungeonLevelDocument floor
        ) => new() { Path = path, FloorJson = DungeonLevelJsonSerializer.Serialize(floor) };

        private static int ParseCanonicalDepth(string path)
        {
            string value = path.Substring("floors/".Length);
            return int.Parse(
                value.Substring(0, value.Length - ".json".Length),
                CultureInfo.InvariantCulture
            );
        }

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
        [Serializable]
        private sealed class SaveFile
        {
            public DungeonRunSaveManifest Manifest;
            public DungeonFloorSavePayload[] Floors;
        }

        internal static string Serialize(DungeonRunSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            return JsonUtility.ToJson(
                new SaveFile { Manifest = save.Manifest, Floors = save.FloorPayloads.ToArray() }
            );
        }

        internal static DungeonSaveResult<DungeonRunSave> Parse(string json, string path)
        {
            try
            {
                SaveFile file = JsonUtility.FromJson<SaveFile>(json);
                if (file?.Manifest == null)
                    throw new ArgumentException("The autosave manifest is missing.");
                if (file.Manifest.DocumentVersion != DungeonSaveSchema.ManifestVersion)
                {
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        file.Manifest.DocumentVersion == 0
                            ? DungeonSaveDiagnosticCode.CorruptSave
                            : DungeonSaveDiagnosticCode.IncompatibleVersion,
                        "manifest.documentVersion",
                        "The autosave manifest version is missing or unsupported."
                    );
                }
                if (file.Manifest.Floors != null)
                {
                    DungeonFloorSaveReference unsupported = file.Manifest.Floors.FirstOrDefault(
                        reference =>
                            reference != null
                            && reference.DocumentVersion != DungeonSaveSchema.FloorDocumentVersion
                    );
                    if (unsupported != null)
                    {
                        return DungeonSaveResult<DungeonRunSave>.Failure(
                            unsupported.DocumentVersion == 0
                                ? DungeonSaveDiagnosticCode.CorruptSave
                                : DungeonSaveDiagnosticCode.IncompatibleVersion,
                            "manifest.floors.documentVersion",
                            "A floor document version is missing or unsupported."
                        );
                    }
                }
                return DungeonSaveResult<DungeonRunSave>.Success(
                    new DungeonRunSave(file.Manifest, file.Floors)
                );
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NullReferenceException
                )
            {
                return DungeonSaveResult<DungeonRunSave>.Failure(
                    DungeonSaveDiagnosticCode.CorruptSave,
                    path,
                    exception.Message
                );
            }
        }

        internal static string SerializeActor(DungeonActorSaveState state)
        {
            ValidateActor(state);
            return JsonUtility.ToJson(state);
        }

        internal static DungeonSaveResult<DungeonActorSaveState> ParseActor(string json)
        {
            try
            {
                DungeonActorSaveState state = JsonUtility.FromJson<DungeonActorSaveState>(json);
                ValidateActor(state);
                return DungeonSaveResult<DungeonActorSaveState>.Success(state);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NullReferenceException
                )
            {
                return DungeonSaveResult<DungeonActorSaveState>.Failure(
                    DungeonSaveDiagnosticCode.CorruptSave,
                    "actor.state",
                    exception.Message
                );
            }
        }

        internal static void ValidateManifest(DungeonRunSaveManifest manifest)
        {
            if (
                manifest == null
                || manifest.DocumentVersion != DungeonSaveSchema.ManifestVersion
                || string.IsNullOrWhiteSpace(manifest.GeneratorVersion)
                || manifest.CurrentDepth < 0
                || manifest.Floors == null
                || manifest.Floors.Length == 0
                || manifest.Party == null
                || manifest.Party.Length == 0
            )
                throw new ArgumentException("The autosave manifest is incomplete or incompatible.");

            HashSet<string> slots = new(StringComparer.Ordinal);
            HashSet<string> livingCells = new(StringComparer.Ordinal);
            foreach (DungeonPartyMemberSaveState member in manifest.Party)
            {
                if (
                    member == null
                    || string.IsNullOrWhiteSpace(member.RosterSlotId)
                    || string.IsNullOrWhiteSpace(member.CreatureContentId)
                    || !slots.Add(member.RosterSlotId)
                    || member.CurrentHitPoints < 0
                    || member.IsDefeated != (member.CurrentHitPoints == 0)
                    || !member.IsDefeated && !livingCells.Add(member.CellX + ":" + member.CellZ)
                )
                    throw new ArgumentException("A saved party member is invalid or duplicated.");
                ValidateActor(member.State);
            }
        }

        internal static void ValidateActor(DungeonActorSaveState state)
        {
            if (
                state == null
                || state.TemporaryHitPoints < 0
                || state.TemporaryHitPointImmunities == null
                || state.Conditions == null
                || state.TimedEffects == null
                || state.PreparedEffects == null
                || state.Equipment == null
                || state.Equipment.LeftHandId == null
                || state.Equipment.RightHandId == null
                || state.Equipment.ArmorId == null
                || state.Equipment.Ammunition == null
                || state.Equipment.UnloadedWeaponIds == null
            )
                throw new ArgumentException("Saved actor state is incomplete.");
            state.TemporaryHitPointSource ??= string.Empty;
            if (state.TemporaryHitPoints == 0 && state.TemporaryHitPointSource.Length > 0)
                throw new ArgumentException(
                    "Zero temporary Hit Points cannot retain an owning source."
                );
            if (
                state.TemporaryHitPointImmunities.Any(string.IsNullOrWhiteSpace)
                || state.Conditions.Any(item =>
                    item == null
                    || string.IsNullOrWhiteSpace(item.ConditionId)
                    || string.IsNullOrWhiteSpace(item.SourceKey)
                )
                || state.TimedEffects.Any(item =>
                    item == null
                    || !IsSupportedTimedEffect(item.Kind)
                    || string.IsNullOrWhiteSpace(item.SourceActorId)
                    || item.RemainingTurnStarts < 0
                )
                || state.PreparedEffects.Any(item =>
                    item == null
                    || string.IsNullOrWhiteSpace(item.Name)
                    || string.IsNullOrWhiteSpace(item.Slug)
                    || string.IsNullOrWhiteSpace(item.SourceSlug)
                )
                || state.Equipment.Ammunition.Any(item =>
                    string.IsNullOrWhiteSpace(item.ammoName) || item.quantity < 0
                )
                || state.Equipment.UnloadedWeaponIds.Any(string.IsNullOrWhiteSpace)
            )
                throw new ArgumentException("Saved actor state contains an invalid entry.");
        }

        private static bool IsSupportedTimedEffect(string kind) =>
            kind == "shield"
            || kind == "guidance"
            || kind == "guidance-immunity"
            || kind == "bless"
            || kind == "infuse-vitality";
    }
}
