using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Captures one party or encounter creature independently of Unity scene serialization.</summary>
    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonCreatureSaveState
    {
        /// <summary>Creates a complete meaningful creature record.</summary>
        /// <param name="instanceId">The stable party or encounter instance identifier.</param>
        /// <param name="creatureContentId">The stable creature catalog identifier.</param>
        /// <param name="cell">The occupied grid cell.</param>
        /// <param name="health">Authoritative health and temporary-HP state.</param>
        /// <param name="isDefeated">Whether the actor is permanently defeated for this run.</param>
        /// <param name="conditions">Source-aware condition applications.</param>
        /// <param name="timedEffects">Non-consumed timed-effect bindings targeting this actor.</param>
        /// <param name="preparedRules">Mutable prepared roll options, effects, and spell resources.</param>
        /// <param name="equipment">Meaningful inventory, equipped-slot, ammunition, and loading state.</param>
        public DungeonCreatureSaveState(
            string instanceId,
            string creatureContentId,
            DungeonSaveCell cell,
            DungeonHealthSaveState health,
            bool isDefeated,
            IEnumerable<DungeonConditionSaveState> conditions,
            IEnumerable<DungeonTimedEffectSaveState> timedEffects,
            DungeonPreparedRuleSaveState preparedRules,
            DungeonEquipmentSaveState equipment
        )
        {
            InstanceId = DungeonSaveContractGuard.RequiredId(instanceId, nameof(instanceId));
            CreatureContentId = DungeonSaveContractGuard.RequiredId(
                creatureContentId,
                nameof(creatureContentId)
            );
            Cell = cell;
            Health = health ?? throw new ArgumentNullException(nameof(health));
            IsDefeated = isDefeated;
            if (isDefeated != (health.CurrentHitPoints == 0))
            {
                throw new ArgumentException(
                    "Defeat state must exactly agree with zero current HP.",
                    nameof(isDefeated)
                );
            }
            Conditions = DungeonSaveContractGuard.UniqueSorted(
                conditions,
                condition => condition.ApplicationId,
                nameof(conditions)
            );
            TimedEffects = DungeonSaveContractGuard.UniqueSorted(
                timedEffects,
                effect => effect.InstanceId,
                nameof(timedEffects)
            );
            if (TimedEffects.Any(effect => effect.TargetCreatureId != InstanceId))
            {
                throw new ArgumentException(
                    "Every timed effect stored on an actor must target that actor's stable ID.",
                    nameof(timedEffects)
                );
            }
            DungeonSaveContractGuard.RequireUnique(
                TimedEffects.Select(effect => effect.BindingCreationOrder.ToString("D20")),
                nameof(timedEffects)
            );
            PreparedRules = preparedRules ?? throw new ArgumentNullException(nameof(preparedRules));
            Equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        }

        /// <summary>Gets the stable party or encounter instance identifier.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the stable creature catalog identifier.</summary>
        public string CreatureContentId { get; }

        /// <summary>Gets the occupied grid cell.</summary>
        public DungeonSaveCell Cell { get; }

        /// <summary>Gets authoritative health and temporary-HP state.</summary>
        public DungeonHealthSaveState Health { get; }

        /// <summary>Gets whether the actor is permanently defeated for this run.</summary>
        public bool IsDefeated { get; }

        /// <summary>Gets source-aware conditions in deterministic order.</summary>
        public IReadOnlyList<DungeonConditionSaveState> Conditions { get; }

        /// <summary>Gets timed effects ordered by stable effect instance ID.</summary>
        public IReadOnlyList<DungeonTimedEffectSaveState> TimedEffects { get; }

        /// <summary>Gets mutable prepared roll options, active effects, and spell resources.</summary>
        public DungeonPreparedRuleSaveState PreparedRules { get; }

        /// <summary>Gets meaningful equipment and ammunition state.</summary>
        public DungeonEquipmentSaveState Equipment { get; }
    }

    /// <summary>Associates a party creature with its stable roster slot.</summary>
    internal sealed class DungeonPartyMemberSaveState
    {
        /// <summary>Creates a party member record from a versioned actor-state token.</summary>
        /// <param name="rosterSlotId">The stable roster slot or character-save identifier.</param>
        /// <param name="actorStateJson">The complete versioned actor state.</param>
        public DungeonPartyMemberSaveState(string rosterSlotId, string actorStateJson)
        {
            RosterSlotId = DungeonSaveContractGuard.RequiredId(rosterSlotId, nameof(rosterSlotId));
            DungeonSaveResult<DungeonCreatureSaveState> parsed = DungeonSaveJsonCodec.ParseCreature(
                actorStateJson
            );
            if (!parsed.IsSuccess)
                throw new ArgumentException(
                    "Party actor state is incomplete or incompatible.",
                    nameof(actorStateJson)
                );
            Creature = parsed.Value;
            ActorStateJson = DungeonSaveJsonCodec.SerializeCreature(Creature);
        }

        /// <summary>Gets the stable roster slot or character-save identifier.</summary>
        public string RosterSlotId { get; }

        /// <summary>Gets the canonical versioned actor-state token.</summary>
        public string ActorStateJson { get; }

        internal DungeonPartyMemberSaveState(string rosterSlotId, DungeonCreatureSaveState creature)
        {
            RosterSlotId = DungeonSaveContractGuard.RequiredId(rosterSlotId, nameof(rosterSlotId));
            Creature = creature ?? throw new ArgumentNullException(nameof(creature));
            ActorStateJson = DungeonSaveJsonCodec.SerializeCreature(creature);
        }

        internal DungeonCreatureSaveState Creature { get; }
    }

    /// <summary>Captures the ordered party roster and its selected exploration leader.</summary>
    internal sealed class DungeonPartySaveState
    {
        /// <summary>Creates an immutable party record while preserving meaningful roster order.</summary>
        /// <param name="leaderRosterSlotId">The living exploration leader's roster slot, or an empty string only after a total-party defeat.</param>
        /// <param name="members">Distinct roster members in follower order.</param>
        public DungeonPartySaveState(
            string leaderRosterSlotId,
            IEnumerable<DungeonPartyMemberSaveState> members
        )
        {
            LeaderRosterSlotId = DungeonSaveContractGuard.Normalized(leaderRosterSlotId);
            if (members == null)
                throw new ArgumentNullException(nameof(members));
            DungeonPartyMemberSaveState[] copied = members.ToArray();
            DungeonSaveContractGuard.RequiredElements(copied, nameof(members));
            if (copied.Length == 0)
                throw new ArgumentException(
                    "A saved party requires at least one member.",
                    nameof(members)
                );
            DungeonSaveContractGuard.RequireUnique(
                copied.Select(member => member.RosterSlotId),
                nameof(members)
            );
            DungeonSaveContractGuard.RequireUnique(
                copied.Select(member => member.Creature.InstanceId),
                nameof(members)
            );
            DungeonSaveContractGuard.RequireUnique(
                copied
                    .Where(member => !member.Creature.IsDefeated)
                    .Select(member => member.Creature.Cell.X + ":" + member.Creature.Cell.Z),
                nameof(members)
            );
            DungeonPartyMemberSaveState[] living = copied
                .Where(member => !member.Creature.IsDefeated)
                .ToArray();
            if (
                living.Length > 0
                && !living.Any(member => member.RosterSlotId == LeaderRosterSlotId)
            )
            {
                throw new ArgumentException(
                    "The exploration leader must identify a living saved party member.",
                    nameof(leaderRosterSlotId)
                );
            }
            if (living.Length == 0 && LeaderRosterSlotId.Length > 0)
            {
                throw new ArgumentException(
                    "A totally defeated party cannot retain an exploration leader.",
                    nameof(leaderRosterSlotId)
                );
            }
            Members = Array.AsReadOnly(copied);
        }

        /// <summary>Gets the living exploration leader's roster slot, or an empty string after total-party defeat.</summary>
        public string LeaderRosterSlotId { get; }

        /// <summary>Gets party members in meaningful follower order.</summary>
        public IReadOnlyList<DungeonPartyMemberSaveState> Members { get; }
    }
}
