using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>Owns the single authoritative rules store and dispatcher for one Unity encounter.</summary>
    public sealed class UnityEncounterRulesBridge
    {
        private readonly Dictionary<CreatureComponent, CreatureId> creatureIds = new();
        private readonly Dictionary<CreatureId, CreatureComponent> creatures = new();
        private readonly Dictionary<ActionController, CreatureId> controllerIds = new();
        private readonly Dictionary<CreatureId, ActionController> controllers = new();
        private readonly Dictionary<string, PlayerId> teamIds = new(StringComparer.Ordinal);
        private readonly Dictionary<PlayerId, string> teamDisplayNames = new();
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();
        private readonly Queue<Action> presentation = new();
        private readonly RuleDispatcher dispatcher;
        private readonly EncounterId encounterId;
        private readonly PlayerId protagonistTeam;
        private long nextOriginId;

        private UnityEncounterRulesBridge(
            IReadOnlyList<CreatureComponent> encounterCreatures,
            IReadOnlyList<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls,
            bool requireProtagonistTeam
        )
        {
            RulesStateSeed seed = new RulesStateSeed();
            for (int index = 0; index < encounterCreatures.Count; index++)
            {
                CreatureComponent creature = encounterCreatures[index];
                CreatureId id = new CreatureId($"encounter-creature-{index + 1}");
                creatureIds.Add(creature, id);
                creatures.Add(id, creature);
                seed.SeedHealth(id, creature.GetHealthInitializationState());
            }
            foreach (ActionController controller in encounterControllers)
            {
                CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                if (creature == null || !creatureIds.TryGetValue(creature, out CreatureId id))
                    throw new ArgumentException(
                        "Every encounter controller requires a registered CreatureComponent.",
                        nameof(encounterControllers)
                    );
                controllerIds.Add(controller, id);
                controllers.Add(id, controller);
                ResolveTeam(controller);
            }
            if (requireProtagonistTeam && !TryFindTeam(protagonistTeamName, out protagonistTeam))
                throw new ArgumentException(
                    $"Protagonist team '{protagonistTeamName}' is not registered.",
                    nameof(protagonistTeamName)
                );
            encounterId = new EncounterId("unity-encounter-1");
            RuleRegistry registry = new RuleRegistryBuilder().AddOutcomeRule().Build();
            dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore(seed), rolls)
                .UseRuleRegistry(registry)
                .UseHealthRules()
                .UseActiveEffectRules(registry)
                .UseEncounterRules()
                .Build();
            BridgeFactObserver observer = new BridgeFactObserver(this);
            dispatcher.RegisterFactObserver<HealthFact>(observer);
            dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(observer);
            dispatcher.RegisterFactObserver<TurnBeganFact>(observer);
            dispatcher.RegisterFactObserver<TurnEndedFact>(observer);
            dispatcher.RegisterFactObserver<EncounterEndedFact>(observer);
            foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
                entry.Key.AttachEncounterRules(this, entry.Value);
            foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                entry.Key.AttachEncounterRules(this, entry.Value);
        }

        /// <summary>Creates one bridge with the production random roll service.</summary>
        public static UnityEncounterRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName
        ) => Create(encounterControllers, protagonistTeamName, new RandomRollService());

        /// <summary>Creates one bridge with an explicit roll service for deterministic composition.</summary>
        public static UnityEncounterRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls
        )
        {
            ActionController[] copied =
                encounterControllers?.ToArray()
                ?? throw new ArgumentNullException(nameof(encounterControllers));
            if (
                copied.Length == 0
                || copied.Any(value => value == null)
                || copied.Distinct().Count() != copied.Length
            )
                throw new ArgumentException(
                    "An encounter requires unique non-null controllers.",
                    nameof(encounterControllers)
                );
            if (string.IsNullOrWhiteSpace(protagonistTeamName))
                throw new ArgumentException(
                    "A protagonist team display name is required.",
                    nameof(protagonistTeamName)
                );
            if (rolls == null)
                throw new ArgumentNullException(nameof(rolls));
            return new UnityEncounterRulesBridge(
                copied.Select(controller => controller.GetComponent<CreatureComponent>()).ToArray(),
                copied,
                protagonistTeamName.Trim(),
                rolls,
                requireProtagonistTeam: true
            );
        }

        /// <summary>
        /// Creates a health-only test composition without attaching action or turn authority.
        /// </summary>
        /// <remarks>
        /// Production encounters should use <see cref="Create(IEnumerable{ActionController}, string, IRollService)"/>.
        /// This narrow seam keeps unit tests for legacy effects on the same health runtime while avoiding a
        /// fabricated turn or writable action state.
        /// </remarks>
        public static UnityEncounterRulesBridge CreateHealthTestComposition(
            IEnumerable<CreatureComponent> encounterCreatures
        )
        {
            CreatureComponent[] copied =
                encounterCreatures?.ToArray()
                ?? throw new ArgumentNullException(nameof(encounterCreatures));
            if (
                copied.Length == 0
                || copied.Any(value => value == null)
                || copied.Distinct().Count() != copied.Length
            )
                throw new ArgumentException(
                    "A test composition requires unique non-null creatures.",
                    nameof(encounterCreatures)
                );
            return new UnityEncounterRulesBridge(
                copied,
                Array.Empty<ActionController>(),
                string.Empty,
                new RandomRollService(),
                requireProtagonistTeam: false
            );
        }

        /// <summary>Gets the latest snapshot shared by health, encounter, action, movement, and effects.</summary>
        public RulesSnapshot Snapshot => dispatcher.Snapshot;

        /// <summary>Gets the bridge's encounter identity.</summary>
        public EncounterId EncounterId => encounterId;

        /// <summary>Raised after an outer dispatch fully settles and commits a turn.</summary>
        public event Action<TurnIdentity> TurnBegan = delegate { };

        /// <summary>Raised after an outer dispatch fully settles and closes a turn.</summary>
        public event Action<TurnIdentity> TurnEnded = delegate { };

        /// <summary>Raised once after an outer dispatch fully settles and commits an outcome.</summary>
        public event Action<EncounterOutcome> EncounterEnded = delegate { };

        /// <summary>Rolls initiative and begins the first eligible turn.</summary>
        public async ValueTask<EncounterStartOutcome> StartEncounter(
            IEnumerable<ActionController> participants
        )
        {
            ActionController[] selected =
                participants?.ToArray() ?? throw new ArgumentNullException(nameof(participants));
            EncounterParticipant[] registrations = selected
                .Select(controller => new EncounterParticipant(
                    GetCreatureId(controller),
                    ResolveTeam(controller),
                    controller.GetInitiativeModifier()
                ))
                .ToArray();
            return await DispatchAsync(
                new StartEncounterOp(encounterId, protagonistTeam, registrations)
            );
        }

        /// <summary>Adds reinforcements to this store without rebuilding existing state.</summary>
        public async ValueTask<EncounterJoinOutcome> JoinEncounter(
            IEnumerable<ActionController> additions
        )
        {
            ActionController[] copied =
                additions?.ToArray() ?? throw new ArgumentNullException(nameof(additions));
            if (
                copied.Length == 0
                || copied.Any(value => value == null)
                || copied.Distinct().Count() != copied.Length
            )
                throw new ArgumentException(
                    "Reinforcements require unique non-null controllers.",
                    nameof(additions)
                );
            EncounterJoinParticipant[] participants = copied
                .Select(RegisterReinforcement)
                .ToArray();
            EncounterJoinOutcome outcome = await DispatchAsync(
                new JoinEncounterOp(encounterId, participants)
            );
            foreach (ActionController controller in copied)
            {
                CreatureId id = controllerIds[controller];
                controller.GetComponent<CreatureComponent>().AttachEncounterRules(this, id);
                controller.AttachEncounterRules(this, id);
            }
            return outcome;
        }

        private EncounterJoinParticipant RegisterReinforcement(ActionController controller)
        {
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            if (creature == null)
                throw new ArgumentException(
                    "Every reinforcement requires a CreatureComponent.",
                    nameof(controller)
                );
            if (controllerIds.TryGetValue(controller, out CreatureId existingId))
                return new EncounterJoinParticipant(
                    new EncounterParticipant(
                        existingId,
                        ResolveTeam(controller),
                        controller.GetInitiativeModifier()
                    ),
                    GetHealth(existingId)
                );
            if (creatureIds.ContainsKey(creature))
                throw new InvalidOperationException(
                    "A creature cannot be registered through two controllers."
                );
            CreatureId id = new CreatureId($"encounter-creature-{creatureIds.Count + 1}");
            HealthState health = creature.GetHealthInitializationState();
            creatureIds.Add(creature, id);
            creatures.Add(id, creature);
            controllerIds.Add(controller, id);
            controllers.Add(id, controller);
            PlayerId team = ResolveTeam(controller);
            return new EncounterJoinParticipant(
                new EncounterParticipant(id, team, controller.GetInitiativeModifier()),
                health
            );
        }

        /// <summary>Ends the exact current turn and advances within the same awaited dispatch.</summary>
        public ValueTask<EncounterAdvanceOutcome> EndTurn(TurnIdentity turn) =>
            DispatchAsync(new EndTurnOp(turn));

        /// <summary>Suspends the encounter without an outcome.</summary>
        public ValueTask<EncounterSuspensionOutcome> SuspendEncounter() =>
            DispatchAsync(new SuspendEncounterOp(encounterId));

        /// <summary>Gets the stable rules identity assigned to a registered creature.</summary>
        public CreatureId GetCreatureId(CreatureComponent creature) =>
            creatureIds.TryGetValue(creature, out CreatureId id)
                ? id
                : throw new InvalidOperationException(
                    "Creature is not registered in this encounter."
                );

        /// <summary>Gets the stable rules identity assigned to a registered controller.</summary>
        public CreatureId GetCreatureId(ActionController controller) =>
            controllerIds.TryGetValue(controller, out CreatureId id)
                ? id
                : throw new InvalidOperationException(
                    "Controller is not registered in this encounter."
                );

        /// <summary>Gets the Unity controller mapped to a rules creature.</summary>
        public ActionController GetController(CreatureId creature) =>
            controllers.TryGetValue(creature, out ActionController controller)
                ? controller
                : throw new InvalidOperationException("Creature has no registered controller.");

        /// <summary>Gets the original Unity display name for a collision-safe team identity.</summary>
        public string GetTeamDisplayName(PlayerId team) =>
            teamDisplayNames.TryGetValue(team, out string display)
                ? display
                : throw new InvalidOperationException("Team is not registered in this encounter.");

        /// <summary>Gets authoritative health from the shared snapshot.</summary>
        public HealthState GetHealth(CreatureId creature) =>
            Snapshot.Health.TryGet(creature, out HealthState health)
                ? health
                : throw new InvalidOperationException(
                    "Creature has no authoritative health state."
                );

        /// <summary>Gets authoritative actions and reaction availability.</summary>
        public ActionEconomyState GetActionEconomy(CreatureId creature) =>
            Snapshot.ActionEconomy.TryGet(creature, out ActionEconomyState state)
                ? state
                : new ActionEconomyState(0, false);

        /// <summary>Gets authoritative turn-scoped multiple-attack state.</summary>
        public MultipleAttackPenaltyState GetMultipleAttackPenalty(CreatureId creature) =>
            Snapshot.MultipleAttackPenalty.TryGet(creature, out MultipleAttackPenaltyState state)
                ? state
                : new MultipleAttackPenaltyState(0);

        /// <summary>Gets the exact current turn when the encounter has one.</summary>
        public TurnIdentity? CurrentTurn =>
            Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
                ? encounter.CurrentTurn
                : null;

        /// <summary>Commits already-final damage through this encounter's dispatcher.</summary>
        public DamageOutcome ApplyFinalDamage(
            CreatureId target,
            int finalDamage,
            RuleSource source
        ) => DispatchNow(new ApplyDamageOp(target, finalDamage, AllocateOrigin(source), source));

        /// <summary>Commits healing through this encounter's dispatcher.</summary>
        public HealingOutcome ApplyHealing(CreatureId target, int healing, RuleSource source) =>
            DispatchNow(new ApplyHealingOp(target, healing, AllocateOrigin(source), source));

        /// <summary>Commits source-owned temporary Hit Points.</summary>
        public TemporaryHitPointsGrantOutcome GrantTemporaryHitPoints(
            CreatureId target,
            int amount,
            RuleSource source
        ) =>
            DispatchNow(
                new GrantTemporaryHitPointsOp(target, amount, AllocateOrigin(source), source)
            );

        /// <summary>Removes source-owned temporary Hit Points.</summary>
        public TemporaryHitPointsRemovalOutcome RemoveTemporaryHitPoints(
            CreatureId target,
            RuleSource source
        ) => DispatchNow(new RemoveTemporaryHitPointsOp(target, AllocateOrigin(source), source));

        /// <summary>Adds source-scoped temporary-Hit-Point immunity.</summary>
        public TemporaryHitPointImmunityOutcome AddTemporaryHitPointImmunity(
            CreatureId target,
            RuleSource source
        ) =>
            DispatchNow(new AddTemporaryHitPointImmunityOp(target, AllocateOrigin(source), source));

        /// <summary>Spends actions through the narrow legacy same-store port.</summary>
        public LegacyActionSpendOutcome SpendActions(CreatureId actor, int amount) =>
            DispatchNow(new SpendLegacyActionsOp(actor, amount));

        /// <summary>Increments MAP through the narrow legacy same-store port.</summary>
        public LegacyMapOutcome IncrementMap(CreatureId actor) =>
            DispatchNow(new IncrementLegacyMapOp(actor));

        /// <summary>Resolves a bridge-created health origin to its Unity rules source.</summary>
        public bool TryGetOriginSource(HealthChangeOriginId origin, out RuleSource source) =>
            origins.TryGetValue(origin, out source);

        private PlayerId ResolveTeam(ActionController controller)
        {
            Team team = controller.GetComponent<Team>();
            string display =
                team == null || string.IsNullOrWhiteSpace(team.Name)
                    ? "Unassigned"
                    : team.Name.Trim();
            if (teamIds.TryGetValue(display, out PlayerId existing))
                return existing;
            string baseSlug = new string(
                display
                    .ToLowerInvariant()
                    .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                    .ToArray()
            ).Trim('-');
            if (baseSlug.Length == 0)
                baseSlug = "team";
            string slug = baseSlug;
            int suffix = 2;
            while (teamDisplayNames.ContainsKey(new PlayerId(slug)))
                slug = $"{baseSlug}-{suffix++}";
            PlayerId id = new PlayerId(slug);
            teamIds.Add(display, id);
            teamDisplayNames.Add(id, display);
            return id;
        }

        private bool TryFindTeam(string displayName, out PlayerId id)
        {
            foreach (KeyValuePair<string, PlayerId> pair in teamIds)
                if (string.Equals(pair.Key, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    id = pair.Value;
                    return true;
                }
            id = default;
            return false;
        }

        private HealthChangeOriginId AllocateOrigin(RuleSource source)
        {
            if (source.IsEmpty)
                throw new ArgumentException("A health rule source is required.", nameof(source));
            HealthChangeOriginId id = new HealthChangeOriginId($"health-origin-{++nextOriginId}");
            origins.Add(id, source);
            return id;
        }

        private TResult DispatchNow<TResult>(IRuleOp<TResult> operation)
        {
            ValueTask<OpResult<TResult>> pending = dispatcher.Dispatch(operation);
            if (!pending.IsCompleted)
                throw new InvalidOperationException(
                    "Unity encounter requests must be awaited before presentation callbacks are drained."
                );
            OpResult<TResult> result = pending.GetAwaiter().GetResult();
            DrainPresentation();
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException($"Encounter request returned {result.Status}.");
        }

        private async ValueTask<TResult> DispatchAsync<TResult>(IRuleOp<TResult> operation)
        {
            OpResult<TResult> result = await dispatcher.Dispatch(operation);
            DrainPresentation();
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException($"Encounter request returned {result.Status}.");
        }

        private void DrainPresentation()
        {
            while (presentation.Count > 0)
                presentation.Dequeue().Invoke();
        }

        private sealed class BridgeFactObserver
            : IFactObserver<HealthFact>,
                IFactObserver<CreatureReducedToZeroFact>,
                IFactObserver<TurnBeganFact>,
                IFactObserver<TurnEndedFact>,
                IFactObserver<EncounterEndedFact>
        {
            private readonly UnityEncounterRulesBridge owner;

            public BridgeFactObserver(UnityEncounterRulesBridge owner) => this.owner = owner;

            public ValueTask OnFactCommitted(HealthFact fact, RulesSnapshot snapshot)
            {
                if (
                    owner.creatures.TryGetValue(fact.Creature, out CreatureComponent creature)
                    && creature != null
                )
                {
                    HealthState health = snapshot.Health[fact.Creature];
                    owner.presentation.Enqueue(() =>
                    {
                        creature.ProjectCommittedHealth(health);
                        if (fact is DamageAppliedFact && health.Current > 0)
                            creature.PresentCommittedHit();
                    });
                }
                return default;
            }

            public ValueTask OnFactCommitted(CreatureReducedToZeroFact fact, RulesSnapshot snapshot)
            {
                if (
                    owner.creatures.TryGetValue(fact.Creature, out CreatureComponent creature)
                    && creature != null
                )
                    owner.presentation.Enqueue(creature.PresentCommittedDefeat);
                return default;
            }

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() => owner.TurnBegan.Invoke(fact.Turn));
                return default;
            }

            public ValueTask OnFactCommitted(TurnEndedFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() => owner.TurnEnded.Invoke(fact.Turn));
                return default;
            }

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() => owner.EncounterEnded.Invoke(fact.Outcome));
                return default;
            }
        }
    }
}
