using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.DungeonGeneration;
using GridPrivate;
using GridPublic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Combat.Encounters
{
    /// <summary>Creates and destroys one catalog-backed runtime creature root.</summary>
    public interface IDungeonEncounterCreatureFactory
    {
        /// <summary>Instantiates and applies JSON to one creature at its final grid position.</summary>
        /// <param name="definition">The validated catalog definition.</param>
        /// <param name="worldPosition">The final world position visible to Awake registration.</param>
        /// <param name="worldRotation">The final world rotation visible to Awake registration.</param>
        /// <param name="parent">The required encounter-owned parent.</param>
        /// <returns>A non-null creature root parented and positioned exactly as requested.</returns>
        GameObject Create(
            DungeonEncounterCreatureCatalogEntry definition,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform parent
        );

        /// <summary>Immediately rolls back one instance previously returned by <see cref="Create"/>.</summary>
        /// <param name="instance">The non-null creature root to remove.</param>
        void Destroy(GameObject instance);
    }

    /// <summary>Creates encounter creatures from their Resources JSON and existing prefabs.</summary>
    public sealed class JsonDungeonEncounterCreatureFactory : IDungeonEncounterCreatureFactory
    {
        /// <inheritdoc/>
        public GameObject Create(
            DungeonEncounterCreatureCatalogEntry definition,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform parent
        )
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (definition.Prefab == null || definition.Prefab.GetComponent<Team>() == null)
                throw new InvalidOperationException(
                    $"Encounter creature '{definition.ContentId}' requires a prefab with a root Team."
                );

            GameObject instance = null;
            try
            {
                instance = CreatureJsonConverter.CreateFromFile(
                    definition.ResourcePath,
                    definition.Prefab,
                    worldPosition,
                    worldRotation,
                    parent
                );
                Team team = instance.GetComponent<Team>();
                if (team == null)
                    throw new InvalidOperationException(
                        $"Encounter creature prefab '{definition.Prefab.name}' lost its root Team during creation."
                    );
                team.Name = DungeonEncounterCreatureCatalog.HostileTeamName;
                if (TeamRules.TryGetInstance(out TeamRules rules) && !rules.Contains(team.Name))
                {
                    rules.AddHostileTeam(team.Name);
                    rules.OneWayFriendly(team.Name, team.Name);
                }
                return instance;
            }
            catch
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
                throw;
            }
        }

        /// <inheritdoc/>
        public void Destroy(GameObject instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// Validates planned spawn cells and owns external runtime registrations during rollback.
    /// </summary>
    public interface IDungeonEncounterRuntimeRegistration
    {
        /// <summary>Finds the nearest walkable, unoccupied, unreserved cell in a room.</summary>
        /// <param name="preferred">The encounter plan's preferred spawn cell.</param>
        /// <param name="room">The inclusive room bounds that constrain fallback selection.</param>
        /// <param name="reserved">Cells already selected for this materialization transaction.</param>
        /// <returns>The deterministic available cell nearest to <paramref name="preferred"/>.</returns>
        DungeonCell ResolveAvailable(
            DungeonCell preferred,
            DungeonRoom room,
            IReadOnlyCollection<DungeonCell> reserved
        );

        /// <summary>Requires one planned spawn cell to be walkable and unoccupied.</summary>
        /// <param name="cell">The cell that will be used for a new creature.</param>
        void RequireAvailable(DungeonCell cell);

        /// <summary>Validates registrations performed by the created creature's lifecycle.</summary>
        /// <param name="instance">The newly created creature root.</param>
        /// <param name="cell">The cell at which the creature was created.</param>
        void ValidateCreated(GameObject instance, DungeonCell cell);

        /// <summary>Removes combat and grid registrations before an instance is destroyed.</summary>
        /// <param name="instance">The created creature being rolled back.</param>
        void Rollback(GameObject instance);
    }

    /// <summary>Connects encounter materialization to the active combat manager and grid.</summary>
    public sealed class UnityDungeonEncounterRuntimeRegistration
        : IDungeonEncounterRuntimeRegistration
    {
        /// <inheritdoc/>
        public DungeonCell ResolveAvailable(
            DungeonCell preferred,
            DungeonRoom room,
            IReadOnlyCollection<DungeonCell> reserved
        )
        {
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            if (reserved == null)
                throw new ArgumentNullException(nameof(reserved));

            DungeonCell[] candidates = Enumerable
                .Range(room.MinimumX, room.MaximumX - room.MinimumX + 1)
                .SelectMany(x =>
                    Enumerable
                        .Range(room.MinimumZ, room.MaximumZ - room.MinimumZ + 1)
                        .Select(z => new DungeonCell(x, z))
                )
                .OrderBy(cell => Math.Abs(cell.X - preferred.X) + Math.Abs(cell.Z - preferred.Z))
                .ThenBy(cell => cell.Z)
                .ThenBy(cell => cell.X)
                .ToArray();

            if (!GridAPI.TryGetInstance(out GridAPI grid))
            {
                if (!reserved.Contains(preferred))
                    return preferred;
                foreach (DungeonCell candidate in candidates)
                {
                    if (!reserved.Contains(candidate))
                        return candidate;
                }
                throw new InvalidOperationException(
                    $"Encounter room {room.Id} has no unreserved spawn cell."
                );
            }
            if (grid is not GridAPIPrivate privateGrid)
                throw new InvalidOperationException(
                    "The active grid does not expose its runtime tile data."
                );

            Tile[,] tiles = privateGrid.GetTiles();
            if (IsAvailable(tiles, preferred, reserved))
                return preferred;
            foreach (DungeonCell candidate in candidates)
            {
                if (!IsAvailable(tiles, candidate, reserved))
                    continue;
                return candidate;
            }

            throw new InvalidOperationException(
                $"Encounter room {room.Id} has no walkable, unoccupied spawn cell."
            );
        }

        private static bool IsAvailable(
            Tile[,] tiles,
            DungeonCell cell,
            IReadOnlyCollection<DungeonCell> reserved
        )
        {
            Vector3Int position = new(cell.X, 0, cell.Z);
            return !reserved.Contains(cell)
                && GridTargeting.IsInBounds(tiles, position)
                && tiles[cell.X, cell.Z] != null
                && tiles[cell.X, cell.Z].Occupants.Count == 0;
        }

        /// <inheritdoc/>
        public void RequireAvailable(DungeonCell cell)
        {
            if (!GridAPI.TryGetInstance(out GridAPI grid))
                return;
            if (grid is not GridAPIPrivate privateGrid)
                throw new InvalidOperationException(
                    "The active grid does not expose its runtime tile data."
                );

            Tile[,] tiles = privateGrid.GetTiles();
            Vector3Int position = new(cell.X, 0, cell.Z);
            if (!GridTargeting.IsInBounds(tiles, position) || tiles[cell.X, cell.Z] == null)
            {
                throw new InvalidOperationException(
                    $"Encounter spawn cell ({cell.X}, {cell.Z}) is not walkable on the active grid."
                );
            }
            if (tiles[cell.X, cell.Z].Occupants.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Encounter spawn cell ({cell.X}, {cell.Z}) is already occupied."
                );
            }
        }

        /// <inheritdoc/>
        public void ValidateCreated(GameObject instance, DungeonCell cell)
        {
            if (!GridAPI.TryGetInstance(out _))
                return;
            Token token = instance.GetComponent<Token>();
            if (token == null || !token.IsRegistered)
            {
                throw new InvalidOperationException(
                    $"Materialized creature '{instance.name}' did not register at encounter spawn cell ({cell.X}, {cell.Z})."
                );
            }
        }

        /// <inheritdoc/>
        public void Rollback(GameObject instance)
        {
            ActionController controller = instance.GetComponent<ActionController>();
            if (
                controller != null
                && CombatManagerInterface.TryGetInstance(out CombatManagerInterface combatManager)
            )
            {
                combatManager.Remove(controller);
            }

            Token token = instance.GetComponent<Token>();
            if (token != null && token.IsRegistered && GridAPI.TryGetInstance(out GridAPI grid))
            {
                grid.DestroyToken(instance);
            }
        }
    }

    /// <summary>Owns aligned immutable materialization results in encounter-plan order.</summary>
    public sealed class DungeonEncounterMaterialization
    {
        internal DungeonEncounterMaterialization(
            IEnumerable<DungeonEncounterMember> members,
            IEnumerable<ActionController> controllers
        )
        {
            Members = Array.AsReadOnly(members.ToArray());
            Controllers = Array.AsReadOnly(controllers.ToArray());
        }

        /// <summary>Gets configured encounter members in encounter-plan order.</summary>
        public IReadOnlyList<DungeonEncounterMember> Members { get; }

        /// <summary>Gets the corresponding action controllers in encounter-plan order.</summary>
        public IReadOnlyList<ActionController> Controllers { get; }
    }

    /// <summary>
    /// Materializes one complete encounter plan transactionally from a validated runtime catalog.
    /// </summary>
    public sealed class DungeonEncounterMaterializer
    {
        private readonly DungeonEncounterCreatureCatalog catalog;
        private readonly IDungeonEncounterCreatureFactory factory;
        private readonly IDungeonEncounterRuntimeRegistration runtimeRegistration;
        private readonly IReadOnlyDictionary<string, DungeonCreatureRuntimeState> restoredCreatures;

        /// <summary>Creates a materializer with an injectable creature lifecycle boundary.</summary>
        /// <param name="catalog">The required runtime creature catalog.</param>
        /// <param name="factory">The required creator and rollback owner.</param>
        /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
        public DungeonEncounterMaterializer(
            DungeonEncounterCreatureCatalog catalog,
            IDungeonEncounterCreatureFactory factory
        )
            : this(
                catalog,
                factory,
                new UnityDungeonEncounterRuntimeRegistration(),
                Array.Empty<DungeonCreatureRuntimeState>()
            ) { }

        /// <summary>Creates a materializer with injectable creature and registration boundaries.</summary>
        /// <param name="catalog">The required runtime creature catalog.</param>
        /// <param name="factory">The required creator and destruction owner.</param>
        /// <param name="runtimeRegistration">
        /// The required grid/combat validation and rollback boundary.
        /// </param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public DungeonEncounterMaterializer(
            DungeonEncounterCreatureCatalog catalog,
            IDungeonEncounterCreatureFactory factory,
            IDungeonEncounterRuntimeRegistration runtimeRegistration
        )
            : this(
                catalog,
                factory,
                runtimeRegistration,
                Array.Empty<DungeonCreatureRuntimeState>()
            ) { }

        /// <summary>Creates a materializer that can restore live creature cell, HP, and child state.</summary>
        /// <param name="catalog">The required runtime creature catalog.</param>
        /// <param name="factory">The required creator and destruction owner.</param>
        /// <param name="runtimeRegistration">The required grid/combat registration boundary.</param>
        /// <param name="restoredCreatures">Unique persisted live creatures keyed by stable instance ID.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        /// <exception cref="ArgumentException">Restored creatures contain null or duplicate IDs.</exception>
        public DungeonEncounterMaterializer(
            DungeonEncounterCreatureCatalog catalog,
            IDungeonEncounterCreatureFactory factory,
            IDungeonEncounterRuntimeRegistration runtimeRegistration,
            IEnumerable<DungeonCreatureRuntimeState> restoredCreatures
        )
        {
            this.catalog =
                catalog != null ? catalog : throw new ArgumentNullException(nameof(catalog));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.runtimeRegistration =
                runtimeRegistration ?? throw new ArgumentNullException(nameof(runtimeRegistration));
            if (restoredCreatures == null)
                throw new ArgumentNullException(nameof(restoredCreatures));
            DungeonCreatureRuntimeState[] copiedRestoredCreatures = restoredCreatures.ToArray();
            if (copiedRestoredCreatures.Any(creature => creature == null))
                throw new ArgumentException(
                    "Restored creatures cannot contain null.",
                    nameof(restoredCreatures)
                );
            if (copiedRestoredCreatures.Any(creature => creature.HitPoints <= 0))
                throw new ArgumentException(
                    "Restored live creatures must have positive hit points.",
                    nameof(restoredCreatures)
                );
            if (
                copiedRestoredCreatures
                    .Select(creature => creature.InstanceId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != copiedRestoredCreatures.Length
            )
            {
                throw new ArgumentException(
                    "Restored creature instance IDs must be unique.",
                    nameof(restoredCreatures)
                );
            }
            this.restoredCreatures = copiedRestoredCreatures.ToDictionary(
                creature => creature.InstanceId,
                StringComparer.Ordinal
            );
        }

        /// <summary>Creates every planned creature at its ordered unit-grid spawn cell.</summary>
        /// <param name="plan">The unresolved, internally consistent encounter plan.</param>
        /// <param name="root">The non-null parent that owns all created creature roots.</param>
        /// <returns>Aligned member and controller views in plan order.</returns>
        /// <exception cref="ArgumentNullException">The plan or root is null.</exception>
        /// <exception cref="ArgumentException">The plan has invalid identity or mismatched collections.</exception>
        /// <exception cref="InvalidOperationException">
        /// The plan is resolved, the catalog is invalid, or a factory result violates its contract.
        /// Every instance created earlier in the attempt is rolled back before the exception escapes.
        /// </exception>
        public DungeonEncounterMaterialization Materialize(
            DungeonEncounterPlan plan,
            Transform root
        )
        {
            ValidatePlan(plan, root);
            return Materialize(plan, root, Enumerable.Range(0, plan.CreatureIds.Count).ToArray());
        }

        /// <summary>
        /// Creates the living members of a lifecycle view while retaining their original stable
        /// plan indexes.
        /// </summary>
        /// <param name="encounter">The current lifecycle view for one unresolved encounter.</param>
        /// <param name="root">The non-null parent that owns all created creature roots.</param>
        /// <returns>Aligned living member and controller views in original plan order.</returns>
        /// <exception cref="ArgumentNullException">The encounter or root is null.</exception>
        /// <exception cref="InvalidOperationException">A cleared encounter cannot be materialized.</exception>
        public DungeonEncounterMaterialization Materialize(
            DungeonEncounterGroupView encounter,
            Transform root
        )
        {
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));
            if (encounter.State == DungeonEncounterGroupState.Cleared)
                throw new InvalidOperationException(
                    $"Cleared encounter '{encounter.Plan.Id}' cannot be materialized."
                );

            ValidatePlan(encounter.Plan, root);
            int[] livingPlanIndexes = encounter
                .Creatures.Select((creature, index) => new { creature, index })
                .Where(entry => !entry.creature.IsDefeated)
                .Select(entry => entry.index)
                .ToArray();
            return Materialize(encounter.Plan, root, livingPlanIndexes);
        }

        /// <summary>
        /// Creates the living members of a lifecycle view, relocating occupied planned spawns to
        /// the nearest deterministic available cells inside the encounter room.
        /// </summary>
        /// <param name="encounter">The current lifecycle view for one unresolved encounter.</param>
        /// <param name="root">The non-null parent that owns all created creature roots.</param>
        /// <param name="room">The source room that constrains spawn fallback.</param>
        /// <returns>Aligned living member and controller views in original plan order.</returns>
        public DungeonEncounterMaterialization Materialize(
            DungeonEncounterGroupView encounter,
            Transform root,
            DungeonRoom room
        )
        {
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));
            if (room == null)
                throw new ArgumentNullException(nameof(room));
            if (encounter.Plan.RoomId != room.Id)
                throw new ArgumentException(
                    $"Room {room.Id} does not own encounter '{encounter.Plan.Id}'.",
                    nameof(room)
                );
            if (encounter.State == DungeonEncounterGroupState.Cleared)
                throw new InvalidOperationException(
                    $"Cleared encounter '{encounter.Plan.Id}' cannot be materialized."
                );

            ValidatePlan(encounter.Plan, root);
            int[] livingPlanIndexes = encounter
                .Creatures.Select((creature, index) => new { creature, index })
                .Where(entry => !entry.creature.IsDefeated)
                .Select(entry => entry.index)
                .ToArray();
            List<DungeonCell> reserved = new(livingPlanIndexes.Length);
            DungeonCell[] resolvedCells = livingPlanIndexes
                .Select(index =>
                {
                    string instanceId = DungeonEncounterStateMachine.CreateCreatureInstanceId(
                        encounter.Plan.Id,
                        index
                    );
                    DungeonCell preferred = restoredCreatures.TryGetValue(
                        instanceId,
                        out DungeonCreatureRuntimeState restored
                    )
                        ? restored.Cell
                        : encounter.Plan.SpawnCells[index];
                    DungeonCell resolved = runtimeRegistration.ResolveAvailable(
                        preferred,
                        room,
                        reserved
                    );
                    reserved.Add(resolved);
                    return resolved;
                })
                .ToArray();
            return Materialize(encounter.Plan, root, livingPlanIndexes, resolvedCells, false);
        }

        private DungeonEncounterMaterialization Materialize(
            DungeonEncounterPlan plan,
            Transform root,
            IReadOnlyList<int> planIndexes
        )
        {
            DungeonCell[] plannedCells = planIndexes
                .Select(index => plan.SpawnCells[index])
                .ToArray();
            return Materialize(plan, root, planIndexes, plannedCells, true);
        }

        private DungeonEncounterMaterialization Materialize(
            DungeonEncounterPlan plan,
            Transform root,
            IReadOnlyList<int> planIndexes,
            IReadOnlyList<DungeonCell> spawnCells,
            bool requirePlannedCells
        )
        {
            catalog.ValidateOrThrow();

            DungeonEncounterCreatureCatalogEntry[] definitions = planIndexes
                .Select(index => catalog.Require(plan.CreatureIds[index]))
                .ToArray();
            if (requirePlannedCells)
            {
                foreach (DungeonCell spawnCell in spawnCells)
                    runtimeRegistration.RequireAvailable(spawnCell);
            }

            List<GameObject> created = new(definitions.Length);
            List<DungeonEncounterMember> members = new(definitions.Length);
            List<ActionController> controllers = new(definitions.Length);
            HashSet<GameObject> uniqueInstances = new();
            try
            {
                for (int index = 0; index < definitions.Length; index++)
                {
                    int planIndex = planIndexes[index];
                    DungeonCell cell = spawnCells[index];
                    Vector3 position = new(cell.X, 0f, cell.Z);
                    GameObject instance = factory.Create(
                        definitions[index],
                        position,
                        Quaternion.identity,
                        root
                    );
                    if (instance == null)
                        throw new InvalidOperationException(
                            $"The creature factory returned no instance for '{definitions[index].ContentId}'."
                        );
                    if (!uniqueInstances.Add(instance))
                        throw new InvalidOperationException(
                            $"The creature factory returned instance '{instance.name}' more than once."
                        );
                    created.Add(instance);
                    runtimeRegistration.ValidateCreated(instance, cell);
                    if (instance.transform.parent != root)
                        throw new InvalidOperationException(
                            $"The creature factory did not parent '{instance.name}' beneath '{root.name}'."
                        );
                    if (instance.transform.position != position)
                        throw new InvalidOperationException(
                            $"The creature factory placed '{instance.name}' at {instance.transform.position} instead of {position}."
                        );

                    ActionController controller = instance.GetComponent<ActionController>();
                    if (controller == null)
                        throw new InvalidOperationException(
                            $"Materialized creature '{instance.name}' has no root ActionController."
                        );
                    if (instance.GetComponent<DungeonEncounterMember>() != null)
                        throw new InvalidOperationException(
                            $"Materialized creature '{instance.name}' already has encounter identity."
                        );

                    DungeonEncounterMember member = instance.AddComponent<DungeonEncounterMember>();
                    string instanceId = DungeonEncounterStateMachine.CreateCreatureInstanceId(
                        plan.Id,
                        planIndex
                    );
                    string persistentState = string.Empty;
                    if (
                        restoredCreatures.TryGetValue(
                            instanceId,
                            out DungeonCreatureRuntimeState restored
                        )
                    )
                    {
                        CreatureComponent creature = instance.GetComponent<CreatureComponent>();
                        if (creature == null)
                            throw new InvalidOperationException(
                                $"Restored encounter creature '{instance.name}' has no CreatureComponent."
                            );
                        creature.InitializeHealthBeforeEncounter(
                            restored.HitPoints,
                            creature.maxHp,
                            creature.tempHp
                        );
                        persistentState = restored.State ?? string.Empty;
                    }
                    member.Configure(
                        plan.Id,
                        instanceId,
                        plan.CreatureIds[planIndex],
                        persistentState
                    );
                    members.Add(member);
                    controllers.Add(controller);
                }

                return new DungeonEncounterMaterialization(members, controllers);
            }
            catch
            {
                for (int index = created.Count - 1; index >= 0; index--)
                {
                    runtimeRegistration.Rollback(created[index]);
                    factory.Destroy(created[index]);
                }
                throw;
            }
        }

        private static void ValidatePlan(DungeonEncounterPlan plan, Transform root)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(plan.Id))
                throw new ArgumentException(
                    "An encounter plan requires a stable ID.",
                    nameof(plan)
                );
            if (plan.IsResolved)
                throw new InvalidOperationException(
                    $"Resolved encounter '{plan.Id}' cannot be materialized."
                );
            if (plan.CreatureIds.Count != plan.SpawnCells.Count)
                throw new ArgumentException(
                    $"Encounter '{plan.Id}' has {plan.CreatureIds.Count} creatures but {plan.SpawnCells.Count} spawn cells.",
                    nameof(plan)
                );
            if (plan.CreatureIds.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException(
                    $"Encounter '{plan.Id}' contains a blank creature content ID.",
                    nameof(plan)
                );
            if (plan.SpawnCells.Distinct().Count() != plan.SpawnCells.Count)
                throw new ArgumentException(
                    $"Encounter '{plan.Id}' contains duplicate spawn cells.",
                    nameof(plan)
                );
        }
    }
}
