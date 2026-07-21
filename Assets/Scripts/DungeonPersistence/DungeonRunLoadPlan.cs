using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Floors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence
{
    /// <summary>
    /// Applies a prevalidated actor restore and then reinstates the saved living exploration leader.
    /// </summary>
    public sealed class DungeonRunActorRestorePlan
    {
        private readonly DungeonActorRestorePlan actors;
        private readonly DungeonEncounterRuntimeController runtime;
        private readonly ActionController leader;
        private readonly bool hasLeader;
        private bool applied;

        internal DungeonRunActorRestorePlan(
            DungeonActorRestorePlan actors,
            DungeonEncounterRuntimeController runtime,
            ActionController leader,
            bool hasLeader
        )
        {
            this.actors = actors;
            this.runtime = runtime;
            this.leader = leader;
            this.hasLeader = hasLeader;
        }

        /// <summary>Restores every actor and the selected leader exactly once.</summary>
        public void Apply()
        {
            if (applied)
                throw new InvalidOperationException(
                    "A dungeon run actor restore plan can only be applied once."
                );
            if (hasLeader && !runtime.CanSelectExplorationLeader(leader))
                throw new InvalidOperationException(
                    "The saved living exploration leader cannot be restored in the current runtime state."
                );
            actors.Apply();
            if (hasLeader && !runtime.TrySelectExplorationLeader(leader))
                throw new InvalidOperationException(
                    "The saved living exploration leader could not be restored."
                );
            applied = true;
        }
    }

    /// <summary>
    /// Represents either a fully prevalidated current-floor load plan or structured diagnostics.
    /// </summary>
    public abstract class DungeonRunLoadPreparationResult
    {
        private protected DungeonRunLoadPreparationResult(
            bool isSuccess,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether a complete no-mutation load plan is available.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets compatibility or corruption diagnostics suitable for menu presentation.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }
    }

    /// <summary>Contains one complete no-mutation load plan.</summary>
    public sealed class DungeonRunLoadPreparationSuccess : DungeonRunLoadPreparationResult
    {
        internal DungeonRunLoadPreparationSuccess(DungeonRunLoadPlan plan)
            : base(true, Array.Empty<DungeonSaveDiagnostic>())
        {
            Plan = plan;
        }

        /// <summary>Gets the fully prevalidated plan.</summary>
        public DungeonRunLoadPlan Plan { get; }
    }

    /// <summary>Reports why no scene population may begin.</summary>
    public sealed class DungeonRunLoadPreparationFailure : DungeonRunLoadPreparationResult
    {
        internal DungeonRunLoadPreparationFailure(IEnumerable<DungeonSaveDiagnostic> diagnostics)
            : base(false, diagnostics) { }
    }

    /// <summary>
    /// Holds a validated current-floor population document and prepares exact actor restoration
    /// after the map and fresh actor definitions have been materialized.
    /// </summary>
    public sealed class DungeonRunLoadPlan
    {
        private DungeonRunLoadPlan(
            DungeonRunSave save,
            DungeonFloorSaveState currentFloor,
            DungeonLevelDocument populationDocument
        )
        {
            Save = save;
            CurrentFloor = currentFloor;
            PopulationDocument = populationDocument;
        }

        /// <summary>Gets the complete loaded run retained for later atomic commits.</summary>
        public DungeonRunSave Save { get; }

        /// <summary>Gets the exact mutable state for the current depth.</summary>
        public DungeonFloorSaveState CurrentFloor { get; }

        /// <summary>Gets the reparsed generated document that may now populate the scene.</summary>
        public DungeonLevelDocument PopulationDocument { get; }

        /// <summary>
        /// Validates a complete repository value and prepares current-floor population without
        /// reading or mutating a Unity scene.
        /// </summary>
        /// <param name="save">The complete value returned by a successful repository load.</param>
        /// <returns>A load plan or structured diagnostics with no partial plan.</returns>
        public static DungeonRunLoadPreparationResult Prepare(DungeonRunSave save)
        {
            IReadOnlyList<DungeonSaveDiagnostic> repositoryDiagnostics =
                DungeonRunSaveValidator.Validate(save);
            if (repositoryDiagnostics.Count > 0)
                return new DungeonRunLoadPreparationFailure(repositoryDiagnostics);

            try
            {
                DungeonFloorSaveState currentFloor = save.Floors.Single(floor =>
                    floor.Depth == save.Manifest.CurrentDepth
                );
                DungeonActorStateAdapter.ValidateForRestore(
                    save.Manifest.Party.Members.Select(member => member.Creature)
                        .Concat(currentFloor.Creatures.Select(creature => creature.Creature))
                );
                foreach (
                    DungeonFloorSaveState floor in save.Floors.Where(floor =>
                        floor.Depth != save.Manifest.CurrentDepth
                    )
                )
                {
                    DungeonActorStateAdapter.ValidateForRestore(
                        floor.Creatures.Select(creature => creature.Creature)
                    );
                }
                DungeonLevelDocument population = DungeonFloorSaveProjector.ProjectForPopulation(
                    currentFloor
                );
                return new DungeonRunLoadPreparationSuccess(
                    new DungeonRunLoadPlan(save, currentFloor, population)
                );
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return new DungeonRunLoadPreparationFailure(
                    new[]
                    {
                        new DungeonSaveDiagnostic(
                            DungeonSaveDiagnosticCode.IncompatibleVersion,
                            DungeonSaveDiagnosticSeverity.Error,
                            "run.currentFloor",
                            "The saved run cannot be restored by this build: " + exception.Message
                        ),
                    }
                );
            }
        }

        /// <summary>
        /// Prevalidates freshly materialized party members and living enemies before applying any
        /// saved actor state.
        /// </summary>
        /// <param name="runtime">The initialized current-floor encounter runtime.</param>
        /// <returns>A single-use plan that restores all actors in safe dependency order.</returns>
        /// <remarks>
        /// Call <see cref="DungeonActorRestorePlan.Apply"/> only after this method succeeds. A
        /// content-definition mismatch therefore leaves every live actor untouched.
        /// </remarks>
        public DungeonRunActorRestorePlan PreflightActors(DungeonEncounterRuntimeController runtime)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (!runtime.IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon encounter runtime must be initialized before actor restoration."
                );

            IReadOnlyList<ActionController> partyControllers = runtime.CapturePartyControllers();
            Dictionary<string, ActionController> partyByRosterSlot = new(StringComparer.Ordinal);
            Dictionary<string, ActionController> partyByActorId = new(StringComparer.Ordinal);
            foreach (ActionController controller in partyControllers)
            {
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (identity == null || !identity.IsConfigured)
                    throw new InvalidOperationException(
                        "Every materialized party member requires configured stable dungeon identity."
                    );
                if (
                    !partyByRosterSlot.TryAdd(identity.RosterSlotId, controller)
                    || !partyByActorId.TryAdd(identity.ActorInstanceId, controller)
                )
                {
                    throw new InvalidOperationException(
                        "Materialized party roster and actor identities must be unique."
                    );
                }
            }

            List<DungeonActorRestoreTarget> targets = new();
            foreach (DungeonPartyMemberSaveState member in Save.Manifest.Party.Members)
            {
                if (
                    !partyByRosterSlot.TryGetValue(
                        member.RosterSlotId,
                        out ActionController controller
                    )
                    || !partyByActorId.TryGetValue(member.Creature.InstanceId, out var byActorId)
                    || byActorId != controller
                )
                {
                    throw new InvalidOperationException(
                        $"Saved party member '{member.RosterSlotId}' has no matching materialized actor."
                    );
                }
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (
                    !string.Equals(
                        identity.CreatureContentId,
                        member.Creature.CreatureContentId,
                        StringComparison.Ordinal
                    )
                )
                    throw new InvalidOperationException(
                        $"Saved party member '{member.RosterSlotId}' does not match its materialized creature content."
                    );
                targets.Add(new DungeonActorRestoreTarget(controller, member.Creature));
            }
            if (targets.Count != partyControllers.Count)
                throw new InvalidOperationException(
                    "The materialized party does not exactly match the saved roster."
                );

            Dictionary<string, DungeonEncounterCreatureSaveState> livingById = CurrentFloor
                .Creatures.Where(creature => !creature.Creature.IsDefeated)
                .ToDictionary(creature => creature.Creature.InstanceId, StringComparer.Ordinal);
            IReadOnlyList<DungeonEncounterCreatureCapture> materialized =
                runtime.CaptureMaterializedCreatures();
            foreach (DungeonEncounterCreatureCapture actor in materialized)
            {
                if (
                    actor.IsDefeated
                    || !livingById.Remove(
                        actor.InstanceId,
                        out DungeonEncounterCreatureSaveState saved
                    )
                    || !string.Equals(
                        saved.EncounterId,
                        actor.EncounterId,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        saved.Creature.CreatureContentId,
                        actor.CreatureContentId,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Materialized encounter actor '{actor.InstanceId}' does not match the saved floor."
                    );
                }

                DungeonEncounterMember member =
                    actor.Controller.GetComponent<DungeonEncounterMember>();
                string expectedToken = DungeonSaveJsonCodec.SerializeCreature(saved.Creature);
                if (
                    member == null
                    || !member.IsConfigured
                    || !string.Equals(
                        member.PersistentState,
                        expectedToken,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Materialized encounter actor '{actor.InstanceId}' lost its canonical restore token."
                    );
                }
                targets.Add(new DungeonActorRestoreTarget(actor.Controller, saved.Creature));
            }
            if (livingById.Count > 0)
                throw new InvalidOperationException(
                    "The current floor did not materialize every saved living encounter actor."
                );

            bool hasLeader = Save.Manifest.Party.LeaderRosterSlotId.Length > 0;
            ActionController leader = hasLeader
                ? partyByRosterSlot[Save.Manifest.Party.LeaderRosterSlotId]
                : default;
            return new DungeonRunActorRestorePlan(
                DungeonActorStateAdapter.PreflightRestore(targets),
                runtime,
                leader,
                hasLeader
            );
        }
    }
}
