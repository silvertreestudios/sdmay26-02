using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using GridPrivate;
using GridPublic;
using UnityEngine;

[assembly: InternalsVisibleTo("EditModeAssembly")]

namespace Game.Rules.Unity
{
    /// <summary>
    /// Carries one Unity creature health change until the bridge assigns stable rules identities.
    /// </summary>
    internal sealed class UnityHealthBatchChange
    {
        internal UnityHealthBatchChange(
            HealthBatchChangeKind kind,
            CreatureComponent target,
            int amount,
            RuleSource source
        )
        {
            if (kind != HealthBatchChangeKind.Damage && kind != HealthBatchChangeKind.Healing)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Kind = kind;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Amount = amount;
            Source = source.IsEmpty
                ? throw new ArgumentException("A health rule source is required.", nameof(source))
                : source;
        }

        internal HealthBatchChangeKind Kind { get; }
        internal CreatureComponent Target { get; }
        internal int Amount { get; }
        internal RuleSource Source { get; }
    }

    /// <summary>Owns the single authoritative rules store and dispatcher for one Unity encounter.</summary>
    public sealed class UnityEncounterRulesBridge
    {
        private Dictionary<CreatureComponent, CreatureId> creatureIds = new();
        private Dictionary<CreatureId, CreatureComponent> creatures = new();
        private Dictionary<ActionController, CreatureId> controllerIds = new();
        private Dictionary<CreatureId, ActionController> controllers = new();
        private Dictionary<string, PlayerId> teamIds = new(StringComparer.Ordinal);
        private Dictionary<PlayerId, string> teamDisplayNames = new();
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();
        private readonly Queue<Func<ValueTask>> presentation = new();
        private readonly RuleDispatcher dispatcher;
        private readonly EncounterId encounterId;
        private readonly PlayerId protagonistTeam;
        private long nextOriginId;

        private UnityEncounterRulesBridge(
            IReadOnlyList<CreatureComponent> encounterCreatures,
            IReadOnlyList<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls,
            bool requireProtagonistTeam,
            RuleRegistry registry,
            IEnumerable<ActiveRuleBinding> initialBindings
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
            ActiveRuleBinding[] copiedBindings =
                initialBindings?.ToArray()
                ?? throw new ArgumentNullException(nameof(initialBindings));
            if (copiedBindings.Any(binding => binding == null))
                throw new ArgumentException(
                    "Initial rule bindings cannot contain null entries.",
                    nameof(initialBindings)
                );
            foreach (ActiveRuleBinding binding in copiedBindings)
                seed.SeedRuleBinding(binding);
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
            IEncounterTurnStartAdapter[] turnStartAdapters =
            {
                new SpellExpiryTurnStartAdapter(this),
                new RottingAuraTurnStartAdapter(this),
                new SlowedTurnStartAdapter(this),
            };
            dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore(seed), rolls)
                .UseRuleRegistry(registry)
                .UseHealthRules()
                .UseMovementBudgetResetRules()
                .UseActiveEffectRules(registry)
                .UseEncounterRules(turnStartAdapters)
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
        /// <param name="encounterControllers">Unique controllers seeded into this composition.</param>
        /// <param name="protagonistTeamName">The display name used to locate the player team.</param>
        /// <returns>A bridge whose components project the shared initial health snapshot.</returns>
        public static UnityEncounterRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName
        ) => Create(encounterControllers, protagonistTeamName, new RandomRollService());

        /// <summary>Creates one bridge with an explicit roll service for deterministic composition.</summary>
        /// <param name="encounterControllers">Unique controllers seeded into this composition.</param>
        /// <param name="protagonistTeamName">The display name used to locate the player team.</param>
        /// <param name="rolls">The initiative roll service shared by the dispatcher.</param>
        /// <returns>A bridge ready to start one authoritative encounter.</returns>
        public static UnityEncounterRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls
        ) =>
            CreateConfigured(
                encounterControllers,
                protagonistTeamName,
                rolls,
                new RuleRegistryBuilder().AddOutcomeRule().Build(),
                Array.Empty<ActiveRuleBinding>()
            );

        internal static UnityEncounterRulesBridge CreateWithRuleComposition(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls,
            RuleRegistry registry,
            IEnumerable<ActiveRuleBinding> initialBindings
        ) =>
            CreateConfigured(
                encounterControllers,
                protagonistTeamName,
                rolls,
                registry,
                initialBindings
            );

        private static UnityEncounterRulesBridge CreateConfigured(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName,
            IRollService rolls,
            RuleRegistry registry,
            IEnumerable<ActiveRuleBinding> initialBindings
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
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (initialBindings == null)
                throw new ArgumentNullException(nameof(initialBindings));
            return new UnityEncounterRulesBridge(
                copied.Select(controller => controller.GetComponent<CreatureComponent>()).ToArray(),
                copied,
                protagonistTeamName.Trim(),
                rolls,
                requireProtagonistTeam: true,
                registry: registry,
                initialBindings: initialBindings
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
        /// <param name="encounterCreatures">Unique creatures needed by health-only test fixtures.</param>
        /// <returns>A shared health dispatcher without an encounter roster.</returns>
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
                requireProtagonistTeam: false,
                registry: new RuleRegistryBuilder().AddOutcomeRule().Build(),
                initialBindings: Array.Empty<ActiveRuleBinding>()
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
        public event Func<EncounterOutcome, ValueTask> EncounterEnded = outcome => default;

        /// <summary>Rolls initiative and begins the first eligible turn.</summary>
        /// <param name="participants">Registered controllers eligible for the initial roster.</param>
        /// <returns>The encounter after the complete first-turn root settles.</returns>
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
            await DispatchAsync(new StartEncounterOp(encounterId, protagonistTeam, registrations));
            return new EncounterStartOutcome(Snapshot.Encounters[encounterId]);
        }

        /// <summary>Adds reinforcements to this store without rebuilding existing state.</summary>
        /// <param name="additions">Unique controllers not already in the immutable roster.</param>
        /// <returns>The accepted roster replacement after identity attachments commit.</returns>
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
            Dictionary<CreatureComponent, CreatureId> plannedCreatureIds = new(creatureIds);
            Dictionary<CreatureId, CreatureComponent> plannedCreatures = new(creatures);
            Dictionary<ActionController, CreatureId> plannedControllerIds = new(controllerIds);
            Dictionary<CreatureId, ActionController> plannedControllers = new(controllers);
            Dictionary<string, PlayerId> plannedTeamIds = new(teamIds, StringComparer.Ordinal);
            Dictionary<PlayerId, string> plannedTeamDisplayNames = new(teamDisplayNames);
            EncounterJoinParticipant[] participants = copied
                .Select(controller =>
                    PlanReinforcement(
                        controller,
                        plannedCreatureIds,
                        plannedCreatures,
                        plannedControllerIds,
                        plannedControllers,
                        plannedTeamIds,
                        plannedTeamDisplayNames
                    )
                )
                .ToArray();
            await DispatchAsync(new JoinEncounterOp(encounterId, participants));
            creatureIds = plannedCreatureIds;
            creatures = plannedCreatures;
            controllerIds = plannedControllerIds;
            controllers = plannedControllers;
            teamIds = plannedTeamIds;
            teamDisplayNames = plannedTeamDisplayNames;
            foreach (ActionController controller in copied)
            {
                CreatureId id = controllerIds[controller];
                controller.GetComponent<CreatureComponent>().AttachEncounterRules(this, id);
                controller.AttachEncounterRules(this, id);
            }
            return new EncounterJoinOutcome(Snapshot.Encounters[encounterId]);
        }

        private EncounterJoinParticipant PlanReinforcement(
            ActionController controller,
            IDictionary<CreatureComponent, CreatureId> plannedCreatureIds,
            IDictionary<CreatureId, CreatureComponent> plannedCreatures,
            IDictionary<ActionController, CreatureId> plannedControllerIds,
            IDictionary<CreatureId, ActionController> plannedControllers,
            IDictionary<string, PlayerId> plannedTeamIds,
            IDictionary<PlayerId, string> plannedTeamDisplayNames
        )
        {
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            if (creature == null)
                throw new ArgumentException(
                    "Every reinforcement requires a CreatureComponent.",
                    nameof(controller)
                );
            if (plannedControllerIds.TryGetValue(controller, out CreatureId existingId))
                return new EncounterJoinParticipant(
                    new EncounterParticipant(
                        existingId,
                        ResolveTeam(controller, plannedTeamIds, plannedTeamDisplayNames),
                        controller.GetInitiativeModifier()
                    ),
                    GetHealth(existingId)
                );
            if (plannedCreatureIds.ContainsKey(creature))
                throw new InvalidOperationException(
                    "A creature cannot be registered through two controllers."
                );
            CreatureId id = new CreatureId($"encounter-creature-{plannedCreatureIds.Count + 1}");
            HealthState health = creature.GetHealthInitializationState();
            plannedCreatureIds.Add(creature, id);
            plannedCreatures.Add(id, creature);
            plannedControllerIds.Add(controller, id);
            plannedControllers.Add(id, controller);
            PlayerId team = ResolveTeam(controller, plannedTeamIds, plannedTeamDisplayNames);
            return new EncounterJoinParticipant(
                new EncounterParticipant(id, team, controller.GetInitiativeModifier()),
                health
            );
        }

        /// <summary>Ends the exact current turn and advances within the same awaited dispatch.</summary>
        /// <param name="turn">The exact encounter turn identity to validate and close.</param>
        /// <returns>The encounter after the next eligible turn or outcome settles.</returns>
        public async ValueTask<EncounterAdvanceOutcome> EndTurn(TurnIdentity turn)
        {
            await DispatchAsync(new EndTurnOp(turn));
            return new EncounterAdvanceOutcome(Snapshot.Encounters[encounterId]);
        }

        /// <summary>Suspends the encounter without an outcome.</summary>
        /// <returns>The suspended encounter after encounter-duration effects expire.</returns>
        public async ValueTask<EncounterSuspensionOutcome> SuspendEncounter()
        {
            await DispatchAsync(new SuspendEncounterOp(encounterId));
            return new EncounterSuspensionOutcome(Snapshot.Encounters[encounterId]);
        }

        /// <summary>Gets the stable rules identity assigned to a registered creature.</summary>
        /// <param name="creature">The attached Unity creature.</param>
        /// <returns>The encounter-stable rules identity.</returns>
        public CreatureId GetCreatureId(CreatureComponent creature) =>
            creatureIds.TryGetValue(creature, out CreatureId id)
                ? id
                : throw new InvalidOperationException(
                    "Creature is not registered in this encounter."
                );

        /// <summary>Gets the stable rules identity assigned to a registered controller.</summary>
        /// <param name="controller">The attached Unity action controller.</param>
        /// <returns>The encounter-stable rules identity.</returns>
        public CreatureId GetCreatureId(ActionController controller) =>
            controllerIds.TryGetValue(controller, out CreatureId id)
                ? id
                : throw new InvalidOperationException(
                    "Controller is not registered in this encounter."
                );

        /// <summary>Gets the Unity controller mapped to a rules creature.</summary>
        /// <param name="creature">The registered rules creature identity.</param>
        /// <returns>The attached Unity controller.</returns>
        public ActionController GetController(CreatureId creature) =>
            controllers.TryGetValue(creature, out ActionController controller)
                ? controller
                : throw new InvalidOperationException("Creature has no registered controller.");

        /// <summary>Gets the original Unity display name for a collision-safe team identity.</summary>
        /// <param name="team">The rules-owned team identity.</param>
        /// <returns>The original trimmed Unity team name.</returns>
        public string GetTeamDisplayName(PlayerId team) =>
            teamDisplayNames.TryGetValue(team, out string display)
                ? display
                : throw new InvalidOperationException("Team is not registered in this encounter.");

        /// <summary>Gets authoritative health from the shared snapshot.</summary>
        /// <param name="creature">The registered creature identity.</param>
        /// <returns>The latest committed health state.</returns>
        public HealthState GetHealth(CreatureId creature) =>
            Snapshot.Health.TryGet(creature, out HealthState health)
                ? health
                : throw new InvalidOperationException(
                    "Creature has no authoritative health state."
                );

        /// <summary>Gets authoritative actions and reaction availability.</summary>
        /// <param name="creature">The registered creature identity.</param>
        /// <returns>Committed action economy, or unavailable resources before roster registration.</returns>
        public ActionEconomyState GetActionEconomy(CreatureId creature) =>
            Snapshot.ActionEconomy.TryGet(creature, out ActionEconomyState state)
                ? state
                : new ActionEconomyState(0, false);

        /// <summary>Gets authoritative turn-scoped multiple-attack state.</summary>
        /// <param name="creature">The registered creature identity.</param>
        /// <returns>The committed attack count, or zero before roster registration.</returns>
        public MultipleAttackPenaltyState GetMultipleAttackPenalty(CreatureId creature) =>
            Snapshot.MultipleAttackPenalty.TryGet(creature, out MultipleAttackPenaltyState state)
                ? state
                : new MultipleAttackPenaltyState(0);

        /// <summary>Gets the exact current turn when the encounter has one.</summary>
        public TurnIdentity? CurrentTurn =>
            Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
                ? encounter.CurrentTurn
                : null;

        /// <summary>
        /// Commits already-final damage and awaits reactions, outcome evaluation, and presentation.
        /// </summary>
        /// <param name="target">The registered creature receiving damage.</param>
        /// <param name="finalDamage">The non-negative damage after upstream calculations.</param>
        /// <param name="source">The rule source responsible for the request.</param>
        /// <returns>The exact health changes after the complete causal root settles.</returns>
        public ValueTask<DamageOutcome> ApplyFinalDamageAsync(
            CreatureId target,
            int finalDamage,
            RuleSource source
        ) => DispatchAsync(new ApplyDamageOp(target, finalDamage, AllocateOrigin(source), source));

        /// <summary>Commits healing and awaits the complete causal root.</summary>
        /// <param name="target">The registered creature receiving healing.</param>
        /// <param name="healing">The non-negative requested healing.</param>
        /// <param name="source">The rule source responsible for the request.</param>
        /// <returns>The amount applied after maximum-HP clamping.</returns>
        public ValueTask<HealingOutcome> ApplyHealingAsync(
            CreatureId target,
            int healing,
            RuleSource source
        ) => DispatchAsync(new ApplyHealingOp(target, healing, AllocateOrigin(source), source));

        internal ValueTask<HealthBatchOutcome> ApplyFinalHealthBatchAsync(
            IEnumerable<UnityHealthBatchChange> changes
        )
        {
            UnityHealthBatchChange[] copied =
                changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
            if (copied.Length == 0 || copied.Any(change => change == null))
                throw new ArgumentException(
                    "A Unity health batch requires at least one non-null change.",
                    nameof(changes)
                );
            HealthBatchChange[] rulesChanges = copied
                .Select(change =>
                {
                    if (!creatureIds.TryGetValue(change.Target, out CreatureId target))
                        throw new InvalidOperationException(
                            "Every health-batch target must belong to this encounter bridge."
                        );
                    return new HealthBatchChange(
                        change.Kind,
                        target,
                        change.Amount,
                        AllocateOrigin(change.Source),
                        change.Source
                    );
                })
                .ToArray();
            return DispatchAsync(new ApplyHealthBatchOp(rulesChanges));
        }

        /// <summary>Commits source-owned temporary Hit Points.</summary>
        /// <param name="target">The registered creature receiving the pool.</param>
        /// <param name="amount">The non-negative temporary-HP offer.</param>
        /// <param name="source">The source that owns the pool and immunity key.</param>
        /// <returns>Whether the offer applied, replaced a pool, or was blocked.</returns>
        public ValueTask<TemporaryHitPointsGrantOutcome> GrantTemporaryHitPointsAsync(
            CreatureId target,
            int amount,
            RuleSource source
        ) =>
            DispatchAsync(
                new GrantTemporaryHitPointsOp(target, amount, AllocateOrigin(source), source)
            );

        /// <summary>Removes source-owned temporary Hit Points.</summary>
        /// <param name="target">The registered creature whose pool may be removed.</param>
        /// <param name="source">The source expected to own the active pool.</param>
        /// <returns>The removed amount, or zero when ownership does not match.</returns>
        public ValueTask<TemporaryHitPointsRemovalOutcome> RemoveTemporaryHitPointsAsync(
            CreatureId target,
            RuleSource source
        ) => DispatchAsync(new RemoveTemporaryHitPointsOp(target, AllocateOrigin(source), source));

        /// <summary>Adds source-scoped temporary-Hit-Point immunity.</summary>
        /// <param name="target">The registered creature receiving immunity.</param>
        /// <param name="source">The blocked temporary-HP source.</param>
        /// <returns>Whether a new immunity entry was committed.</returns>
        public ValueTask<TemporaryHitPointImmunityOutcome> AddTemporaryHitPointImmunityAsync(
            CreatureId target,
            RuleSource source
        ) =>
            DispatchAsync(
                new AddTemporaryHitPointImmunityOp(target, AllocateOrigin(source), source)
            );

        /// <summary>Awaits an action spend through the transitional same-store port.</summary>
        /// <param name="actor">The registered actor paying the cost.</param>
        /// <param name="amount">The positive number of actions to spend.</param>
        /// <returns>The actor's committed remaining actions.</returns>
        public ValueTask<LegacyActionSpendOutcome> SpendActionsAsync(
            CreatureId actor,
            int amount
        ) => DispatchAsync(new SpendLegacyActionsOp(actor, amount));

        /// <summary>Awaits a MAP increment through the transitional same-store port.</summary>
        /// <param name="actor">The registered actor completing an unmigrated attack.</param>
        /// <returns>The actor's committed turn-scoped attack count.</returns>
        public ValueTask<LegacyMapOutcome> IncrementMapAsync(CreatureId actor) =>
            DispatchAsync(new IncrementLegacyMapOp(actor));

        /// <summary>Resolves a bridge-created health origin to its Unity rules source.</summary>
        /// <param name="origin">The encounter-stable health request identity.</param>
        /// <param name="source">Receives the responsible rule source when found.</param>
        /// <returns>Whether this bridge allocated the origin.</returns>
        public bool TryGetOriginSource(HealthChangeOriginId origin, out RuleSource source) =>
            origins.TryGetValue(origin, out source);

        private PlayerId ResolveTeam(ActionController controller)
        {
            return ResolveTeam(controller, teamIds, teamDisplayNames);
        }

        private static PlayerId ResolveTeam(
            ActionController controller,
            IDictionary<string, PlayerId> resolvedTeamIds,
            IDictionary<PlayerId, string> resolvedTeamDisplayNames
        )
        {
            Team team = controller.GetComponent<Team>();
            string display =
                team == null || string.IsNullOrWhiteSpace(team.Name)
                    ? "Unassigned"
                    : team.Name.Trim();
            if (resolvedTeamIds.TryGetValue(display, out PlayerId existing))
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
            while (resolvedTeamDisplayNames.ContainsKey(new PlayerId(slug)))
                slug = $"{baseSlug}-{suffix++}";
            PlayerId id = new PlayerId(slug);
            resolvedTeamIds.Add(display, id);
            resolvedTeamDisplayNames.Add(id, display);
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

        private async ValueTask<TResult> DispatchAsync<TResult>(IRuleOp<TResult> operation)
        {
            OpResult<TResult> result;
            try
            {
                result = await dispatcher.Dispatch(operation);
            }
            finally
            {
                await DrainPresentationAsync();
            }
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException($"Encounter request returned {result.Status}.");
        }

        private async ValueTask DrainPresentationAsync()
        {
            while (presentation.Count > 0)
                await presentation.Dequeue().Invoke();
        }

        private async ValueTask InvokeEncounterEnded(EncounterOutcome outcome)
        {
            Delegate[] handlers = EncounterEnded.GetInvocationList();
            foreach (Delegate handler in handlers)
                await ((Func<EncounterOutcome, ValueTask>)handler)(outcome);
        }

        private sealed class SpellExpiryTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityEncounterRulesBridge owner;

            public SpellExpiryTurnStartAdapter(UnityEncounterRulesBridge owner) =>
                this.owner = owner;

            public ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                SpellEffectController.ExpireAtStartOfTurn(
                    owner.GetController(context.Actor).gameObject
                );
                return new ValueTask<TurnStartContribution>(current);
            }
        }

        private sealed class RottingAuraTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityEncounterRulesBridge owner;

            public RottingAuraTurnStartAdapter(UnityEncounterRulesBridge owner) =>
                this.owner = owner;

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                GridAPI grid = UnityEngine.Object.FindFirstObjectByType<GridAPI>();
                if (!(grid is GridAPIPrivate gridPrivate))
                    return current;
                EncounterState encounter = context.Snapshot.Encounters[context.Encounter];
                ActionController actor = owner.GetController(context.Actor);
                ActionController[] combatants = encounter
                    .Roster.Where(entry =>
                        context.Snapshot.Health.TryGet(entry.Creature, out HealthState health)
                        && health.Current > 0
                    )
                    .Select(entry => owner.GetController(entry.Creature))
                    .ToArray();
                await CreatureAuraResolver.ApplyTurnStartAurasAwaited(
                    actor,
                    combatants,
                    gridPrivate.GetTiles(),
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
                        return context.Snapshot.Health.TryGet(targetId, out HealthState health)
                            && health.Current > 0;
                    }
                );
                return current;
            }
        }

        private sealed class SlowedTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityEncounterRulesBridge owner;

            public SlowedTurnStartAdapter(UnityEncounterRulesBridge owner) => this.owner = owner;

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
                        return default;
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
                    owner.presentation.Enqueue(() =>
                    {
                        if (
                            !owner.Snapshot.Health.TryGet(
                                fact.Creature,
                                out HealthState settledHealth
                            )
                            || settledHealth.Current > 0
                        )
                            return default;
                        creature.ProjectCommittedHealth(settledHealth);
                        creature.PresentCommittedDefeat();
                        return default;
                    });
                return default;
            }

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() =>
                {
                    owner.TurnBegan.Invoke(fact.Turn);
                    return default;
                });
                return default;
            }

            public ValueTask OnFactCommitted(TurnEndedFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() =>
                {
                    owner.TurnEnded.Invoke(fact.Turn);
                    return default;
                });
                return default;
            }

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot snapshot)
            {
                owner.presentation.Enqueue(() => owner.InvokeEncounterEnded(fact.Outcome));
                return default;
            }
        }
    }
}
