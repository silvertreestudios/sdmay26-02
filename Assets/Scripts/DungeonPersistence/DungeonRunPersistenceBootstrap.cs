using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPublic;
using UnityEngine;

namespace Game.DungeonPersistence
{
    /// <summary>
    /// Represents either a fully owned dungeon runtime plus autosave session or structured
    /// diagnostics that prevented gameplay from starting.
    /// </summary>
    public sealed class DungeonRunPersistenceBootstrapResult
    {
        private readonly DungeonEncounterRuntimeController runtime;
        private readonly DungeonAutosaveCoordinator autosaveCoordinator;

        private DungeonRunPersistenceBootstrapResult(
            bool isSuccess,
            bool restoredExistingRun,
            DungeonEncounterRuntimeController runtime,
            DungeonAutosaveCoordinator autosaveCoordinator,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            RestoredExistingRun = restoredExistingRun;
            this.runtime = runtime;
            this.autosaveCoordinator = autosaveCoordinator;
            Diagnostics = Array.AsReadOnly(
                (diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray()
            );
        }

        /// <summary>Gets whether the runtime and autosave owner are ready for gameplay.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets blocking errors or non-blocking repository recovery warnings.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        /// <summary>Gets whether gameplay resumed a committed run instead of creating one.</summary>
        public bool RestoredExistingRun { get; }

        /// <summary>Gets the initialized current-floor encounter runtime.</summary>
        public DungeonEncounterRuntimeController Runtime => RequireSuccess(runtime);

        internal DungeonAutosaveCoordinator AutosaveCoordinator =>
            RequireSuccess(autosaveCoordinator);

        internal static DungeonRunPersistenceBootstrapResult Success(
            bool restoredExistingRun,
            DungeonEncounterRuntimeController runtime,
            DungeonAutosaveCoordinator autosaveCoordinator,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) =>
            new(
                true,
                restoredExistingRun,
                runtime ?? throw new ArgumentNullException(nameof(runtime)),
                autosaveCoordinator ?? throw new ArgumentNullException(nameof(autosaveCoordinator)),
                diagnostics
            );

        internal static DungeonRunPersistenceBootstrapResult Failure(
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) => new(false, false, default, default, diagnostics);

        private T RequireSuccess<T>(T value) =>
            IsSuccess
                ? value
                : throw new InvalidOperationException("A failed bootstrap has no runtime value.");
    }

    /// <summary>
    /// Composes repository load, map population, exact actor restoration, and current-floor
    /// autosave ownership before generated-dungeon gameplay begins.
    /// </summary>
    public static class DungeonRunPersistenceBootstrap
    {
        /// <summary>Initializes a new run or restores the production autosave repository.</summary>
        public static DungeonRunPersistenceBootstrapResult Initialize(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot
        ) =>
            Initialize(
                map,
                initialDocument,
                encounterCatalog,
                combatManager,
                sceneParty,
                explorationPresentation,
                DungeonAutosaveProductionRepositoryFactory.Create(),
                runtimeRoot
            );

        /// <summary>Initializes a new run or restores the repository's complete current run.</summary>
        /// <param name="map">The validated generated JSON map used as the scene shell.</param>
        /// <param name="initialDocument">The current build's validated generated document.</param>
        /// <param name="encounterCatalog">The creature catalog used to materialize encounters.</param>
        /// <param name="combatManager">The inactive combat scheduler for this floor.</param>
        /// <param name="sceneParty">Every authored party controller available to this run.</param>
        /// <param name="explorationPresentation">The movement-only exploration presentation.</param>
        /// <param name="repository">The explicit atomic repository.</param>
        /// <param name="runtimeRoot">The object that owns runtime and autosave components.</param>
        /// <returns>A complete gameplay runtime or structured diagnostics with no partial value.</returns>
        internal static DungeonRunPersistenceBootstrapResult Initialize(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            IDungeonSaveRepository repository,
            GameObject runtimeRoot
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
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
            if (runtimeRoot == null)
                throw new ArgumentNullException(nameof(runtimeRoot));

            ActionController[] configuredParty;
            try
            {
                configuredParty = RequireConfiguredParty(sceneParty);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Failure("party", exception.Message);
            }

            DungeonSaveResult<DungeonRunSave> load;
            try
            {
                load = repository.Load();
            }
            catch (Exception exception)
            {
                return Failure(
                    "repository",
                    $"The dungeon autosave could not be read ({exception.GetType().Name}: {exception.Message}).",
                    DungeonSaveDiagnosticCode.IoFailure
                );
            }

            if (load.IsSuccess)
            {
                return Restore(
                    map,
                    initialDocument,
                    encounterCatalog,
                    combatManager,
                    configuredParty,
                    explorationPresentation,
                    repository,
                    runtimeRoot,
                    load.Value,
                    load.Diagnostics
                );
            }

            if (
                load.Diagnostics.Count > 0
                && load.Diagnostics.All(diagnostic =>
                    diagnostic.Code == DungeonSaveDiagnosticCode.MissingSave
                )
            )
            {
                return CreateNew(
                    initialDocument,
                    encounterCatalog,
                    combatManager,
                    configuredParty.OrderBy(
                        controller =>
                            controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                        StringComparer.Ordinal
                    ),
                    explorationPresentation,
                    repository,
                    runtimeRoot
                );
            }

            return DungeonRunPersistenceBootstrapResult.Failure(load.Diagnostics);
        }

        private static DungeonRunPersistenceBootstrapResult CreateNew(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            IDungeonSaveRepository repository,
            GameObject runtimeRoot
        )
        {
            if (document.RuntimeState != null)
            {
                return Failure(
                    "map.runtimeState",
                    "A new dungeon run requires pristine generated JSON without embedded runtime state."
                );
            }

            try
            {
                ActionController[] orderedParty = party.ToArray();
                DungeonEncounterRuntimeController runtime =
                    runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
                runtime.InitializePristine(
                    document,
                    encounterCatalog,
                    combatManager,
                    orderedParty,
                    explorationPresentation
                );
                DungeonAutosaveCoordinator coordinator =
                    runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
                coordinator.InitializeNewFloor(
                    document.Generation.RunSeed,
                    document.Generation.Algorithm,
                    repository,
                    DungeonLevelJsonSerializer.Serialize(document),
                    runtime
                );
                if (!coordinator.LastResult.IsSuccess)
                    return AutosaveFailure(coordinator.LastResult);
                return DungeonRunPersistenceBootstrapResult.Success(
                    false,
                    runtime,
                    coordinator,
                    Array.Empty<DungeonSaveDiagnostic>()
                );
            }
            catch (Exception exception)
            {
                return Failure(
                    "runtime.newRun",
                    $"The generated dungeon run could not initialize ({exception.GetType().Name}: {exception.Message})."
                );
            }
        }

        private static DungeonRunPersistenceBootstrapResult Restore(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IReadOnlyList<ActionController> configuredParty,
            IDungeonExplorationPresentation explorationPresentation,
            IDungeonSaveRepository repository,
            GameObject runtimeRoot,
            DungeonRunSave save,
            IReadOnlyList<DungeonSaveDiagnostic> loadDiagnostics
        )
        {
            if (
                !string.Equals(
                    save.Manifest.GeneratorVersion,
                    initialDocument.Generation.Algorithm,
                    StringComparison.Ordinal
                )
            )
            {
                return Failure(
                    "manifest.generatorVersion",
                    "The autosave was created by a dungeon generator this build does not support.",
                    DungeonSaveDiagnosticCode.IncompatibleVersion
                );
            }

            DungeonSaveResult<DungeonRunLoadPlan> preparation = DungeonRunLoadPlan.Prepare(save);
            if (!preparation.IsSuccess)
                return DungeonRunPersistenceBootstrapResult.Failure(preparation.Diagnostics);
            DungeonRunLoadPlan plan = preparation.Value;

            ActionController[] orderedParty;
            try
            {
                orderedParty = OrderRestoredParty(configuredParty, save.Manifest.Party);
            }
            catch (InvalidOperationException exception)
            {
                return Failure("party", exception.Message);
            }

            PartyPopulationStaging partyStaging;
            TextAsset originalJsonSource = map.JsonSource;
            KayKitDungeonCatalog originalCatalog = map.DungeonCatalog;
            float originalSpacing = map.Spacing;
            bool originallyUsedRuntimeJson = map.UsesRuntimeJsonSource;
            try
            {
                partyStaging = PartyPopulationStaging.Stage(orderedParty, save.Manifest.Party);
            }
            catch (InvalidOperationException exception)
            {
                return Failure("party.population", exception.Message);
            }

            string populationJson = DungeonLevelJsonSerializer.Serialize(plan.PopulationDocument);
            if (
                !map.TryPopulateJson(
                    populationJson,
                    map.DungeonCatalog,
                    out MapSourceValidationResult validation
                )
            )
            {
                partyStaging.Rollback();
                return Failure(
                    "floor.population",
                    "The saved floor could not populate the scene: "
                        + string.Join(" ", validation.Errors)
                );
            }

            try
            {
                DungeonEncounterRuntimeController runtime =
                    runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
                runtime.InitializePersisted(
                    plan.PopulationDocument,
                    encounterCatalog,
                    combatManager,
                    orderedParty,
                    explorationPresentation
                );
                plan.PreflightActors(runtime).Apply();
                DungeonAutosaveCoordinator coordinator =
                    runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
                coordinator.InitializeRestoredFloor(save, repository, runtime);
                return DungeonRunPersistenceBootstrapResult.Success(
                    true,
                    runtime,
                    coordinator,
                    loadDiagnostics
                );
            }
            catch (Exception exception)
            {
                string failureMessage =
                    $"The saved dungeon run could not initialize ({exception.GetType().Name}: {exception.Message}).";
                if (
                    TryRollbackFailedRestore(
                        map,
                        initialDocument,
                        originalJsonSource,
                        originalCatalog,
                        originalSpacing,
                        originallyUsedRuntimeJson,
                        combatManager,
                        runtimeRoot,
                        partyStaging,
                        out string rollbackFailure
                    )
                )
                    return Failure("runtime.restore", failureMessage);

                return DungeonRunPersistenceBootstrapResult.Failure(
                    new[]
                    {
                        Diagnostic("runtime.restore", failureMessage),
                        Diagnostic(
                            "runtime.rollback",
                            "The failed restore could not reinstate the original scene shell: "
                                + rollbackFailure
                        ),
                    }
                );
            }
        }

        private static bool TryRollbackFailedRestore(
            Map map,
            DungeonLevelDocument originalDocument,
            TextAsset originalJsonSource,
            KayKitDungeonCatalog originalCatalog,
            float originalSpacing,
            bool originallyUsedRuntimeJson,
            CombatManagerInterface combatManager,
            GameObject runtimeRoot,
            PartyPopulationStaging partyStaging,
            out string failure
        )
        {
            try
            {
                RemoveFailedRuntimeActors(runtimeRoot, combatManager);
                partyStaging.PrepareForMapRollback();
                if (
                    !map.TryPopulateJson(
                        DungeonLevelJsonSerializer.Serialize(originalDocument),
                        originalCatalog,
                        out MapSourceValidationResult validation
                    )
                )
                {
                    failure = string.Join(" ", validation.Errors);
                    return false;
                }

                if (!originallyUsedRuntimeJson)
                    map.ConfigureJson(originalJsonSource, originalCatalog, originalSpacing);
                partyStaging.CompleteMapRollback();
                failure = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }

        private static void RemoveFailedRuntimeActors(
            GameObject runtimeRoot,
            CombatManagerInterface combatManager
        )
        {
            GridAPI.TryGetInstance(out GridAPI grid);
            ActionController[] createdActors =
                runtimeRoot.GetComponentsInChildren<ActionController>(includeInactive: true);
            foreach (ActionController controller in createdActors)
            {
                Token token = controller.GetComponent<Token>();
                if (token != null && token.IsRegistered)
                {
                    if (grid == null || !grid.DestroyToken(controller.gameObject))
                    {
                        throw new InvalidOperationException(
                            $"Failed runtime actor '{controller.name}' could not leave the active grid."
                        );
                    }
                }
                combatManager.Remove(controller);
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
            }
            runtimeRoot.SetActive(false);
        }

        private static ActionController[] RequireConfiguredParty(
            IEnumerable<ActionController> sceneParty
        )
        {
            ActionController[] party = sceneParty.ToArray();
            if (party.Length == 0)
                throw new InvalidOperationException("A generated dungeon requires a party.");

            HashSet<string> rosterSlots = new(StringComparer.Ordinal);
            HashSet<string> actorIds = new(StringComparer.Ordinal);
            foreach (ActionController controller in party)
            {
                if (controller == null)
                    throw new InvalidOperationException(
                        "The generated dungeon party lost an actor."
                    );
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (identity == null || !identity.IsConfigured)
                {
                    throw new InvalidOperationException(
                        $"Party actor '{controller.name}' requires authored stable dungeon identity."
                    );
                }
                if (controller.GetComponent<Token>() == null)
                {
                    throw new InvalidOperationException(
                        $"Party actor '{controller.name}' requires a grid Token."
                    );
                }
                if (
                    !rosterSlots.Add(identity.RosterSlotId)
                    || !actorIds.Add(identity.ActorInstanceId)
                )
                {
                    throw new InvalidOperationException(
                        "Party roster-slot and actor identities must be unique."
                    );
                }
            }
            return party;
        }

        private static ActionController[] OrderRestoredParty(
            IReadOnlyList<ActionController> configuredParty,
            DungeonPartySaveState savedParty
        )
        {
            Dictionary<string, ActionController> byRosterSlot = configuredParty.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                StringComparer.Ordinal
            );
            if (byRosterSlot.Count != savedParty.Members.Count)
            {
                throw new InvalidOperationException(
                    "The authored party does not exactly match the saved dungeon roster."
                );
            }

            List<ActionController> ordered = new(savedParty.Members.Count);
            foreach (DungeonPartyMemberSaveState member in savedParty.Members)
            {
                if (!byRosterSlot.Remove(member.RosterSlotId, out ActionController controller))
                {
                    throw new InvalidOperationException(
                        $"Saved party roster slot '{member.RosterSlotId}' is not materialized."
                    );
                }
                DungeonPartyMemberIdentity identity =
                    controller.GetComponent<DungeonPartyMemberIdentity>();
                if (
                    !string.Equals(
                        identity.ActorInstanceId,
                        member.Creature.InstanceId,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        identity.CreatureContentId,
                        member.Creature.CreatureContentId,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        $"Saved party roster slot '{member.RosterSlotId}' does not match its authored actor."
                    );
                }
                ordered.Add(controller);
            }
            return ordered.ToArray();
        }

        private sealed class PartyPopulationStaging
        {
            private readonly IReadOnlyList<PartyActorPopulationState> actors;

            private PartyPopulationStaging(IReadOnlyList<PartyActorPopulationState> actors)
            {
                this.actors = actors;
            }

            internal static PartyPopulationStaging Stage(
                IReadOnlyList<ActionController> orderedParty,
                DungeonPartySaveState savedParty
            )
            {
                GridAPI.TryGetInstance(out GridAPI activeGrid);
                PartyActorPopulationState[] actors = orderedParty
                    .Select(
                        (controller, index) =>
                            new PartyActorPopulationState(
                                controller,
                                savedParty.Members[index].Creature,
                                activeGrid
                            )
                    )
                    .ToArray();
                PartyPopulationStaging staging = new(actors);
                try
                {
                    foreach (PartyActorPopulationState actor in actors)
                        actor.Stage();
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
                for (int index = actors.Count - 1; index >= 0; index--)
                    actors[index].Rollback();
            }

            internal void PrepareForMapRollback()
            {
                for (int index = actors.Count - 1; index >= 0; index--)
                    actors[index].PrepareForMapRollback();
            }

            internal void CompleteMapRollback()
            {
                if (!GridAPI.TryGetInstance(out GridAPI activeGrid))
                    throw new InvalidOperationException(
                        "The original grid did not return after failed dungeon restoration."
                    );
                foreach (PartyActorPopulationState actor in actors)
                    actor.CompleteMapRollback(activeGrid);
            }

            private sealed class PartyActorPopulationState
            {
                private readonly ActionController controller;
                private readonly DungeonCreatureSaveState saved;
                private readonly GridAPI activeGrid;
                private readonly Token token;
                private readonly Vector3 originalPosition;
                private readonly bool wasActive;
                private readonly bool wasRegistered;
                private bool staged;

                internal PartyActorPopulationState(
                    ActionController controller,
                    DungeonCreatureSaveState saved,
                    GridAPI activeGrid
                )
                {
                    this.controller = controller;
                    this.saved = saved;
                    this.activeGrid = activeGrid;
                    token = controller.GetComponent<Token>();
                    originalPosition = controller.transform.position;
                    wasActive = controller.gameObject.activeSelf;
                    wasRegistered = token.IsRegistered;
                    if (wasRegistered && activeGrid == null)
                    {
                        throw new InvalidOperationException(
                            $"Party actor '{saved.InstanceId}' is registered without an active grid."
                        );
                    }
                }

                internal void Stage()
                {
                    if (
                        saved.IsDefeated
                        && wasRegistered
                        && !activeGrid.DestroyToken(controller.gameObject)
                    )
                    {
                        throw new InvalidOperationException(
                            $"Defeated party actor '{saved.InstanceId}' could not detach from its authored grid cell."
                        );
                    }

                    controller.transform.position = new Vector3(
                        saved.Cell.X,
                        originalPosition.y,
                        saved.Cell.Z
                    );
                    if (saved.IsDefeated)
                        controller.gameObject.SetActive(false);
                    staged = true;
                }

                internal void Rollback()
                {
                    if (!staged && token.IsRegistered == wasRegistered)
                        return;
                    controller.transform.position = originalPosition;
                    controller.gameObject.SetActive(wasActive);
                    if (wasRegistered && !token.IsRegistered)
                        token.TryRegisterWithGrid(activeGrid);
                    staged = false;
                }

                internal void PrepareForMapRollback()
                {
                    if (!wasRegistered && token.IsRegistered)
                    {
                        if (
                            !GridAPI.TryGetInstance(out GridAPI currentGrid)
                            || !currentGrid.DestroyToken(controller.gameObject)
                        )
                        {
                            throw new InvalidOperationException(
                                $"Party actor '{saved.InstanceId}' could not leave the restored grid."
                            );
                        }
                    }
                    controller.transform.position = originalPosition;
                    controller.gameObject.SetActive(wasActive);
                }

                internal void CompleteMapRollback(GridAPI restoredGrid)
                {
                    if (wasRegistered && !token.IsRegistered)
                        token.TryRegisterWithGrid(restoredGrid);
                    else if (!wasRegistered && token.IsRegistered)
                        restoredGrid.DestroyToken(controller.gameObject);
                    if (token.IsRegistered != wasRegistered)
                    {
                        throw new InvalidOperationException(
                            $"Party actor '{saved.InstanceId}' did not recover its original grid registration."
                        );
                    }
                    staged = false;
                }
            }
        }

        private static DungeonRunPersistenceBootstrapResult AutosaveFailure(
            DungeonAutosaveAttemptResult attempt
        )
        {
            List<DungeonSaveDiagnostic> diagnostics = new(attempt.Diagnostics);
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(
                    new DungeonSaveDiagnostic(
                        DungeonSaveDiagnosticCode.InvalidSnapshot,
                        DungeonSaveDiagnosticSeverity.Error,
                        "autosave",
                        $"The initial floor checkpoint ended with outcome '{attempt.Outcome}'."
                    )
                );
            }
            return DungeonRunPersistenceBootstrapResult.Failure(diagnostics);
        }

        private static DungeonRunPersistenceBootstrapResult Failure(
            string path,
            string message,
            DungeonSaveDiagnosticCode code = DungeonSaveDiagnosticCode.InvalidSnapshot
        ) =>
            DungeonRunPersistenceBootstrapResult.Failure(new[] { Diagnostic(path, message, code) });

        private static DungeonSaveDiagnostic Diagnostic(
            string path,
            string message,
            DungeonSaveDiagnosticCode code = DungeonSaveDiagnosticCode.InvalidSnapshot
        ) => new(code, DungeonSaveDiagnosticSeverity.Error, path, message);
    }
}
