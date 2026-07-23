using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using GridPrivate;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Owns the authoritative rules store and Unity projections for one combat encounter.
    /// </summary>
    /// <remarks>
    /// Combat initiative remains scheduled by <see cref="CombatManager"/>. This bridge owns only
    /// the shared state required by the first vertical action slice: health, actions, positions,
    /// land Speeds, movement budgets, and immutable topology snapshots.
    /// </remarks>
    public sealed class UnityCombatRulesBridge
    {
        private readonly Dictionary<CreatureComponent, CreatureId> creatureIds = new();
        private readonly Dictionary<CreatureId, CreatureComponent> creatures = new();
        private readonly Dictionary<ActionController, CreatureId> controllerIds = new();
        private readonly Dictionary<string, PlayerId> playerIds = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();
        private readonly MutableGridTopologyProvider topologyProvider;
        private readonly StrideActionDefinition strideDefinition;
        private readonly RuleDispatcher dispatcher;
        private readonly bool supportsCombatActions;
        private long nextCreatureId;
        private long nextOriginId;

        private UnityCombatRulesBridge(IReadOnlyList<CreatureComponent> encounterCreatures)
        {
            supportsCombatActions = false;
            topologyProvider = new MutableGridTopologyProvider(CreateHealthTestTopology());
            strideDefinition = new StrideActionDefinition(topologyProvider);
            RulesStateSeed seed = new RulesStateSeed();
            foreach (CreatureComponent creature in encounterCreatures)
            {
                CreatureId id = AllocateCreatureId();
                creatureIds.Add(creature, id);
                creatures.Add(id, creature);
                seed.SeedHealth(id, creature.GetHealthInitializationState());
            }

            dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseHealthRules()
                .Build();
            RegisterHealthProjection();
            AttachCreatures();
        }

        private UnityCombatRulesBridge(
            IReadOnlyList<ActionController> encounterControllers,
            Tile[,] tiles
        )
        {
            supportsCombatActions = true;
            topologyProvider = new MutableGridTopologyProvider(CreateTopology(tiles));
            strideDefinition = new StrideActionDefinition(topologyProvider);
            RulesStateSeed seed = new RulesStateSeed();
            foreach (ActionController controller in encounterControllers)
            {
                CombatantRegistration registration = CreateRegistration(controller);
                AddRegistrationMaps(registration);
                Seed(seed, registration.State);
            }

            dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseHealthRules()
                .UseCombatRuntimeRules()
                .UseActionLifecycle(strideDefinition)
                .UseMovementRules(topologyProvider)
                .UseStrideRules(strideDefinition)
                .Build();
            RegisterHealthProjection();
            AttachCreatures();
            foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                entry.Key.AttachCombatRules(this, entry.Value);
        }

        /// <summary>Creates the complete rules composition for one combat encounter.</summary>
        /// <param name="encounterControllers">The non-empty, unique participant sequence.</param>
        /// <param name="tiles">The initialized live grid used to take an immutable topology snapshot.</param>
        /// <returns>The initialized encounter rules bridge.</returns>
        public static UnityCombatRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            Tile[,] tiles
        )
        {
            if (encounterControllers == null)
                throw new ArgumentNullException(nameof(encounterControllers));
            ActionController[] copied = encounterControllers.ToArray();
            ValidateControllers(copied, nameof(encounterControllers));
            ValidateTiles(tiles);
            return new UnityCombatRulesBridge(copied, tiles);
        }

        /// <summary>
        /// Creates a health-only composition for focused tests that do not construct combat or grid
        /// infrastructure.
        /// </summary>
        /// <param name="encounterCreatures">The non-empty, unique creature sequence.</param>
        /// <returns>An initialized bridge with only health operations registered.</returns>
        public static UnityCombatRulesBridge CreateHealthTestComposition(
            IEnumerable<CreatureComponent> encounterCreatures
        )
        {
            if (encounterCreatures == null)
                throw new ArgumentNullException(nameof(encounterCreatures));
            CreatureComponent[] copied = encounterCreatures.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException(
                    "A health test composition requires at least one creature.",
                    nameof(encounterCreatures)
                );
            if (copied.Any(creature => creature == null))
                throw new ArgumentException(
                    "A health test composition cannot contain a null creature.",
                    nameof(encounterCreatures)
                );
            if (copied.Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "A creature cannot be registered more than once.",
                    nameof(encounterCreatures)
                );
            return new UnityCombatRulesBridge(copied);
        }

        /// <summary>Gets the latest authoritative encounter snapshot.</summary>
        public RulesSnapshot Snapshot => dispatcher.Snapshot;

        /// <summary>Gets the stable rules ID assigned to a registered creature.</summary>
        /// <param name="creature">The registered Unity creature.</param>
        /// <returns>The encounter-stable rules identifier.</returns>
        public CreatureId GetCreatureId(CreatureComponent creature)
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            if (!creatureIds.TryGetValue(creature, out CreatureId id))
                throw new InvalidOperationException(
                    "Creature is not registered in this encounter."
                );
            return id;
        }

        /// <summary>Gets authoritative health for one registered creature ID.</summary>
        /// <param name="creature">The encounter-stable creature identifier.</param>
        /// <returns>The latest committed health state.</returns>
        public HealthState GetHealth(CreatureId creature)
        {
            if (!Snapshot.Health.TryGet(creature, out HealthState health))
                throw new InvalidOperationException("Creature has no authoritative health state.");
            return health;
        }

        /// <summary>Gets the current authoritative action count for one controller.</summary>
        /// <param name="creature">The controller's encounter-stable creature ID.</param>
        /// <returns>The non-negative action count.</returns>
        public int GetActionsRemaining(CreatureId creature)
        {
            if (!Snapshot.ActionEconomy.TryGet(creature, out ActionEconomyState economy))
                throw new InvalidOperationException(
                    "Creature has no authoritative action economy."
                );
            return economy.ActionsRemaining;
        }

        /// <summary>Starts the controller's scheduled turn with its final action count.</summary>
        /// <param name="creature">The registered creature receiving turn authority.</param>
        /// <param name="actions">The action count after Unity-side start-of-turn modifiers.</param>
        public void BeginTurn(CreatureId creature, int actions) =>
            RequireSuccess(DispatchNow(new BeginCombatTurnOp(creature, actions)));

        /// <summary>Ends the controller's scheduled turn and clears transient movement state.</summary>
        /// <param name="creature">The registered creature losing turn authority.</param>
        public void EndTurn(CreatureId creature) =>
            RequireSuccess(DispatchNow(new EndCombatTurnOp(creature)));

        /// <summary>Spends actions for a feature still using the legacy Unity action path.</summary>
        /// <param name="creature">The registered creature paying the cost.</param>
        /// <param name="amount">The positive action count to spend.</param>
        public void SpendLegacyActions(CreatureId creature, int amount) =>
            RequireSuccess(DispatchNow(new SpendLegacyActionsOp(creature, amount)));

        /// <summary>Registers dungeon reinforcements in the existing encounter store.</summary>
        /// <param name="reinforcements">New, unique controllers not already registered.</param>
        public void RegisterCombatants(IEnumerable<ActionController> reinforcements)
        {
            RequireCombatComposition();
            if (reinforcements == null)
                throw new ArgumentNullException(nameof(reinforcements));
            ActionController[] copied = reinforcements.ToArray();
            ValidateControllers(copied, nameof(reinforcements));
            if (copied.Any(controllerIds.ContainsKey))
                throw new InvalidOperationException("A reinforcement is already registered.");

            CombatantRegistration[] registrations = copied.Select(CreateRegistration).ToArray();
            foreach (CombatantRegistration registration in registrations)
            {
                RequireSuccess(DispatchNow(new RegisterCombatantOp(registration.State)));
                AddRegistrationMaps(registration);
                registration.Creature.AttachHealthRules(this, registration.State.Creature.Id);
                registration.Controller.AttachCombatRules(this, registration.State.Creature.Id);
            }
        }

        /// <summary>Dispatches the dormant rules-native Stride path for integration tests.</summary>
        /// <param name="creature">The registered mover.</param>
        /// <param name="path">The exact path selected before dispatch.</param>
        /// <returns>The structural root result and committed movement facts.</returns>
        public async ValueTask<OpResult<MovePathOutcome>> DispatchStride(
            CreatureId creature,
            MovementPath path
        )
        {
            RequireCombatComposition();
            topologyProvider.BeginResolution();
            try
            {
                OpResult<MovePathOutcome> result = await dispatcher.Dispatch(
                    new StrideActionOp(creature, path)
                );
                foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                {
                    if (entry.Value == creature)
                    {
                        entry.Key.SyncActionPointsFromRules();
                        break;
                    }
                }
                return result;
            }
            finally
            {
                topologyProvider.EndResolution();
            }
        }

        /// <summary>Replaces topology after a live grid mutation and before another rules root.</summary>
        /// <param name="tiles">The current initialized grid tiles.</param>
        public void RefreshTopology(Tile[,] tiles)
        {
            RequireCombatComposition();
            topologyProvider.Replace(CreateTopology(tiles));
        }

        /// <summary>Looks up the retained source for an encounter health origin.</summary>
        /// <param name="origin">The origin allocated for a health request.</param>
        /// <param name="source">Receives the source when the origin belongs to this bridge.</param>
        /// <returns>Whether the origin belongs to this encounter.</returns>
        public bool TryGetOriginSource(HealthChangeOriginId origin, out RuleSource source) =>
            origins.TryGetValue(origin, out source);

        /// <summary>Commits already-final damage.</summary>
        /// <param name="target">The registered creature to damage.</param>
        /// <param name="finalDamage">Damage after upstream calculations.</param>
        /// <param name="source">The rule source responsible for the damage.</param>
        /// <returns>The exact committed damage breakdown.</returns>
        public DamageOutcome ApplyFinalDamage(
            CreatureId target,
            int finalDamage,
            RuleSource source
        ) => DispatchNow(new ApplyDamageOp(target, finalDamage, AllocateOrigin(source), source));

        /// <summary>Commits healing.</summary>
        /// <param name="target">The registered creature to heal.</param>
        /// <param name="healing">The non-negative healing offered.</param>
        /// <param name="source">The rule source responsible for the healing.</param>
        /// <returns>The exact committed healing outcome.</returns>
        public HealingOutcome ApplyHealing(CreatureId target, int healing, RuleSource source) =>
            DispatchNow(new ApplyHealingOp(target, healing, AllocateOrigin(source), source));

        /// <summary>Attempts a source-owned temporary Hit Point grant.</summary>
        /// <param name="target">The registered creature receiving the offer.</param>
        /// <param name="amount">The non-negative temporary Hit Point pool offered.</param>
        /// <param name="source">The source that owns an accepted pool.</param>
        /// <returns>The committed or blocked grant outcome.</returns>
        public TemporaryHitPointsGrantOutcome GrantTemporaryHitPoints(
            CreatureId target,
            int amount,
            RuleSource source
        ) =>
            DispatchNow(
                new GrantTemporaryHitPointsOp(target, amount, AllocateOrigin(source), source)
            );

        /// <summary>Removes temporary Hit Points owned by the supplied source.</summary>
        /// <param name="target">The registered creature whose pool may be removed.</param>
        /// <param name="source">The source that must own the active pool.</param>
        /// <returns>The exact amount removed.</returns>
        public TemporaryHitPointsRemovalOutcome RemoveTemporaryHitPoints(
            CreatureId target,
            RuleSource source
        ) => DispatchNow(new RemoveTemporaryHitPointsOp(target, AllocateOrigin(source), source));

        /// <summary>Adds temporary Hit Point immunity for the supplied source.</summary>
        /// <param name="target">The registered creature receiving immunity.</param>
        /// <param name="source">The source whose future grants will be blocked.</param>
        /// <returns>Whether a new immunity was committed.</returns>
        public TemporaryHitPointImmunityOutcome AddTemporaryHitPointImmunity(
            CreatureId target,
            RuleSource source
        ) =>
            DispatchNow(new AddTemporaryHitPointImmunityOp(target, AllocateOrigin(source), source));

        private CombatantRegistration CreateRegistration(ActionController controller)
        {
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            if (creature == null)
                throw new InvalidOperationException(
                    "Every combat controller requires a creature component."
                );
            CreatureId creatureId = AllocateCreatureId();
            PlayerId playerId = GetPlayerId(controller);
            Vector3Int position = Vector3Int.RoundToInt(controller.transform.position);
            int speedFeet = Mathf.Max(0, Mathf.RoundToInt(creature.speed));
            CombatantRulesState state = new CombatantRulesState(
                new CreatureState(creatureId, playerId),
                creature.GetHealthInitializationState(),
                new GridPosition(position.x, position.y, position.z),
                new GridDistance(speedFeet)
            );
            return new CombatantRegistration(controller, creature, state);
        }

        private PlayerId GetPlayerId(ActionController controller)
        {
            Team team = controller.GetComponent<Team>();
            string teamName = team == null ? string.Empty : team.Name;
            if (!string.IsNullOrWhiteSpace(teamName))
            {
                if (playerIds.TryGetValue(teamName, out PlayerId existing))
                    return existing;
                if (TeamRules.TryGetInstance(out TeamRules rules) && rules.Contains(teamName))
                {
                    foreach (KeyValuePair<string, PlayerId> pair in playerIds)
                    {
                        if (rules.Contains(pair.Key) && rules.IsFriendly(teamName, pair.Key))
                        {
                            playerIds.Add(teamName, pair.Value);
                            return pair.Value;
                        }
                    }
                }
                PlayerId grouped = new PlayerId($"combat-side-{playerIds.Count + 1}");
                playerIds.Add(teamName, grouped);
                return grouped;
            }
            return new PlayerId($"combat-side-unassigned-{nextCreatureId}");
        }

        private CreatureId AllocateCreatureId()
        {
            nextCreatureId++;
            return new CreatureId($"combat-creature-{nextCreatureId}");
        }

        private HealthChangeOriginId AllocateOrigin(RuleSource source)
        {
            if (source.IsEmpty)
                throw new ArgumentException("A health rule source is required.", nameof(source));
            nextOriginId++;
            HealthChangeOriginId id = new HealthChangeOriginId($"health-origin-{nextOriginId}");
            origins.Add(id, source);
            return id;
        }

        private TResult DispatchNow<TResult>(IRuleOp<TResult> operation)
        {
            topologyProvider.BeginResolution();
            try
            {
                ValueTask<OpResult<TResult>> pending = dispatcher.Dispatch(operation);
                if (!pending.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Synchronous Unity rules requests cannot contain asynchronous callbacks."
                    );
                }
                OpResult<TResult> result = pending.GetAwaiter().GetResult();
                if (result is ResolvedOpResult<TResult> resolved)
                    return resolved.Value;
                if (result is InvalidOpResult<TResult> invalid)
                    throw new InvalidOperationException(invalid.Reason);
                throw new InvalidOperationException(
                    "The synchronous rules request did not resolve."
                );
            }
            finally
            {
                topologyProvider.EndResolution();
            }
        }

        private static void RequireSuccess(CombatRuntimeOutcome outcome)
        {
            if (!outcome.Succeeded)
                throw new InvalidOperationException(outcome.Reason);
        }

        private void RequireCombatComposition()
        {
            if (!supportsCombatActions)
                throw new InvalidOperationException(
                    "This health-only test composition has no combat actions."
                );
        }

        private void RegisterHealthProjection()
        {
            HealthProjectionObserver observer = new HealthProjectionObserver(creatures);
            dispatcher.RegisterFactObserver<HealthFact>(observer);
            dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(observer);
        }

        private void AttachCreatures()
        {
            foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
                entry.Key.AttachHealthRules(this, entry.Value);
        }

        private void AddRegistrationMaps(CombatantRegistration registration)
        {
            CreatureId id = registration.State.Creature.Id;
            controllerIds.Add(registration.Controller, id);
            creatureIds.Add(registration.Creature, id);
            creatures.Add(id, registration.Creature);
        }

        private static void Seed(RulesStateSeed seed, CombatantRulesState state)
        {
            CreatureId id = state.Creature.Id;
            seed.SeedCreature(state.Creature)
                .SeedHealth(id, state.Health)
                .SeedPosition(id, state.Position)
                .SeedLandSpeed(id, state.LandSpeed)
                .SeedActionEconomy(id, new ActionEconomyState(0, false));
        }

        private static void ValidateControllers(
            ActionController[] controllers,
            string parameterName
        )
        {
            if (controllers.Length == 0)
                throw new ArgumentException(
                    "A combat rules bridge requires at least one controller.",
                    parameterName
                );
            if (controllers.Any(controller => controller == null))
                throw new ArgumentException(
                    "A combat rules bridge cannot contain a null controller.",
                    parameterName
                );
            if (controllers.Distinct().Count() != controllers.Length)
                throw new ArgumentException(
                    "A combat controller cannot be registered more than once.",
                    parameterName
                );
        }

        private static void ValidateTiles(Tile[,] tiles)
        {
            if (tiles == null)
                throw new ArgumentNullException(nameof(tiles));
            if (tiles.GetLength(0) == 0 || tiles.GetLength(1) == 0)
                throw new ArgumentException(
                    "Combat topology requires a non-empty tile grid.",
                    nameof(tiles)
                );
        }

        private static GridTopology CreateTopology(Tile[,] tiles)
        {
            ValidateTiles(tiles);
            List<GridCell> cells = new List<GridCell>();
            for (int x = 0; x < tiles.GetLength(0); x++)
            {
                for (int z = 0; z < tiles.GetLength(1); z++)
                {
                    Tile tile = tiles[x, z];
                    if (tile == null || tile.IsObstructing)
                    {
                        cells.Add(
                            new GridCell(new GridPosition(x, 0, z), true, TerrainCost.Normal)
                        );
                    }
                }
            }
            return new GridTopology(
                new GridBounds(
                    new GridPosition(0, 0, 0),
                    new GridPosition(tiles.GetLength(0) - 1, 0, tiles.GetLength(1) - 1)
                ),
                cells
            );
        }

        private static GridTopology CreateHealthTestTopology() =>
            new GridTopology(
                new GridBounds(new GridPosition(0, 0, 0), new GridPosition(0, 0, 0)),
                Array.Empty<GridCell>()
            );

        private sealed class MutableGridTopologyProvider : IGridTopologyProvider
        {
            private GridTopology current;
            private bool resolutionActive;

            public MutableGridTopologyProvider(GridTopology initial) =>
                current = initial ?? throw new ArgumentNullException(nameof(initial));

            public GridTopology Current => current;

            public void BeginResolution()
            {
                if (resolutionActive)
                    throw new InvalidOperationException(
                        "A Unity rules resolution is already active."
                    );
                resolutionActive = true;
            }

            public void EndResolution() => resolutionActive = false;

            public void Replace(GridTopology replacement)
            {
                if (replacement == null)
                    throw new ArgumentNullException(nameof(replacement));
                if (resolutionActive)
                    throw new InvalidOperationException(
                        "Topology cannot change during a rules resolution."
                    );
                current = replacement;
            }
        }

        private sealed class CombatantRegistration
        {
            public CombatantRegistration(
                ActionController controller,
                CreatureComponent creature,
                CombatantRulesState state
            )
            {
                Controller = controller;
                Creature = creature;
                State = state;
            }

            public ActionController Controller { get; }
            public CreatureComponent Creature { get; }
            public CombatantRulesState State { get; }
        }

        private sealed class HealthProjectionObserver
            : IFactObserver<HealthFact>,
                IFactObserver<CreatureReducedToZeroFact>
        {
            private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

            public HealthProjectionObserver(
                IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
            ) => this.creatures = creatures;

            public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot)
            {
                if (
                    creatures.TryGetValue(fact.Creature, out CreatureComponent creature)
                    && creature != null
                )
                {
                    HealthState health = currentSnapshot.Health[fact.Creature];
                    creature.ProjectCommittedHealth(health);
                    if (fact is DamageAppliedFact && health.Current > 0)
                        creature.PresentCommittedHit();
                }
                return default;
            }

            public ValueTask OnFactCommitted(
                CreatureReducedToZeroFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                if (
                    creatures.TryGetValue(fact.Creature, out CreatureComponent creature)
                    && creature != null
                )
                {
                    creature.PresentCommittedDefeat();
                }
                return default;
            }
        }
    }
}
