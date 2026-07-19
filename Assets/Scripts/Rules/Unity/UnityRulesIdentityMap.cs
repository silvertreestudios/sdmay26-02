using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Game.Rules.Runtime;
using GridPrivate;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Maintains explicit, bidirectional bindings between Unity encounter objects and stable rules IDs.
    /// </summary>
    /// <remarks>
    /// Callers supply every ID. This map never derives identity from a display name, hierarchy,
    /// sibling order, or Unity instance ID, and it never writes to a mapped object or to
    /// <see cref="RulesState"/>. Reference identity is used for all object keys, including plain
    /// equipment and definition models that may implement value equality.
    /// </remarks>
    public sealed class UnityRulesIdentityMap
    {
        private readonly ExplicitBindings<GameObject, CreatureId> creatures =
            new ExplicitBindings<GameObject, CreatureId>("creature");
        private readonly ExplicitBindings<object, ItemId> equipment =
            new ExplicitBindings<object, ItemId>("equipment");
        private readonly ExplicitBindings<Team, TeamId> teams =
            new ExplicitBindings<Team, TeamId>("team");
        private readonly ExplicitBindings<UnityObject, PlayerId> players =
            new ExplicitBindings<UnityObject, PlayerId>("player adapter");
        private readonly ExplicitBindings<object, RuleDefinitionId> definitions =
            new ExplicitBindings<object, RuleDefinitionId>("rule definition");
        private readonly ExplicitBindings<Tile, GridPosition> gridCells =
            new ExplicitBindings<Tile, GridPosition>("grid cell");

        /// <summary>
        /// Registers a combatant GameObject under its caller-owned creature ID.
        /// </summary>
        /// <param name="combatant">The live scene object that presents the creature.</param>
        /// <param name="id">The stable encounter identity supplied by composition or saved data.</param>
        /// <exception cref="ArgumentNullException"><paramref name="combatant"/> is missing or destroyed.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">
        /// Either side is already registered to a different counterpart.
        /// </exception>
        public void RegisterCreature(GameObject combatant, CreatureId id)
        {
            RequireUnityObject(combatant, nameof(combatant));
            RequireId(id.IsEmpty, nameof(id), "creature");
            creatures.Register(combatant, id);
        }

        /// <summary>
        /// Gets the stable creature ID registered for a combatant.
        /// </summary>
        /// <param name="combatant">The live combatant scene object.</param>
        /// <returns>The combatant's explicit creature ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="combatant"/> is missing or destroyed.</exception>
        /// <exception cref="KeyNotFoundException">The combatant has not been registered.</exception>
        public CreatureId GetCreatureId(GameObject combatant)
        {
            RequireUnityObject(combatant, nameof(combatant));
            return creatures.GetId(combatant);
        }

        /// <summary>
        /// Gets the combatant scene object registered for a stable creature ID.
        /// </summary>
        /// <param name="id">The registered creature ID.</param>
        /// <returns>The exact GameObject supplied during registration.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">The ID has not been registered.</exception>
        public GameObject GetCreatureObject(CreatureId id)
        {
            RequireId(id.IsEmpty, nameof(id), "creature");
            return creatures.GetObject(id);
        }

        /// <summary>
        /// Registers one plain equipment instance under its stable item ID.
        /// </summary>
        /// <param name="item">The specific equipment model instance owned by legacy Unity code.</param>
        /// <param name="id">The stable item-instance identity.</param>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Either side conflicts with an existing registration.</exception>
        public void RegisterEquipment(object item, ItemId id)
        {
            RequireReference(item, nameof(item));
            RequireId(id.IsEmpty, nameof(id), "item");
            equipment.Register(item, id);
        }

        /// <summary>
        /// Gets the stable item ID registered for a plain equipment instance.
        /// </summary>
        /// <param name="item">The exact equipment instance supplied during registration.</param>
        /// <returns>The equipment instance's stable item ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
        /// <exception cref="KeyNotFoundException">The instance has not been registered.</exception>
        public ItemId GetItemId(object item)
        {
            RequireReference(item, nameof(item));
            return equipment.GetId(item);
        }

        /// <summary>
        /// Gets the plain equipment instance registered for a stable item ID.
        /// </summary>
        /// <param name="id">The registered item-instance ID.</param>
        /// <returns>The exact equipment object supplied during registration.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">The ID has not been registered.</exception>
        public object GetEquipment(ItemId id)
        {
            RequireId(id.IsEmpty, nameof(id), "item");
            return equipment.GetObject(id);
        }

        /// <summary>
        /// Registers a Unity team component under a stable team ID.
        /// </summary>
        /// <param name="team">The live component that currently owns legacy team presentation data.</param>
        /// <param name="id">The stable team identity; it need not match the display name.</param>
        /// <exception cref="ArgumentNullException"><paramref name="team"/> is missing or destroyed.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Either side conflicts with an existing registration.</exception>
        public void RegisterTeam(Team team, TeamId id)
        {
            RequireUnityObject(team, nameof(team));
            RequireId(id.IsEmpty, nameof(id), "team");
            teams.Register(team, id);
        }

        /// <summary>
        /// Gets the stable team ID registered for a Unity team component.
        /// </summary>
        /// <param name="team">The exact live component supplied during registration.</param>
        /// <returns>The component's stable team ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="team"/> is missing or destroyed.</exception>
        /// <exception cref="KeyNotFoundException">The component has not been registered.</exception>
        public TeamId GetTeamId(Team team)
        {
            RequireUnityObject(team, nameof(team));
            return teams.GetId(team);
        }

        /// <summary>
        /// Gets the Unity team component registered for a stable team ID.
        /// </summary>
        /// <param name="id">The registered team ID.</param>
        /// <returns>The exact team component supplied during registration.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">The ID has not been registered.</exception>
        public Team GetTeamComponent(TeamId id)
        {
            RequireId(id.IsEmpty, nameof(id), "team");
            return teams.GetObject(id);
        }

        /// <summary>
        /// Registers a Unity player or AI adapter under the player identity it represents.
        /// </summary>
        /// <param name="adapter">
        /// The live GameObject, MonoBehaviour, ScriptableObject, or other Unity object that owns input
        /// or decision-making for the player.
        /// </param>
        /// <param name="id">The stable player identity used by creature ownership data.</param>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is missing or destroyed.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Either side conflicts with an existing registration.</exception>
        public void RegisterPlayerAdapter(UnityObject adapter, PlayerId id)
        {
            RequireUnityObject(adapter, nameof(adapter));
            RequireId(id.IsEmpty, nameof(id), "player");
            players.Register(adapter, id);
        }

        /// <summary>
        /// Gets the stable player ID registered for a Unity player or AI adapter.
        /// </summary>
        /// <param name="adapter">The exact live adapter supplied during registration.</param>
        /// <returns>The adapter's stable player ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="adapter"/> is missing or destroyed.</exception>
        /// <exception cref="KeyNotFoundException">The adapter has not been registered.</exception>
        public PlayerId GetPlayerId(UnityObject adapter)
        {
            RequireUnityObject(adapter, nameof(adapter));
            return players.GetId(adapter);
        }

        /// <summary>
        /// Gets the Unity player or AI adapter registered for a stable player ID.
        /// </summary>
        /// <param name="id">The registered player ID.</param>
        /// <returns>The exact Unity object supplied during registration.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">The ID has not been registered.</exception>
        public UnityObject GetPlayerAdapter(PlayerId id)
        {
            RequireId(id.IsEmpty, nameof(id), "player");
            return players.GetObject(id);
        }

        /// <summary>
        /// Registers a plain definition or catalog object under its stable rule-definition ID.
        /// </summary>
        /// <param name="definition">The specific immutable definition object used by composition.</param>
        /// <param name="id">The stable ID stored by rules bindings and registries.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">Either side conflicts with an existing registration.</exception>
        public void RegisterDefinition(object definition, RuleDefinitionId id)
        {
            RequireReference(definition, nameof(definition));
            RequireId(id.IsEmpty, nameof(id), "rule definition");
            definitions.Register(definition, id);
        }

        /// <summary>
        /// Gets the stable rule-definition ID registered for a plain definition object.
        /// </summary>
        /// <param name="definition">The exact definition instance supplied during registration.</param>
        /// <returns>The definition's stable rules ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="KeyNotFoundException">The definition has not been registered.</exception>
        public RuleDefinitionId GetRuleDefinitionId(object definition)
        {
            RequireReference(definition, nameof(definition));
            return definitions.GetId(definition);
        }

        /// <summary>
        /// Gets the plain definition object registered for a stable rule-definition ID.
        /// </summary>
        /// <param name="id">The registered rule-definition ID.</param>
        /// <returns>The exact definition object supplied during registration.</returns>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty.</exception>
        /// <exception cref="KeyNotFoundException">The ID has not been registered.</exception>
        public object GetDefinition(RuleDefinitionId id)
        {
            RequireId(id.IsEmpty, nameof(id), "rule definition");
            return definitions.GetObject(id);
        }

        /// <summary>
        /// Registers a legacy grid tile under the plain rules position it presents.
        /// </summary>
        /// <param name="tile">The specific legacy tile instance.</param>
        /// <param name="position">The explicit grid coordinate represented by the tile.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tile"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Either side conflicts with an existing registration.</exception>
        public void RegisterGridCell(Tile tile, GridPosition position)
        {
            RequireReference(tile, nameof(tile));
            gridCells.Register(tile, position);
        }

        /// <summary>
        /// Gets the rules position explicitly registered for a legacy grid tile.
        /// </summary>
        /// <param name="tile">The exact tile instance supplied during registration.</param>
        /// <returns>The tile's plain three-dimensional grid position.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tile"/> is <see langword="null"/>.</exception>
        /// <exception cref="KeyNotFoundException">The tile has not been registered.</exception>
        public GridPosition GetGridPosition(Tile tile)
        {
            RequireReference(tile, nameof(tile));
            return gridCells.GetId(tile);
        }

        /// <summary>
        /// Gets the legacy grid tile registered for a plain rules position.
        /// </summary>
        /// <param name="position">The registered three-dimensional grid coordinate.</param>
        /// <returns>The exact tile instance supplied during registration.</returns>
        /// <exception cref="KeyNotFoundException">The position has not been registered.</exception>
        public Tile GetTile(GridPosition position) => gridCells.GetObject(position);

        private static void RequireUnityObject(UnityObject value, string parameterName)
        {
            if (value == null)
                throw new ArgumentNullException(parameterName);
        }

        private static void RequireReference(object value, string parameterName)
        {
            if (ReferenceEquals(value, null))
                throw new ArgumentNullException(parameterName);
        }

        private static void RequireId(bool isEmpty, string parameterName, string kind)
        {
            if (isEmpty)
                throw new ArgumentException($"A {kind} ID is required.", parameterName);
        }

        private sealed class ExplicitBindings<TObject, TId>
            where TObject : class
            where TId : struct
        {
            private readonly string kind;
            private readonly Dictionary<TObject, TId> idByObject =
                new Dictionary<TObject, TId>(ReferenceComparer<TObject>.Instance);
            private readonly Dictionary<TId, TObject> objectById = new Dictionary<TId, TObject>();

            public ExplicitBindings(string kind) => this.kind = kind;

            public void Register(TObject value, TId id)
            {
                bool objectRegistered = idByObject.TryGetValue(value, out TId registeredId);
                bool idRegistered = objectById.TryGetValue(id, out TObject registeredObject);

                if (objectRegistered && idRegistered &&
                    EqualityComparer<TId>.Default.Equals(registeredId, id) &&
                    ReferenceEquals(registeredObject, value))
                {
                    return;
                }

                if (objectRegistered)
                {
                    throw new InvalidOperationException(
                        $"This {kind} object is already registered to a different ID.");
                }

                if (idRegistered)
                {
                    throw new InvalidOperationException(
                        $"This {kind} ID is already registered to a different object.");
                }

                idByObject.Add(value, id);
                objectById.Add(id, value);
            }

            public TId GetId(TObject value)
            {
                if (!idByObject.TryGetValue(value, out TId id))
                    throw new KeyNotFoundException($"The {kind} object is not registered.");
                return id;
            }

            public TObject GetObject(TId id)
            {
                if (!objectById.TryGetValue(id, out TObject value))
                    throw new KeyNotFoundException($"The {kind} ID is not registered.");
                return value;
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static ReferenceComparer<T> Instance { get; } = new ReferenceComparer<T>();

            public bool Equals(T left, T right) => ReferenceEquals(left, right);

            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
