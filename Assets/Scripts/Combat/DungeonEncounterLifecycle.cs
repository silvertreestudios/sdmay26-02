using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.Combat.Encounters
{
    /// <summary>Identifies the persistent lifecycle phase of one room encounter group.</summary>
    public enum DungeonEncounterGroupState
    {
        /// <summary>The room has not been entered and its planned creatures remain unmaterialized.</summary>
        Dormant,

        /// <summary>The group's living creatures participate in the current combat.</summary>
        Active,

        /// <summary>The group retains meaningful state while no combat is running for it.</summary>
        Suspended,

        /// <summary>The group is permanently resolved and can never activate again.</summary>
        Cleared,
    }

    /// <summary>Describes how entering a room affected its encounter group.</summary>
    public enum DungeonRoomEntryTransition
    {
        /// <summary>A dormant group became the first active group in a new combat.</summary>
        FirstActivation,

        /// <summary>A dormant group joined a combat that already had an active group.</summary>
        Reinforcement,

        /// <summary>A suspended group became active again.</summary>
        Resume,

        /// <summary>The room's group was already active, so entry made no state change.</summary>
        AlreadyActive,

        /// <summary>The room's group was permanently cleared, so entry made no state change.</summary>
        Cleared,
    }

    /// <summary>Describes whether a party-position evaluation suspended the current combat.</summary>
    public enum DungeonEncounterSuspensionTransition
    {
        /// <summary>
        /// A living PC remains in an active encounter region, or a living active enemy is still
        /// moving or has left its source room and therefore requires turn authority.
        /// </summary>
        RemainedActive,

        /// <summary>Every active group became suspended because all living PCs left their regions.</summary>
        Suspended,
    }

    /// <summary>Provides an immutable view of one planned creature instance.</summary>
    public sealed class DungeonEncounterCreatureView
    {
        internal DungeonEncounterCreatureView(
            string instanceId,
            string creatureId,
            string encounterId,
            bool isDefeated
        )
        {
            InstanceId = instanceId;
            CreatureId = creatureId;
            EncounterId = encounterId;
            IsDefeated = isDefeated;
        }

        /// <summary>Gets the stable instance ID derived from encounter ID and plan order.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the creature content ID from the immutable encounter plan.</summary>
        public string CreatureId { get; }

        /// <summary>Gets the stable encounter ID that owns this instance.</summary>
        public string EncounterId { get; }

        /// <summary>Gets whether this instance has been permanently defeated.</summary>
        public bool IsDefeated { get; }
    }

    /// <summary>Provides an immutable view of one encounter group and its creature instances.</summary>
    public sealed class DungeonEncounterGroupView
    {
        internal DungeonEncounterGroupView(
            DungeonEncounterPlan plan,
            DungeonEncounterGroupState state,
            IEnumerable<DungeonEncounterCreatureView> creatures
        )
        {
            Plan = plan;
            State = state;
            Creatures = Array.AsReadOnly(creatures.ToArray());
            LivingCreatures = Array.AsReadOnly(
                Creatures.Where(creature => !creature.IsDefeated).ToArray()
            );
        }

        /// <summary>Gets the immutable generation plan that defines this group.</summary>
        public DungeonEncounterPlan Plan { get; }

        /// <summary>Gets the group's current persistent lifecycle state.</summary>
        public DungeonEncounterGroupState State { get; }

        /// <summary>Gets every stable creature instance in deterministic plan order.</summary>
        public IReadOnlyList<DungeonEncounterCreatureView> Creatures { get; }

        /// <summary>Gets the living creature instances in deterministic plan order.</summary>
        public IReadOnlyList<DungeonEncounterCreatureView> LivingCreatures { get; }
    }

    /// <summary>Reports the complete lifecycle effect of a living PC entering a planned room.</summary>
    public sealed class DungeonRoomEntryResult
    {
        internal DungeonRoomEntryResult(
            DungeonEncounterGroupView encounter,
            DungeonRoomEntryTransition transition,
            bool startsCombat,
            bool joinsRunningCombat,
            bool completedImmediately
        )
        {
            Encounter = encounter;
            Transition = transition;
            StartsCombat = startsCombat;
            JoinsRunningCombat = joinsRunningCombat;
            CompletedImmediately = completedImmediately;
        }

        /// <summary>Gets the encounter state after processing room entry.</summary>
        public DungeonEncounterGroupView Encounter { get; }

        /// <summary>Gets the state transition, including explicit no-op outcomes.</summary>
        public DungeonRoomEntryTransition Transition { get; }

        /// <summary>Gets whether the caller should start a new combat with this group.</summary>
        public bool StartsCombat { get; }

        /// <summary>Gets whether this group should be inserted into an already-running combat.</summary>
        public bool JoinsRunningCombat { get; }

        /// <summary>
        /// Gets whether an empty plan was permanently cleared during entry without producing a
        /// combatant.
        /// </summary>
        public bool CompletedImmediately { get; }
    }

    /// <summary>Reports whether evaluating living-PC regions suspended the active groups.</summary>
    public sealed class DungeonEncounterSuspensionResult
    {
        internal DungeonEncounterSuspensionResult(
            DungeonEncounterSuspensionTransition transition,
            IEnumerable<string> suspendedEncounterIds
        )
        {
            Transition = transition;
            SuspendedEncounterIds = Array.AsReadOnly(suspendedEncounterIds.ToArray());
        }

        /// <summary>Gets whether active combat remained active or became suspended.</summary>
        public DungeonEncounterSuspensionTransition Transition { get; }

        /// <summary>Gets the stable IDs changed to suspended, ordered deterministically.</summary>
        public IReadOnlyList<string> SuspendedEncounterIds { get; }
    }

    /// <summary>Reports the lifecycle effect of permanently defeating one creature instance.</summary>
    public sealed class DungeonCreatureDefeatResult
    {
        internal DungeonCreatureDefeatResult(
            string encounterId,
            string creatureInstanceId,
            int remainingLivingCreatureCount,
            bool groupCleared,
            bool currentCombatCompleted
        )
        {
            EncounterId = encounterId;
            CreatureInstanceId = creatureInstanceId;
            RemainingLivingCreatureCount = remainingLivingCreatureCount;
            GroupCleared = groupCleared;
            CurrentCombatCompleted = currentCombatCompleted;
        }

        /// <summary>Gets the stable encounter ID that owned the defeated creature.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the stable instance ID that was permanently defeated.</summary>
        public string CreatureInstanceId { get; }

        /// <summary>Gets the number of living creatures remaining in that encounter group.</summary>
        public int RemainingLivingCreatureCount { get; }

        /// <summary>Gets whether this defeat permanently cleared its encounter group.</summary>
        public bool GroupCleared { get; }

        /// <summary>
        /// Gets whether clearing this group left no active groups, allowing the current combat to
        /// return to exploration. Suspended groups elsewhere do not prevent completion.
        /// </summary>
        public bool CurrentCombatCompleted { get; }
    }
}
