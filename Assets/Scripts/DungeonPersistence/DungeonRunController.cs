using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Encounters;
using Game.Creature;
using Game.DungeonGeneration;
using Game.DungeonPersistence.Actors;
using Game.DungeonPersistence.Autosave;
using Game.DungeonPersistence.Repository;
using Game.KayKit;
using GridPrivate;
using UnityEngine;

namespace Game.DungeonPersistence
{
    /// <summary>Classifies a rejected or failed dungeon stair transition.</summary>
    public enum DungeonTravelDiagnosticCode
    {
        /// <summary>The requested stair is not the current floor's matching endpoint.</summary>
        InvalidStair,

        /// <summary>The player did not explicitly confirm the transition.</summary>
        ConfirmationRequired,

        /// <summary>One or more living player characters are not on or beside the stair.</summary>
        PartyMissing,

        /// <summary>Combat or an action prevents a stable floor checkpoint.</summary>
        RuntimeBusy,

        /// <summary>Deterministic generation did not produce a complete floor.</summary>
        GenerationFailed,

        /// <summary>The serialized first-visit document failed strict JSON reparse validation.</summary>
        ValidationFailed,

        /// <summary>The one-envelope run autosave could not be published.</summary>
        SaveFailed,

        /// <summary>The reusable map rejected destination population.</summary>
        PopulationFailed,

        /// <summary>The destination runtime or rollback could not be reconstructed.</summary>
        RuntimeFailed,
    }

    /// <summary>Describes one structured stair-travel failure.</summary>
    public sealed class DungeonTravelDiagnostic
    {
        /// <summary>Creates a diagnostic for one stable traversal stage.</summary>
        /// <param name="code">The machine-readable failure category.</param>
        /// <param name="stage">The stable transition stage.</param>
        /// <param name="message">The actionable human-readable explanation.</param>
        public DungeonTravelDiagnostic(
            DungeonTravelDiagnosticCode code,
            string stage,
            string message
        )
        {
            Code = code;
            Stage = stage ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the failure category.</summary>
        public DungeonTravelDiagnosticCode Code { get; }

        /// <summary>Gets the stable transition stage.</summary>
        public string Stage { get; }

        /// <summary>Gets the actionable explanation.</summary>
        public string Message { get; }
    }

    /// <summary>Reports whether a stair transition committed and identifies missing party members.</summary>
    public sealed class DungeonTravelResult
    {
        internal DungeonTravelResult(
            bool isSuccess,
            int depth,
            IEnumerable<string> missingPartyMembers,
            IEnumerable<DungeonTravelDiagnostic> diagnostics
        )
        {
            IsSuccess = isSuccess;
            Depth = depth;
            MissingPartyMembers = Array.AsReadOnly(missingPartyMembers.ToArray());
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }

        /// <summary>Gets whether the complete transition committed.</summary>
        public bool IsSuccess { get; }

        /// <summary>Gets the still-playable current depth after the attempt.</summary>
        public int Depth { get; }

        /// <summary>Gets stable roster IDs for living PCs absent from the stair area.</summary>
        public IReadOnlyList<string> MissingPartyMembers { get; }

        /// <summary>Gets structured failure details; successful results contain none.</summary>
        public IReadOnlyList<DungeonTravelDiagnostic> Diagnostics { get; }
    }

    /// <summary>Describes a player-facing request to use one generated stair.</summary>
    public sealed class DungeonStairTraversalPrompt
    {
        internal DungeonStairTraversalPrompt(
            DungeonStairKind kind,
            int currentDepth,
            int targetDepth,
            IEnumerable<string> missingPartyMembers
        )
        {
            Kind = kind;
            CurrentDepth = currentDepth;
            TargetDepth = targetDepth;
            MissingPartyMembers = Array.AsReadOnly(missingPartyMembers.ToArray());
        }

        /// <summary>Gets the direction of the selected stair.</summary>
        public DungeonStairKind Kind { get; }

        /// <summary>Gets the currently playable depth.</summary>
        public int CurrentDepth { get; }

        /// <summary>Gets the destination depth that confirmation would select.</summary>
        public int TargetDepth { get; }

        /// <summary>Gets stable roster IDs for living PCs outside the stair area.</summary>
        public IReadOnlyList<string> MissingPartyMembers { get; }

        /// <summary>Gets whether the full living party is eligible to confirm travel.</summary>
        public bool CanConfirm => MissingPartyMembers.Count == 0;
    }

    /// <summary>
    /// Presents stair eligibility and obtains an explicit player decision without coupling
    /// traversal policy to a particular UI implementation.
    /// </summary>
    public interface IDungeonStairTraversalPresentation
    {
        /// <summary>Shows one stair request and reports an explicit confirm or reject decision.</summary>
        /// <param name="prompt">The immutable direction, depth, and missing-party details.</param>
        /// <param name="respond">
        /// Completes the request. Presenters must never report <see langword="true"/> when
        /// <see cref="DungeonStairTraversalPrompt.CanConfirm"/> is false.
        /// </param>
        void PresentStairTraversal(DungeonStairTraversalPrompt prompt, Action<bool> respond);

        /// <summary>Dismisses any stale prompt after a floor switch or owner teardown.</summary>
        void DismissStairTraversal();
    }

    /// <summary>
    /// Owns deterministic multi-floor acquisition, exact state capture, reusable-map population,
    /// and paired stair traversal for one dungeon run.
    /// </summary>
    /// <remarks>
    /// First visits always generate, serialize, strictly reparse, publish the run envelope, and
    /// only then populate the reusable scene. Revisits use only indexed saved JSON/runtime state.
    /// A failed stage leaves the prior floor selected and playable; a post-save population failure
    /// republishes the prior complete envelope before returning diagnostics.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DungeonRunController : MonoBehaviour
    {
        private const string EncounterCatalogResource = "DataFiles/dungeon/encounter-enemies";

        private Map map;
        private DungeonEncounterCreatureCatalog encounterCatalog;
        private CombatManagerInterface combatManager;
        private ActionController[] party = Array.Empty<ActionController>();
        private IDungeonExplorationPresentation explorationPresentation;
        private DungeonEncounterRuntimeController runtime;
        private DungeonAutosaveCoordinator autosave;
        private IDungeonGenerator generator;
        private DungeonEncounterPlanner encounterPlanner;
        private IReadOnlyList<DungeonEncounterCandidate> encounterCandidates =
            Array.Empty<DungeonEncounterCandidate>();
        private IDungeonStairTraversalPresentation stairPresentation;
        private GridInput gridInput;
        private int width;
        private int height;

        /// <summary>Gets whether the controller owns a stable current floor.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Gets the selected nonnegative depth.</summary>
        public int CurrentDepth { get; private set; }

        /// <summary>Gets diagnostics from the most recent rejected or failed attempt.</summary>
        public IReadOnlyList<DungeonTravelDiagnostic> LastDiagnostics { get; private set; } =
            Array.Empty<DungeonTravelDiagnostic>();

        internal void Initialize(
            Map map,
            DungeonEncounterCreatureCatalog encounterCatalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            DungeonEncounterRuntimeController runtime,
            DungeonAutosaveCoordinator autosave,
            IDungeonGenerator generator,
            DungeonEncounterPlanner encounterPlanner,
            IReadOnlyList<DungeonEncounterCandidate> encounterCandidates
        )
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon run controller can only initialize once."
                );
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.encounterCatalog =
                encounterCatalog ?? throw new ArgumentNullException(nameof(encounterCatalog));
            this.combatManager =
                combatManager ?? throw new ArgumentNullException(nameof(combatManager));
            this.explorationPresentation =
                explorationPresentation
                ?? throw new ArgumentNullException(nameof(explorationPresentation));
            stairPresentation =
                explorationPresentation as IDungeonStairTraversalPresentation
                ?? throw new ArgumentException(
                    "Dungeon exploration presentation must also present stair traversal.",
                    nameof(explorationPresentation)
                );
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.autosave = autosave ?? throw new ArgumentNullException(nameof(autosave));
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.encounterPlanner =
                encounterPlanner ?? throw new ArgumentNullException(nameof(encounterPlanner));
            this.encounterCandidates =
                encounterCandidates ?? throw new ArgumentNullException(nameof(encounterCandidates));
            this.party = (party ?? throw new ArgumentNullException(nameof(party))).ToArray();
            if (this.party.Length == 0 || this.party.Any(member => member == null))
                throw new ArgumentException(
                    "Dungeon traversal requires a complete authored party.",
                    nameof(party)
                );

            DungeonRunSaveManifest manifest = autosave.LastCommittedSnapshot.Manifest;
            this.party = OrderParty(this.party, manifest.Party);
            DungeonLevelDocument current = autosave.LastCommittedSnapshot.GetFloor(
                manifest.CurrentDepth
            );
            width = current.Width;
            height = current.Height;
            CurrentDepth = manifest.CurrentDepth;
            gridInput = map.GetComponent<GridInput>();
            if (gridInput != null)
                gridInput.CellClicked += OnGridCellClicked;
            IsInitialized = true;
        }

        internal static IReadOnlyList<DungeonEncounterCandidate> LoadEncounterCandidates()
        {
            TextAsset source = Resources.Load<TextAsset>(EncounterCatalogResource);
            if (source == null)
                throw new InvalidOperationException(
                    $"The encounter catalog is missing at Resources/{EncounterCatalogResource}.json."
                );
            return DungeonEncounterCatalogJson.Parse(source.text);
        }

        internal void ReplaceGenerationForTests(
            IDungeonGenerator replacementGenerator,
            DungeonEncounterPlanner replacementPlanner,
            IReadOnlyList<DungeonEncounterCandidate> replacementCandidates
        )
        {
            generator =
                replacementGenerator
                ?? throw new ArgumentNullException(nameof(replacementGenerator));
            encounterPlanner =
                replacementPlanner ?? throw new ArgumentNullException(nameof(replacementPlanner));
            encounterCandidates =
                replacementCandidates
                ?? throw new ArgumentNullException(nameof(replacementCandidates));
        }

        /// <summary>
        /// Presents eligibility and confirmation for a live stair, then traverses only after the
        /// presenter explicitly confirms.
        /// </summary>
        /// <param name="stair">The selected marker belonging to the current reusable map.</param>
        public void RequestUseStair(DungeonStairMarker stair)
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon run controller is not initialized."
                );

            DungeonLevelDocument current = autosave.LastCommittedSnapshot.GetFloor(CurrentDepth);
            if (!TryMatchCurrentStair(stair, current, out DungeonStair documented))
            {
                _ = Failure(
                    DungeonTravelDiagnosticCode.InvalidStair,
                    "eligibility.stair",
                    "The selected stair is not a live endpoint on the current floor."
                );
                return;
            }

            string[] missing = FindMissingLivingParty(documented.Cell);
            int targetDepth =
                documented.Kind == DungeonStairKind.Down
                    ? CurrentDepth == int.MaxValue
                        ? int.MaxValue
                        : CurrentDepth + 1
                    : Math.Max(0, CurrentDepth - 1);
            DungeonStairTraversalPrompt prompt = new(
                documented.Kind,
                CurrentDepth,
                targetDepth,
                missing
            );
            int requestedDepth = CurrentDepth;
            string requestedId = documented.Id;
            stairPresentation.PresentStairTraversal(
                prompt,
                confirmed =>
                    CompletePresentedRequest(requestedDepth, requestedId, prompt, confirmed)
            );
        }

        private void CompletePresentedRequest(
            int requestedDepth,
            string requestedId,
            DungeonStairTraversalPrompt prompt,
            bool confirmed
        )
        {
            if (!IsInitialized || requestedDepth != CurrentDepth)
                return;
            DungeonStairMarker currentMarker = map.GetComponentsInChildren<DungeonStairMarker>(
                    includeInactive: false
                )
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.StableId, requestedId, StringComparison.Ordinal)
                );
            if (currentMarker == null)
            {
                _ = Failure(
                    DungeonTravelDiagnosticCode.InvalidStair,
                    "eligibility.stair",
                    "The selected stair is no longer active."
                );
                return;
            }
            _ = TryUseStair(currentMarker, confirmed && prompt.CanConfirm);
        }

        /// <summary>Attempts a confirmed full-party transition through a generated stair.</summary>
        /// <param name="stair">The live marker belonging to the current reusable map.</param>
        /// <param name="confirmed">Whether the player explicitly confirmed this transition.</param>
        /// <returns>A committed destination depth or structured rejection/failure diagnostics.</returns>
        public DungeonTravelResult TryUseStair(DungeonStairMarker stair, bool confirmed)
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon run controller is not initialized."
                );

            DungeonRunSave baseline = autosave.LastCommittedSnapshot;
            DungeonLevelDocument current = baseline.GetFloor(CurrentDepth);
            if (!TryMatchCurrentStair(stair, current, out DungeonStair documented))
                return Failure(
                    DungeonTravelDiagnosticCode.InvalidStair,
                    "eligibility.stair",
                    "The selected stair is not a live endpoint on the current floor."
                );
            if (combatManager.IsCombatActive || runtime.HasActionInProgress)
                return Failure(
                    DungeonTravelDiagnosticCode.RuntimeBusy,
                    "eligibility.runtime",
                    "Dungeon stair travel requires an inactive combat and no action in progress."
                );

            string[] missing = FindMissingLivingParty(documented.Cell);
            if (missing.Length > 0)
            {
                DungeonTravelDiagnostic diagnostic = new(
                    DungeonTravelDiagnosticCode.PartyMissing,
                    "eligibility.party",
                    "Every living party member must be on or orthogonally adjacent to the stair."
                );
                LastDiagnostics = new[] { diagnostic };
                return new DungeonTravelResult(false, CurrentDepth, missing, LastDiagnostics);
            }
            if (!confirmed)
                return Failure(
                    DungeonTravelDiagnosticCode.ConfirmationRequired,
                    "eligibility.confirmation",
                    "Dungeon stair travel requires explicit player confirmation."
                );

            if (documented.Kind == DungeonStairKind.Down && CurrentDepth == int.MaxValue)
                return Failure(
                    DungeonTravelDiagnosticCode.InvalidStair,
                    "eligibility.depth",
                    "The current depth cannot be represented by a deeper 32-bit integer."
                );
            int targetDepth =
                documented.Kind == DungeonStairKind.Down ? CurrentDepth + 1 : CurrentDepth - 1;
            if (targetDepth < 0)
                return Failure(
                    DungeonTravelDiagnosticCode.InvalidStair,
                    "eligibility.depth",
                    "An Up stair cannot traverse above dungeon depth zero."
                );

            DungeonSaveResult<DungeonRunSave> checkpoint = autosave.CheckpointCurrentFloor();
            if (!checkpoint.IsSuccess)
                return SaveFailure("capture", checkpoint.Diagnostics);
            baseline = checkpoint.Value;

            DungeonLevelDocument target;
            bool firstVisit = !baseline.HasFloor(targetDepth);
            if (firstVisit)
            {
                DungeonTravelResult acquisitionFailure = TryAcquireFirstVisit(
                    baseline.Manifest.StartingSeed,
                    targetDepth,
                    out target
                );
                if (acquisitionFailure != null)
                    return acquisitionFailure;
            }
            else
            {
                target = baseline.GetFloor(targetDepth);
            }

            DungeonStairKind arrivalKind =
                documented.Kind == DungeonStairKind.Down
                    ? DungeonStairKind.Up
                    : DungeonStairKind.Down;
            DungeonPartyMemberSaveState[] arrivalParty;
            try
            {
                arrivalParty = CreateArrivalParty(baseline.Manifest.Party, target, arrivalKind);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(
                    DungeonTravelDiagnosticCode.ValidationFailed,
                    "arrival",
                    exception.Message
                );
            }

            DungeonRunSave candidate;
            try
            {
                candidate = firstVisit
                    ? baseline.WithAddedAndSelectedFloor(arrivalParty, target)
                    : baseline.WithSelectedFloor(targetDepth, arrivalParty);
            }
            catch (Exception exception)
                when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return Failure(
                    DungeonTravelDiagnosticCode.ValidationFailed,
                    "save.candidate",
                    exception.Message
                );
            }

            DungeonSaveResult<DungeonRunSave> published = autosave.Publish(candidate);
            if (!published.IsSuccess)
                return SaveFailure("publish", published.Diagnostics);

            runtime.ResetForFloorTransition();
            if (!TryPopulateFloor(target, arrivalParty, out MapSourceValidationResult validation))
            {
                return RecoverFromPopulationFailure(baseline, validation);
            }

            try
            {
                PlaceParty(arrivalParty);
                DungeonRunPersistenceBootstrap.RestoreFloorRuntime(
                    target,
                    arrivalParty,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtime
                );
            }
            catch (Exception transitionException)
            {
                return RollBackRuntime(baseline, transitionException);
            }

            autosave.AdoptPublishedFloor(candidate, target, runtime);
            CurrentDepth = targetDepth;
            stairPresentation.DismissStairTraversal();
            LastDiagnostics = Array.Empty<DungeonTravelDiagnostic>();
            return new DungeonTravelResult(
                true,
                CurrentDepth,
                Array.Empty<string>(),
                LastDiagnostics
            );
        }

        private void OnDestroy()
        {
            if (gridInput != null)
                gridInput.CellClicked -= OnGridCellClicked;
            stairPresentation?.DismissStairTraversal();
            IsInitialized = false;
        }

        private void OnGridCellClicked(Vector3Int cell)
        {
            DungeonStairMarker stair = map.GetComponentsInChildren<DungeonStairMarker>(
                    includeInactive: false
                )
                .SingleOrDefault(candidate =>
                    candidate.Cell.X == cell.x && candidate.Cell.Z == cell.z
                );
            if (stair != null)
                RequestUseStair(stair);
        }

        private DungeonTravelResult TryAcquireFirstVisit(
            int runSeed,
            int depth,
            out DungeonLevelDocument parsedDocument
        )
        {
            DungeonTravelDiagnostic diagnostic = AcquireFirstVisit(
                generator,
                encounterPlanner,
                encounterCandidates,
                party,
                runSeed,
                depth,
                width,
                height,
                out parsedDocument
            );
            return diagnostic == null
                ? null
                : Failure(diagnostic.Code, diagnostic.Stage, diagnostic.Message);
        }

        internal static DungeonTravelDiagnostic AcquireFirstVisit(
            IDungeonGenerator generator,
            DungeonEncounterPlanner encounterPlanner,
            IReadOnlyList<DungeonEncounterCandidate> encounterCandidates,
            IReadOnlyList<ActionController> party,
            int runSeed,
            int depth,
            int width,
            int height,
            out DungeonLevelDocument parsedDocument
        )
        {
            parsedDocument = null;
            (
                DungeonLayout Layout,
                DungeonRoomLayout Room,
                DungeonCorridorLayout Corridor
            )[] profiles = new[]
            {
                (DungeonLayout.Box, DungeonRoomLayout.Scattered, DungeonCorridorLayout.Bent),
            }
                .Concat(
                    from layout in Enum.GetValues(typeof(DungeonLayout)).Cast<DungeonLayout>()
                    from room in Enum.GetValues(typeof(DungeonRoomLayout)).Cast<DungeonRoomLayout>()
                    from corridor in Enum.GetValues(typeof(DungeonCorridorLayout))
                        .Cast<DungeonCorridorLayout>()
                    select (layout, room, corridor)
                )
                .Distinct()
                .ToArray();
            DungeonLevelDocument planned = null;
            string contractFailure = string.Empty;
            string generationFailure = string.Empty;
            foreach (
                (
                    DungeonLayout Layout,
                    DungeonRoomLayout Room,
                    DungeonCorridorLayout Corridor
                ) profile in profiles
            )
            {
                DungeonGenerationResult generated = generator.Generate(
                    new DungeonGenerationRequest
                    {
                        RunSeed = runSeed,
                        Depth = depth,
                        Width = width,
                        Height = height,
                        Layout = profile.Layout,
                        RoomLayout = profile.Room,
                        CorridorLayout = profile.Corridor,
                        StairCount = depth == 0 ? 1 : 2,
                    }
                );
                if (!generated.IsSuccess)
                {
                    generationFailure = string.Join(
                        " ",
                        generated.Diagnostics.Select(item => item.Message)
                    );
                    continue;
                }

                try
                {
                    int partyLevel = party
                        .Select(member => member.GetComponent<CreatureComponent>()?.level ?? 1)
                        .Max();
                    DungeonLevelDocument encounters = encounterPlanner.Plan(
                        generated.Document,
                        partyLevel,
                        party.Count,
                        encounterCandidates
                    );
                    planned = WithPristineRuntime(encounters);
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        || exception is InvalidOperationException
                        || exception is FormatException
                    )
                {
                    return new DungeonTravelDiagnostic(
                        DungeonTravelDiagnosticCode.GenerationFailed,
                        "generation.encounters",
                        exception.Message
                    );
                }

                contractFailure = ValidateFirstVisitContract(planned, depth);
                if (string.IsNullOrEmpty(contractFailure))
                    break;
                planned = null;
            }
            if (planned == null)
            {
                if (string.IsNullOrEmpty(contractFailure))
                {
                    return new DungeonTravelDiagnostic(
                        DungeonTravelDiagnosticCode.GenerationFailed,
                        "generation",
                        generationFailure
                    );
                }
                return new DungeonTravelDiagnostic(
                    DungeonTravelDiagnosticCode.ValidationFailed,
                    "validation.contract",
                    contractFailure
                );
            }

            string json = DungeonLevelJsonSerializer.Serialize(planned);
            DungeonLevelParseResult parsed = DungeonLevelJsonParser.Parse(json);
            if (!parsed.IsSuccess)
            {
                return new DungeonTravelDiagnostic(
                    DungeonTravelDiagnosticCode.ValidationFailed,
                    "validation.reparse",
                    string.Join(" ", parsed.Diagnostics.Select(item => item.Message))
                );
            }
            parsedDocument = parsed.Document;
            return null;
        }

        private static string ValidateFirstVisitContract(DungeonLevelDocument document, int depth)
        {
            int expectedStairs = depth == 0 ? 1 : 2;
            if (
                document.Stairs.Count != expectedStairs
                || document.Stairs.Count(stair => stair.Kind == DungeonStairKind.Down) != 1
                || document.Stairs.Count(stair => stair.Kind == DungeonStairKind.Up)
                    != (depth == 0 ? 0 : 1)
            )
            {
                return $"Depth {depth} does not have the required paired stair contract.";
            }

            DungeonCell arrival =
                depth == 0
                    ? document.StartCell
                    : document
                        .Stairs.Single(stair => stair.Kind == DungeonStairKind.Up)
                        .ArrivalCell;
            int? arrivalRoomId = DungeonEncounterPlanner.FindArrivalRoomId(document, arrival);
            if (!arrivalRoomId.HasValue)
                return $"Depth {depth} arrival region has no reachable generated room.";
            if (document.EncounterPlans.Any(plan => plan.RoomId == arrivalRoomId.Value))
                return $"Depth {depth} arrival room contains an encounter plan.";
            DungeonStair down = document.Stairs.Single(stair =>
                stair.Kind == DungeonStairKind.Down
            );
            int? downRoomId = DungeonEncounterPlanner.FindArrivalRoomId(document, down.ArrivalCell);
            if (downRoomId == arrivalRoomId)
                return $"Depth {depth} places its Down stair in the party arrival room.";
            if (
                depth == 0
                && Math.Abs(document.StartCell.X - down.Cell.X)
                    + Math.Abs(document.StartCell.Z - down.Cell.Z)
                    <= 1
            )
            {
                return $"Depth {depth} places its start in the Down stair interaction area.";
            }
            return string.Empty;
        }

        private DungeonTravelResult RollBackRuntime(
            DungeonRunSave baseline,
            Exception transitionException,
            DungeonTravelDiagnosticCode code = DungeonTravelDiagnosticCode.RuntimeFailed,
            string stage = "runtime"
        )
        {
            List<string> failures = new() { transitionException.Message };
            try
            {
                DungeonRunSaveManifest manifest = baseline.Manifest;
                DungeonLevelDocument prior = baseline.GetFloor(manifest.CurrentDepth);
                runtime.ResetForFloorTransition();
                if (
                    !TryPopulateFloor(
                        prior,
                        manifest.Party,
                        out MapSourceValidationResult validation
                    )
                )
                    throw new InvalidOperationException(string.Join(" ", validation.Errors));
                PlaceParty(manifest.Party);
                DungeonRunPersistenceBootstrap.RestoreFloorRuntime(
                    prior,
                    manifest.Party,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtime
                );
                DungeonSaveResult<DungeonRunSave> republished = autosave.Publish(baseline);
                if (!republished.IsSuccess)
                    failures.AddRange(republished.Diagnostics.Select(item => item.Message));
            }
            catch (Exception rollbackException)
            {
                failures.Add("Rollback failed: " + rollbackException.Message);
            }
            return Failure(code, stage, string.Join(" ", failures));
        }

        private DungeonTravelResult RecoverFromPopulationFailure(
            DungeonRunSave baseline,
            MapSourceValidationResult validation
        )
        {
            List<string> failures = new(validation.Errors);
            try
            {
                DungeonRunSaveManifest manifest = baseline.Manifest;
                DungeonLevelDocument prior = baseline.GetFloor(manifest.CurrentDepth);
                PlaceParty(manifest.Party);
                DungeonRunPersistenceBootstrap.RestoreFloorRuntime(
                    prior,
                    manifest.Party,
                    encounterCatalog,
                    combatManager,
                    party,
                    explorationPresentation,
                    runtime
                );
                DungeonSaveResult<DungeonRunSave> republished = autosave.Publish(baseline);
                if (!republished.IsSuccess)
                    failures.AddRange(republished.Diagnostics.Select(item => item.Message));
            }
            catch (Exception recoveryException)
            {
                failures.Add("Recovery failed: " + recoveryException.Message);
            }
            return Failure(
                DungeonTravelDiagnosticCode.PopulationFailed,
                "population",
                string.Join(" ", failures)
            );
        }

        private bool TryPopulateFloor(
            DungeonLevelDocument floor,
            IReadOnlyList<DungeonPartyMemberSaveState> savedParty,
            out MapSourceValidationResult validation
        )
        {
            bool[] wasActive = party.Select(member => member.gameObject.activeSelf).ToArray();
            Vector3[] priorPositions = party.Select(member => member.transform.position).ToArray();
            foreach (ActionController member in party)
                member.gameObject.SetActive(false);
            for (int index = 0; index < party.Length; index++)
            {
                Transform actor = party[index].transform;
                DungeonPartyMemberSaveState saved = savedParty[index];
                actor.position = new Vector3(saved.CellX, actor.position.y, saved.CellZ);
            }

            if (
                map.TryPopulateJson(
                    DungeonLevelJsonSerializer.Serialize(floor),
                    map.DungeonCatalog,
                    out validation
                )
            )
                return true;

            for (int index = 0; index < party.Length; index++)
            {
                party[index].transform.position = priorPositions[index];
                party[index].gameObject.SetActive(wasActive[index]);
            }
            return false;
        }

        private static DungeonLevelDocument WithPristineRuntime(DungeonLevelDocument source) =>
            new(
                source.Generation,
                source.Rows,
                source.Rooms,
                source.Doors,
                source.Stairs,
                source.StartCell,
                source.SafeCells,
                source.Objects,
                source.EncounterPlans,
                new DungeonRuntimeState(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>()
                )
            );

        private static DungeonPartyMemberSaveState[] CreateArrivalParty(
            IReadOnlyList<DungeonPartyMemberSaveState> source,
            DungeonLevelDocument target,
            DungeonStairKind arrivalKind
        )
        {
            DungeonStair stair = target.Stairs.SingleOrDefault(item => item.Kind == arrivalKind);
            if (stair == null)
                throw new InvalidOperationException(
                    $"Depth {target.Generation.Depth} has no {arrivalKind} arrival stair."
                );

            HashSet<DungeonCell> blocked = new(
                target.RuntimeState.Creatures.Select(creature => creature.Cell)
            );
            foreach (DungeonObjectPlacement placement in target.Objects)
                blocked.Add(placement.Cell);
            int livingCount = source.Count(member => !member.IsDefeated);
            DungeonCell[] cells = ArrivalCells(target, stair, blocked, livingCount).ToArray();
            int livingIndex = 0;
            return source
                .Select(member =>
                {
                    DungeonCell cell = member.IsDefeated ? target.StartCell : cells[livingIndex++];
                    return new DungeonPartyMemberSaveState
                    {
                        RosterSlotId = member.RosterSlotId,
                        CreatureContentId = member.CreatureContentId,
                        CellX = cell.X,
                        CellZ = cell.Z,
                        CurrentHitPoints = member.CurrentHitPoints,
                        IsDefeated = member.IsDefeated,
                        State = member.State,
                    };
                })
                .ToArray();
        }

        private static IEnumerable<DungeonCell> ArrivalCells(
            DungeonLevelDocument target,
            DungeonStair stair,
            ISet<DungeonCell> blocked,
            int partyCount
        )
        {
            DungeonCell[] available =
            {
                stair.ArrivalCell,
                stair.Cell,
                new(stair.Cell.X, stair.Cell.Z + 1),
                new(stair.Cell.X + 1, stair.Cell.Z),
                new(stair.Cell.X, stair.Cell.Z - 1),
                new(stair.Cell.X - 1, stair.Cell.Z),
            };
            DungeonCell[] selected = available
                .Distinct()
                .Where(cell => IsWalkable(target.Rows, cell) && !blocked.Contains(cell))
                .Take(partyCount)
                .ToArray();
            if (selected.Length < partyCount)
                throw new InvalidOperationException(
                    "The destination stair has too few unique walkable on-or-adjacent cells for the living party."
                );
            return selected;
        }

        private void PlaceParty(IReadOnlyList<DungeonPartyMemberSaveState> savedParty)
        {
            for (int index = 0; index < party.Length; index++)
            {
                DungeonPartyMemberSaveState saved = savedParty[index];
                Transform actor = party[index].transform;
                actor.position = new Vector3(saved.CellX, actor.position.y, saved.CellZ);
                party[index].gameObject.SetActive(!saved.IsDefeated);
            }
        }

        private bool TryMatchCurrentStair(
            DungeonStairMarker marker,
            DungeonLevelDocument current,
            out DungeonStair documented
        )
        {
            documented = null;
            if (marker == null || !marker.gameObject.activeInHierarchy)
                return false;
            documented = current.Stairs.FirstOrDefault(item =>
                string.Equals(item.Id, marker.StableId, StringComparison.Ordinal)
                && item.Kind == marker.Kind
                && item.Cell == marker.Cell
                && item.ArrivalCell == marker.ArrivalCell
            );
            return documented != null && marker.GetComponentInParent<Map>() == map;
        }

        private string[] FindMissingLivingParty(DungeonCell stair)
        {
            return party
                .Where(member =>
                {
                    CreatureComponent creature = member.GetComponent<CreatureComponent>();
                    if (creature != null && creature.IsDefeated)
                        return false;
                    Vector3Int cell = Vector3Int.RoundToInt(member.transform.position);
                    long distance =
                        Math.Abs((long)cell.x - stair.X) + Math.Abs((long)cell.z - stair.Z);
                    return !member.gameObject.activeInHierarchy || distance > 1;
                })
                .Select(member =>
                    member.GetComponent<DungeonPartyMemberIdentity>()?.RosterSlotId ?? member.name
                )
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }

        private DungeonTravelResult SaveFailure(
            string stage,
            IReadOnlyList<DungeonSaveDiagnostic> diagnostics
        ) =>
            Failure(
                DungeonTravelDiagnosticCode.SaveFailed,
                "save." + stage,
                string.Join(" ", diagnostics.Select(item => item.Message))
            );

        private DungeonTravelResult Failure(
            DungeonTravelDiagnosticCode code,
            string stage,
            string message
        )
        {
            DungeonTravelDiagnostic diagnostic = new(code, stage, message);
            LastDiagnostics = new[] { diagnostic };
            return new DungeonTravelResult(
                false,
                CurrentDepth,
                Array.Empty<string>(),
                LastDiagnostics
            );
        }

        private static ActionController[] OrderParty(
            IEnumerable<ActionController> unordered,
            IReadOnlyList<DungeonPartyMemberSaveState> saved
        )
        {
            Dictionary<string, ActionController> bySlot = unordered.ToDictionary(
                member => member.GetComponent<DungeonPartyMemberIdentity>().RosterSlotId,
                StringComparer.Ordinal
            );
            return saved.Select(member => bySlot[member.RosterSlotId]).ToArray();
        }

        private static bool IsWalkable(IReadOnlyList<string> rows, DungeonCell cell)
        {
            if (rows.Count == 0 || cell.Z < 0 || cell.Z >= rows.Count || cell.X < 0)
                return false;
            string row = rows[rows.Count - 1 - cell.Z];
            return cell.X < row.Length && (row[cell.X] == '.' || row[cell.X] == 'D');
        }
    }
}
