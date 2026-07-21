using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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
        private readonly Dictionary<CreatureComponent, CreatureId> creatureIds = new();
        private readonly Dictionary<CreatureId, CreatureComponent> creatures = new();
        private readonly Dictionary<ActionController, CreatureId> controllerIds = new();
        private readonly Dictionary<CreatureId, ActionController> controllers = new();
        private readonly Dictionary<string, PlayerId> teamIds = new(StringComparer.Ordinal);
        private readonly Dictionary<PlayerId, string> teamDisplayNames = new();
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();

        // Join planning runs synchronously up to its dispatcher await. These reservations are not
        // authoritative membership: they only prevent queued plans from reusing an identity, and
        // public maps/attachments still publish exclusively after reducer acceptance.
        private readonly HashSet<CreatureComponent> pendingJoinCreatures = new();
        private readonly HashSet<ActionController> pendingJoinControllers = new();
        private readonly Dictionary<string, PlayerId> pendingJoinTeamIds = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<string, int> pendingJoinTeamReferences = new(
            StringComparer.Ordinal
        );
        private readonly Dictionary<OpId, Queue<Func<ValueTask>>> presentationByRoot = new();

        // Exact roots retain separate callback queues. Causal edges let the terminal dispatcher
        // phase drain parent presentation before descendants without touching unrelated roots.
        private readonly Dictionary<OpId, List<OpId>> presentationChildrenByRoot = new();
        private readonly HashSet<OpId> settledPresentationRoots = new();
        private StartupPresentationBuffer startupPresentationBuffer;
        private readonly RuleDispatcher dispatcher;
        private readonly BridgeRootSettlementObserver rootSettlementObserver;
        private readonly BridgeFactObserver factObserver;
        private readonly EncounterId encounterId;
        private readonly PlayerId protagonistTeam;
        private long nextCreatureId;
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
            nextCreatureId = encounterCreatures.Count;
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
            rootSettlementObserver = new BridgeRootSettlementObserver(this);
            factObserver = new BridgeFactObserver(this);
            dispatcher.RegisterRootSettlementObserver(rootSettlementObserver);
            dispatcher.RegisterCausalTreeSettlementObserver(rootSettlementObserver);
            dispatcher.RegisterFactObserver<HealthFact>(factObserver);
            dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(factObserver);
            dispatcher.RegisterFactObserver<TurnBeganFact>(factObserver);
            dispatcher.RegisterFactObserver<TurnEndedFact>(factObserver);
            dispatcher.RegisterFactObserver<EncounterEndedFact>(factObserver);
            foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
                entry.Key.AttachEncounterRules(this, entry.Value);
            foreach (KeyValuePair<ActionController, CreatureId> entry in controllerIds)
                entry.Key.AttachEncounterRules(this, entry.Value);
        }

        /// <summary>Creates one bridge with the production random roll service.</summary>
        /// <param name="encounterControllers">Unique controllers seeded into this composition.</param>
        /// <param name="protagonistTeamName">
        /// The exact trimmed display name used to locate the player team with ordinal matching.
        /// </param>
        /// <returns>A bridge whose components project the shared initial health snapshot.</returns>
        public static UnityEncounterRulesBridge Create(
            IEnumerable<ActionController> encounterControllers,
            string protagonistTeamName
        ) => Create(encounterControllers, protagonistTeamName, new RandomRollService());

        /// <summary>Creates one bridge with an explicit roll service for deterministic composition.</summary>
        /// <param name="encounterControllers">Unique controllers seeded into this composition.</param>
        /// <param name="protagonistTeamName">
        /// The exact trimmed display name used to locate the player team with ordinal matching.
        /// </param>
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

        internal bool HasActiveEncounter =>
            Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
            && encounter.Phase == EncounterPhase.Active;

        /// <summary>
        /// Gets whether new action-driven rules work may begin through this bridge.
        /// </summary>
        /// <remarks>
        /// A health-only composition intentionally has no <see cref="EncounterState"/> and remains
        /// usable by standalone spell and Strike fixtures. Once this bridge has committed an
        /// encounter lifecycle, only its active phase permits new work; ended and suspended
        /// encounters remain closed even while Unity host completion is still settling.
        /// </remarks>
        internal bool AllowsNewActionLifecycle
        {
            get
            {
                if (!Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter))
                    return true;
                return encounter.Phase == EncounterPhase.Active;
            }
        }

        // Membership requires both this bridge's identity map and its immutable active roster. A
        // CreatureComponent attached to another encounter cannot pass by sharing a Unity scene.
        internal bool IsActiveEncounterParticipant(CreatureComponent creature)
        {
            if (
                creature == null
                || !creatureIds.TryGetValue(creature, out CreatureId creatureId)
                || !Snapshot.Encounters.TryGet(encounterId, out EncounterState encounter)
                || encounter.Phase != EncounterPhase.Active
            )
                return false;
            return encounter.Roster.Any(entry => entry.Creature == creatureId);
        }

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

        // Initial combat hooks and the complete first-turn causal tree buffer Unity presentation
        // until CombatManager accepts the rules startup. The later callback batch is deliberately
        // not described as transactional because arbitrary UnityEvent work cannot be rolled back.
        internal void BeginStartupPresentationBuffering()
        {
            if (startupPresentationBuffer != null)
                throw new InvalidOperationException(
                    "Only one startup presentation buffer may be active."
                );
            startupPresentationBuffer = new StartupPresentationBuffer();
        }

        internal async ValueTask DrainAcceptedStartupPresentationAsync()
        {
            StartupPresentationBuffer buffer =
                startupPresentationBuffer
                ?? throw new InvalidOperationException("No startup presentation buffer is active.");
            startupPresentationBuffer = null;
            List<Exception> failures = new();
            try
            {
                while (buffer.Work.Count > 0)
                {
                    try
                    {
                        await buffer.Work.Dequeue().Invoke();
                    }
                    catch (Exception exception)
                    {
                        // Startup is already durable. Attempt every accepted callback exactly once
                        // so one presentation bug cannot suppress later turn/end host work.
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                foreach (OpId rootId in buffer.Roots)
                    DiscardPresentationTree(rootId);
            }

            if (failures.Count == 0)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(
                "Multiple accepted startup presentation callbacks failed.",
                failures
            );
        }

        internal void DiscardStartupPresentationBuffer()
        {
            StartupPresentationBuffer buffer = startupPresentationBuffer;
            startupPresentationBuffer = null;
            if (buffer == null)
                return;
            foreach (OpId rootId in buffer.Roots)
                DiscardPresentationTree(rootId);
        }

        /// <summary>Adds reinforcements to this store without rebuilding existing state.</summary>
        /// <param name="additions">Unique controllers not already in the immutable roster.</param>
        /// <returns>
        /// The accepted roster replacement after identity attachments and combat-start hooks settle
        /// inside the join root, before the additions become turn eligible.
        /// </returns>
        public ValueTask<EncounterJoinOutcome> JoinEncounter(
            IEnumerable<ActionController> additions
        ) => JoinEncounter(additions, () => { }, () => { });

        // CombatManager observes reducer acceptance separately from identity/controller publication
        // so later root-owned initialization faults cannot be misreported as pre-accept rejection.
        // Keeping this overload internal prevents callers from manufacturing a second host boundary.
        internal async ValueTask<EncounterJoinOutcome> JoinEncounter(
            IEnumerable<ActionController> additions,
            Action markAccepted,
            Action publishAcceptedControllers
        )
        {
            if (markAccepted == null)
                throw new ArgumentNullException(nameof(markAccepted));
            if (publishAcceptedControllers == null)
                throw new ArgumentNullException(nameof(publishAcceptedControllers));
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
            List<KeyValuePair<ActionController, CreatureComponent>> reservedIdentities = new();
            Dictionary<string, PlayerId> reservedTeams = new(StringComparer.Ordinal);
            try
            {
                foreach (ActionController controller in copied)
                {
                    CreatureComponent creature = controller.GetComponent<CreatureComponent>();
                    if (creature == null)
                        throw new ArgumentException(
                            "Every reinforcement requires a CreatureComponent.",
                            nameof(additions)
                        );
                    if (controllerIds.ContainsKey(controller))
                        continue;
                    if (
                        pendingJoinControllers.Contains(controller)
                        || pendingJoinCreatures.Contains(creature)
                    )
                        throw new InvalidOperationException(
                            "A reinforcement identity is already pending registration."
                        );
                    pendingJoinControllers.Add(controller);
                    pendingJoinCreatures.Add(creature);
                    reservedIdentities.Add(
                        new KeyValuePair<ActionController, CreatureComponent>(controller, creature)
                    );
                }

                Dictionary<CreatureComponent, CreatureId> plannedCreatureIds = new(creatureIds);
                Dictionary<CreatureId, CreatureComponent> plannedCreatures = new(creatures);
                Dictionary<ActionController, CreatureId> plannedControllerIds = new(controllerIds);
                Dictionary<CreatureId, ActionController> plannedControllers = new(controllers);
                Dictionary<string, PlayerId> plannedTeamIds = new(teamIds, StringComparer.Ordinal);
                Dictionary<PlayerId, string> plannedTeamDisplayNames = new(teamDisplayNames);
                foreach (KeyValuePair<string, PlayerId> pendingTeam in pendingJoinTeamIds)
                {
                    if (!plannedTeamIds.ContainsKey(pendingTeam.Key))
                        plannedTeamIds.Add(pendingTeam.Key, pendingTeam.Value);
                    if (!plannedTeamDisplayNames.ContainsKey(pendingTeam.Value))
                        plannedTeamDisplayNames.Add(pendingTeam.Value, pendingTeam.Key);
                }

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
                for (int index = 0; index < copied.Length; index++)
                {
                    string display = GetTeamDisplayName(copied[index]);
                    if (teamIds.ContainsKey(display) || reservedTeams.ContainsKey(display))
                        continue;
                    PlayerId id = participants[index].Participant.Team;
                    if (
                        pendingJoinTeamIds.TryGetValue(display, out PlayerId pendingId)
                        && pendingId != id
                    )
                        throw new InvalidOperationException(
                            "Pending reinforcement team identity changed during planning."
                        );
                    if (!pendingJoinTeamIds.ContainsKey(display))
                        pendingJoinTeamIds.Add(display, id);
                    pendingJoinTeamReferences[display] = pendingJoinTeamReferences.TryGetValue(
                        display,
                        out int references
                    )
                        ? references + 1
                        : 1;
                    reservedTeams.Add(display, id);
                }

                JoinRootResolutionObserver observer = new(
                    this,
                    copied,
                    reservedIdentities,
                    reservedTeams,
                    plannedControllerIds,
                    markAccepted,
                    publishAcceptedControllers
                );
                await DispatchAsync(new JoinEncounterOp(encounterId, participants), observer);
                return new EncounterJoinOutcome(Snapshot.Encounters[encounterId]);
            }
            finally
            {
                foreach (
                    KeyValuePair<ActionController, CreatureComponent> reserved in reservedIdentities
                )
                {
                    pendingJoinControllers.Remove(reserved.Key);
                    pendingJoinCreatures.Remove(reserved.Value);
                }
                foreach (string display in reservedTeams.Keys)
                {
                    int references = pendingJoinTeamReferences[display] - 1;
                    if (references > 0)
                    {
                        pendingJoinTeamReferences[display] = references;
                        continue;
                    }
                    pendingJoinTeamReferences.Remove(display);
                    pendingJoinTeamIds.Remove(display);
                }
            }
        }

        private void PublishAcceptedJoin(
            IReadOnlyList<KeyValuePair<ActionController, CreatureComponent>> reservedIdentities,
            IReadOnlyDictionary<string, PlayerId> reservedTeams,
            IReadOnlyDictionary<ActionController, CreatureId> plannedControllerIds
        )
        {
            foreach (KeyValuePair<string, PlayerId> team in reservedTeams)
            {
                if (teamIds.TryGetValue(team.Key, out PlayerId committedTeam))
                {
                    if (committedTeam != team.Value)
                        throw new InvalidOperationException(
                            "Committed reinforcement team identity conflicts with its reservation."
                        );
                    continue;
                }
                teamIds.Add(team.Key, team.Value);
                teamDisplayNames.Add(team.Value, team.Key);
            }
            foreach (
                KeyValuePair<ActionController, CreatureComponent> reserved in reservedIdentities
            )
            {
                CreatureId id = plannedControllerIds[reserved.Key];
                creatureIds.Add(reserved.Value, id);
                creatures.Add(id, reserved.Value);
                controllerIds.Add(reserved.Key, id);
                controllers.Add(id, reserved.Key);
            }
            foreach (
                KeyValuePair<ActionController, CreatureComponent> reserved in reservedIdentities
            )
            {
                CreatureId id = controllerIds[reserved.Key];
                reserved.Value.AttachEncounterRules(this, id);
                reserved.Key.AttachEncounterRules(this, id);
            }
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
            // Rejected plans may leave a harmless sequence gap; IDs are never reused while another
            // accepted or pending join can still reference them.
            CreatureId id = new CreatureId($"encounter-creature-{++nextCreatureId}");
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
        /// <returns>
        /// The suspended encounter after effects driven by its retired finite clock expire.
        /// </returns>
        public async ValueTask<EncounterSuspensionOutcome> SuspendEncounter()
        {
            await DispatchAsync(new SuspendEncounterOp(encounterId));
            return new EncounterSuspensionOutcome(Snapshot.Encounters[encounterId]);
        }

        internal void ReleaseHostOwnership()
        {
            // A failed initial composition is discarded rather than resumed. Remove its observers
            // and identity maps only after dispatcher ownership returns to idle; component mementos
            // then restore the attachment that existed before this bridge was created.
            dispatcher.UnregisterFactObserver<HealthFact>(factObserver);
            dispatcher.UnregisterFactObserver<CreatureReducedToZeroFact>(factObserver);
            dispatcher.UnregisterFactObserver<TurnBeganFact>(factObserver);
            dispatcher.UnregisterFactObserver<TurnEndedFact>(factObserver);
            dispatcher.UnregisterFactObserver<EncounterEndedFact>(factObserver);
            dispatcher.UnregisterRootSettlementObserver(rootSettlementObserver);
            dispatcher.UnregisterCausalTreeSettlementObserver(rootSettlementObserver);
            DiscardStartupPresentationBuffer();
            presentationByRoot.Clear();
            presentationChildrenByRoot.Clear();
            settledPresentationRoots.Clear();
            creatureIds.Clear();
            creatures.Clear();
            controllerIds.Clear();
            controllers.Clear();
            pendingJoinCreatures.Clear();
            pendingJoinControllers.Clear();
            pendingJoinTeamIds.Clear();
            pendingJoinTeamReferences.Clear();
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

        /// <summary>Awaits exact-turn authorization and an optional same-store action spend.</summary>
        /// <param name="actor">The registered actor paying the cost.</param>
        /// <param name="amount">
        /// The non-negative number of actions to spend. Zero validates current-turn authority
        /// without changing action state or emitting a spend Fact.
        /// </param>
        /// <returns>The actor's committed remaining actions.</returns>
        public ValueTask<LegacyActionSpendOutcome> SpendActionsAsync(
            CreatureId actor,
            int amount
        ) => DispatchAsync(new SpendLegacyActionsOp(actor, amount));

        /// <summary>Awaits a turn-authorized MAP increment through the transitional same-store port.</summary>
        /// <param name="actor">The registered actor that must still own the exact current turn.</param>
        /// <returns>The actor's committed turn-scoped attack count.</returns>
        /// <exception cref="InvalidOperationException">
        /// The actor lost turn authority or the encounter ended before the queued request committed.
        /// </exception>
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
            string display = GetTeamDisplayName(controller);
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

        private static string GetTeamDisplayName(ActionController controller)
        {
            Team team = controller.GetComponent<Team>();
            return team == null || string.IsNullOrWhiteSpace(team.Name)
                ? "Unassigned"
                : team.Name.Trim();
        }

        private bool TryFindTeam(string displayName, out PlayerId id) =>
            teamIds.TryGetValue(displayName, out id);

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
            OpResult<TResult> result = await dispatcher.Dispatch(operation);
            return RequireResolved(result);
        }

        private async ValueTask<TResult> DispatchAsync<TResult>(
            IRuleOp<TResult> operation,
            IRootResolutionObserver<TResult> observer
        )
        {
            OpResult<TResult> result = await dispatcher.Dispatch(operation, observer);
            return RequireResolved(result);
        }

        private static TResult RequireResolved<TResult>(OpResult<TResult> result)
        {
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException($"Encounter request returned {result.Status}.");
        }

        private void EnqueuePresentation(RuleFact fact, Func<ValueTask> callback)
        {
            if (fact == null || !fact.IsStamped)
                throw new ArgumentException(
                    "Presentation requires a committed root-owned Fact.",
                    nameof(fact)
                );
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (!presentationByRoot.TryGetValue(fact.RootOpId, out Queue<Func<ValueTask>> root))
            {
                root = new Queue<Func<ValueTask>>();
                presentationByRoot.Add(fact.RootOpId, root);
            }
            root.Enqueue(callback);
        }

        private ValueTask PresentOrBufferStartupCallback(Func<ValueTask> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (startupPresentationBuffer == null)
                return callback();
            startupPresentationBuffer.Work.Enqueue(callback);
            return default;
        }

        private async ValueTask DrainPresentationAsync(OpId rootId)
        {
            List<Exception> failures = null;
            while (presentationByRoot.TryGetValue(rootId, out Queue<Func<ValueTask>> callbacks))
            {
                // Detach the exact root batch before invocation. A failure cannot leak its remaining
                // callbacks into another root, and causal dispatch receives a distinct root queue.
                presentationByRoot.Remove(rootId);
                while (callbacks.Count > 0)
                {
                    try
                    {
                        await callbacks.Dequeue().Invoke();
                    }
                    catch (Exception exception)
                    {
                        if (failures == null)
                            failures = new List<Exception>();
                        failures.Add(exception);
                    }
                }
            }

            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(
                $"Multiple presentation callbacks failed for root {rootId.Value}.",
                failures
            );
        }

        private void RecordSettledPresentationRoot(OpId rootId, OpId? causalParentRootId)
        {
            if (!settledPresentationRoots.Add(rootId))
                throw new InvalidOperationException(
                    $"Presentation root {rootId.Value} settled more than once."
                );

            if (causalParentRootId.HasValue)
            {
                if (
                    !presentationChildrenByRoot.TryGetValue(
                        causalParentRootId.Value,
                        out List<OpId> children
                    )
                )
                {
                    children = new List<OpId>();
                    presentationChildrenByRoot.Add(causalParentRootId.Value, children);
                }
                children.Add(rootId);
            }
        }

        private async ValueTask DrainSettledPresentationTreeAsync(OpId rootId)
        {
            List<Exception> failures = new List<Exception>();
            await DrainPresentationTreeAsync(rootId, failures);
            if (failures.Count == 0)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(
                $"Multiple presentation callbacks failed for causal tree {rootId.Value}.",
                failures
            );
        }

        private ValueTask DrainOrBufferSettledPresentationTreeAsync(OpId rootId)
        {
            if (startupPresentationBuffer == null)
                return DrainSettledPresentationTreeAsync(rootId);
            startupPresentationBuffer.AddRoot(rootId);
            startupPresentationBuffer.Work.Enqueue(() => DrainSettledPresentationTreeAsync(rootId));
            return default;
        }

        private async ValueTask DrainPresentationTreeAsync(OpId rootId, List<Exception> failures)
        {
            try
            {
                await DrainPresentationAsync(rootId);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            int childIndex = 0;
            while (
                presentationChildrenByRoot.TryGetValue(rootId, out List<OpId> children)
                && childIndex < children.Count
            )
            {
                OpId child = children[childIndex++];
                if (!settledPresentationRoots.Contains(child))
                {
                    failures.Add(
                        new InvalidOperationException(
                            $"Causal presentation root {child.Value} was not settled before ancestor {rootId.Value}."
                        )
                    );
                    continue;
                }
                await DrainPresentationTreeAsync(child, failures);
            }

            presentationChildrenByRoot.Remove(rootId);
            settledPresentationRoots.Remove(rootId);
        }

        private void DiscardPresentationTree(OpId rootId)
        {
            if (presentationChildrenByRoot.TryGetValue(rootId, out List<OpId> children))
            {
                foreach (OpId child in children.ToArray())
                    DiscardPresentationTree(child);
            }
            presentationByRoot.Remove(rootId);
            presentationChildrenByRoot.Remove(rootId);
            settledPresentationRoots.Remove(rootId);
        }

        private async ValueTask InvokeEncounterEnded(EncounterOutcome outcome)
        {
            Delegate[] handlers = EncounterEnded.GetInvocationList();
            foreach (Delegate handler in handlers)
                await ((Func<EncounterOutcome, ValueTask>)handler)(outcome);
        }

        private sealed class JoinRootResolutionObserver
            : IRootResolutionObserver<EncounterJoinOutcome>
        {
            private readonly UnityEncounterRulesBridge owner;
            private readonly ActionController[] controllers;
            private readonly IReadOnlyList<
                KeyValuePair<ActionController, CreatureComponent>
            > reservedIdentities;
            private readonly IReadOnlyDictionary<string, PlayerId> reservedTeams;
            private readonly IReadOnlyDictionary<ActionController, CreatureId> plannedControllerIds;
            private readonly Action markAccepted;
            private readonly Action publishAcceptedControllers;

            internal JoinRootResolutionObserver(
                UnityEncounterRulesBridge owner,
                ActionController[] controllers,
                IReadOnlyList<KeyValuePair<ActionController, CreatureComponent>> reservedIdentities,
                IReadOnlyDictionary<string, PlayerId> reservedTeams,
                IReadOnlyDictionary<ActionController, CreatureId> plannedControllerIds,
                Action markAccepted,
                Action publishAcceptedControllers
            )
            {
                this.owner = owner;
                this.controllers = controllers;
                this.reservedIdentities = reservedIdentities;
                this.reservedTeams = reservedTeams;
                this.plannedControllerIds = plannedControllerIds;
                this.markAccepted = markAccepted;
                this.publishAcceptedControllers = publishAcceptedControllers;
            }

            /// <inheritdoc/>
            public async ValueTask OnRootResolved(
                OpId rootId,
                OpResult<EncounterJoinOutcome> result,
                RulesSnapshot snapshot
            )
            {
                if (!(result is ResolvedOpResult<EncounterJoinOutcome>))
                    return;

                markAccepted();
                owner.PublishAcceptedJoin(reservedIdentities, reservedTeams, plannedControllerIds);
                // Reducer acceptance and Unity identity/host publication are one root-owned
                // boundary. Initialization may await or fail, but a durably joined future turn
                // owner must already be resolvable and included in host cleanup.
                publishAcceptedControllers();
                await Pf2eRulesEngine.ApplyCombatStartRulesAsync(controllers);
            }
        }

        private sealed class BridgeRootSettlementObserver
            : IRootSettlementObserver,
                ICausalTreeSettlementObserver
        {
            private readonly UnityEncounterRulesBridge owner;

            internal BridgeRootSettlementObserver(UnityEncounterRulesBridge owner) =>
                this.owner = owner;

            /// <inheritdoc/>
            public ValueTask OnRootSettled(
                OpId rootId,
                OpId? causalParentRootId,
                RulesSnapshot snapshot
            )
            {
                owner.RecordSettledPresentationRoot(rootId, causalParentRootId);
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnCausalTreeSettled(OpId rootId, RulesSnapshot snapshot) =>
                owner.DrainOrBufferSettledPresentationTreeAsync(rootId);
        }

        private sealed class SpellExpiryTurnStartAdapter : IEncounterTurnStartAdapter
        {
            private readonly UnityEncounterRulesBridge owner;

            public SpellExpiryTurnStartAdapter(UnityEncounterRulesBridge owner) =>
                this.owner = owner;

            public async ValueTask<TurnStartContribution> Apply(
                EncounterTurnStartContext context,
                TurnStartContribution current
            )
            {
                await owner.PresentOrBufferStartupCallback(() =>
                {
                    SpellEffectController.ExpireAtStartOfTurn(
                        owner.GetController(context.Actor).gameObject
                    );
                    return default;
                });
                return current;
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
                    },
                    result =>
                        owner.PresentOrBufferStartupCallback(() =>
                        {
                            RottingAuraRule.Present(result);
                            return default;
                        })
                );
                return current;
            }
        }

        private sealed class StartupPresentationBuffer
        {
            internal Queue<Func<ValueTask>> Work { get; } = new();
            internal List<OpId> Roots { get; } = new();

            internal void AddRoot(OpId rootId)
            {
                if (!Roots.Contains(rootId))
                    Roots.Add(rootId);
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
                    bool presentHit = fact is DamageAppliedFact;
                    owner.EnqueuePresentation(
                        fact,
                        () =>
                        {
                            if (
                                !owner.Snapshot.Health.TryGet(
                                    fact.Creature,
                                    out HealthState settledHealth
                                )
                            )
                                return default;
                            creature.ProjectCommittedHealth(settledHealth);
                            if (presentHit)
                                creature.PresentCommittedHit();
                            return default;
                        }
                    );
                }
                return default;
            }

            public ValueTask OnFactCommitted(CreatureReducedToZeroFact fact, RulesSnapshot snapshot)
            {
                if (
                    owner.creatures.TryGetValue(fact.Creature, out CreatureComponent creature)
                    && creature != null
                )
                    owner.EnqueuePresentation(
                        fact,
                        () =>
                        {
                            if (
                                !owner.Snapshot.Health.TryGet(
                                    fact.Creature,
                                    out HealthState settledHealth
                                )
                            )
                                return default;
                            creature.ProjectCommittedHealth(settledHealth);
                            if (settledHealth.Current > 0)
                                return default;
                            creature.PresentCommittedDefeat();
                            return default;
                        }
                    );
                return default;
            }

            public ValueTask OnFactCommitted(TurnBeganFact fact, RulesSnapshot snapshot)
            {
                owner.EnqueuePresentation(
                    fact,
                    () =>
                    {
                        owner.TurnBegan.Invoke(fact.Turn);
                        return default;
                    }
                );
                return default;
            }

            public ValueTask OnFactCommitted(TurnEndedFact fact, RulesSnapshot snapshot)
            {
                owner.EnqueuePresentation(
                    fact,
                    () =>
                    {
                        owner.TurnEnded.Invoke(fact.Turn);
                        return default;
                    }
                );
                return default;
            }

            public ValueTask OnFactCommitted(EncounterEndedFact fact, RulesSnapshot snapshot)
            {
                owner.EnqueuePresentation(fact, () => owner.InvokeEncounterEnded(fact.Outcome));
                return default;
            }
        }
    }
}
