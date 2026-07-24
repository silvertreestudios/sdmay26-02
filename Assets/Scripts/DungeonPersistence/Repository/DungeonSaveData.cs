using System;
using System.Collections.Generic;
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
        internal const int Version = 1;
        internal const string FloorPath = "current-floor";
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
    internal sealed class DungeonRunSaveManifest
    {
        public int DocumentVersion;
        public int StartingSeed;
        public string GeneratorVersion;
        public int CurrentDepth;
        public int CurrentFloorVersion;
        public string CurrentFloorPath;
        public DungeonPartyMemberSaveState[] Party;
    }

    internal sealed class DungeonRunSave
    {
        internal DungeonRunSave(DungeonRunSaveManifest manifest, DungeonLevelDocument floorDocument)
        {
            DungeonSaveJson.ValidateManifest(manifest);
            Manifest = manifest;
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
                manifest
                    .Party.Where(member => !member.IsDefeated)
                    .Select(member => member.CellX + ":" + member.CellZ),
                StringComparer.Ordinal
            );
            foreach (DungeonPartyMemberSaveState member in manifest.Party)
            {
                if (!IsWalkable(floorDocument.Rows, member.CellX, member.CellZ))
                    throw new ArgumentException(
                        $"Party member '{member.RosterSlotId}' is not on a walkable floor cell."
                    );
            }
            if (
                floorDocument.RuntimeState.Creatures.Any(creature =>
                    livingCells.Contains(creature.Cell.X + ":" + creature.Cell.Z)
                )
            )
                throw new ArgumentException(
                    "A living party member and enemy cannot occupy the same floor cell."
                );
        }

        internal DungeonRunSaveManifest Manifest { get; }
        internal DungeonLevelDocument FloorDocument { get; }

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
            public string FloorJson;
        }

        internal static string Serialize(DungeonRunSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            ValidateManifest(save.Manifest);
            return JsonUtility.ToJson(
                new SaveFile
                {
                    Manifest = save.Manifest,
                    FloorJson = DungeonLevelJsonSerializer.Serialize(save.FloorDocument),
                }
            );
        }

        internal static DungeonSaveResult<DungeonRunSave> Parse(string json, string path)
        {
            try
            {
                SaveFile file = JsonUtility.FromJson<SaveFile>(json);
                if (file?.Manifest == null)
                    throw new ArgumentException("The autosave manifest is missing.");
                if (file.Manifest.DocumentVersion != DungeonSaveSchema.Version)
                {
                    return DungeonSaveResult<DungeonRunSave>.Failure(
                        file.Manifest.DocumentVersion == 0
                            ? DungeonSaveDiagnosticCode.CorruptSave
                            : DungeonSaveDiagnosticCode.IncompatibleVersion,
                        "manifest.documentVersion",
                        "The autosave manifest version is missing or unsupported."
                    );
                }
                ValidateManifest(file.Manifest);
                if (string.IsNullOrWhiteSpace(file.FloorJson))
                    throw new ArgumentException("The current-floor JSON is missing.");

                DungeonLevelParseResult floor = DungeonLevelJsonParser.Parse(file.FloorJson);
                if (!floor.IsSuccess)
                    throw new ArgumentException(
                        string.Join(" ", floor.Diagnostics.Select(item => item.Message))
                    );
                return DungeonSaveResult<DungeonRunSave>.Success(
                    new DungeonRunSave(file.Manifest, floor.Document)
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
                || string.IsNullOrWhiteSpace(manifest.GeneratorVersion)
                || manifest.CurrentDepth < 0
                || manifest.CurrentFloorVersion != DungeonSaveSchema.Version
                || !string.Equals(
                    manifest.CurrentFloorPath,
                    DungeonSaveSchema.FloorPath,
                    StringComparison.Ordinal
                )
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
