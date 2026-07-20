using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>Displays movement-only controls while a party explores between encounters.</summary>
    public interface IDungeonExplorationPresentation
    {
        /// <summary>Shows movement controls for one selected living party member.</summary>
        /// <param name="party">All living party controllers available for selection.</param>
        /// <param name="selected">The party controller whose movement controls are shown.</param>
        void ShowExploration(IReadOnlyList<ActionController> party, ActionController selected);

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
    public sealed class DungeonEncounterRuntimeController : MonoBehaviour
    {
        private DungeonRoom[] rooms = Array.Empty<DungeonRoom>();
        private ActionController[] party = Array.Empty<ActionController>();
        private HashSet<int> encounterRoomIds = new();
        private readonly SortedSet<int> pendingEncounterRooms = new();
        private readonly Dictionary<ActionController, int> previousRoomByParty = new();
        private DungeonEncounterDirector director;
        private CombatManagerInterface combatManager;
        private IDungeonExplorationPresentation explorationPresentation;

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
                restoredCreatures
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
            combatManager.CombatActivityChanged += OnCombatActivityChanged;
            IsInitialized = true;
            OnCombatActivityChanged(combatManager.IsCombatActive);
        }

        private void Update()
        {
            if (!IsInitialized)
                return;

            ActionController[] livingParty = party.Where(CanObserve).ToArray();
            HashSet<ActionController> livingPartySet = new(livingParty);
            foreach (
                ActionController missing in previousRoomByParty
                    .Keys.Where(controller => !livingPartySet.Contains(controller))
                    .ToArray()
            )
            {
                previousRoomByParty.Remove(missing);
            }

            SortedSet<int> enteredEncounterRooms = new();
            HashSet<int> occupiedRooms = new();
            Dictionary<ActionController, int> currentRoomByParty = new();
            foreach (ActionController partyMember in livingParty)
            {
                int currentRoom = FindRoomId(partyMember.transform.position);
                if (currentRoom <= 0)
                    continue;

                currentRoomByParty.Add(partyMember, currentRoom);
                occupiedRooms.Add(currentRoom);
                bool roomChanged =
                    !previousRoomByParty.TryGetValue(partyMember, out int previousRoom)
                    || previousRoom != currentRoom;
                if (roomChanged && encounterRoomIds.Contains(currentRoom))
                    enteredEncounterRooms.Add(currentRoom);
            }

            pendingEncounterRooms.UnionWith(enteredEncounterRooms);

            // Sample each movement frame so a room crossed during one Stride is not lost, but do
            // not reset turn state until the movement coroutine has completed its own cleanup.
            if (livingParty.Any(controller => controller.IsTakingAction))
            {
                CommitRoomObservations(livingParty, currentRoomByParty);
                return;
            }

            foreach (int roomId in pendingEncounterRooms)
                director.EnterRoom(roomId);
            pendingEncounterRooms.Clear();
            director.ResumeReachedSuspendedEncounters(
                livingParty.Select(controller => controller.transform.position)
            );

            // Commit observations only after all room-entry work succeeds. A failed materialization
            // therefore retries instead of permanently consuming the room transition.
            CommitRoomObservations(livingParty, currentRoomByParty);

            if (
                livingParty.Length > 0
                && director.Lifecycle.HasActiveEncounters
                && combatManager.IsCombatActive
            )
            {
                director.EvaluatePartyRegions(livingParty.Length, occupiedRooms);
            }
        }

        private void OnDestroy()
        {
            if (combatManager != null)
                combatManager.CombatActivityChanged -= OnCombatActivityChanged;
            foreach (ActionController partyMember in party)
            {
                if (partyMember != null)
                    partyMember.SetDungeonExploration(false);
            }
            director?.Dispose();
            IsInitialized = false;
        }

        private void OnCombatActivityChanged(bool isActive)
        {
            ActionController[] livingParty = party.Where(CanObserve).ToArray();
            foreach (ActionController partyMember in party)
            {
                if (partyMember != null)
                    partyMember.SetDungeonExploration(false);
            }
            if (!isActive)
            {
                foreach (ActionController partyMember in livingParty)
                    partyMember.SetDungeonExploration(true);
            }

            if (isActive || livingParty.Length == 0)
            {
                explorationPresentation.HideExploration();
                return;
            }

            explorationPresentation.ShowExploration(livingParty, livingParty[0]);
        }

        private void CommitRoomObservations(
            IReadOnlyList<ActionController> livingParty,
            IReadOnlyDictionary<ActionController, int> currentRoomByParty
        )
        {
            foreach (ActionController partyMember in livingParty)
            {
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
    }
}
