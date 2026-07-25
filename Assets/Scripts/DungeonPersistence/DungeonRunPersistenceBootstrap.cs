using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.DungeonPersistence
{
    /// <summary>Represents a ready persistent dungeon runtime or blocking diagnostics.</summary>
    public sealed class DungeonRunPersistenceBootstrapResult
    {
        private DungeonRunPersistenceBootstrapResult(
            DungeonEncounterRuntimeController runtime,
            DungeonRunController controller,
            bool restoredExistingRun,
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        )
        {
            Runtime = runtime;
            Controller = controller;
            RestoredExistingRun = restoredExistingRun;
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        /// <summary>Gets whether dungeon play can begin.</summary>
        public bool IsSuccess => Runtime != null && Diagnostics.Count == 0;

        /// <summary>Gets whether an existing autosave was restored.</summary>
        public bool RestoredExistingRun { get; }

        /// <summary>Gets the initialized current-floor runtime on success.</summary>
        public DungeonEncounterRuntimeController Runtime { get; }

        /// <summary>Gets the initialized multi-floor traversal owner on success.</summary>
        public DungeonRunController Controller { get; }

        /// <summary>Gets structured blocking diagnostics.</summary>
        public IReadOnlyList<DungeonSaveDiagnostic> Diagnostics { get; }

        internal static DungeonRunPersistenceBootstrapResult Success(
            DungeonEncounterRuntimeController runtime,
            DungeonRunController controller,
            bool restoredExistingRun
        ) => new(runtime, controller, restoredExistingRun, Array.Empty<DungeonSaveDiagnostic>());

        internal static DungeonRunPersistenceBootstrapResult Failure(
            IEnumerable<DungeonSaveDiagnostic> diagnostics
        ) => new(null, null, false, diagnostics);
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
        private readonly struct PartySceneState
        {
            internal PartySceneState(Vector3 position, bool wasActive, bool wasRegistered)
            {
                Position = position;
                WasActive = wasActive;
                WasRegistered = wasRegistered;
            }

            internal Vector3 Position { get; }
            internal bool WasActive { get; }
            internal bool WasRegistered { get; }
        }

        /// <summary>
        /// Retains the authored scene state that a generated New Run temporarily replaces. The
        /// repository checkpoint stays opaque so New Run never parses or adopts an old autosave.
        /// </summary>
        private sealed class GeneratedRunRollbackState
        {
            internal GeneratedRunRollbackState(
                string mapJson,
                TextAsset jsonSource,
                KayKitDungeonCatalog mapCatalog,
                float mapSpacing,
                bool usedRuntimeJson,
                PartySceneState[] party,
                IDungeonSaveRepositoryCheckpoint repository
            )
            {
                MapJson = mapJson;
                JsonSource = jsonSource;
                MapCatalog = mapCatalog;
                MapSpacing = mapSpacing;
                UsedRuntimeJson = usedRuntimeJson;
                Party = party;
                Repository = repository;
            }

            internal string MapJson { get; }
            internal TextAsset JsonSource { get; }
            internal KayKitDungeonCatalog MapCatalog { get; }
            internal float MapSpacing { get; }
            internal bool UsedRuntimeJson { get; }
            internal PartySceneState[] Party { get; }
            internal IDungeonSaveRepositoryCheckpoint Repository { get; }
        }

        /// <summary>Generates and commits depth zero without inspecting an existing autosave.</summary>
        /// <param name="map">The reusable generated-dungeon map in the active scene.</param>
        /// <param name="initialDocument">The authored template supplying seed and dimensions.</param>
        /// <param name="encounterCatalog">The creature catalog used to initialize floor encounters.</param>
        /// <param name="combatManager">The active scene combat scheduler.</param>
        /// <param name="sceneParty">The complete authored party to persist.</param>
        /// <param name="explorationPresentation">
        /// The combined exploration and stair-traversal presentation.
        /// </param>
        /// <param name="runtimeRoot">The object that will own persistence runtime components.</param>
        /// <returns>The initialized new runtime or blocking staged diagnostics.</returns>
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
                autosaveDirectory
            );
        }

        /// <summary>
        /// Generates and commits depth zero to an explicit autosave directory. This overload lets
        /// automated and embedded hosts isolate run persistence from the player's default save.
        /// </summary>
        /// <param name="map">The reusable generated-dungeon map in the active scene.</param>
        /// <param name="initialDocument">The authored template supplying seed and dimensions.</param>
        /// <param name="encounterCatalog">The creature catalog used to initialize encounters.</param>
        /// <param name="combatManager">The active scene combat scheduler.</param>
        /// <param name="sceneParty">The complete authored party to persist.</param>
        /// <param name="explorationPresentation">Exploration and stair-travel presentation.</param>
        /// <param name="runtimeRoot">The object that owns persistence runtime components.</param>
        /// <param name="autosaveDirectory">A non-empty directory dedicated to this run.</param>
        /// <returns>The initialized new runtime or blocking staged diagnostics.</returns>
        public static DungeonRunPersistenceBootstrapResult StartNewRun(
            Map map,
            DungeonLevelDocument initialDocument,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            string autosaveDirectory
        )
        {
            if (string.IsNullOrWhiteSpace(autosaveDirectory))
                throw new ArgumentException(
                    "An autosave directory is required.",
                    nameof(autosaveDirectory)
                );
            return StartGeneratedRun(
                map,
                initialDocument,
                encounterCatalog,
                combatManager,
                sceneParty,
                explorationPresentation,
                runtimeRoot,
                new FileSystemDungeonSaveRepository(autosaveDirectory),
                new DeterministicDungeonGenerator(),
                new DungeonEncounterPlanner(),
                DungeonRunController.LoadEncounterCandidates()
            );
        }

        /// <summary>Continues the complete current-schema autosave or returns its load diagnostics.</summary>
        /// <param name="map">The reusable generated-dungeon map to populate with the indexed floor.</param>
        /// <param name="initialDocument">A current-build document that identifies the supported generator.</param>
        /// <param name="encounterCatalog">The creature catalog used to restore floor encounters.</param>
        /// <param name="combatManager">The active scene combat scheduler.</param>
        /// <param name="sceneParty">The complete authored party that must match the saved roster.</param>
        /// <param name="explorationPresentation">
        /// The combined exploration and stair-traversal presentation.
        /// </param>
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

        internal static DungeonRunPersistenceBootstrapResult StartPreparedRunForTests(
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

            return CreatePrepared(
                map,
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

        internal static DungeonRunPersistenceBootstrapResult StartGeneratedRun(
            Map map,
            DungeonLevelDocument template,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> sceneParty,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            IDungeonSaveRepository repository,
            IDungeonGenerator generator,
            DungeonEncounterPlanner encounterPlanner,
            IReadOnlyList<DungeonEncounterCandidate> encounterCandidates
        )
        {
            ValidateArguments(
                map,
                template,
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
                party = ValidateParty(sceneParty)
                    .OrderBy(
                        controller =>
                            controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                        StringComparer.Ordinal
                    )
                    .ToArray();
            }
            catch (InvalidOperationException exception)
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "party",
                    exception.Message
                );
            }

            DungeonTravelDiagnostic acquisition = DungeonRunController.AcquireFirstVisit(
                generator,
                encounterPlanner,
                encounterCandidates,
                party,
                template.Generation.RunSeed,
                depth: 0,
                template.Width,
                template.Height,
                out DungeonLevelDocument floor
            );
            if (acquisition != null)
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    acquisition.Stage,
                    acquisition.Message
                );
            }

            DungeonPartyMemberSaveState[] savedParty;
            DungeonRunSave candidate;
            try
            {
                savedParty = CreateInitialPartyState(party, floor);
                candidate = DungeonRunSave.CreateNew(savedParty, floor);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "initial-party",
                    exception.Message
                );
            }

            GeneratedRunRollbackState rollbackState;
            try
            {
                PreflightGeneratedRunRuntime(map, floor, savedParty, party, runtimeRoot);
                DungeonSaveResult<IDungeonSaveRepositoryCheckpoint> checkpoint =
                    repository.CaptureCheckpoint();
                if (!checkpoint.IsSuccess)
                    return DungeonRunPersistenceBootstrapResult.Failure(checkpoint.Diagnostics);
                rollbackState = CaptureGeneratedRunRollbackState(map, party, checkpoint.Value);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Failure(
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "runtime.preflight",
                    exception.Message
                );
            }

            DungeonSaveResult<bool> published;
            try
            {
                published = repository.Save(candidate);
            }
            catch (Exception exception)
            {
                return RollBackGeneratedRunPublication(
                    repository,
                    rollbackState.Repository,
                    DungeonSaveDiagnosticCode.IoFailure,
                    "publication",
                    exception.Message
                );
            }
            if (!published.IsSuccess)
            {
                DungeonSaveDiagnostic diagnostic = published.Diagnostics[0];
                return RollBackGeneratedRunPublication(
                    repository,
                    rollbackState.Repository,
                    diagnostic.Code,
                    diagnostic.Path,
                    diagnostic.Message
                );
            }
            if (
                !TryPopulateMap(
                    map,
                    floor,
                    savedParty,
                    party,
                    out MapSourceValidationResult validation
                )
            )
            {
                return RollBackGeneratedRunPublication(
                    repository,
                    rollbackState.Repository,
                    DungeonSaveDiagnosticCode.InvalidSnapshot,
                    "population",
                    string.Join(" ", validation.Errors)
                );
            }

            try
            {
                DungeonEncounterRuntimeController runtime =
                    runtimeRoot.AddComponent<DungeonEncounterRuntimeController>();
                RestoreFloorRuntime(
                    floor,
                    savedParty,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtime
                );
                DungeonAutosaveCoordinator coordinator =
                    runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
                coordinator.InitializeLoadedRun(candidate, repository, runtime, party);
                DungeonRunController controller = CreateRunController(
                    runtimeRoot,
                    map,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtime,
                    coordinator
                );
                return DungeonRunPersistenceBootstrapResult.Success(runtime, controller, false);
            }
            catch (Exception exception)
            {
                return RollBackGeneratedRun(
                    map,
                    party,
                    explorationPresentation,
                    runtimeRoot,
                    repository,
                    rollbackState,
                    exception
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
            if (explorationPresentation is not IDungeonStairTraversalPresentation)
                throw new ArgumentException(
                    "Dungeon exploration presentation must also present stair traversal.",
                    nameof(explorationPresentation)
                );
            if (runtimeRoot == null)
                throw new ArgumentNullException(nameof(runtimeRoot));
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));
        }

        private static void PreflightGeneratedRunRuntime(
            Map map,
            DungeonLevelDocument floor,
            IReadOnlyList<DungeonPartyMemberSaveState> savedParty,
            IReadOnlyList<ActionController> party,
            GameObject runtimeRoot
        )
        {
            PreflightFloorRuntime(floor, savedParty, party);
            MapSourceValidationResult source = map.ValidateSource();
            if (map.SourceMode != MapSourceMode.Json || !source.IsValid)
                throw new InvalidOperationException(
                    "A generated New Run requires a valid current JSON map for rollback. "
                        + string.Join(" ", source.Errors)
                );
            if (map.DungeonCatalog == null)
                throw new InvalidOperationException(
                    "A generated New Run requires the current map's dungeon catalog."
                );
            GridBase grid = map.GetComponent<GridBase>();
            if (grid == null || !grid.IsInitialized || map.GetComponent<GridInput>() == null)
                throw new InvalidOperationException(
                    "A generated New Run requires GridInput and an initialized GridBase before publication."
                );
            if (runtimeRoot.GetComponentInParent<Map>() != map)
                throw new InvalidOperationException(
                    "The dungeon runtime root must be parented beneath the reusable map."
                );
            if (
                runtimeRoot.GetComponent<DungeonEncounterRuntimeController>() != null
                || runtimeRoot.GetComponent<DungeonAutosaveCoordinator>() != null
                || runtimeRoot.GetComponent<DungeonRunController>() != null
            )
            {
                throw new InvalidOperationException(
                    "The dungeon runtime root already owns persistence runtime components."
                );
            }
        }

        private static GeneratedRunRollbackState CaptureGeneratedRunRollbackState(
            Map map,
            IReadOnlyList<ActionController> party,
            IDungeonSaveRepositoryCheckpoint repository
        )
        {
            MapSourceValidationResult source = map.ValidateSource();
            PartySceneState[] partyState = party
                .Select(member => new PartySceneState(
                    member.transform.position,
                    member.gameObject.activeSelf,
                    member.GetComponent<Token>().IsRegistered
                ))
                .ToArray();
            return new GeneratedRunRollbackState(
                DungeonLevelJsonSerializer.Serialize(source.JsonMap.LevelDocument),
                map.JsonSource,
                map.DungeonCatalog,
                map.Spacing,
                map.UsesRuntimeJsonSource,
                partyState,
                repository
            );
        }

        private static DungeonRunPersistenceBootstrapResult RollBackGeneratedRunPublication(
            IDungeonSaveRepository repository,
            IDungeonSaveRepositoryCheckpoint checkpoint,
            DungeonSaveDiagnosticCode code,
            string path,
            string message
        )
        {
            DungeonSaveResult<bool> restored = repository.RestoreCheckpoint(checkpoint);
            string combined = restored.IsSuccess
                ? message
                : message
                    + " Repository rollback failed: "
                    + string.Join(" ", restored.Diagnostics.Select(item => item.Message));
            return Failure(code, path, combined);
        }

        private static DungeonRunPersistenceBootstrapResult RollBackGeneratedRun(
            Map map,
            IReadOnlyList<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            GameObject runtimeRoot,
            IDungeonSaveRepository repository,
            GeneratedRunRollbackState rollback,
            Exception failure
        )
        {
            List<string> failures = new() { failure.Message };
            DungeonEncounterRuntimeController runtime =
                runtimeRoot.GetComponent<DungeonEncounterRuntimeController>();
            try
            {
                runtime?.ResetForFloorTransition();
            }
            catch (Exception exception)
            {
                failures.Add("Runtime cleanup failed: " + exception.Message);
            }
            try
            {
                explorationPresentation.HideExploration();
            }
            catch (Exception exception)
            {
                failures.Add("Presentation cleanup failed: " + exception.Message);
            }

            DestroyRuntimeComponent(runtimeRoot.GetComponent<DungeonRunController>(), failures);
            DestroyRuntimeComponent(
                runtimeRoot.GetComponent<DungeonAutosaveCoordinator>(),
                failures
            );
            DestroyRuntimeComponent(runtime, failures);

            GridBase grid = map.GetComponent<GridBase>();
            try
            {
                PreparePartyForMapRollback(party, rollback.Party, grid);
                if (
                    !map.TryPopulateJson(
                        rollback.MapJson,
                        rollback.MapCatalog,
                        out MapSourceValidationResult validation
                    )
                )
                {
                    failures.Add("Map rollback failed: " + string.Join(" ", validation.Errors));
                }
                else if (!rollback.UsedRuntimeJson)
                {
                    map.RestoreAuthoredJsonSourceAfterRuntimeRollback(
                        rollback.JsonSource,
                        rollback.MapCatalog,
                        rollback.MapSpacing
                    );
                }
            }
            catch (Exception exception)
            {
                failures.Add("Map rollback failed: " + exception.Message);
            }

            try
            {
                RestorePartySceneState(party, rollback.Party, grid);
            }
            catch (Exception exception)
            {
                failures.Add("Party rollback failed: " + exception.Message);
            }

            DungeonSaveResult<bool> repositoryRestore = repository.RestoreCheckpoint(
                rollback.Repository
            );
            if (!repositoryRestore.IsSuccess)
            {
                failures.Add(
                    "Repository rollback failed: "
                        + string.Join(
                            " ",
                            repositoryRestore.Diagnostics.Select(item => item.Message)
                        )
                );
            }
            return Failure(
                DungeonSaveDiagnosticCode.InvalidSnapshot,
                "runtime",
                string.Join(" ", failures)
            );
        }

        private static void DestroyRuntimeComponent(
            Component component,
            ICollection<string> failures
        )
        {
            if (component == null)
                return;
            try
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"Runtime component cleanup for '{component.GetType().Name}' failed: "
                        + exception.Message
                );
            }
        }

        private static void PreparePartyForMapRollback(
            IReadOnlyList<ActionController> party,
            IReadOnlyList<PartySceneState> prior,
            GridBase grid
        )
        {
            for (int index = 0; index < party.Count; index++)
            {
                ActionController member = party[index];
                member.gameObject.SetActive(false);
                DetachPartyToken(member, grid);
                member.transform.position = prior[index].Position;
            }
        }

        private static void RestorePartySceneState(
            IReadOnlyList<ActionController> party,
            IReadOnlyList<PartySceneState> prior,
            GridBase grid
        )
        {
            for (int index = 0; index < party.Count; index++)
            {
                ActionController member = party[index];
                PartySceneState state = prior[index];
                member.transform.position = state.Position;
                if (state.WasRegistered && grid != null)
                {
                    if (!member.GetComponent<Token>().RestoreRegistrationWithGrid(grid))
                    {
                        throw new InvalidOperationException(
                            $"Party actor '{member.name}' could not restore its prior grid reservation."
                        );
                    }
                }
                member.gameObject.SetActive(state.WasActive);
            }
        }

        private static DungeonRunPersistenceBootstrapResult CreatePrepared(
            Map map,
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
            DungeonRunController controller = CreateRunController(
                runtimeRoot,
                map,
                encounterCatalog,
                combatManager,
                party,
                explorationPresentation,
                runtime,
                coordinator
            );
            return DungeonRunPersistenceBootstrapResult.Success(runtime, controller, false);
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
            PreflightFloorRuntime(currentFloor, manifest.Party, orderedParty);

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
            RestoreFloorRuntime(
                currentFloor,
                manifest.Party,
                encounterCatalog,
                combatManager,
                orderedParty,
                explorationPresentation,
                runtime
            );

            DungeonAutosaveCoordinator coordinator =
                runtimeRoot.AddComponent<DungeonAutosaveCoordinator>();
            coordinator.InitializeLoadedRun(save, repository, runtime, orderedParty);
            DungeonRunController controller = CreateRunController(
                runtimeRoot,
                map,
                encounterCatalog,
                combatManager,
                orderedParty,
                explorationPresentation,
                runtime,
                coordinator
            );
            return DungeonRunPersistenceBootstrapResult.Success(runtime, controller, true);
        }

        private static DungeonRunController CreateRunController(
            GameObject runtimeRoot,
            Map map,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IReadOnlyList<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            DungeonEncounterRuntimeController runtime,
            DungeonAutosaveCoordinator coordinator
        )
        {
            DungeonRunController controller = runtimeRoot.AddComponent<DungeonRunController>();
            controller.Initialize(
                map,
                encounterCatalog,
                combatManager,
                party,
                explorationPresentation,
                runtime,
                coordinator,
                new DeterministicDungeonGenerator(),
                new DungeonEncounterPlanner(),
                DungeonRunController.LoadEncounterCandidates()
            );
            return controller;
        }

        /// <summary>
        /// Recreates one floor runtime and restores the complete actor graph from a saved envelope.
        /// </summary>
        internal static void RestoreFloorRuntime(
            DungeonLevelDocument floor,
            IReadOnlyList<DungeonPartyMemberSaveState> savedParty,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IReadOnlyList<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            DungeonEncounterRuntimeController runtime
        )
        {
            PreflightFloorRuntime(floor, savedParty, party);
            Dictionary<string, DungeonActorSaveState> enemyState = ParseEnemyState(
                floor.RuntimeState.Creatures
            );
            Dictionary<string, GameObject> preflightActors = party.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                controller => controller.gameObject,
                StringComparer.Ordinal
            );
            runtime.InitializePersisted(
                floor,
                encounterCatalog,
                combatManager,
                party,
                explorationPresentation
            );

            Dictionary<string, GameObject> actors = new(preflightActors, StringComparer.Ordinal);
            foreach (
                DungeonEncounterMember member in runtime.GetComponentsInChildren<DungeonEncounterMember>(
                    includeInactive: true
                )
            )
            {
                if (member != null && member.IsConfigured)
                    actors.Add(member.InstanceId, member.gameObject);
            }
            GameObject ResolveActor(string actorId) =>
                actors.TryGetValue(actorId, out GameObject actor) ? actor : null;

            Action[] partyRestores = savedParty
                .Select(
                    (member, index) =>
                        DungeonActorStateAdapter.PrepareRestore(
                            party[index],
                            member.State,
                            member.CurrentHitPoints,
                            member.IsDefeated,
                            ResolveActor
                        )
                )
                .ToArray();

            List<Action> enemyRestores = new();
            foreach (
                DungeonEncounterMember member in runtime
                    .GetComponentsInChildren<DungeonEncounterMember>(includeInactive: true)
                    .OrderBy(member => member.InstanceId, StringComparer.Ordinal)
            )
            {
                if (!enemyState.TryGetValue(member.InstanceId, out DungeonActorSaveState state))
                    throw new InvalidOperationException(
                        $"Materialized enemy '{member.InstanceId}' has no saved actor state."
                    );
                ActionController controller = member.GetComponent<ActionController>();
                DungeonCreatureRuntimeState outer = floor.RuntimeState.Creatures.Single(creature =>
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
        }

        private static void PreflightFloorRuntime(
            DungeonLevelDocument floor,
            IReadOnlyList<DungeonPartyMemberSaveState> savedParty,
            IReadOnlyList<ActionController> party
        )
        {
            _ = ParseEnemyState(floor.RuntimeState.Creatures);
            Dictionary<string, GameObject> actors = party.ToDictionary(
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                controller => controller.gameObject,
                StringComparer.Ordinal
            );
            GameObject ResolveActor(string actorId) =>
                actors.TryGetValue(actorId, out GameObject actor) ? actor : null;
            _ = savedParty
                .Select(
                    (member, index) =>
                        DungeonActorStateAdapter.PrepareRestore(
                            party[index],
                            member.State,
                            member.CurrentHitPoints,
                            member.IsDefeated,
                            ResolveActor
                        )
                )
                .ToArray();
        }

        private static DungeonPartyMemberSaveState[] CreateInitialPartyState(
            IReadOnlyList<ActionController> party,
            DungeonLevelDocument floor
        )
        {
            DungeonRoom initialRoom = floor.Rooms.Single(room => Contains(room, floor.StartCell));
            HashSet<DungeonCell> blocked = new(floor.Objects.Select(placement => placement.Cell));
            foreach (DungeonStair stair in floor.Stairs)
            {
                blocked.Add(stair.Cell);
                blocked.Add(stair.ArrivalCell);
            }
            foreach (DungeonEncounterPlan plan in floor.EncounterPlans)
            foreach (DungeonCell cell in plan.SpawnCells)
                blocked.Add(cell);

            DungeonCell[] cells = Cells(initialRoom)
                .Where(cell => IsWalkable(floor.Rows, cell) && !blocked.Contains(cell))
                .OrderBy(cell => cell == floor.StartCell ? 0 : 1)
                .ThenBy(cell =>
                    Math.Abs(cell.X - floor.StartCell.X) + Math.Abs(cell.Z - floor.StartCell.Z)
                )
                .ThenBy(cell => cell.Z)
                .ThenBy(cell => cell.X)
                .ToArray();
            int livingCount = party.Count(controller =>
                !controller.GetComponent<CreatureComponent>().IsDefeated
            );
            if (cells.Length < livingCount)
                throw new InvalidOperationException(
                    "The encounter-free initial room has too few unique party cells."
                );

            Dictionary<GameObject, string> actorIds = party.ToDictionary(
                controller => controller.gameObject,
                controller => controller.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId
            );
            string IdentifyActor(GameObject actor) =>
                actor != null && actorIds.TryGetValue(actor, out string id)
                    ? id
                    : throw new InvalidOperationException(
                        $"Actor '{actor?.name}' has no dungeon persistence identity."
                    );

            int livingIndex = 0;
            return party
                .Select(controller =>
                {
                    DungeonPartyMemberIdentity identity =
                        controller.GetComponent<DungeonPartyMemberIdentity>();
                    CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                    bool defeated = creature.IsDefeated;
                    DungeonCell cell = defeated ? floor.StartCell : cells[livingIndex++];
                    return new DungeonPartyMemberSaveState
                    {
                        RosterSlotId = identity.RosterSlotId,
                        CreatureContentId = identity.CreatureContentId,
                        CellX = cell.X,
                        CellZ = cell.Z,
                        CurrentHitPoints = creature.hp,
                        IsDefeated = defeated,
                        State = DungeonActorStateAdapter.Capture(controller, IdentifyActor),
                    };
                })
                .ToArray();
        }

        private static IEnumerable<DungeonCell> Cells(DungeonRoom room)
        {
            for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
            for (int x = room.MinimumX; x <= room.MaximumX; x++)
                yield return new DungeonCell(x, z);
        }

        private static bool Contains(DungeonRoom room, DungeonCell cell) =>
            cell.X >= room.MinimumX
            && cell.X <= room.MaximumX
            && cell.Z >= room.MinimumZ
            && cell.Z <= room.MaximumZ;

        private static bool IsWalkable(IReadOnlyList<string> rows, DungeonCell cell)
        {
            if (rows.Count == 0 || cell.Z < 0 || cell.Z >= rows.Count || cell.X < 0)
                return false;
            string row = rows[rows.Count - 1 - cell.Z];
            return cell.X < row.Length && (row[cell.X] == '.' || row[cell.X] == 'D');
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
                    || controller.GetComponent<CreatureComponent>() == null
                    || controller.GetComponent<Token>() == null
                )
                    throw new InvalidOperationException(
                        $"Party actor '{controller.name}' requires unique authored dungeon identity, a CreatureComponent, and a grid Token."
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
            validation = new MapSourceValidationResult(Array.Empty<string>());
            bool[] wasActive = party.Select(member => member.gameObject.activeSelf).ToArray();
            Vector3[] priorPositions = party.Select(member => member.transform.position).ToArray();
            bool[] wasRegistered = party
                .Select(member => member.GetComponent<Token>()?.IsRegistered ?? false)
                .ToArray();
            GridBase grid = map.GetComponent<GridBase>();
            bool populationSucceeded = false;
            List<string> rollbackErrors = new();
            try
            {
                for (int index = 0; index < party.Count; index++)
                {
                    ActionController member = party[index];
                    member.gameObject.SetActive(false);
                    DungeonPartyMemberSaveState saved = savedParty[index];
                    Transform transform = member.transform;
                    if (saved.IsDefeated)
                        DetachPartyToken(member, grid);
                    transform.position = new Vector3(
                        saved.CellX,
                        transform.position.y,
                        saved.CellZ
                    );
                }

                if (
                    !map.TryPopulateJson(
                        DungeonLevelJsonSerializer.Serialize(floor),
                        map.DungeonCatalog,
                        out validation
                    )
                )
                {
                    return false;
                }

                populationSucceeded = true;
                for (int index = 0; index < party.Count; index++)
                {
                    party[index].gameObject.SetActive(!savedParty[index].IsDefeated);
                    if (!savedParty[index].IsDefeated && grid != null)
                        party[index].GetComponent<Token>()?.TryRegisterWithGrid(grid);
                }
                return true;
            }
            finally
            {
                if (!populationSucceeded)
                {
                    for (int index = 0; index < party.Count; index++)
                    {
                        party[index].transform.position = priorPositions[index];
                        if (wasRegistered[index] && grid != null)
                        {
                            if (
                                !TryRestorePartyReservation(
                                    party[index],
                                    grid,
                                    out string reservationFailure
                                )
                            )
                                rollbackErrors.Add(reservationFailure);
                        }
                        party[index].gameObject.SetActive(wasActive[index]);
                    }

                    if (rollbackErrors.Count > 0)
                    {
                        validation = new MapSourceValidationResult(
                            validation.Errors.Concat(rollbackErrors),
                            validation.JsonMap
                        );
                    }
                }
            }
        }

        private static bool TryRestorePartyReservation(
            ActionController member,
            GridBase grid,
            out string failure
        )
        {
            try
            {
                if (member.GetComponent<Token>()?.RestoreRegistrationWithGrid(grid) == true)
                {
                    failure = string.Empty;
                    return true;
                }

                failure =
                    $"Party actor '{member.name}' could not restore its prior grid reservation.";
            }
            catch (Exception exception)
            {
                failure =
                    $"Party actor '{member.name}' could not restore its prior grid reservation "
                    + $"({exception.GetType().Name}: {exception.Message}).";
            }

            return false;
        }

        private static void DetachPartyToken(ActionController member, GridBase grid)
        {
            Token token = member.GetComponent<Token>();
            if (token == null || grid == null)
                return;
            if (token.IsRegistered)
                grid.DestroyToken(member.gameObject);
            token.DetachFromGrid(grid);
        }
    }
}
