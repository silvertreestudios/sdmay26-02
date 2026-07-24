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
    /// Starts or restores a current-schema dungeon run through explicit all-or-nothing operations.
    /// </summary>
    /// <remarks>
    /// Continue never migrates, repairs, regenerates, salvages, or partially accepts an autosave.
    /// Any load failure is returned before scene population or runtime component creation.
    /// </remarks>
    public static class DungeonRunPersistenceBootstrap
    {
        /// <summary>Starts and commits a new generated run without inspecting an existing autosave.</summary>
        /// <param name="map">The reusable generated-dungeon map in the active scene.</param>
        /// <param name="initialDocument">The pristine generated floor that begins the run.</param>
        /// <param name="encounterCatalog">The creature catalog used to initialize floor encounters.</param>
        /// <param name="combatManager">The active scene combat scheduler.</param>
        /// <param name="sceneParty">The complete authored party to persist.</param>
        /// <param name="explorationPresentation">The movement-only exploration presentation.</param>
        /// <param name="runtimeRoot">The object that will own persistence runtime components.</param>
        /// <returns>The initialized new runtime or blocking capture/publication diagnostics.</returns>
        public static DungeonRunPersistenceBootstrapResult StartNewRun(
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
            return StartNewRun(
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

        /// <summary>Continues the complete current-schema autosave or returns its load diagnostics.</summary>
        /// <param name="map">The reusable generated-dungeon map to populate with the indexed floor.</param>
        /// <param name="initialDocument">A current-build document that identifies the supported generator.</param>
        /// <param name="encounterCatalog">The creature catalog used to restore floor encounters.</param>
        /// <param name="combatManager">The active scene combat scheduler.</param>
        /// <param name="sceneParty">The complete authored party that must match the saved roster.</param>
        /// <param name="explorationPresentation">The movement-only exploration presentation.</param>
        /// <param name="runtimeRoot">The object that will own restored runtime components.</param>
        /// <returns>The restored runtime or unchanged repository load diagnostics.</returns>
        public static DungeonRunPersistenceBootstrapResult ContinueRun(
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
            return ContinueRun(
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

        internal static DungeonRunPersistenceBootstrapResult StartNewRun(
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
            ValidateArguments(
                map,
                initialDocument,
                encounterCatalog,
                combatManager,
                sceneParty,
                explorationPresentation,
                runtimeRoot,
                repository
            );

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

        internal static DungeonRunPersistenceBootstrapResult ContinueRun(
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
            ValidateArguments(
                map,
                initialDocument,
                encounterCatalog,
                combatManager,
                sceneParty,
                explorationPresentation,
                runtimeRoot,
                repository
            );

            DungeonSaveResult<DungeonRunSave> loaded = repository.Load();
            if (!loaded.IsSuccess)
                return DungeonRunPersistenceBootstrapResult.Failure(loaded.Diagnostics);

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

        private static void ValidateArguments(
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
            coordinator.InitializeNewRun(document, repository, runtime, party);
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
            DungeonRunSaveManifest manifest = save.Manifest;
            DungeonLevelDocument currentFloor = save.GetFloor(manifest.CurrentDepth);
            if (
                !string.Equals(
                    manifest.GeneratorVersion,
                    initialDocument.Generation.Algorithm,
                    StringComparison.Ordinal
                )
            )
                return Failure(
                    DungeonSaveDiagnosticCode.IncompatibleVersion,
                    "manifest.generatorVersion",
                    "The autosave uses an unsupported dungeon generator."
                );

            ActionController[] orderedParty = OrderParty(party, manifest.Party);
            Dictionary<string, DungeonActorSaveState> enemyState = ParseEnemyState(
                currentFloor.RuntimeState.Creatures
            );
            ValidateTimedEffectSources(manifest, currentFloor, enemyState.Values);
            Dictionary<string, GameObject> preflightActors = orderedParty.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                controller => controller.gameObject,
                StringComparer.Ordinal
            );
            GameObject ResolvePreflightActor(string actorId) =>
                preflightActors.TryGetValue(actorId, out GameObject actor) ? actor : null;
            _ = save
                .Manifest.Party.Select(
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

            if (
                !TryPopulateMap(
                    map,
                    currentFloor,
                    manifest.Party,
                    orderedParty,
                    out MapSourceValidationResult validation
                )
            )
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "floor",
                    string.Join(" ", validation.Errors)
                );
            }

            DungeonEncounterRuntimeController runtime =
                runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
            runtime.InitializePersisted(
                currentFloor,
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
                .Manifest.Party.Select(
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
                DungeonCreatureRuntimeState outer = currentFloor.RuntimeState.Creatures.Single(
                    creature => creature.InstanceId == member.InstanceId
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
            coordinator.InitializeLoadedRun(save, repository, runtime, orderedParty);
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
            IReadOnlyList<DungeonPartyMemberSaveState> saved
        )
        {
            Dictionary<string, ActionController> available = party.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                StringComparer.Ordinal
            );
            if (available.Count != saved.Count)
                throw new InvalidOperationException(
                    "The authored party does not match the saved roster."
                );

            return saved
                .Select(member =>
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
            DungeonRunSaveManifest manifest,
            DungeonLevelDocument currentFloor,
            IEnumerable<DungeonActorSaveState> enemyStates
        )
        {
            HashSet<string> actorIds = new(StringComparer.Ordinal);
            foreach (DungeonPartyMemberSaveState member in manifest.Party)
            {
                if (!actorIds.Add(member.RosterSlotId))
                    throw new InvalidOperationException(
                        $"Actor identity '{member.RosterSlotId}' is duplicated."
                    );
            }
            foreach (DungeonCreatureRuntimeState creature in currentFloor.RuntimeState.Creatures)
            {
                if (!actorIds.Add(creature.InstanceId))
                    throw new InvalidOperationException(
                        $"Actor identity '{creature.InstanceId}' is duplicated."
                    );
            }
            foreach (string defeatedId in currentFloor.RuntimeState.DefeatedCreatureIds)
            {
                if (!actorIds.Add(defeatedId))
                    throw new InvalidOperationException(
                        $"Actor identity '{defeatedId}' is duplicated."
                    );
            }

            IEnumerable<DungeonActorSaveState> allStates = manifest
                .Party.Select(member => member.State)
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

        private static bool TryPopulateMap(
            Map map,
            DungeonLevelDocument floor,
            IReadOnlyList<DungeonPartyMemberSaveState> savedParty,
            IReadOnlyList<ActionController> party,
            out MapSourceValidationResult validation
        )
        {
            bool[] wasActive = party.Select(member => member.gameObject.activeSelf).ToArray();
            foreach (ActionController member in party)
                member.gameObject.SetActive(false);

            if (
                !map.TryPopulateJson(
                    DungeonLevelJsonSerializer.Serialize(floor),
                    map.DungeonCatalog,
                    out validation
                )
            )
            {
                for (int index = 0; index < party.Count; index++)
                    party[index].gameObject.SetActive(wasActive[index]);
                return false;
            }

            for (int index = 0; index < party.Count; index++)
            {
                DungeonPartyMemberSaveState saved = savedParty[index];
                Transform transform = party[index].transform;
                transform.position = new Vector3(saved.CellX, transform.position.y, saved.CellZ);
                party[index].gameObject.SetActive(!saved.IsDefeated);
            }
            return true;
        }
    }
}
