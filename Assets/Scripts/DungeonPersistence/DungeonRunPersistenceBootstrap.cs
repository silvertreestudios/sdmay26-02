using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using GridPublic;
using UnityEngine;

namespace Game.DungeonPersistence
{
    /// <summary>Represents a ready persistent dungeon runtime or blocking diagnostics.</summary>
    public sealed class DungeonRunPersistenceBootstrapResult
    {
        private DungeonRunPersistenceBootstrapResult(
            DungeonEncounterRuntimeController runtime,
            bool restoredExistingRun,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            Runtime = runtime;
            RestoredExistingRun = restoredExistingRun;
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        /// <summary>Gets whether dungeon play can begin.</summary>
        public bool IsSuccess => Runtime != null && Diagnostics.Count == 0;

        /// <summary>Gets whether an existing autosave was restored.</summary>
        public bool RestoredExistingRun { get; }

        /// <summary>Gets the initialized current-floor runtime on success.</summary>
        public DungeonEncounterRuntimeController Runtime { get; }

        /// <summary>Gets structured blocking diagnostics.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        internal static DungeonRunPersistenceBootstrapResult Success(
            DungeonEncounterRuntimeController runtime,
            bool restoredExistingRun
        ) => new(runtime, restoredExistingRun, Array.Empty<DungeonSaveDiagnostic>());

        internal static DungeonRunPersistenceBootstrapResult Failure(
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) => new(null, false, diagnostics);
    }

    /// <summary>
    /// Loads or creates one current-floor autosave before generated dungeon play begins.
    /// </summary>
    public static class DungeonRunPersistenceBootstrap
    {
        /// <summary>Initializes production persistence for one generated JSON map.</summary>
        public static DungeonRunPersistenceBootstrapResult Initialize(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot
        )
        {
            string autosaveDirectory = Path.Combine(
                Application.persistentDataPath,
                "DungeonAutosave"
            );
            return Initialize(
                map,
                initialDocument,
                encounterCatalog,
                combatManager,
                sceneParty,
                explorationPresentation,
                runtimeRoot,
                new FileSystemDungeonSaveRepository(autosaveDirectory)
            );
        }

        internal static DungeonRunPersistenceBootstrapResult Initialize(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            IDungeonSaveRepository repository
        )
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (initialDocument == null)
                throw new ArgumentNullException(nameof(initialDocument));
            if (encounterCatalog == null)
                throw new ArgumentNullException(nameof(encounterCatalog));
            if (combatManager == null)
                throw new ArgumentNullException(nameof(combatManager));
            if (sceneParty == null)
                throw new ArgumentNullException(nameof(sceneParty));
            if (explorationPresentation == null)
                throw new ArgumentNullException(nameof(explorationPresentation));
            if (runtimeRoot == null)
                throw new ArgumentNullException(nameof(runtimeRoot));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            ActionController[] party;
            try
            {
                party = ValidateParty(sceneParty);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "party",
                    exception.Message
                );
            }

            DungeonSaveResult<DungeonRunSave> loaded = repository.Load();
            if (loaded.IsSuccess)
            {
                try
                {
                    return Restore(
                        map,
                        initialDocument,
                        encounterCatalog,
                        combatManager,
                        party,
                        explorationPresentation,
                        runtimeRoot,
                        repository,
                        loaded.Value
                    );
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException || exception is ArgumentException)
                {
                    return Failure(
                        DungeonSaveDiagnosticCode.InvalidSnapshot,
                        "restore",
                        exception.Message
                    );
                }
            }

            if (
                loaded.Diagnostics.Count == 1
                && loaded.Diagnostics[0].Code == DungeonSaveDiagnosticCode.MissingSave
            )
            {
                return CreateNew(
                    initialDocument,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtimeRoot,
                    repository
                );
            }
            return DungeonRunPersistenceBootstrapResult.Failure(loaded.Diagnostics);
        }

        private static DungeonRunPersistenceBootstrapResult CreateNew(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            ActionController[] party,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            IDungeonSaveRepository repository
        )
        {
            if (document.RuntimeState != null)
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "floor.runtimeState",
                    "A new run requires a pristine generated floor."
                );

            DungeonEncounterRuntimeController runtime =
                runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
            runtime.InitializePristine(
                document,
                encounterCatalog,
                combatManager,
                party,
                explorationPresentation
            );
            DungeonAutosaveCoordinator coordinator =
                runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
            coordinator.Initialize(document, repository, runtime, party, saveImmediately: true);
            if (coordinator.LastDiagnostics.Count > 0)
                return DungeonRunPersistenceBootstrapResult.Failure(coordinator.LastDiagnostics);
            return DungeonRunPersistenceBootstrapResult.Success(runtime, false);
        }

        private static DungeonRunPersistenceBootstrapResult Restore(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            ActionController[] party,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            IDungeonSaveRepository repository,
            DungeonRunSave save
        )
        {
            if (
                !string.Equals(
                    save.Manifest.GeneratorVersion,
                    initialDocument.Generation.Algorithm,
                    StringComparison.Ordinal
                )
            )
                return Failure(
                    DungeonSaveDiagnosticCode.IncompatibleVersion,
                    "manifest.generatorVersion",
                    "The autosave uses an unsupported dungeon generator."
                );

            ActionController[] orderedParty = OrderParty(party, save.Manifest.Party);
            Dictionary<string, DungeonActorSaveState> enemyState = ParseEnemyState(
                save.FloorDocument.RuntimeState.Creatures
            );
            ValidateTimedEffectSources(save, enemyState.Values);
            Dictionary<string, GameObject> preflightActors = orderedParty.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                controller => controller.gameObject,
                StringComparer.Ordinal
            );
            GameObject ResolvePreflightActor(string actorId) =>
                preflightActors.TryGetValue(actorId, out GameObject actor) ? actor : null;
            _ = save
                .Manifest.Party.Members.Select(
                    (member, index) =>
                        DungeonActorStateAdapter.PrepareRestore(
                            orderedParty[index],
                            member.State,
                            member.CurrentHitPoints,
                            member.IsDefeated,
                            ResolvePreflightActor
                        )
                )
                .ToArray();

            PartyStaging staging = PartyStaging.Stage(orderedParty, save.Manifest.Party.Members);
            if (
                !map.TryPopulateJson(
                    DungeonLevelJsonSerializer.Serialize(save.FloorDocument),
                    map.DungeonCatalog,
                    out MapSourceValidationResult validation
                )
            )
            {
                staging.Rollback();
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "floor",
                    string.Join(" ", validation.Errors)
                );
            }

            DungeonEncounterRuntimeController runtime =
                runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
            runtime.InitializePersisted(
                save.FloorDocument,
                encounterCatalog,
                combatManager,
                orderedParty,
                explorationPresentation
            );

            Dictionary<string, GameObject> actors = new(preflightActors, StringComparer.Ordinal);
            foreach (
                DungeonEncounterMember member in runtimeRoot.GetComponentsInChildren<DungeonEncounterMember>(
                    includeInactive: true
                )
            )
            {
                if (member != null && member.IsConfigured)
                    actors.Add(member.InstanceId, member.gameObject);
            }
            GameObject ResolveActor(string actorId) =>
                actors.TryGetValue(actorId, out GameObject actor) ? actor : null;

            Action[] partyRestores = save
                .Manifest.Party.Members.Select(
                    (member, index) =>
                        DungeonActorStateAdapter.PrepareRestore(
                            orderedParty[index],
                            member.State,
                            member.CurrentHitPoints,
                            member.IsDefeated,
                            ResolveActor
                        )
                )
                .ToArray();

            List<Action> enemyRestores = new();
            foreach (
                DungeonEncounterMember member in runtimeRoot
                    .GetComponentsInChildren<DungeonEncounterMember>(includeInactive: true)
                    .OrderBy(member => member.InstanceId, StringComparer.Ordinal)
            )
            {
                if (!enemyState.TryGetValue(member.InstanceId, out DungeonActorSaveState state))
                    throw new InvalidOperationException(
                        $"Materialized enemy '{member.InstanceId}' has no saved actor state."
                    );
                ActionController controller = member.GetComponent<ActionController>();
                DungeonCreatureRuntimeState outer =
                    save.FloorDocument.RuntimeState.Creatures.Single(creature =>
                        creature.InstanceId == member.InstanceId
                    );
                enemyRestores.Add(
                    DungeonActorStateAdapter.PrepareRestore(
                        controller,
                        state,
                        outer.HitPoints,
                        isDefeated: false,
                        ResolveActor
                    )
                );
            }
            if (enemyRestores.Count != enemyState.Count)
                throw new InvalidOperationException(
                    "Saved living enemies were not all materialized."
                );

            foreach (Action restore in partyRestores)
                restore();
            foreach (Action restore in enemyRestores)
                restore();

            DungeonAutosaveCoordinator coordinator =
                runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
            coordinator.Initialize(
                save.FloorDocument,
                repository,
                runtime,
                orderedParty,
                saveImmediately: false
            );
            return DungeonRunPersistenceBootstrapResult.Success(runtime, true);
        }

        private static ActionController[] ValidateParty(IEnumerable<ActionController> sceneParty)
        {
            ActionController[] party = sceneParty.ToArray();
            if (party.Length == 0 || party.Any(controller => controller == null))
                throw new InvalidOperationException(
                    "A generated dungeon requires a complete party."
                );
            HashSet<string> slots = new(StringComparer.Ordinal);
            foreach (ActionController controller in party)
            {
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (
                    identity == null
                    || !identity.IsConfigured
                    || !slots.Add(identity.RosterSlotId)
                    || controller.GetComponent<Token>() == null
                )
                    throw new InvalidOperationException(
                        $"Party actor '{controller.name}' requires unique authored dungeon identity and a grid Token."
                    );
            }
            return party;
        }

        private static ActionController[] OrderParty(
            IReadOnlyList<ActionController> party,
            DungeonPartySaveState saved
        )
        {
            Dictionary<string, ActionController> available = party.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                StringComparer.Ordinal
            );
            if (available.Count != saved.Members.Count)
                throw new InvalidOperationException(
                    "The authored party does not match the saved roster."
                );

            return saved
                .Members.Select(member =>
                {
                    if (!available.Remove(member.RosterSlotId, out ActionController controller))
                        throw new InvalidOperationException(
                            $"Saved roster slot '{member.RosterSlotId}' is unavailable."
                        );
                    DungeonPartyMemberIdentity identity =
                        controller.GetComponent<DungeonPartyMemberIdentity>();
                    if (
                        !string.Equals(
                            identity.CreatureContentId,
                            member.CreatureContentId,
                            StringComparison.Ordinal
                        )
                    )
                        throw new InvalidOperationException(
                            $"Saved roster slot '{member.RosterSlotId}' has different creature content."
                        );
                    return controller;
                })
                .ToArray();
        }

        private static Dictionary<string, DungeonActorSaveState> ParseEnemyState(
            IReadOnlyList<DungeonCreatureRuntimeState> creatures
        )
        {
            Dictionary<string, DungeonActorSaveState> states = new(StringComparer.Ordinal);
            foreach (DungeonCreatureRuntimeState creature in creatures)
            {
                DungeonSaveResult<DungeonActorSaveState> parsed = DungeonSaveJson.ParseActor(
                    creature.State
                );
                if (!parsed.IsSuccess)
                    throw new InvalidOperationException(
                        $"Enemy '{creature.InstanceId}' actor state is invalid: "
                            + parsed.Diagnostics[0].Message
                    );
                states.Add(creature.InstanceId, parsed.Value);
            }
            return states;
        }

        private static void ValidateTimedEffectSources(
            DungeonRunSave save,
            IEnumerable<DungeonActorSaveState> enemyStates
        )
        {
            HashSet<string> actorIds = new(StringComparer.Ordinal);
            foreach (DungeonPartyMemberSaveState member in save.Manifest.Party.Members)
            {
                if (!actorIds.Add(member.RosterSlotId))
                    throw new InvalidOperationException(
                        $"Actor identity '{member.RosterSlotId}' is duplicated."
                    );
            }
            foreach (
                DungeonCreatureRuntimeState creature in save.FloorDocument.RuntimeState.Creatures
            )
            {
                if (!actorIds.Add(creature.InstanceId))
                    throw new InvalidOperationException(
                        $"Actor identity '{creature.InstanceId}' is duplicated."
                    );
            }
            foreach (string defeatedId in save.FloorDocument.RuntimeState.DefeatedCreatureIds)
            {
                if (!actorIds.Add(defeatedId))
                    throw new InvalidOperationException(
                        $"Actor identity '{defeatedId}' is duplicated."
                    );
            }

            IEnumerable<DungeonActorSaveState> allStates = save
                .Manifest.Party.Members.Select(member => member.State)
                .Concat(enemyStates);
            foreach (
                DungeonTimedEffectSaveState effect in allStates.SelectMany(state =>
                    state.TimedEffects
                )
            )
            {
                if (!actorIds.Contains(effect.SourceActorId))
                    throw new InvalidOperationException(
                        $"Timed effect source actor '{effect.SourceActorId}' is unavailable."
                    );
            }
        }

        private static DungeonRunPersistenceBootstrapResult Failure(
            DungeonSaveDiagnosticCode code,
            string path,
            string message
        ) =>
            DungeonRunPersistenceBootstrapResult.Failure(
                new[] { new DungeonSaveDiagnostic(code, path, message) }
            );

        private sealed class PartyStaging
        {
            private readonly Entry[] entries;

            private PartyStaging(Entry[] entries)
            {
                this.entries = entries;
            }

            internal static PartyStaging Stage(
                IReadOnlyList<ActionController> party,
                IReadOnlyList<DungeonPartyMemberSaveState> saved
            )
            {
                GridAPI.TryGetInstance(out GridAPI grid);
                Entry[] entries = party
                    .Select((controller, index) => new Entry(controller, saved[index], grid))
                    .ToArray();
                PartyStaging staging = new(entries);
                try
                {
                    foreach (Entry entry in entries)
                        entry.Stage();
                    return staging;
                }
                catch
                {
                    staging.Rollback();
                    throw;
                }
            }

            internal void Rollback()
            {
                for (int index = entries.Length - 1; index >= 0; index--)
                    entries[index].Rollback();
            }

            private sealed class Entry
            {
                private readonly ActionController controller;
                private readonly DungeonPartyMemberSaveState saved;
                private readonly GridAPI grid;
                private readonly Token token;
                private readonly Vector3 originalPosition;
                private readonly bool wasActive;
                private readonly bool wasRegistered;

                internal Entry(
                    ActionController controller,
                    DungeonPartyMemberSaveState saved,
                    GridAPI grid
                )
                {
                    this.controller = controller;
                    this.saved = saved;
                    this.grid = grid;
                    token = controller.GetComponent<Token>();
                    originalPosition = controller.transform.position;
                    wasActive = controller.gameObject.activeSelf;
                    wasRegistered = token.IsRegistered;
                }

                internal void Stage()
                {
                    if (
                        saved.IsDefeated
                        && wasRegistered
                        && (grid == null || !grid.DestroyToken(controller.gameObject))
                    )
                        throw new InvalidOperationException(
                            $"Defeated party actor '{saved.RosterSlotId}' could not leave the grid."
                        );
                    controller.transform.position = new Vector3(
                        saved.CellX,
                        originalPosition.y,
                        saved.CellZ
                    );
                    if (saved.IsDefeated)
                        controller.gameObject.SetActive(false);
                }

                internal void Rollback()
                {
                    controller.transform.position = originalPosition;
                    controller.gameObject.SetActive(wasActive);
                    if (wasRegistered && !token.IsRegistered)
                        token.TryRegisterWithGrid(grid);
                }
            }
        }
    }
}
