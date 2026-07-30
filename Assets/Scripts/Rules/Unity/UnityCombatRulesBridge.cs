using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Light;
using Game.Rules.Unity.Spells;
using Game.Rules.Unity.Strike;
using Game.Strikes;
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
        private readonly UnityStrikeContext strikeContext;
        private readonly UnitySpellAttackContext spellAttackContext;
        private readonly RuleDispatcher dispatcher;
        private readonly UnitySpellDefinitionCatalog spellCatalog;
        private readonly ISpellActionCatalog spellActionCatalog;
        private readonly IReadOnlyList<IDisposable> presentationResources;
        private readonly Dictionary<OpId, Queue<Action>> encounterPresentationByRoot = new();
        private readonly Dictionary<OpId, List<OpId>> encounterPresentationChildren = new();
        private readonly HashSet<OpId> settledEncounterPresentationRoots = new();
        private readonly EncounterRootSettlementObserver encounterSettlementObserver;
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
            IRollService rollService
        )
        {
            currentTiles = tiles;
            topologyProvider = new MutableGridTopologyProvider(CreateTopology(tiles));
            strideDefinition = new StrideActionDefinition(
                topologyProvider,
                strideFriendshipProvider
            );
            RulesStateSeed seed = new RulesStateSeed();
            foreach (ActionController controller in encounterControllers)
            {
                CombatantRegistration registration = CreateRegistration(controller);
                if (attachControllers)
                {
                    registration.Controller.ValidateCombatRulesAttachment(
                        this,
                        registration.State.Creature.Id
                    );
                    registration.Creature.ValidateHealthRulesAttachment(
                        this,
                        registration.State.Creature.Id
                    );
                }
                AddRegistrationMaps(registration);
                Seed(seed, registration.State);
                if (!attachControllers)
                {
                    seed.SeedActionEconomy(
                        registration.State.Creature.Id,
                        new ActionEconomyState(1, false)
                    );
                }
            }
            strikeContext = new UnityStrikeContext(creatures, tiles, seed);
            spellAttackContext = new UnitySpellAttackContext(creatures, tiles);
            RageActionDefinition rageDefinition = new RageActionDefinition(
                new UnityRageActorStateProvider(creatures)
            );
            spellCatalog = UnitySpellDefinitionCatalog.Load();
            UnitySpellBookProvider spellBooks = new(creatures);
            RuleRegistryBuilder registryBuilder = new RuleRegistryBuilder();
            RageRules.DefineRuleBindings(registryBuilder);
            registryBuilder.AddOutcomeRule();
            foreach (
                RuleDefinitionId definitionId in spellCatalog
                    .Definitions.SelectMany(definition => definition.Effects)
                    .Select(effect => effect.DefinitionId)
                    .Distinct()
            )
                registryBuilder.Define(definitionId);
            RuleRegistry registry = registryBuilder.Build();
            CombatActionCatalog actionCatalog = new CombatActionCatalog(
                strideDefinition,
                strikeContext,
                spellCatalog,
                spellBooks,
                rageDefinition
            );
            spellActionCatalog = actionCatalog;

            dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(seed),
                rollService ?? throw new ArgumentNullException(nameof(rollService))
            )
                .UseHealthRules()
                .UseCombatRuntimeRules()
                .UseCheckResolution()
                .UseActiveEffectRules(registry)
                .UseEncounterRules(
                    new IEncounterTurnStartAdapter[]
                    {
                        new RottingAuraTurnStartAdapter(this),
                        new SlowedTurnStartAdapter(this),
                    }
                )
                .UseActionLifecycle(actionCatalog)
                .UseMovementRules(topologyProvider)
                .UseStrideRules(strideDefinition)
                .UseRageRules(rageDefinition)
                .UseSpellcastingRules(actionCatalog, spellAttackContext)
                .UseStrikeRules(strikeContext, strikeContext, strikeContext)
                .Build();
            dispatcher.RegisterFactObserver<AmmunitionSpentFact>(strikeContext);
            dispatcher.RegisterFactObserver<StrikeItemLoadedChangedFact>(strikeContext);
            dispatcher.RegisterResolvedOpObserver<CastSpellActionOp, CastSpellOutcome>(
                new UnityResolvedSpellCastPresentationObserver(creatures, spellCatalog)
            );
            dispatcher.RegisterResolvedOpObserver<ResolveSpellAttackOp, SpellAttackResolution>(
                new UnitySpellAttackPresentationObserver(creatures, spellCatalog)
            );
            UnityLightEffectPresentationObserver effectPresentation =
                UnityLightEffectPresentationObserver.Create(spellCatalog, creatures);
            presentationResources = new IDisposable[] { effectPresentation };
            dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(effectPresentation);
            dispatcher.RegisterFactObserver<ActiveEffectExpiredFact>(effectPresentation);
            dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(effectPresentation);
            dispatcher.RegisterFactObserver<EncounterEndedFact>(effectPresentation);
            EncounterProjectionObserver encounterProjection = new EncounterProjectionObserver(this);
            encounterSettlementObserver = new EncounterRootSettlementObserver(this);
            dispatcher.RegisterRootSettlementObserver(encounterSettlementObserver);
            dispatcher.RegisterCausalTreeSettlementObserver(encounterSettlementObserver);
            dispatcher.RegisterFactObserver<EncounterStartedFact>(encounterProjection);
            dispatcher.RegisterFactObserver<TurnBeganFact>(encounterProjection);
            dispatcher.RegisterFactObserver<TurnEndedFact>(encounterProjection);
            dispatcher.RegisterFactObserver<EncounterEndedFact>(encounterProjection);
            if (attachControllers)
            {
                Dictionary<
                    ActionController,
                    UnityStrikeActionInstallationPlan
                > strikeInstallationPlans = new();
                Dictionary<
                    ActionController,
                    UnitySpellActionInstallationPlan
                > spellInstallationPlans = new();
                foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                {
                    strikeInstallationPlans.Add(
                        entry.Key,
                        UnityStrikeActionInstaller.Prepare(entry.Key, entry.Value, strikeContext)
                    );
                    spellInstallationPlans.Add(
                        entry.Key,
                        UnitySpellActionInstaller.Prepare(entry.Key, entry.Value, actionCatalog)
                    );
                }
                UnityStrikePresentationObserver strikePresentation =
                    new UnityStrikePresentationObserver(controllers, creatures, strikeContext);
                dispatcher.RegisterResolvedOpObserver<ResolveStrikeOp, StrikeResolution>(
                    strikePresentation
                );
                dispatcher.RegisterResolvedOpObserver<StrikeActionOp, StrikeResolution>(
                    strikePresentation
                );
                List<CreatureComponent> attachedCreatures = new();
                List<ActionController> attachedControllers = new();
                try
                {
                    RegisterHealthProjection();
                    foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
                    {
                        entry.Key.AttachHealthRules(this, entry.Value);
                        attachedCreatures.Add(entry.Key);
                    }
                    foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                    {
                        entry.Key.AttachCombatRules(this, entry.Value);
                        attachedControllers.Add(entry.Key);
                    }
                    foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                    {
                        strikeInstallationPlans[entry.Key].Apply();
                        spellInstallationPlans[entry.Key].Apply();
                    }
                }
                catch
                {
                    foreach (ActionController controller in attachedControllers)
                        controller.DetachCombatRules(this);
                    foreach (CreatureComponent creature in attachedCreatures)
                    {
                        CreatureId id = creatureIds[creature];
                        creature.DetachHealthRules(this, GetHealth(id));
                    }
                    throw;
                }
            }
        }

        /// <summary>Creates the complete rules composition for one combat encounter.</summary>
        /// <param name="encounterControllers">The non-empty, unique participant sequence.</param>
        /// <param name="tiles">The initialized live grid used to take an immutable topology snapshot.</param>
        /// <returns>The initialized encounter rules bridge.</returns>
        public static UnityCombatRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            Tile[,] tiles
        ) => Create(encounterControllers, tiles, new RandomRollService());

        /// <summary>
        /// Creates combat rules with an explicit deterministic or production roll source.
        /// </summary>
        /// <param name="encounterControllers">The non-empty, unique participant sequence.</param>
        /// <param name="tiles">The initialized live grid.</param>
        /// <param name="rollService">The required source for all rules-owned rolls.</param>
        /// <returns>The initialized encounter rules bridge.</returns>
        public static UnityCombatRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            Tile[,] tiles,
            IRollService rollService
        )
        {
            if (encounterControllers == null)
                throw new ArgumentNullException(nameof(encounterControllers));
            ActionController[] copied = encounterControllers.ToArray();
            ValidateControllers(copied, nameof(encounterControllers));
            ValidateTiles(tiles);
            return new UnityCombatRulesBridge(copied, tiles, true, rollService);
        }

        /// <summary>
        /// Creates an isolated one-action Stride composition for movement outside initiative.
        /// </summary>
        /// <param name="controller">The exploration leader selecting and committing the Stride.</param>
        /// <param name="tiles">The initialized live grid used for immutable topology.</param>
        /// <returns>A temporary rules composition that is not attached as combat authority.</returns>
        /// <remarks>
        /// Exploration has no combat action economy. The temporary action exists only so the same
        /// Stride definition, validation, operation, reducers, and Facts own the selected path.
        /// A later encounter composition is seeded from the fully projected boundary position.
        /// </remarks>
        public static UnityCombatRulesBridge CreateExplorationStride(
            ActionController controller,
            Tile[,] tiles
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            ValidateTiles(tiles);
            UnityCombatRulesBridge bridge = new UnityCombatRulesBridge(
                FindExplorationControllers(controller, tiles),
                tiles,
                false,
                new RandomRollService()
            );
            bridge.strideFriendshipProvider.AllowFriendlyTraversal = false;
            return bridge;
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
            Snapshot.Encounters.Any(pair =>
                pair.Value.Phase == EncounterPhase.Active
                && pair.Value.CurrentTurn.HasValue
                && pair.Value.CurrentTurn.Value.Actor == creature
            );

        /// <summary>Starts initiative and advances to the first eligible turn.</summary>
        /// <param name="protagonistTeamName">The registered team used for player-relative outcome.</param>
        /// <returns>The authoritative state after start-turn causal work settles.</returns>
        public EncounterState StartEncounter(string protagonistTeamName)
        {
            if (
                string.IsNullOrWhiteSpace(protagonistTeamName)
                || !playerIds.TryGetValue(protagonistTeamName, out PlayerId protagonistTeam)
            )
                throw new ArgumentException(
                    "The protagonist team must be registered in this composition.",
                    nameof(protagonistTeamName)
                );
            return StartEncounter(protagonistTeam);
        }

        /// <summary>Starts initiative for a registered protagonist team identity.</summary>
        /// <param name="protagonistTeam">The registered player-relative protagonist team.</param>
        /// <returns>The authoritative state after start-turn causal work settles.</returns>
        public EncounterState StartEncounter(PlayerId protagonistTeam)
        {
            if (
                protagonistTeam.IsEmpty
                || !Snapshot.Creatures.Any(pair => pair.Value.Player == protagonistTeam)
            )
                throw new ArgumentException(
                    "The protagonist team must be registered in this composition.",
                    nameof(protagonistTeam)
                );
            EncounterParticipant[] participants = controllers
                .Select(pair => new EncounterParticipant(
                    pair.Key,
                    Snapshot.Creatures[pair.Key].Player,
                    creatures[pair.Key].GetInitiative()
                ))
                .ToArray();
            EncounterStartOutcome outcome = DispatchNow(
                new StartEncounterOp(encounterId, protagonistTeam, participants)
            );
            return outcome.State;
        }

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

        /// <summary>Spends actions for a feature still using the legacy Unity action path.</summary>
        /// <param name="creature">The registered creature paying the cost.</param>
        /// <param name="amount">The positive action count to spend.</param>
        public void SpendLegacyActions(CreatureId creature, int amount)
        {
            DispatchNow(new SpendLegacyActionsOp(creature, amount));
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
                throw new InvalidOperationException("The encounter has not started.");
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

        /// <summary>Registers dungeon reinforcements in the existing encounter store.</summary>
        /// <param name="reinforcements">New, unique controllers not already registered.</param>
        public void RegisterCombatants(IEnumerable<ActionController> reinforcements)
        {
            if (reinforcements == null)
                throw new ArgumentNullException(nameof(reinforcements));
            ActionController[] copied = reinforcements.ToArray();
            ValidateControllers(copied, nameof(reinforcements));
            if (copied.Any(controllerIds.ContainsKey))
                throw new InvalidOperationException("A reinforcement is already registered.");
            CreatureId ownershipProbe = new CreatureId("reinforcement-ownership-probe");
            foreach (ActionController controller in copied)
            {
                CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                if (creature == null)
                    throw new InvalidOperationException(
                        "Every combat controller requires a creature component."
                    );
                controller.ValidateCombatRulesAttachment(this, ownershipProbe);
                creature.ValidateHealthRulesAttachment(this, ownershipProbe);
            }

            CombatantRegistration[] registrations = copied.Select(CreateRegistration).ToArray();
            foreach (CombatantRegistration registration in registrations)
            {
                registration.Controller.ValidateCombatRulesAttachment(
                    this,
                    registration.State.Creature.Id
                );
                registration.Creature.ValidateHealthRulesAttachment(
                    this,
                    registration.State.Creature.Id
                );
            }
            StrikeCombatantRegistration[] strikeRegistrations = new StrikeCombatantRegistration[
                registrations.Length
            ];
            Dictionary<
                ActionController,
                UnityStrikeActionInstallationPlan
            > strikeInstallationPlans = new();
            Dictionary<ActionController, UnitySpellActionInstallationPlan> spellInstallationPlans =
                new();
            try
            {
                foreach (CombatantRegistration registration in registrations)
                    AddRegistrationMaps(registration);
                for (int index = 0; index < registrations.Length; index++)
                {
                    CombatantRegistration registration = registrations[index];
                    spellInstallationPlans.Add(
                        registration.Controller,
                        UnitySpellActionInstaller.Prepare(
                            registration.Controller,
                            registration.State.Creature.Id,
                            spellActionCatalog
                        )
                    );
                    strikeRegistrations[index] = strikeContext.RegisterReinforcement(
                        registration.State.Creature.Id,
                        registration.Creature
                    );
                    strikeInstallationPlans.Add(
                        registration.Controller,
                        UnityStrikeActionInstaller.Prepare(
                            registration.Controller,
                            registration.State.Creature.Id,
                            strikeContext
                        )
                    );
                }
            }
            catch
            {
                foreach (CombatantRegistration registration in registrations)
                {
                    strikeContext.UnregisterReinforcement(registration.State.Creature.Id);
                    RemoveRegistrationMaps(registration);
                }
                throw;
            }
            bool joiningEncounter = Snapshot.Encounters.Contains(encounterId);
            try
            {
                if (joiningEncounter)
                {
                    DispatchNow(
                        new JoinEncounterOp(
                            encounterId,
                            registrations.Select(registration => new EncounterJoinParticipant(
                                new EncounterParticipant(
                                    registration.State.Creature.Id,
                                    registration.State.Creature.Player,
                                    registration.Creature.GetInitiative()
                                ),
                                registration.State
                            ))
                        )
                    );
                }
                else
                {
                    foreach (CombatantRegistration registration in registrations)
                        RequireSuccess(DispatchNow(new RegisterCombatantOp(registration.State)));
                }
            }
            catch
            {
                foreach (CombatantRegistration registration in registrations)
                {
                    RemoveRegistrationMaps(registration);
                    strikeContext.UnregisterReinforcement(registration.State.Creature.Id);
                }
                throw;
            }
            for (int index = 0; index < registrations.Length; index++)
            {
                CombatantRegistration registration = registrations[index];
                RequireResolved(
                    DispatchResultNow(new RegisterStrikeCombatantOp(strikeRegistrations[index]))
                );
                registration.Creature.AttachHealthRules(this, registration.State.Creature.Id);
                registration.Controller.AttachCombatRules(this, registration.State.Creature.Id);
                strikeInstallationPlans[registration.Controller].Apply();
                spellInstallationPlans[registration.Controller].Apply();
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
                onReleased?.Invoke();
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
            if (encounterSettlementObserver != null)
            {
                dispatcher.UnregisterRootSettlementObserver(encounterSettlementObserver);
                dispatcher.UnregisterCausalTreeSettlementObserver(encounterSettlementObserver);
            }
            foreach (IDisposable resource in presentationResources)
                resource.Dispose();

            foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
            {
                if (entry.Key != null)
                    entry.Key.DetachHealthRules(this, GetHealth(entry.Value));
            }

            foreach (ActionController controller in controllerIds.Keys)
            {
                if (controller != null)
                    controller.DetachCombatRules(this);
            }
            Action callbacks = ownershipReleasedCallbacks;
            ownershipReleasedCallbacks = delegate { };
            callbacks.Invoke();
        }

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
            topologyProvider.BeginResolution();
            try
            {
                return await dispatcher.Dispatch(new StrideActionOp(creature, path));
            }
            finally
            {
                topologyProvider.EndResolution();
            }
        }

        /// <summary>
        /// Dispatches Stride while awaiting one Unity projection for each committed movement Fact.
        /// </summary>
        /// <param name="creature">The registered mover.</param>
        /// <param name="path">The exact completed selection.</param>
        /// <param name="projection">The projection observer retained for this root only.</param>
        /// <returns>
        /// Whether the rules root resolved, including a boundary step whose obsolete exploration
        /// suffix was intentionally abandoned after combat took authority.
        /// </returns>
        public async ValueTask<bool> DispatchProjectedStride(
            CreatureId creature,
            MovementPath path,
            IFactObserver<TokenMovedFact> projection
        )
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));
            dispatcher.RegisterFactObserver(projection);
            try
            {
                try
                {
                    OpResult<MovePathOutcome> result = await DispatchStride(creature, path);
                    return result is ResolvedOpResult<MovePathOutcome>;
                }
                catch (ExplorationStrideProjectionInterruptedException)
                {
                    // The committed boundary step has already been projected. Encounter startup
                    // creates the next authoritative composition from that Unity position, so the
                    // temporary exploration root must not project its uncommitted path suffix.
                    return true;
                }
            }
            finally
            {
                dispatcher.UnregisterFactObserver(projection);
            }
        }

        /// <summary>Replaces topology after a live grid mutation and before another rules root.</summary>
        /// <param name="tiles">The current initialized grid tiles.</param>
        public void RefreshTopology(Tile[,] tiles)
        {
            GridTopology topology = CreateTopology(tiles);
            topologyProvider.Replace(topology);
            strikeContext.ReplaceTiles(tiles);
            spellAttackContext.ReplaceTiles(tiles);
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
            RageActorState rageState = UnityRageActorStateProvider.CreateState(creature);
            CombatantRulesState state = new CombatantRulesState(
                new CreatureState(creatureId, playerId),
                creature.GetHealthInitializationState(),
                new GridPosition(position.x, position.y, position.z),
                new GridDistance(speedFeet),
                (creature.Prepared?.SpellBook ?? EmptySpellBook.Instance).CreateInitialSlotStates(
                    creatureId
                ),
                RageRules.CreateInitialBindings(creatureId, rageState)
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
            OpResult<TResult> result = DispatchResultNow(operation);
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("The synchronous rules request did not resolve.");
        }

        private OpResult<TResult> DispatchResultNow<TResult>(IRuleOp<TResult> operation)
        {
            dispatchDepth++;
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
                return pending.GetAwaiter().GetResult();
            }
            finally
            {
                topologyProvider.EndResolution();
                dispatchDepth--;
                if (dispatchDepth == 0 && releaseRequested)
                    CompleteReleaseOwnership();
            }
        }

        private void EnqueueEncounterPresentation(RuleFact fact, Action presentation)
        {
            if (fact == null || !fact.IsStamped)
                throw new ArgumentException(
                    "Encounter presentation requires a committed root-owned Fact.",
                    nameof(fact)
                );
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation));
            if (
                !encounterPresentationByRoot.TryGetValue(fact.RootOpId, out Queue<Action> callbacks)
            )
            {
                callbacks = new Queue<Action>();
                encounterPresentationByRoot.Add(fact.RootOpId, callbacks);
            }
            callbacks.Enqueue(presentation);
        }

        private void RecordSettledEncounterRoot(OpId root, OpId? parent)
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

        private void DrainEncounterPresentationTree(OpId root)
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

        private static void RequireSuccess(CombatRuntimeOutcome outcome)
        {
            if (!outcome.Succeeded)
                throw new InvalidOperationException(outcome.Reason);
        }

        private static TResult RequireResolved<TResult>(OpResult<TResult> result)
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException("The rules request did not resolve.");
        }

        private void RegisterHealthProjection()
        {
            HealthProjectionObserver observer = new HealthProjectionObserver(creatures);
            dispatcher.RegisterFactObserver<HealthFact>(observer);
            dispatcher.RegisterFactObserver<CreatureDefeatCommittedFact>(observer);
        }

        private void AddRegistrationMaps(CombatantRegistration registration)
        {
            CreatureId id = registration.State.Creature.Id;
            controllerIds.Add(registration.Controller, id);
            controllers.Add(id, registration.Controller);
            creatureIds.Add(registration.Creature, id);
            creatures.Add(id, registration.Creature);
        }

        private void RemoveRegistrationMaps(CombatantRegistration registration)
        {
            CreatureId id = registration.State.Creature.Id;
            controllerIds.Remove(registration.Controller);
            controllers.Remove(id);
            creatureIds.Remove(registration.Creature);
            creatures.Remove(id);
        }

        private static void Seed(RulesStateSeed seed, CombatantRulesState state)
        {
            CreatureId id = state.Creature.Id;
            seed.SeedCreature(state.Creature)
                .SeedHealth(id, state.Health)
                .SeedPosition(id, state.Position)
                .SeedLandSpeed(id, state.LandSpeed)
                .SeedActionEconomy(id, new ActionEconomyState(0, false))
                .SeedMultipleAttackPenalty(id, new MultipleAttackPenaltyState(0));
            foreach (SpellSlotState slot in state.SpellSlots)
                seed.SeedSpellSlot(slot);
            foreach (ActiveRuleBinding binding in state.RuleBindings)
                seed.SeedRuleBinding(binding);
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
            internal bool AllowFriendlyTraversal { get; set; } = true;
            private readonly Dictionary<PlayerId, string> teamNames = new();

            public void Register(PlayerId player, string teamName) =>
                teamNames.Add(player, teamName);

            /// <inheritdoc/>
            public bool IsFriendly(PlayerId mover, PlayerId occupant)
            {
                if (!AllowFriendlyTraversal)
                    return false;
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

        private sealed class CombatActionCatalog
            : IActionCatalog,
                IStrikeActionCatalog,
                ISpellActionCatalog
        {
            private readonly StrideActionDefinition stride;
            private readonly IStrikeActionCatalog strike;
            private readonly ISpellDefinitionCatalog spell;
            private readonly ISpellBookProvider spellBooks;
            private readonly IReadOnlyList<IActionCatalog> featureCatalogs;

            public CombatActionCatalog(
                StrideActionDefinition stride,
                IStrikeActionCatalog strike,
                ISpellDefinitionCatalog spell,
                ISpellBookProvider spellBooks,
                params IActionCatalog[] featureCatalogs
            )
            {
                this.stride = stride ?? throw new ArgumentNullException(nameof(stride));
                this.strike = strike ?? throw new ArgumentNullException(nameof(strike));
                this.spell = spell ?? throw new ArgumentNullException(nameof(spell));
                this.spellBooks = spellBooks ?? throw new ArgumentNullException(nameof(spellBooks));
                if (featureCatalogs == null || featureCatalogs.Any(catalog => catalog == null))
                    throw new ArgumentException(
                        "Feature action catalogs cannot be null.",
                        nameof(featureCatalogs)
                    );
                this.featureCatalogs = Array.AsReadOnly(featureCatalogs.ToArray());
            }

            /// <inheritdoc/>
            public ActionProfile GetBaseProfile(ActionDefinitionId definitionId)
            {
                if (definitionId == StrideActionDefinition.DefinitionId)
                    return stride.GetBaseProfile(definitionId);
                if (definitionId == StrikeActionDefinition.DefinitionId)
                    throw new InvalidOperationException(
                        "Strike profiles require the selected item on StrikeActionOp."
                    );
                if (definitionId == ReloadActionDefinition.DefinitionId)
                    throw new InvalidOperationException(
                        "Reload profiles require the selected item on ReloadActionOp."
                    );
                foreach (IActionCatalog catalog in featureCatalogs)
                {
                    try
                    {
                        return catalog.GetBaseProfile(definitionId);
                    }
                    catch (KeyNotFoundException)
                    {
                        // Each feature catalog owns its definition IDs. Continue to the next
                        // composed feature without teaching this shared catalog feature names.
                    }
                }
                throw new KeyNotFoundException($"Unknown action definition '{definitionId}'.");
            }

            /// <inheritdoc/>
            public StrikeItemDefinition GetStrikeItem(ItemId item) => strike.GetStrikeItem(item);

            /// <inheritdoc/>
            public bool TryGetSpell(
                SpellReference reference,
                out Game.Rules.Runtime.SpellDefinition definition
            ) => spell.TryGetSpell(reference, out definition);

            /// <inheritdoc/>
            public ISpellBook GetSpellBook(CreatureId creature) =>
                spellBooks.GetSpellBook(creature);
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

        private sealed class RottingAuraTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityCombatRulesBridge owner;

            public RottingAuraTurnStartAdapter(UnityCombatRulesBridge owner) => this.owner = owner;

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                EncounterState encounter = context.Snapshot.Encounters[context.Encounter];
                ActionController actor = owner.GetController(context.Actor);
                ActionController[] combatants = encounter
                    .Roster.Where(entry => owner.GetHealth(entry.Creature).Current > 0)
                    .Select(entry => owner.GetController(entry.Creature))
                    .ToArray();
                await CreatureAuraResolver.ApplyTurnStartAurasAwaited(
                    actor,
                    combatants,
                    owner.currentTiles,
                    async (target, amount, source) =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        return await context.ApplyFinalDamage(
                            targetId,
                            amount,
                            owner.AllocateOrigin(source),
                            source
                        );
                    },
                    target =>
                    {
                        CreatureId targetId = owner.GetCreatureId(target);
                        if (!context.Snapshot.Health.TryGet(targetId, out HealthState health))
                            throw new InvalidOperationException(
                                "An aura target has no authoritative health state."
                            );
                        return health.Current > 0;
                    },
                    result =>
                    {
                        RottingAuraRule.Present(result);
                        return default;
                    }
                );
                return current;
            }
        }

        private sealed class SlowedTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityCombatRulesBridge owner;

            public SlowedTurnStartAdapter(UnityCombatRulesBridge owner) => this.owner = owner;

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            ) =>
                new ValueTask<TurnStartContribution>(
                    new TurnStartContribution(
                        checked((int)owner.GetController(context.Actor).CalculateTurnStartActions())
                    )
                );
        }

        private sealed class EncounterProjectionObserver
            : IFactObserver<EncounterStartedFact>,
                IFactObserver<TurnBeganFact>,
                IFactObserver<TurnEndedFact>,
                IFactObserver<EncounterEndedFact>
        {
            private readonly UnityCombatRulesBridge owner;

            public EncounterProjectionObserver(UnityCombatRulesBridge owner) => this.owner = owner;

            public ValueTask OnFactCommitted(
                EncounterStartedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                owner.EncounterStarted.Invoke();
                return default;
            }

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot currentSnapshot)
            {
                owner.EnqueueEncounterPresentation(
                    fact,
                    () =>
                    {
                        ActionController controller = owner.GetController(fact.Turn.Actor);
                        controller.StartTurn();
                        owner.TurnBegan.Invoke(fact.Turn);
                    }
                );
                return default;
            }

            public ValueTask OnFactCommitted(TurnEndedFact fact, RulesSnapshot currentSnapshot)
            {
                owner.EnqueueEncounterPresentation(
                    fact,
                    () =>
                    {
                        owner.GetController(fact.Turn.Actor).ResetEncounterTurnState();
                        owner.TurnEnded.Invoke(fact.Turn);
                    }
                );
                return default;
            }

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot currentSnapshot)
            {
                owner.EnqueueEncounterPresentation(
                    fact,
                    () => owner.EncounterEnded.Invoke(fact.Outcome)
                );
                return default;
            }
        }

        private sealed class EncounterRootSettlementObserver
            : IRootSettlementObserver,
                ICausalTreeSettlementObserver
        {
            private readonly UnityCombatRulesBridge owner;

            public EncounterRootSettlementObserver(UnityCombatRulesBridge owner) =>
                this.owner = owner;

            public ValueTask OnRootSettled(
                OpId rootId,
                OpId? causalParentRootId,
                RulesSnapshot snapshot
            )
            {
                owner.RecordSettledEncounterRoot(rootId, causalParentRootId);
                return default;
            }

            public ValueTask OnCausalTreeSettled(OpId rootId, RulesSnapshot snapshot)
            {
                owner.DrainEncounterPresentationTree(rootId);
                return default;
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
                IFactObserver<CreatureDefeatCommittedFact>
        {
            private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

            public HealthProjectionObserver(
                IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
            ) => this.creatures = creatures;

            public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot currentSnapshot)
            {
                CreatureComponent creature = RequireCreature(fact.Creature);
                HealthState health = currentSnapshot.Health[fact.Creature];
                creature.ProjectCommittedHealth(health);
                if (fact is DamageAppliedFact && health.Current > 0)
                    creature.PresentCommittedHit();
                return default;
            }

            public ValueTask OnFactCommitted(
                CreatureDefeatCommittedFact fact,
                RulesSnapshot currentSnapshot
            )
            {
                RequireCreature(fact.Creature).PresentCommittedDefeat();
                return default;
            }

            private CreatureComponent RequireCreature(CreatureId id)
            {
                if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                    throw new InvalidOperationException(
                        $"Encounter creature {id.Value} has no required Unity mapping."
                    );
                return creature;
            }
        }

        private static ActionController[] FindExplorationControllers(
            ActionController leader,
            Tile[,] tiles
        )
        {
            return tiles
                .Cast<Tile>()
                .Where(tile => tile != null)
                .SelectMany(tile => tile.Occupants)
                .Where(occupant => occupant != null)
                .Select(occupant => occupant.GetComponent<ActionController>())
                .Where(controller =>
                    controller != null && controller.GetComponent<CreatureComponent>() != null
                )
                .Prepend(leader)
                .Distinct()
                .ToArray();
        }
    }
}
