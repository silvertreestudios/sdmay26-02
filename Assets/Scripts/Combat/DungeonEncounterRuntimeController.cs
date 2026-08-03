using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Exploration;
using Game.Creature;
using Game.DungeonGeneration;
using Game.KayKit;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>Displays movement-only controls while a party explores between encounters.</summary>
    public interface IDungeonExplorationPresentation
    {
        /// <summary>Shows movement controls for one selected living party member.</summary>
        /// <param name="party">All living party controllers available for selection.</param>
        /// <param name="selected">The party controller whose movement controls are shown.</param>
        /// <param name="trySelectLeader">
        /// Attempts to make a selected living party member the authoritative exploration leader.
        /// </param>
        void ShowExploration(
            IReadOnlyList<ActionController> party,
            ActionController selected,
            Func<ActionController, bool> trySelectLeader
        );

        /// <summary>Clears exploration controls and any stale selected controller.</summary>
        void HideExploration();
    }

    /// <summary>
    /// Observes party room transitions and owns one generated floor's encounter director.
    /// </summary>
    /// <remarks>
    /// This component is the runtime composition boundary between generated map documents,
    /// transform-based room occupancy, materialization, and initiative. It deliberately receives
    /// its party and combat manager explicitly so tests and future floor-loading code can restore
    /// the same lifecycle without adding another singleton.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DungeonEncounterRuntimeController
        : MonoBehaviour,
            IExplorationStrideCoordinator
    {
        private DungeonRoom[] rooms = Array.Empty<DungeonRoom>();
        private ActionController[] party = Array.Empty<ActionController>();
        private HashSet<int> encounterRoomIds = new();
        private readonly SortedSet<int> pendingEncounterRooms = new();
        private readonly Dictionary<ActionController, int> previousRoomByParty = new();
        private readonly List<ActionController> livingPartyBuffer = new();
        private readonly HashSet<ActionController> livingPartySet = new();
        private readonly List<ActionController> stalePartyObservations = new();
        private readonly HashSet<int> occupiedRooms = new();
        private readonly HashSet<int> doorRevealedEncounterRooms = new();
        private readonly Dictionary<ActionController, int> currentRoomByParty = new();
        private readonly List<Vector3> livingPartyPositions = new();
        private readonly Dictionary<DungeonCell, DungeonDoorController> doorsByCell = new();
        private readonly SortedSet<string> openDoorIds = new(StringComparer.Ordinal);
        private DungeonEncounterDirector director;
        private CombatManagerInterface combatManager;
        private IDungeonExplorationPresentation explorationPresentation;
        private ActionController selectedLeader;
        private ActionController[] presentedExplorationParty = Array.Empty<ActionController>();
        private GridInput gridInput;
        private GridBase grid;
        private IExplorationStrideCoordinator explorationMovement =
            NoExplorationStrideCoordinator.Instance;

        /// <summary>Gets whether this component owns a fully constructed floor lifecycle.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Gets the authoritative lifecycle after initialization.</summary>
        /// <exception cref="InvalidOperationException">The controller is not initialized.</exception>
        public DungeonEncounterStateMachine Lifecycle =>
            IsInitialized
                ? director.Lifecycle
                : throw new InvalidOperationException(
                    "The dungeon encounter runtime is not initialized."
                );

        /// <summary>Raised after a generated door opens and its navigation state is committed.</summary>
        public event Action<string> DoorOpened = delegate { };

        /// <summary>Raised after any persistent generated-floor mutation commits.</summary>
        public event Action PersistentStateChanged = delegate { };

        /// <summary>Gets whether a party member or materialized enemy is completing an action.</summary>
        public bool HasActionInProgress => IsInitialized && director.HasActionInProgress;

        /// <summary>Gets whether a living selected leader currently owns exploration movement.</summary>
        public bool IsExplorationActive =>
            IsInitialized
            && !combatManager.IsCombatActive
            && selectedLeader != null
            && CanObserve(selectedLeader)
            && selectedLeader.IsInDungeonExploration;

        /// <summary>Initializes pristine encounter state for a newly generated floor.</summary>
        /// <param name="document">The validated source document retained by the active map.</param>
        /// <param name="catalog">The validated runtime creature catalog.</param>
        /// <param name="combatManager">The explicit combat scheduler for the scene.</param>
        /// <param name="party">The floor's distinct player action controllers.</param>
        /// <param name="explorationPresentation">The required movement-only HUD boundary.</param>
        public void InitializePristine(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog catalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation
        )
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            Initialize(
                document,
                catalog,
                combatManager,
                party,
                explorationPresentation,
                new DungeonEncounterStateMachine(document.EncounterPlans),
                Array.Empty<DungeonCreatureRuntimeState>()
            );
        }

        /// <summary>Initializes a generated floor from a previously captured lifecycle snapshot.</summary>
        /// <param name="document">The validated immutable source document for the saved floor.</param>
        /// <param name="catalog">The validated runtime creature catalog.</param>
        /// <param name="combatManager">The explicit inactive combat scheduler for the scene.</param>
        /// <param name="party">The floor's distinct player action controllers.</param>
        /// <param name="explorationPresentation">The required movement-only HUD boundary.</param>
        /// <param name="snapshot">The complete lifecycle snapshot captured for this document.</param>
        public void InitializeRestored(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog catalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation,
            DungeonEncounterLifecycleSnapshot snapshot
        )
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            Initialize(
                document,
                catalog,
                combatManager,
                party,
                explorationPresentation,
                DungeonEncounterStateMachine.Restore(document.EncounterPlans, snapshot),
                Array.Empty<DungeonCreatureRuntimeState>()
            );
        }

        /// <summary>Initializes a generated floor from its validated persisted runtime document.</summary>
        /// <param name="document">The immutable source document containing runtime state.</param>
        /// <param name="catalog">The validated runtime creature catalog.</param>
        /// <param name="combatManager">The explicit inactive combat scheduler for the scene.</param>
        /// <param name="party">The floor's distinct player action controllers.</param>
        /// <param name="explorationPresentation">The required movement-only HUD boundary.</param>
        /// <exception cref="ArgumentException">The document does not contain runtime state.</exception>
        public void InitializePersisted(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog catalog,
            CombatManagerInterface combatManager,
            IEnumerable<ActionController> party,
            IDungeonExplorationPresentation explorationPresentation
        )
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.RuntimeState == null)
                throw new ArgumentException(
                    "Persisted initialization requires document runtime state.",
                    nameof(document)
                );
            Initialize(
                document,
                catalog,
                combatManager,
                party,
                explorationPresentation,
                DungeonEncounterStateMachine.Restore(
                    document.EncounterPlans,
                    DungeonEncounterLifecycleSnapshot.FromRuntimeState(
                        document.EncounterPlans,
                        document.RuntimeState
                    )
                ),
                document.RuntimeState.Creatures
            );
        }

        /// <summary>Captures the persistence-facing state for every encounter on this floor.</summary>
        /// <returns>A complete immutable lifecycle snapshot.</returns>
        /// <exception cref="InvalidOperationException">The controller is not initialized.</exception>
        public DungeonEncounterLifecycleSnapshot CaptureSnapshot()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon encounter runtime is not initialized."
                );
            return director.CaptureSnapshot();
        }

        /// <summary>Captures complete mutable runtime state for the current floor.</summary>
        /// <param name="captureCreatureState">Captures child state for each living enemy.</param>
        /// <returns>Door, encounter, defeat, and living-enemy state.</returns>
        public DungeonRuntimeState CaptureRuntimeState(
            Func<ActionController, string> captureCreatureState
        )
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon encounter runtime is not initialized."
                );
            if (captureCreatureState == null)
                throw new ArgumentNullException(nameof(captureCreatureState));

            DungeonEncounterLifecycleSnapshot snapshot = director.CaptureSnapshot();
            return new DungeonRuntimeState(
                CaptureOpenDoorIds(),
                snapshot
                    .Groups.Where(group => group.State == DungeonEncounterGroupState.Cleared)
                    .Select(group => group.EncounterId),
                snapshot.Groups.SelectMany(group => group.DefeatedCreatureInstanceIds),
                director.CaptureLivingCreatureStates(captureCreatureState)
            );
        }

        /// <summary>Captures all currently open generated-door IDs in deterministic order.</summary>
        /// <returns>A copied stable-ID sequence suitable for the floor-state persistence contract.</returns>
        /// <exception cref="InvalidOperationException">The controller is not initialized.</exception>
        public IReadOnlyList<string> CaptureOpenDoorIds()
        {
            if (!IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon encounter runtime is not initialized."
                );

            SortedSet<string> captured = new(openDoorIds, StringComparer.Ordinal);
            foreach (DungeonDoorController door in doorsByCell.Values)
            {
                if (door.IsOpen)
                    captured.Add(door.StableId);
            }
            return Array.AsReadOnly(captured.ToArray());
        }

        /// <summary>Attempts to open the generated door occupying one grid cell.</summary>
        /// <param name="doorCell">The cell clicked or otherwise selected by the player.</param>
        /// <returns>
        /// <see langword="true"/> when a closed door opens; otherwise <see langword="false"/>
        /// when there is no door, the actor is ineligible, or the interaction cannot be applied.
        /// </returns>
        /// <remarks>
        /// Opening is free for any adjacent living party member during exploration and costs the
        /// current living PC exactly one action in combat. Generated doors are open-only in V1.
        /// Any encounter room made reachable by the opened topology is revealed immediately.
        /// </remarks>
        public bool TryOpenDoor(DungeonCell doorCell)
        {
            if (
                !IsInitialized || !doorsByCell.TryGetValue(doorCell, out DungeonDoorController door)
            )
            {
                return false;
            }

            DungeonDoorInteractionMode mode = combatManager.IsCombatActive
                ? DungeonDoorInteractionMode.Combat
                : DungeonDoorInteractionMode.Exploration;
            bool partyActionInProgress = party.Any(member =>
                member != null && member.IsTakingAction
            );
            ActionController actor =
                mode == DungeonDoorInteractionMode.Exploration
                    ? partyActionInProgress
                        ? null
                        : party.FirstOrDefault(member =>
                            CanObserve(member) && IsCardinallyAdjacent(member, door.Cell)
                        )
                    : combatManager.WhosTurn()?.GetComponent<ActionController>();
            bool actorIsPartyMember = actor != null && party.Contains(actor);
            bool actorIsAlive = actorIsPartyMember && CanObserve(actor);
            if (
                actor == null
                || !actorIsPartyMember
                || !actorIsAlive
                || actor.IsTakingAction
                || mode == DungeonDoorInteractionMode.Combat && !actor.HasTurnAuthority
            )
            {
                return false;
            }

            Vector3Int actorPosition = Vector3Int.RoundToInt(actor.transform.position);
            DungeonDoorInteractionDecision decision = DungeonDoorInteractionPolicy.Evaluate(
                new DungeonDoorInteractionRequest(
                    mode,
                    new DungeonCell(actorPosition.x, actorPosition.z),
                    door.Cell,
                    door.IsOpen
                )
            );
            if (!decision.IsAllowed)
                return false;
            if (mode == DungeonDoorInteractionMode.Combat)
            {
                if (
                    !actor.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId creature
                    )
                    || bridge.Dispatch(new InteractActionOp(creature))
                        is not ResolvedOpResult<InteractOutcome>
                )
                    return false;
            }
            if (!door.TryOpen())
                return false;
            combatManager.RefreshRulesTopology();
            openDoorIds.Add(door.StableId);
            EnterReachableEncounterRooms();
            DoorOpened(door.StableId);
            PersistentStateChanged();
            return true;
        }

        /// <summary>Attempts to select one living party member as the exploration leader.</summary>
        /// <param name="candidate">The party controller requested by the player.</param>
        /// <returns>
        /// <see langword="true"/> when the candidate is now the leader; otherwise
        /// <see langword="false"/> while combat, another party action, or an invalid candidate
        /// prevents the change.
        /// </returns>
        public bool TrySelectExplorationLeader(ActionController candidate)
        {
            if (
                !IsInitialized
                || combatManager.IsCombatActive
                || !party.Contains(candidate)
                || !CanObserve(candidate)
                || party.Any(member => member != null && member.IsTakingAction)
            )
            {
                return false;
            }

            foreach (ActionController partyMember in party)
            {
                if (partyMember != null)
                    partyMember.SetDungeonExploration(partyMember == candidate);
            }
            selectedLeader = candidate;
            return true;
        }

        private void Initialize(
            DungeonLevelDocument document,
            DungeonEncounterCreatureCatalog catalog,
            CombatManagerInterface manager,
            IEnumerable<ActionController> partyMembers,
            IDungeonExplorationPresentation presenter,
            DungeonEncounterStateMachine lifecycle,
            IEnumerable<DungeonCreatureRuntimeState> restoredCreatures
        )
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "The dungeon encounter runtime can only be initialized once."
                );
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));
            if (partyMembers == null)
                throw new ArgumentNullException(nameof(partyMembers));
            if (presenter == null)
                throw new ArgumentNullException(nameof(presenter));

            DungeonRoom[] copiedRooms = document.Rooms.ToArray();
            ActionController[] copiedParty = partyMembers.ToArray();
            HashSet<int> copiedEncounterRoomIds = new(
                document.EncounterPlans.Select(plan => plan.RoomId)
            );
            HashSet<int> documentedRoomIds = new(copiedRooms.Select(room => room.Id));
            if (!copiedEncounterRoomIds.IsSubsetOf(documentedRoomIds))
            {
                throw new ArgumentException(
                    "Every encounter plan must reference a room in the source document.",
                    nameof(document)
                );
            }

            DungeonEncounterMaterializer materializer = new(
                catalog,
                new JsonDungeonEncounterCreatureFactory(),
                new UnityDungeonEncounterRuntimeRegistration(),
                restoredCreatures,
                document.Generation.Depth
            );
            DungeonEncounterDirector createdDirector = new(
                lifecycle,
                copiedParty,
                manager,
                materializer,
                transform,
                copiedRooms
            );

            rooms = copiedRooms;
            party = copiedParty;
            encounterRoomIds = copiedEncounterRoomIds;
            combatManager = manager;
            explorationPresentation = presenter;
            director = createdDirector;
            director.EncounterLifecycleChanged += OnEncounterLifecycleChanged;
            director.CreatureDefeated += OnCreatureDefeated;
            BindGeneratedDoors(document);
            combatManager.CombatActivityChanged += OnCombatActivityChanged;
            IsInitialized = true;
            OnCombatActivityChanged(combatManager.IsCombatActive);
        }

        private void Update()
        {
            if (!IsInitialized)
                return;

            livingPartyBuffer.Clear();
            livingPartySet.Clear();
            stalePartyObservations.Clear();
            occupiedRooms.Clear();
            currentRoomByParty.Clear();
            livingPartyPositions.Clear();

            bool partyActionInProgress = false;
            foreach (ActionController partyMember in party)
            {
                if (!CanObserve(partyMember))
                    continue;

                livingPartyBuffer.Add(partyMember);
                livingPartySet.Add(partyMember);
                partyActionInProgress |= partyMember.IsTakingAction;
                Vector3 position = partyMember.transform.position;
                livingPartyPositions.Add(position);
                int currentRoom = FindRoomId(position);
                if (currentRoom <= 0)
                    continue;

                currentRoomByParty.Add(partyMember, currentRoom);
                occupiedRooms.Add(currentRoom);
                doorRevealedEncounterRooms.Remove(currentRoom);
                bool roomChanged =
                    !previousRoomByParty.TryGetValue(partyMember, out int previousRoom)
                    || previousRoom != currentRoom;
                if (roomChanged && encounterRoomIds.Contains(currentRoom))
                    pendingEncounterRooms.Add(currentRoom);
            }

            foreach (ActionController observed in previousRoomByParty.Keys)
            {
                if (!livingPartySet.Contains(observed))
                    stalePartyObservations.Add(observed);
            }
            foreach (ActionController stale in stalePartyObservations)
                previousRoomByParty.Remove(stale);

            if (
                !combatManager.IsCombatActive
                && (
                    !presentedExplorationParty.SequenceEqual(livingPartyBuffer)
                    || !livingPartySet.Contains(selectedLeader)
                )
            )
            {
                PresentExploration(livingPartyBuffer.ToArray());
            }

            // Sample each movement frame so a room crossed during one Stride is not lost, but do
            // not reset turn state until the movement coroutine has completed its own cleanup.
            if (partyActionInProgress)
            {
                CommitRoomObservations(livingPartyBuffer, currentRoomByParty);
                return;
            }

            foreach (int roomId in pendingEncounterRooms)
                director.EnterRoom(roomId);
            pendingEncounterRooms.Clear();
            director.ResumeReachedSuspendedEncounters(livingPartyPositions);

            // Commit observations only after all room-entry work succeeds. A failed materialization
            // therefore retries instead of permanently consuming the room transition.
            CommitRoomObservations(livingPartyBuffer, currentRoomByParty);

            if (
                livingPartyBuffer.Count > 0
                && director.Lifecycle.HasActiveEncounters
                && combatManager.IsCombatActive
            )
            {
                doorRevealedEncounterRooms.RemoveWhere(roomId =>
                    director.Lifecycle.GetRoomEncounter(roomId).State
                    != DungeonEncounterGroupState.Active
                );
                occupiedRooms.UnionWith(doorRevealedEncounterRooms);
                director.EvaluatePartyRegions(livingPartyBuffer.Count, occupiedRooms);
            }
        }

        private void OnDestroy()
        {
            ResetRuntime(destroyEncounterActors: false);
        }

        /// <summary>
        /// Releases one floor's bindings so the same component can initialize the next floor.
        /// </summary>
        /// <remarks>
        /// The reusable dungeon scene keeps this controller component alive across depth changes.
        /// Encounter actors are owned by the floor runtime and are therefore removed here, while
        /// authored party actors remain available for placement on the destination floor.
        /// </remarks>
        internal void ResetForFloorTransition()
        {
            ResetRuntime(destroyEncounterActors: true);
        }

        private void ResetRuntime(bool destroyEncounterActors)
        {
            if (combatManager != null)
                combatManager.CombatActivityChanged -= OnCombatActivityChanged;
            if (gridInput != null)
                gridInput.CellClicked -= OnGridCellClicked;
            if (grid != null)
                grid.UnbindExplorationStrideCoordinator(this);
            if (director != null)
            {
                director.EncounterLifecycleChanged -= OnEncounterLifecycleChanged;
                director.CreatureDefeated -= OnCreatureDefeated;
            }
            foreach (ActionController partyMember in party)
            {
                if (partyMember != null)
                    partyMember.SetDungeonExploration(false);
            }
            director?.Dispose();

            foreach (
                DungeonEncounterMember member in GetComponentsInChildren<DungeonEncounterMember>(
                    includeInactive: true
                )
            )
            {
                if (member == null)
                    continue;

                ActionController controller = member.GetComponent<ActionController>();
                if (controller != null && combatManager != null)
                    combatManager.Remove(controller);

                Token token = member.GetComponent<Token>();
                if (token != null && grid != null)
                {
                    if (token.IsRegistered)
                        grid.DestroyToken(member.gameObject);
                    token.DetachFromGrid(grid);
                }

                if (destroyEncounterActors)
                {
                    member.gameObject.SetActive(false);
                    member.transform.SetParent(null, true);
                    Destroy(member.gameObject);
                }
            }

            rooms = Array.Empty<DungeonRoom>();
            party = Array.Empty<ActionController>();
            encounterRoomIds.Clear();
            pendingEncounterRooms.Clear();
            previousRoomByParty.Clear();
            livingPartyBuffer.Clear();
            livingPartySet.Clear();
            stalePartyObservations.Clear();
            occupiedRooms.Clear();
            doorRevealedEncounterRooms.Clear();
            currentRoomByParty.Clear();
            livingPartyPositions.Clear();
            doorsByCell.Clear();
            openDoorIds.Clear();
            presentedExplorationParty = Array.Empty<ActionController>();
            selectedLeader = null;
            director = null;
            combatManager = null;
            explorationPresentation = null;
            gridInput = null;
            grid = null;
            explorationMovement = NoExplorationStrideCoordinator.Instance;
            IsInitialized = false;
        }

        private void BindGeneratedDoors(DungeonLevelDocument document)
        {
            Dictionary<string, DungeonDoor> documentedDoors = document.Doors.ToDictionary(
                door => door.Id,
                StringComparer.Ordinal
            );
            foreach (DungeonDoor door in document.Doors)
            {
                if (door.IsOpen)
                    openDoorIds.Add(door.Id);
            }

            Map map = GetComponentInParent<Map>();
            if (map == null)
                return;

            DungeonDoorController[] controllers =
                map.GetComponentsInChildren<DungeonDoorController>(true);
            if (controllers.Length != documentedDoors.Count)
            {
                throw new InvalidOperationException(
                    "Generated door components must exactly cover the dungeon document."
                );
            }
            foreach (DungeonDoorController controller in controllers)
            {
                if (
                    controller == null
                    || !documentedDoors.TryGetValue(controller.StableId, out DungeonDoor documented)
                    || documented.Cell != controller.Cell
                    || documented.IsOpen != controller.IsOpen
                )
                {
                    throw new InvalidOperationException(
                        "Every generated door component must match its documented identity, cell, and state."
                    );
                }
                if (!doorsByCell.TryAdd(controller.Cell, controller))
                {
                    throw new InvalidOperationException(
                        "Generated dungeon doors must occupy unique cells."
                    );
                }
                if (controller.IsOpen)
                    openDoorIds.Add(controller.StableId);
            }

            gridInput = map.GetComponent<GridInput>();
            if (gridInput == null)
                throw new InvalidOperationException(
                    "A generated dungeon map requires GridInput for door interaction."
                );
            gridInput.CellClicked += OnGridCellClicked;

            grid = map.GetComponent<GridBase>();
            if (grid == null || !grid.IsInitialized)
                throw new InvalidOperationException(
                    "A generated dungeon map requires an initialized GridBase for exploration."
                );
            explorationMovement = new DungeonExplorationMovementRuntime(
                party,
                () => selectedLeader,
                () => combatManager.IsCombatActive,
                CanObserve,
                ProcessImmediateExplorationBoundary
            );
            grid.BindExplorationStrideCoordinator(this);
        }

        private void OnGridCellClicked(Vector3Int cell)
        {
            TryOpenDoor(new DungeonCell(cell.x, cell.z));
        }

        bool IExplorationStrideCoordinator.Handles(GameObject character) =>
            IsInitialized && explorationMovement.Handles(character);

        IEnumerator IExplorationStrideCoordinator.ProjectCommittedStep(
            GameObject leader,
            Vector3Int from,
            Vector3Int destination,
            Tile[,] tiles,
            TokenMovement movement,
            Ref<bool> continuePath
        ) =>
            explorationMovement.ProjectCommittedStep(
                leader,
                from,
                destination,
                tiles,
                movement,
                continuePath
            );

        private bool ProcessImmediateExplorationBoundary()
        {
            if (combatManager.IsCombatActive)
                return true;

            ActionController[] livingParty = party.Where(CanObserve).ToArray();
            Dictionary<ActionController, int> currentRooms = new();
            List<Vector3> positions = new(livingParty.Length);
            SortedSet<int> roomsToEnter = new(pendingEncounterRooms);
            foreach (ActionController partyMember in livingParty)
            {
                Vector3 position = partyMember.transform.position;
                positions.Add(position);
                int roomId = FindRoomId(position);
                if (roomId > 0)
                    currentRooms.Add(partyMember, roomId);
            }

            foreach (ActionController partyMember in livingParty)
            {
                if (!currentRooms.TryGetValue(partyMember, out int roomId))
                    continue;
                bool entered =
                    !previousRoomByParty.TryGetValue(partyMember, out int previousRoom)
                    || previousRoom != roomId;
                if (entered && encounterRoomIds.Contains(roomId))
                    roomsToEnter.Add(roomId);
            }

            foreach (int roomId in roomsToEnter)
            {
                director.EnterRoom(roomId);
                if (combatManager.IsCombatActive)
                    break;
            }
            pendingEncounterRooms.Clear();
            if (!combatManager.IsCombatActive)
                director.ResumeReachedSuspendedEncounters(positions);

            CommitRoomObservations(livingParty, currentRooms);
            return combatManager.IsCombatActive;
        }

        private void OnCombatActivityChanged(bool isActive)
        {
            ActionController[] livingParty = party.Where(CanObserve).ToArray();
            if (isActive || livingParty.Length == 0)
            {
                foreach (ActionController partyMember in party)
                {
                    if (partyMember != null)
                        partyMember.SetDungeonExploration(false);
                }
                presentedExplorationParty = Array.Empty<ActionController>();
                explorationPresentation.HideExploration();
                return;
            }

            PresentExploration(livingParty);
        }

        private void PresentExploration(ActionController[] livingParty)
        {
            foreach (ActionController partyMember in party)
            {
                if (partyMember != null)
                    partyMember.SetDungeonExploration(false);
            }

            ActionController leader = livingParty.Contains(selectedLeader)
                ? selectedLeader
                : livingParty.FirstOrDefault();
            if (leader == null)
            {
                selectedLeader = null;
                presentedExplorationParty = Array.Empty<ActionController>();
                explorationPresentation.HideExploration();
                return;
            }

            leader.SetDungeonExploration(true);
            selectedLeader = leader;
            presentedExplorationParty = livingParty.ToArray();
            explorationPresentation.ShowExploration(
                livingParty,
                selectedLeader,
                TrySelectExplorationLeader
            );
        }

        private void CommitRoomObservations(
            IReadOnlyList<ActionController> livingParty,
            IReadOnlyDictionary<ActionController, int> currentRoomByParty
        )
        {
            for (int index = 0; index < livingParty.Count; index++)
            {
                ActionController partyMember = livingParty[index];
                if (currentRoomByParty.TryGetValue(partyMember, out int currentRoom))
                    previousRoomByParty[partyMember] = currentRoom;
                else
                    previousRoomByParty.Remove(partyMember);
            }
        }

        private int FindRoomId(Vector3 worldPosition)
        {
            int x = Mathf.RoundToInt(worldPosition.x);
            int z = Mathf.RoundToInt(worldPosition.z);
            foreach (DungeonRoom room in rooms)
            {
                if (
                    x >= room.MinimumX
                    && x <= room.MaximumX
                    && z >= room.MinimumZ
                    && z <= room.MaximumZ
                )
                {
                    return room.Id;
                }
            }
            return 0;
        }

        private static bool IsCardinallyAdjacent(ActionController controller, DungeonCell target)
        {
            Vector3Int position = Vector3Int.RoundToInt(controller.transform.position);
            long xDistance = Math.Abs((long)position.x - target.X);
            long zDistance = Math.Abs((long)position.z - target.Z);
            return xDistance + zDistance == 1;
        }

        private void OnEncounterLifecycleChanged() => PersistentStateChanged();

        private void OnCreatureDefeated(DungeonCreatureDefeatResult _) => PersistentStateChanged();

        private static bool CanObserve(ActionController controller)
        {
            if (
                controller == null
                || !controller.gameObject.activeSelf
                || !controller.isActiveAndEnabled
            )
                return false;
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            return creature == null || !creature.IsDefeated;
        }

        // Opening a door changes the party's connected exploration area immediately. Flood the
        // live topology so every newly visible encounter room materializes before a PC enters it.

        private void EnterReachableEncounterRooms()
        {
            Map map = GetComponentInParent<Map>();
            if (map == null)
                return;

            TileType[,] topology = map.GetMapData();
            HashSet<DungeonCell> reached = new();
            Queue<DungeonCell> pending = new();
            foreach (ActionController partyMember in party.Where(CanObserve))
            {
                Vector3Int position = Vector3Int.RoundToInt(partyMember.transform.position);
                DungeonCell cell = new(position.x, position.z);
                if (
                    IsInBounds(topology, cell)
                    && IsExplorationTraversalCell(topology[cell.X, cell.Z])
                    && reached.Add(cell)
                )
                {
                    pending.Enqueue(cell);
                }
            }

            while (pending.Count > 0)
            {
                DungeonCell current = pending.Dequeue();
                foreach (DungeonCell neighbor in CardinalNeighbors(current))
                {
                    if (
                        IsInBounds(topology, neighbor)
                        && IsExplorationTraversalCell(topology[neighbor.X, neighbor.Z])
                        && reached.Add(neighbor)
                    )
                    {
                        pending.Enqueue(neighbor);
                    }
                }
            }

            foreach (
                DungeonRoom room in rooms
                    .Where(room => encounterRoomIds.Contains(room.Id))
                    .OrderBy(room => room.Id)
            )
            {
                if (!RoomIntersects(room, reached))
                    continue;

                DungeonRoomEntryResult result = director.EnterRoom(room.Id);
                if (result.StartsCombat)
                {
                    // The normal room-occupancy suspension rule must not undo a door-triggered
                    // engagement before either side can act. Once a PC enters the revealed room,
                    // ordinary encounter-region suspension owns the fight again.
                    doorRevealedEncounterRooms.Add(room.Id);
                }
            }
        }

        private static IEnumerable<DungeonCell> CardinalNeighbors(DungeonCell cell)
        {
            yield return new DungeonCell(cell.X, cell.Z + 1);
            yield return new DungeonCell(cell.X + 1, cell.Z);
            yield return new DungeonCell(cell.X, cell.Z - 1);
            yield return new DungeonCell(cell.X - 1, cell.Z);
        }

        private static bool IsInBounds(TileType[,] topology, DungeonCell cell) =>
            cell.X >= 0
            && cell.Z >= 0
            && cell.X < topology.GetLength(0)
            && cell.Z < topology.GetLength(1);

        private static bool IsExplorationTraversalCell(TileType tile) =>
            tile == TileType.Ground || tile == TileType.Door;

        private static bool RoomIntersects(DungeonRoom room, ISet<DungeonCell> reached)
        {
            for (int x = room.MinimumX; x <= room.MaximumX; x++)
            {
                for (int z = room.MinimumZ; z <= room.MaximumZ; z++)
                {
                    if (reached.Contains(new DungeonCell(x, z)))
                        return true;
                }
            }

            return false;
        }
    }
}
