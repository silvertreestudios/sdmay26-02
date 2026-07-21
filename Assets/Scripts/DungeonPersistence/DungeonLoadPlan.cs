using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence
{
    /// <summary>Applies a prevalidated actor restore and reinstates the saved living leader.</summary>
    internal sealed class DungeonRunActorRestorePlan
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
                    "The saved living exploration leader cannot be restored."
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
    /// Holds a validated current-floor document and prepares exact actor restoration after
    /// population.
    /// </summary>
    internal sealed class DungeonRunLoadPlan
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

        /// <summary>Gets the exact current-depth document.</summary>
        public DungeonFloorSaveState CurrentFloor { get; }

        /// <summary>Gets the generated document that may populate the scene.</summary>
        public DungeonLevelDocument PopulationDocument { get; }

        /// <summary>Validates a repository value without reading or mutating a Unity scene.</summary>
        public static DungeonSaveResult<DungeonRunLoadPlan> Prepare(DungeonRunSave save)
        {
            IReadOnlyList<DungeonSaveDiagnostic> diagnostics = DungeonRunSaveValidator.Validate(
                save
            );
            if (diagnostics.Count > 0)
                return DungeonSaveResult<DungeonRunLoadPlan>.Failure(diagnostics);

            try
            {
                DungeonFloorSaveState current = save.Floors.Single(floor =>
                    floor.Depth == save.Manifest.CurrentDepth
                );
                return DungeonSaveResult<DungeonRunLoadPlan>.Success(
                    new DungeonRunLoadPlan(save, current, current.ParseDocument())
                );
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return DungeonSaveResult<DungeonRunLoadPlan>.Failure(
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

        /// <summary>Prevalidates all freshly materialized actors before applying any saved state.</summary>
        public DungeonRunActorRestorePlan PreflightActors(DungeonEncounterRuntimeController runtime)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (!runtime.IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon runtime must be initialized before actor restoration."
                );

            IReadOnlyList<ActionController> partyControllers = runtime.CapturePartyControllers();
            Dictionary<string, ActionController> partyByRosterSlot = new(StringComparer.Ordinal);
            Dictionary<string, ActionController> partyByActorId = new(StringComparer.Ordinal);
            foreach (ActionController controller in partyControllers)
            {
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (
                    identity == null
                    || !identity.IsConfigured
                    || !partyByRosterSlot.TryAdd(identity.RosterSlotId, controller)
                    || !partyByActorId.TryAdd(identity.ActorInstanceId, controller)
                )
                {
                    throw new InvalidOperationException(
                        "Materialized party identities must be configured and unique."
                    );
                }
            }

            List<DungeonActorRestoreTarget> targets = new();
            foreach (DungeonPartyMemberSaveState member in Save.Manifest.Party.Members)
            {
                DungeonCreatureSaveState state = member.Creature;
                if (
                    !partyByRosterSlot.TryGetValue(
                        member.RosterSlotId,
                        out ActionController controller
                    )
                    || !partyByActorId.TryGetValue(state.InstanceId, out ActionController byActorId)
                    || byActorId != controller
                )
                {
                    throw new InvalidOperationException(
                        $"Saved party member '{member.RosterSlotId}' has no matching actor."
                    );
                }

                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (
                    !string.Equals(
                        identity.CreatureContentId,
                        state.CreatureContentId,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Saved party member '{member.RosterSlotId}' does not match its materialized creature content."
                    );
                }
                targets.Add(new DungeonActorRestoreTarget(controller, state));
            }
            if (targets.Count != partyControllers.Count)
                throw new InvalidOperationException(
                    "The materialized party does not exactly match the saved roster."
                );

            Dictionary<string, DungeonCreatureRuntimeState> livingById =
                PopulationDocument.RuntimeState.Creatures.ToDictionary(
                    creature => creature.InstanceId,
                    StringComparer.Ordinal
                );
            foreach (
                DungeonEncounterCreatureCapture actor in runtime.CaptureMaterializedCreatures()
            )
            {
                if (
                    actor.IsDefeated
                    || !livingById.Remove(actor.InstanceId, out DungeonCreatureRuntimeState saved)
                    || saved.EncounterId != actor.EncounterId
                    || saved.CreatureId != actor.CreatureContentId
                )
                {
                    throw new InvalidOperationException(
                        $"Materialized encounter actor '{actor.InstanceId}' does not match the save."
                    );
                }

                DungeonSaveResult<DungeonCreatureSaveState> parsed =
                    DungeonSaveJsonCodec.ParseCreature(saved.State);
                if (!parsed.IsSuccess)
                    throw new InvalidOperationException(
                        $"Encounter actor '{actor.InstanceId}' has invalid restore state."
                    );

                DungeonEncounterMember member =
                    actor.Controller.GetComponent<DungeonEncounterMember>();
                if (
                    member == null
                    || !member.IsConfigured
                    || !string.Equals(member.PersistentState, saved.State, StringComparison.Ordinal)
                )
                {
                    throw new InvalidOperationException(
                        $"Encounter actor '{actor.InstanceId}' lost its canonical restore token."
                    );
                }
                targets.Add(new DungeonActorRestoreTarget(actor.Controller, parsed.Value));
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
