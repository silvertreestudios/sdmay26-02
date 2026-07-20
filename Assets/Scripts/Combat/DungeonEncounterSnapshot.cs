using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.Combat.Encounters
{
    /// <summary>Captures one encounter group's persistence-facing lifecycle state.</summary>
    public sealed class DungeonEncounterGroupSnapshot
    {
        /// <summary>Creates a validated immutable encounter-group snapshot.</summary>
        /// <param name="encounterId">The stable non-empty encounter ID.</param>
        /// <param name="state">The persistent lifecycle state to restore.</param>
        /// <param name="defeatedCreatureInstanceIds">Unique stable IDs permanently defeated in this group.</param>
        /// <exception cref="ArgumentException">
        /// The encounter ID is blank, the state is undefined, or a defeated ID is blank or
        /// duplicated.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="defeatedCreatureInstanceIds"/> is null.
        /// </exception>
        public DungeonEncounterGroupSnapshot(
            string encounterId,
            DungeonEncounterGroupState state,
            IEnumerable<string> defeatedCreatureInstanceIds
        )
        {
            if (string.IsNullOrWhiteSpace(encounterId))
                throw new ArgumentException(
                    "An encounter snapshot requires a stable ID.",
                    nameof(encounterId)
                );
            if (!Enum.IsDefined(typeof(DungeonEncounterGroupState), state))
                throw new ArgumentException(
                    "The encounter snapshot state is undefined.",
                    nameof(state)
                );
            if (defeatedCreatureInstanceIds == null)
                throw new ArgumentNullException(nameof(defeatedCreatureInstanceIds));

            string[] defeated = defeatedCreatureInstanceIds
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (defeated.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Defeated creature instance IDs cannot be blank.",
                    nameof(defeatedCreatureInstanceIds)
                );
            }
            if (defeated.Distinct(StringComparer.Ordinal).Count() != defeated.Length)
            {
                throw new ArgumentException(
                    "Defeated creature instance IDs must be unique.",
                    nameof(defeatedCreatureInstanceIds)
                );
            }

            EncounterId = encounterId;
            State = state;
            DefeatedCreatureInstanceIds = Array.AsReadOnly(defeated);
        }

        /// <summary>Gets the stable encounter ID.</summary>
        public string EncounterId { get; }

        /// <summary>Gets the group's persistent lifecycle state.</summary>
        public DungeonEncounterGroupState State { get; }

        /// <summary>Gets unique defeated instance IDs in deterministic plan order.</summary>
        public IReadOnlyList<string> DefeatedCreatureInstanceIds { get; }
    }

    /// <summary>
    /// Captures the lifecycle state required to restore every encounter group on one dungeon floor.
    /// </summary>
    public sealed class DungeonEncounterLifecycleSnapshot
    {
        /// <summary>Creates a validated immutable floor encounter snapshot.</summary>
        /// <param name="groups">Exactly one snapshot per stable encounter ID.</param>
        /// <exception cref="ArgumentNullException"><paramref name="groups"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// A group is null or multiple groups use the same encounter ID.
        /// </exception>
        public DungeonEncounterLifecycleSnapshot(IEnumerable<DungeonEncounterGroupSnapshot> groups)
        {
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            DungeonEncounterGroupSnapshot[] copied = groups.ToArray();
            if (copied.Any(group => group == null))
                throw new ArgumentException(
                    "Encounter snapshots cannot contain null groups.",
                    nameof(groups)
                );
            copied = copied.OrderBy(group => group.EncounterId, StringComparer.Ordinal).ToArray();
            if (
                copied.Select(group => group.EncounterId).Distinct(StringComparer.Ordinal).Count()
                != copied.Length
            )
            {
                throw new ArgumentException(
                    "Encounter snapshots must use unique encounter IDs.",
                    nameof(groups)
                );
            }

            Groups = Array.AsReadOnly(copied);
        }

        /// <summary>Gets all group snapshots ordered by stable encounter ID.</summary>
        public IReadOnlyList<DungeonEncounterGroupSnapshot> Groups { get; }

        /// <summary>Builds encounter lifecycle state from a validated persisted dungeon document.</summary>
        /// <param name="plans">The immutable encounter plans from the persisted document.</param>
        /// <param name="runtimeState">The document's required mutable runtime state.</param>
        /// <returns>
        /// A complete snapshot where untouched groups remain dormant, materialized unresolved
        /// groups resume suspended, and resolved groups remain cleared.
        /// </returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// Runtime creature identities do not exactly cover the touched plan entries or conflict
        /// with their immutable encounter and content identities.
        /// </exception>
        public static DungeonEncounterLifecycleSnapshot FromRuntimeState(
            IEnumerable<DungeonEncounterPlan> plans,
            DungeonRuntimeState runtimeState
        )
        {
            if (plans == null)
                throw new ArgumentNullException(nameof(plans));
            if (runtimeState == null)
                throw new ArgumentNullException(nameof(runtimeState));

            DungeonEncounterPlan[] copiedPlans = plans.ToArray();
            Dictionary<string, (DungeonEncounterPlan Plan, int Index)> expectedByInstanceId = new(
                StringComparer.Ordinal
            );
            foreach (DungeonEncounterPlan plan in copiedPlans)
            {
                for (int index = 0; index < plan.CreatureIds.Count; index++)
                {
                    string instanceId = DungeonEncounterStateMachine.CreateCreatureInstanceId(
                        plan.Id,
                        index
                    );
                    if (!expectedByInstanceId.TryAdd(instanceId, (plan, index)))
                    {
                        throw new ArgumentException(
                            $"Encounter plans produce duplicate instance ID '{instanceId}'.",
                            nameof(plans)
                        );
                    }
                }
            }

            HashSet<string> defeated = new(
                runtimeState.DefeatedCreatureIds,
                StringComparer.Ordinal
            );
            if (defeated.Any(instanceId => !expectedByInstanceId.ContainsKey(instanceId)))
                throw new ArgumentException(
                    "Runtime state contains a defeated creature outside the encounter plans.",
                    nameof(runtimeState)
                );

            Dictionary<string, DungeonCreatureRuntimeState> livingByInstanceId = new(
                StringComparer.Ordinal
            );
            foreach (DungeonCreatureRuntimeState creature in runtimeState.Creatures)
            {
                if (
                    creature == null
                    || creature.HitPoints <= 0
                    || !expectedByInstanceId.TryGetValue(
                        creature.InstanceId,
                        out (DungeonEncounterPlan Plan, int Index) expected
                    )
                    || !string.Equals(
                        creature.EncounterId,
                        expected.Plan.Id,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        creature.CreatureId,
                        expected.Plan.CreatureIds[expected.Index],
                        StringComparison.Ordinal
                    )
                    || !livingByInstanceId.TryAdd(creature.InstanceId, creature)
                )
                {
                    throw new ArgumentException(
                        "Every live runtime creature must uniquely match its plan-derived instance, encounter, and content IDs.",
                        nameof(runtimeState)
                    );
                }
            }

            HashSet<string> resolved = new(
                runtimeState.ResolvedEncounterIds,
                StringComparer.Ordinal
            );
            if (
                !resolved.SetEquals(
                    copiedPlans.Where(plan => plan.IsResolved).Select(plan => plan.Id)
                )
            )
            {
                throw new ArgumentException(
                    "Runtime resolved encounter IDs must exactly match resolved plan flags.",
                    nameof(runtimeState)
                );
            }

            List<DungeonEncounterGroupSnapshot> groups = new(copiedPlans.Length);
            foreach (DungeonEncounterPlan plan in copiedPlans)
            {
                string[] expectedIds = Enumerable
                    .Range(0, plan.CreatureIds.Count)
                    .Select(index =>
                        DungeonEncounterStateMachine.CreateCreatureInstanceId(plan.Id, index)
                    )
                    .ToArray();
                string[] groupDefeated = expectedIds.Where(defeated.Contains).ToArray();
                int liveCount = expectedIds.Count(livingByInstanceId.ContainsKey);
                if (plan.IsResolved)
                {
                    if (liveCount > 0)
                        throw new ArgumentException(
                            $"Resolved encounter '{plan.Id}' cannot contain live runtime creatures.",
                            nameof(runtimeState)
                        );
                    if (groupDefeated.Length != expectedIds.Length)
                        throw new ArgumentException(
                            $"Resolved encounter '{plan.Id}' must persist every planned creature as defeated.",
                            nameof(runtimeState)
                        );
                    groups.Add(
                        new DungeonEncounterGroupSnapshot(
                            plan.Id,
                            DungeonEncounterGroupState.Cleared,
                            expectedIds
                        )
                    );
                    continue;
                }

                bool wasMaterialized = liveCount > 0 || groupDefeated.Length > 0;
                if (wasMaterialized && liveCount + groupDefeated.Length != expectedIds.Length)
                {
                    throw new ArgumentException(
                        $"Persisted encounter '{plan.Id}' must account for every planned creature after materialization.",
                        nameof(runtimeState)
                    );
                }
                if (groupDefeated.Length == expectedIds.Length && expectedIds.Length > 0)
                {
                    throw new ArgumentException(
                        $"Encounter '{plan.Id}' has no survivors but is not marked resolved.",
                        nameof(runtimeState)
                    );
                }

                groups.Add(
                    new DungeonEncounterGroupSnapshot(
                        plan.Id,
                        wasMaterialized
                            ? DungeonEncounterGroupState.Suspended
                            : DungeonEncounterGroupState.Dormant,
                        groupDefeated
                    )
                );
            }

            return new DungeonEncounterLifecycleSnapshot(groups);
        }
    }
}
