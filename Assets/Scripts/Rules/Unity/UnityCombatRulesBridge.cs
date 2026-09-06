using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity.Composition;
using GridPrivate;
using GridPublic;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Owns the authoritative rules store and Unity projections for one combat encounter.
    /// </summary>
    /// <remarks>
    /// The rules store owns encounter scheduling and the state required by rules-backed action
    /// slices. Unity combat objects are hosts and post-commit projections of that state.
    /// </remarks>
    public sealed class UnityCombatRulesBridge
    {
        private readonly Dictionary<CreatureComponent, CreatureId> creatureIds = new();
        private readonly Dictionary<CreatureId, CreatureComponent> creatures = new();
        private readonly Dictionary<ActionController, CreatureId> controllerIds = new();
        private readonly Dictionary<CreatureId, ActionController> controllers = new();
        private readonly Dictionary<string, PlayerId> playerIds = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly UnityTeamStrideFriendshipProvider strideFriendshipProvider = new();
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();
        private readonly MutableGridTopologyProvider topologyProvider;
        private readonly StrideActionDefinition strideDefinition;
        private readonly RuleDispatcher dispatcher;
        private readonly UnityEncounterComposition composition;
        private readonly UnityCombatantEnrollmentPipeline enrollmentPipeline;
        private readonly CompositeLifetime encounterLifetime = new();
        private readonly UnityActionPresentationCoordinator actionPresentationCoordinator = new();
        private readonly Dictionary<OpId, Queue<Action>> encounterPresentationByRoot = new();
        private readonly Dictionary<OpId, List<OpId>> encounterPresentationChildren = new();
        private readonly HashSet<OpId> settledEncounterPresentationRoots = new();
        private readonly EncounterId encounterId = new EncounterId("unity-encounter-1");
        private Tile[,] currentTiles;
        private long nextCreatureId;
        private long nextOriginId;
        private int dispatchDepth;
        private bool releaseRequested;
        private bool ownershipReleased;
        private Action ownershipReleasedCallbacks = delegate { };

        /// <summary>Raised after an exact authoritative turn begins.</summary>
        public event Action<TurnIdentity> TurnBegan = delegate { };

        /// <summary>Raised once after the encounter roster commits and before its first boundary.</summary>
        public event Action EncounterStarted = delegate { };

        /// <summary>Raised after an exact authoritative turn ends.</summary>
        public event Action<TurnIdentity> TurnEnded = delegate { };

        /// <summary>Raised once after the authoritative encounter outcome commits.</summary>
        public event Action<EncounterOutcome> EncounterEnded = delegate { };

        private UnityCombatRulesBridge(
            IReadOnlyList<ActionController> encounterControllers,
            Tile[,] tiles,
            bool attachControllers,
            IRollService rollService,
            string protagonistTeamName,
            EncounterConclusionPolicy conclusionPolicy
        )
        {
            currentTiles = tiles;
            topologyProvider = new MutableGridTopologyProvider(CreateTopology(tiles));
            strideDefinition = new StrideActionDefinition(
                topologyProvider,
                strideFriendshipProvider
            );
            UnityEncounterModuleSet modules = UnityEncounterModuleSet.Create(
                this,
                actionPresentationCoordinator,
                creatures,
                controllers,
                tiles,
                strideDefinition,
                attachControllers
            );
            composition = modules.Composition;
            enrollmentPipeline = new UnityCombatantEnrollmentPipeline(
                this,
                composition,
                attachControllers
            );
            UnityCombatantEnrollmentPlan enrollment = enrollmentPipeline.Prepare(
                encounterControllers,
                nameof(encounterControllers)
            );
            try
            {
                RulesStateSeed seed = new RulesStateSeed();
                if (!attachControllers)
                    enrollment.SeedExploration(seed);
                RuleDispatcherBuilder dispatcherBuilder = new RuleDispatcherBuilder(
                    new InMemoryRulesStore(seed),
                    rollService ?? throw new ArgumentNullException(nameof(rollService))
                )
                    .UseHealthRules()
                    .UseMultipleAttackPenaltyRules()
                    .UseCheckResolution()
                    .UseActiveEffectRules(modules.Registry)
                    .UseEncounterRules(modules.Registry, composition.CreateTurnStartAdapters())
                    .UseActionLifecycle(modules.ActionCatalog)
                    .UseMovementRules(topologyProvider)
                    .UseStrideRules(strideDefinition);
                composition.ConfigureDispatcher(dispatcherBuilder);
                dispatcher = dispatcherBuilder.Build();
                composition.RegisterRuntime(dispatcher, encounterLifetime);
                if (attachControllers)
                {
                    if (
                        string.IsNullOrWhiteSpace(protagonistTeamName)
                        || !playerIds.TryGetValue(protagonistTeamName, out PlayerId protagonistTeam)
                    )
                        throw new ArgumentException(
                            "The protagonist team must be registered in this composition.",
                            nameof(protagonistTeamName)
                        );
                    DispatchNow(
                        new InitEncounterOp(encounterId, protagonistTeam, conclusionPolicy)
                    );
                    enrollment.Commit();
                }
                enrollment.AttachAndInstall();
                enrollment.TransferTo(encounterLifetime);
            }
            catch (Exception constructionFailure)
            {
                throw CreateConstructionFailure(constructionFailure, enrollment);
            }
        }

        /// <summary>Gets whether an operation root or its Unity projection is still resolving.</summary>
        public bool IsResolutionActive => dispatchDepth > 0;

        /// <summary>Creates the complete rules composition for one combat encounter.</summary>
        /// <param name="encounterControllers">The non-empty, unique participant sequence.</param>
        /// <param name="tiles">The initialized live grid used to take an immutable topology snapshot.</param>
        /// <param name="protagonistTeamName">The registered team whose survival prevents defeat.</param>
        /// <param name="conclusionPolicy">The automatic encounter-conclusion behavior.</param>
        /// <returns>The initialized encounter rules bridge.</returns>
        public static UnityCombatRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            Tile[,] tiles,
            string protagonistTeamName,
            EncounterConclusionPolicy conclusionPolicy = EncounterConclusionPolicy.VictoryOrDefeat
        ) =>
            Create(
                encounterControllers,
                tiles,
                new RandomRollService(),
                protagonistTeamName,
                conclusionPolicy
            );

        /// <summary>
        /// Creates combat rules with an explicit deterministic or production roll source.
        /// </summary>
        /// <param name="encounterControllers">The non-empty, unique participant sequence.</param>
        /// <param name="tiles">The initialized live grid.</param>
        /// <param name="rollService">The required source for all rules-owned rolls.</param>
        /// <param name="protagonistTeamName">The registered team whose survival prevents defeat.</param>
        /// <param name="conclusionPolicy">The automatic encounter-conclusion behavior.</param>
        /// <returns>The initialized encounter rules bridge.</returns>
        public static UnityCombatRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            Tile[,] tiles,
            IRollService rollService,
            string protagonistTeamName,
            EncounterConclusionPolicy conclusionPolicy = EncounterConclusionPolicy.VictoryOrDefeat
        )
        {
            if (encounterControllers == null)
                throw new ArgumentNullException(nameof(encounterControllers));
            ActionController[] copied = encounterControllers.ToArray();
            ValidateTiles(tiles);
            if (string.IsNullOrWhiteSpace(protagonistTeamName))
                throw new ArgumentException(
                    "A protagonist team name is required.",
                    nameof(protagonistTeamName)
                );
            return new UnityCombatRulesBridge(
                copied,
                tiles,
                true,
                rollService,
                protagonistTeamName,
                conclusionPolicy
            );
        }

        /// <summary>
        /// Creates an isolated one-action Stride composition for movement outside initiative.
        /// </summary>
        /// <param name="controller">The exploration leader selecting and committing the Stride.</param>
        /// <param name="tiles">The initialized live grid used for immutable topology.</param>
        /// <param name="exploration">
        /// The coordinator that identifies party occupancy owned by formation projection.
        /// </param>
        /// <returns>A temporary rules composition that is not attached as combat authority.</returns>
        /// <remarks>
        /// Exploration has no combat action economy. The temporary action exists only so the same
        /// Stride definition, validation, operation, reducers, and Facts own the selected path.
        /// A later encounter composition is seeded from the fully projected boundary position.
        /// </remarks>
        public static UnityCombatRulesBridge CreateExplorationStride(
            ActionController controller,
            Tile[,] tiles,
            IExplorationStrideCoordinator exploration
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (exploration == null)
                throw new ArgumentNullException(nameof(exploration));
            ValidateTiles(tiles);
            return new UnityCombatRulesBridge(
                FindExplorationControllers(controller, tiles, exploration),
                tiles,
                false,
                new RandomRollService(),
                string.Empty,
                EncounterConclusionPolicy.VictoryOrDefeat
            );
        }

        /// <summary>Gets the latest authoritative encounter snapshot.</summary>
        public RulesSnapshot Snapshot => dispatcher.Snapshot;

        /// <summary>Gets the stable encounter identity used by the enrollment pipeline.</summary>
        internal EncounterId EncounterId => encounterId;

        /// <summary>Gets the current live tiles for feature-owned transitional adapters.</summary>
        internal Tile[,] CurrentTiles => currentTiles;

        /// <summary>Reports whether this encounter already owns the supplied controller mapping.</summary>
        internal bool IsControllerRegistered(ActionController controller) =>
            controllerIds.ContainsKey(controller);

        /// <summary>Reports whether this encounter already owns the supplied creature mapping.</summary>
        internal bool IsCreatureRegistered(CreatureComponent creature) =>
            creatureIds.ContainsKey(creature);

        /// <summary>Reserves allocator changes so failed preparation restores exact identities.</summary>
        internal RegistrationToken CreateIdentityReservation()
        {
            long savedNextCreatureId = nextCreatureId;
            Dictionary<string, PlayerId> savedPlayers = new(
                playerIds,
                StringComparer.OrdinalIgnoreCase
            );
            return new RegistrationToken(() =>
            {
                nextCreatureId = savedNextCreatureId;
                playerIds.Clear();
                foreach (KeyValuePair<string, PlayerId> pair in savedPlayers)
                    playerIds.Add(pair.Key, pair.Value);
                strideFriendshipProvider.Reset(savedPlayers);
            });
        }

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

        /// <summary>
        /// Tries to get the stable rules ID assigned to a Unity creature in this encounter.
        /// </summary>
        /// <param name="creature">The Unity creature whose encounter registration is queried.</param>
        /// <param name="id">
        /// The encounter-stable rules identifier when <paramref name="creature"/> is registered.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the creature is registered; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="creature"/> is <see langword="null"/>.
        /// </exception>
        public bool TryGetCreatureId(CreatureComponent creature, out CreatureId id)
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            return creatureIds.TryGetValue(creature, out id);
        }

        /// <summary>Gets the stable rules ID assigned to a registered controller.</summary>
        /// <param name="controller">The registered Unity action controller.</param>
        /// <returns>The encounter-stable rules identifier.</returns>
        public CreatureId GetCreatureId(ActionController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (!controllerIds.TryGetValue(controller, out CreatureId id))
                throw new InvalidOperationException(
                    "Controller is not registered in this rules composition."
                );
            return id;
        }

        /// <summary>Gets the required Unity controller for a registered rules creature.</summary>
        public ActionController GetController(CreatureId creature)
        {
            if (!controllers.TryGetValue(creature, out ActionController controller))
                throw new InvalidOperationException("The encounter controller is not registered.");
            return controller;
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
            return GetActionEconomy(creature).ActionsRemaining;
        }

        /// <summary>Gets the required authoritative action-economy slice.</summary>
        public ActionEconomyState GetActionEconomy(CreatureId creature)
        {
            if (!Snapshot.ActionEconomy.TryGet(creature, out ActionEconomyState economy))
                throw new InvalidOperationException(
                    "Creature has no authoritative action economy."
                );
            return economy;
        }

        /// <summary>Gets the required authoritative multiple-attack-penalty slice.</summary>
        public MultipleAttackPenaltyState GetMultipleAttackPenalty(CreatureId creature)
        {
            if (
                !Snapshot.MultipleAttackPenalty.TryGet(
                    creature,
                    out MultipleAttackPenaltyState penalty
                )
            )
                throw new InvalidOperationException(
                    "Creature has no authoritative multiple-attack-penalty state."
                );
            return penalty;
        }

        /// <summary>Checks exact current-turn authority for one registered creature.</summary>
        public bool HasTurnAuthority(CreatureId creature) =>
            Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
            && encounter.Phase == EncounterPhase.Active
            && encounter.CurrentTurn.HasValue
            && encounter.CurrentTurn.Value.Actor == creature;

        /// <summary>Explicitly activates the initialized encounter and reaches its first turn.</summary>
        /// <returns>The authoritative state after first-turn causal work settles.</returns>
        public EncounterState AdvanceEncounter() =>
            DispatchNow(new AdvanceEncounterOp(encounterId)).State;

        /// <summary>Ends the exact current turn owned by a registered creature.</summary>
        /// <param name="creature">The creature expected to own the current exact turn.</param>
        public void EndTurn(CreatureId creature)
        {
            EncounterState encounter = GetEncounter();
            if (
                encounter.Phase != EncounterPhase.Active
                || !encounter.CurrentTurn.HasValue
                || encounter.CurrentTurn.Value.Actor != creature
            )
                throw new InvalidOperationException("The creature does not own the active turn.");
            DispatchNow(new EndTurnOp(encounter.CurrentTurn.Value));
        }

        /// <summary>Spends authoritative actions for a Unity-hosted encounter action.</summary>
        /// <param name="creature">The registered creature paying the cost.</param>
        /// <param name="amount">The positive action count to spend.</param>
        public void SpendEncounterActions(CreatureId creature, int amount)
        {
            DispatchNow(new SpendEncounterActionsOp(creature, amount));
        }

        /// <summary>Suspends this encounter without deciding an outcome.</summary>
        public void SuspendEncounter()
        {
            DispatchNow(new SuspendEncounterOp(encounterId));
        }

        /// <summary>Gets this composition's authoritative encounter state.</summary>
        public EncounterState GetEncounter()
        {
            if (!Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter))
                throw new InvalidOperationException("The encounter has not been initialized.");
            return encounter;
        }

        /// <summary>Dispatches one synchronous typed rules operation.</summary>
        /// <typeparam name="TResult">The operation's structural result type.</typeparam>
        /// <param name="operation">The feature-owned immutable operation to dispatch.</param>
        /// <returns>The resolved, invalid, interrupted, or cancelled structural result.</returns>
        /// <remarks>
        /// Feature adapters construct their own operations. The bridge owns only synchronous Unity
        /// dispatch boundaries, topology stability, and projection of shared action economy.
        /// </remarks>
        public OpResult<TResult> Dispatch<TResult>(IRuleOp<TResult> operation)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            return DispatchResultNow(operation);
        }

        /// <summary>Drains committed presentation for one exact action invocation.</summary>
        /// <typeparam name="TResult">The action's feature-owned result type.</typeparam>
        /// <param name="action">The same immutable action instance supplied to dispatch.</param>
        /// <returns>
        /// A coroutine that releases the sequence after all recorded steps succeed or the first
        /// presenter execution fails. Missing sequences complete immediately.
        /// </returns>
        public IEnumerator DrainActionPresentation<TResult>(ActionOp<TResult> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return actionPresentationCoordinator.Drain(action);
        }

        /// <summary>Adds a prepared combatant batch to the existing encounter store.</summary>
        /// <param name="combatants">New, unique controllers not already registered.</param>
        public void AddCombatants(IEnumerable<ActionController> combatants)
        {
            UnityCombatantEnrollmentPlan enrollment = enrollmentPipeline.Prepare(
                combatants,
                nameof(combatants)
            );
            try
            {
                enrollment.Commit();
                enrollment.AttachAndInstall();
                enrollment.TransferTo(encounterLifetime);
            }
            catch (Exception registrationFailure)
            {
                try
                {
                    enrollment.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Combatant registration and local cleanup both failed.",
                        registrationFailure,
                        cleanupFailure
                    );
                }
                throw;
            }
        }

        /// <summary>
        /// Projects final authoritative health and releases this encounter's Unity ownership.
        /// </summary>
        /// <remarks>
        /// The registration maps intentionally outlive initiative membership, so this boundary
        /// also releases defeated or otherwise removed combatants. Each target verifies exact
        /// bridge identity before accepting the projection or detach, making repeated or delayed
        /// release safe when newer encounter ownership already exists.
        /// </remarks>
        internal void ReleaseOwnership(Action onReleased = null)
        {
            if (ownershipReleased)
            {
                if (onReleased == null)
                    return;
                List<Exception> immediateFailures = new();
                AttemptOwnershipReleaseCallbacks(onReleased, immediateFailures);
                ThrowOwnershipReleaseFailures(immediateFailures);
                return;
            }
            if (onReleased != null)
                ownershipReleasedCallbacks += onReleased;
            if (dispatchDepth > 0)
            {
                releaseRequested = true;
                return;
            }
            CompleteReleaseOwnership();
        }

        private void CompleteReleaseOwnership()
        {
            if (ownershipReleased)
                return;
            ownershipReleased = true;
            releaseRequested = false;
            Action callbacks = ownershipReleasedCallbacks;
            ownershipReleasedCallbacks = delegate { };
            List<Exception> failures = new();
            try
            {
                encounterLifetime.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                AppendCleanupFailures(failures, cleanupFailure);
            }
            AttemptOwnershipReleaseCallbacks(callbacks, failures);
            ThrowOwnershipReleaseFailures(failures);
        }

        private static void AttemptOwnershipReleaseCallbacks(
            Action callbacks,
            ICollection<Exception> failures
        )
        {
            foreach (Delegate callback in callbacks.GetInvocationList())
            {
                try
                {
                    ((Action)callback).Invoke();
                }
                catch (Exception callbackFailure)
                {
                    failures.Add(callbackFailure);
                }
            }
        }

        private static void ThrowOwnershipReleaseFailures(IReadOnlyList<Exception> failures)
        {
            if (failures.Count == 0)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(
                "Encounter cleanup and ownership-release callbacks failed.",
                failures
            );
        }

        private static void AppendCleanupFailures(
            ICollection<Exception> failures,
            Exception failure
        )
        {
            if (failure is AggregateException aggregate)
            {
                foreach (Exception innerFailure in aggregate.InnerExceptions)
                    AppendCleanupFailures(failures, innerFailure);
                return;
            }
            failures.Add(failure);
        }

        /// <summary>Transfers one encounter-scoped resource to the composite release boundary.</summary>
        internal void OwnEncounterResource(IDisposable resource) => encounterLifetime.Add(resource);

        /// <summary>Gets current rules-native Stride availability for a registered creature.</summary>
        /// <param name="creature">The registered mover.</param>
        /// <returns>The typed available or unavailable preview state.</returns>
        public ActionAvailability GetStrideAvailability(CreatureId creature)
        {
            return strideDefinition.GetAvailability(Snapshot, creature);
        }

        /// <summary>Creates the frozen typed selection workflow for a registered mover.</summary>
        /// <param name="creature">The registered mover.</param>
        /// <returns>The Stride path-selection workflow.</returns>
        public SelectionWorkflow<MovementPath> CreateStrideSelectionWorkflow(CreatureId creature)
        {
            return strideDefinition.CreateSelectionWorkflow(Snapshot, creature);
        }

        /// <summary>Dispatches one rules-native Stride path.</summary>
        /// <param name="creature">The registered mover.</param>
        /// <param name="path">The exact path selected before dispatch.</param>
        /// <returns>The structural root result and committed movement facts.</returns>
        public async ValueTask<OpResult<MovePathOutcome>> DispatchStride(
            CreatureId creature,
            MovementPath path
        )
        {
            BeginResolution();
            try
            {
                return await dispatcher.Dispatch(new StrideActionOp(creature, path));
            }
            finally
            {
                EndResolution();
            }
        }

        /// <summary>
        /// Dispatches Stride while one root-scoped observer queues committed movement projection.
        /// </summary>
        /// <param name="creature">The registered mover.</param>
        /// <param name="path">The exact completed selection.</param>
        /// <param name="projection">The projection observer retained for this root only.</param>
        /// <returns>
        /// Whether the rules root resolved. Presentation and exploration route continuation are
        /// owned by the caller after this mechanically complete dispatch.
        /// </returns>
        public async ValueTask<bool> DispatchProjectedStride(
            CreatureId creature,
            MovementPath path,
            IFactObserver<TokenMovedFact> projection
        )
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            using (dispatcher.RegisterFactObserver(projection))
            {
                OpResult<MovePathOutcome> result = await DispatchStride(creature, path);
                return result is ResolvedOpResult<MovePathOutcome>;
            }
        }

        /// <summary>Replaces topology after a live grid mutation and before another rules root.</summary>
        /// <param name="tiles">The current initialized grid tiles.</param>
        public void RefreshTopology(Tile[,] tiles)
        {
            GridTopology topology = CreateTopology(tiles);
            topologyProvider.Replace(topology);
            composition.RefreshTopology(tiles);
            currentTiles = tiles;
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
        ) =>
            DispatchNow(
                new ApplyDamageOp(target, finalDamage, AllocateHealthOrigin(source), source)
            );

        /// <summary>Commits healing.</summary>
        /// <param name="target">The registered creature to heal.</param>
        /// <param name="healing">The non-negative healing offered.</param>
        /// <param name="source">The rule source responsible for the healing.</param>
        /// <returns>The exact committed healing outcome.</returns>
        public HealingOutcome ApplyHealing(CreatureId target, int healing, RuleSource source) =>
            DispatchNow(new ApplyHealingOp(target, healing, AllocateHealthOrigin(source), source));

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
                new GrantTemporaryHitPointsOp(target, amount, AllocateHealthOrigin(source), source)
            );

        /// <summary>Removes temporary Hit Points owned by the supplied source.</summary>
        /// <param name="target">The registered creature whose pool may be removed.</param>
        /// <param name="source">The source that must own the active pool.</param>
        /// <returns>The exact amount removed.</returns>
        public TemporaryHitPointsRemovalOutcome RemoveTemporaryHitPoints(
            CreatureId target,
            RuleSource source
        ) =>
            DispatchNow(
                new RemoveTemporaryHitPointsOp(target, AllocateHealthOrigin(source), source)
            );

        /// <summary>Adds temporary Hit Point immunity for the supplied source.</summary>
        /// <param name="target">The registered creature receiving immunity.</param>
        /// <param name="source">The source whose future grants will be blocked.</param>
        /// <returns>Whether a new immunity was committed.</returns>
        public TemporaryHitPointImmunityOutcome AddTemporaryHitPointImmunity(
            CreatureId target,
            RuleSource source
        ) =>
            DispatchNow(
                new AddTemporaryHitPointImmunityOp(target, AllocateHealthOrigin(source), source)
            );

        internal UnityCombatantEnrollmentBuilder CreateCombatantEnrollmentBuilder(
            ActionController controller,
            CompositeLifetime preparationLifetime
        )
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
            return new UnityCombatantEnrollmentBuilder(
                controller,
                creature,
                new CreatureState(creatureId, playerId),
                creature.GetHealthInitializationState(),
                new GridPosition(position.x, position.y, position.z),
                new GridDistance(speedFeet),
                preparationLifetime
            );
        }

        private PlayerId GetPlayerId(ActionController controller)
        {
            Team team = controller.GetComponent<Team>();
            string teamName = team == null ? string.Empty : team.Name;
            if (!string.IsNullOrWhiteSpace(teamName))
            {
                if (playerIds.TryGetValue(teamName, out PlayerId existing))
                    return existing;
                PlayerId playerId = new PlayerId($"combat-side-{playerIds.Count + 1}");
                playerIds.Add(teamName, playerId);
                strideFriendshipProvider.Register(playerId, teamName);
                return playerId;
            }
            return new PlayerId($"combat-side-unassigned-{nextCreatureId}");
        }

        private CreatureId AllocateCreatureId()
        {
            nextCreatureId++;
            return new CreatureId($"combat-creature-{nextCreatureId}");
        }

        internal HealthChangeOriginId AllocateHealthOrigin(RuleSource source)
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
            OpResult<TResult> result = DispatchResultNow(operation);
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("The synchronous rules request did not resolve.");
        }

        /// <summary>Dispatches one prepared enrollment operation and requires resolution.</summary>
        internal TResult DispatchRequired<TResult>(IRuleOp<TResult> operation) =>
            DispatchNow(operation);

        private OpResult<TResult> DispatchResultNow<TResult>(IRuleOp<TResult> operation)
        {
            BeginResolution();
            try
            {
                ValueTask<OpResult<TResult>> pending = dispatcher.Dispatch(operation);
                if (!pending.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "Synchronous Unity rules requests cannot contain asynchronous callbacks."
                    );
                }
                return pending.GetAwaiter().GetResult();
            }
            finally
            {
                EndResolution();
            }
        }

        private void BeginResolution()
        {
            topologyProvider.BeginResolution();
            dispatchDepth++;
        }

        private void EndResolution()
        {
            try
            {
                topologyProvider.EndResolution();
            }
            finally
            {
                dispatchDepth--;
                if (dispatchDepth == 0 && releaseRequested)
                    CompleteReleaseOwnership();
            }
        }

        internal void EnqueueEncounterPresentation(OpId rootId, Action presentation)
        {
            if (rootId.IsEmpty)
                throw new ArgumentException(
                    "Encounter presentation requires a committed root-owned Fact.",
                    nameof(rootId)
                );
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation));
            if (!encounterPresentationByRoot.TryGetValue(rootId, out Queue<Action> callbacks))
            {
                callbacks = new Queue<Action>();
                encounterPresentationByRoot.Add(rootId, callbacks);
            }
            callbacks.Enqueue(presentation);
        }

        internal void RecordSettledEncounterRoot(OpId root, OpId? parent)
        {
            if (!settledEncounterPresentationRoots.Add(root))
                throw new InvalidOperationException(
                    $"Encounter presentation root {root.Value} settled more than once."
                );
            if (!parent.HasValue)
                return;
            if (!encounterPresentationChildren.TryGetValue(parent.Value, out List<OpId> children))
            {
                children = new List<OpId>();
                encounterPresentationChildren.Add(parent.Value, children);
            }
            children.Add(root);
        }

        internal void DrainEncounterPresentationTree(OpId root)
        {
            if (encounterPresentationByRoot.TryGetValue(root, out Queue<Action> callbacks))
            {
                encounterPresentationByRoot.Remove(root);
                while (callbacks.Count > 0)
                    callbacks.Dequeue().Invoke();
            }
            if (encounterPresentationChildren.TryGetValue(root, out List<OpId> children))
            {
                foreach (OpId child in children)
                {
                    if (!settledEncounterPresentationRoots.Contains(child))
                        throw new InvalidOperationException(
                            $"Causal encounter presentation root {child.Value} did not settle."
                        );
                    DrainEncounterPresentationTree(child);
                }
            }
            encounterPresentationChildren.Remove(root);
            settledEncounterPresentationRoots.Remove(root);
        }

        internal void AddRegistrationMaps(
            ActionController controller,
            CreatureComponent creature,
            CreatureId id
        )
        {
            controllerIds.Add(controller, id);
            controllers.Add(id, controller);
            creatureIds.Add(creature, id);
            creatures.Add(id, creature);
        }

        internal void RemoveRegistrationMaps(
            ActionController controller,
            CreatureComponent creature,
            CreatureId id
        )
        {
            controllerIds.Remove(controller);
            controllers.Remove(id);
            creatureIds.Remove(creature);
            creatures.Remove(id);
        }

        internal static void SeedExploration(RulesStateSeed seed, CombatantRulesState state)
        {
            CreatureId id = state.Creature.Id;
            seed.SeedCreature(state.Creature)
                .SeedHealth(id, state.Health)
                .SeedPosition(id, state.Position)
                .SeedLandSpeed(id, state.LandSpeed)
                .SeedMultipleAttackPenalty(id, new MultipleAttackPenaltyState(0));
            foreach (SpellSlotState slot in state.SpellSlots)
                seed.SeedSpellSlot(slot);
            foreach (ActiveRuleBinding binding in state.RuleBindings)
                seed.SeedRuleBinding(binding);
            foreach (EquipmentState item in state.Equipment)
                seed.SeedEquipment(item);
            foreach (AmmunitionState pool in state.Ammunition)
                seed.SeedAmmunition(pool);
            foreach (ActiveEffectInstance effect in state.ActiveEffects)
                seed.SeedActiveEffect(effect);
            seed.SeedActionEconomy(id, new ActionEconomyState(1, false));
        }

        internal static void ValidateControllers(
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
            CreatureComponent[] creatures = controllers
                .Select(controller => controller.GetComponent<CreatureComponent>())
                .ToArray();
            if (creatures.Any(creature => creature == null))
                throw new ArgumentException(
                    "Every combat controller requires a creature component.",
                    parameterName
                );
            if (creatures.Distinct().Count() != creatures.Length)
                throw new ArgumentException(
                    "A creature cannot be registered through more than one controller.",
                    parameterName
                );
        }

        /// <summary>Raises the Unity encounter-start projection after its committed Fact.</summary>
        internal void ProjectEncounterStarted() => EncounterStarted.Invoke();

        /// <summary>Projects one committed turn start into the Unity controller boundary.</summary>
        internal void ProjectTurnBegan(TurnIdentity turn)
        {
            GetController(turn.Actor).StartTurn();
            TurnBegan.Invoke(turn);
        }

        /// <summary>Projects one committed turn end into the Unity controller boundary.</summary>
        internal void ProjectTurnEnded(TurnIdentity turn)
        {
            GetController(turn.Actor).ResetEncounterTurnState();
            TurnEnded.Invoke(turn);
        }

        /// <summary>Raises the Unity encounter-end projection after causal settlement.</summary>
        internal void ProjectEncounterEnded(EncounterOutcome outcome) =>
            EncounterEnded.Invoke(outcome);

        private Exception CreateConstructionFailure(
            Exception constructionFailure,
            UnityCombatantEnrollmentPlan enrollment
        )
        {
            List<Exception> failures = new() { constructionFailure };
            try
            {
                enrollment.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }
            try
            {
                encounterLifetime.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                failures.Add(cleanupFailure);
            }
            if (failures.Count == 1)
                return constructionFailure;
            return new AggregateException(
                "Encounter construction and its ownership cleanup both failed.",
                failures
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

        private sealed class UnityTeamStrideFriendshipProvider : IStrideFriendshipProvider
        {
            private readonly Dictionary<PlayerId, string> teamNames = new();

            public void Register(PlayerId player, string teamName) =>
                teamNames.Add(player, teamName);

            internal void Reset(IEnumerable<KeyValuePair<string, PlayerId>> players)
            {
                teamNames.Clear();
                foreach (KeyValuePair<string, PlayerId> pair in players)
                    teamNames.Add(pair.Value, pair.Key);
            }

            /// <inheritdoc/>
            public bool IsFriendly(PlayerId mover, PlayerId occupant)
            {
                if (
                    teamNames.TryGetValue(mover, out string moverTeam)
                    && teamNames.TryGetValue(occupant, out string occupantTeam)
                    && TeamRules.TryGetInstance(out TeamRules rules)
                    && rules.Contains(moverTeam)
                    && rules.Contains(occupantTeam)
                )
                {
                    return rules.IsFriendly(moverTeam, occupantTeam);
                }

                return mover == occupant;
            }
        }

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

        private static ActionController[] FindExplorationControllers(
            ActionController leader,
            Tile[,] tiles,
            IExplorationStrideCoordinator exploration
        )
        {
            return tiles
                .Cast<Tile>()
                .Where(tile => tile != null)
                .SelectMany(tile => tile.Occupants)
                .Where(occupant => occupant != null)
                .Select(occupant => occupant.GetComponent<ActionController>())
                // Exploration formation projection owns party occupancy, including swaps. Keep
                // other exploring PCs out of this temporary one-action snapshot so the shared
                // combat validator can continue rejecting occupied destinations in Tactics.
                .Where(controller =>
                    controller != null
                    && !exploration.IsPartyMember(controller.gameObject)
                    && controller.GetComponent<CreatureComponent>() != null
                )
                .Prepend(leader)
                .Distinct()
                .ToArray();
        }
    }
}
