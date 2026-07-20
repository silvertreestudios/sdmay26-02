using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Creature;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Owns the authoritative health store, dispatcher, stable creature IDs, and Unity projection
    /// map for one encounter.
    /// </summary>
    /// <remarks>
    /// IDs are allocated monotonically in the supplied encounter order and never derive from a
    /// name or Unity instance ID. Every health request also receives a monotonically allocated
    /// origin ID whose map retains only its <see cref="RuleSource"/>. The bridge keeps its
    /// dispatcher private and composes only completion-synchronous health handlers and projection
    /// observers, allowing existing Unity gameplay entry points to observe committed state before
    /// returning without blocking the Unity thread.
    /// </remarks>
    public sealed class UnityHealthRulesBridge
    {
        private readonly Dictionary<CreatureComponent, CreatureId> creatureIds;
        private readonly Dictionary<CreatureId, CreatureComponent> creatures;
        private readonly Dictionary<HealthChangeOriginId, RuleSource> origins = new();
        private readonly RuleDispatcher dispatcher;
        private long nextOriginId;

        private UnityHealthRulesBridge(IReadOnlyList<CreatureComponent> encounterCreatures)
        {
            creatureIds = new Dictionary<CreatureComponent, CreatureId>();
            creatures = new Dictionary<CreatureId, CreatureComponent>();
            RulesStateSeed seed = new RulesStateSeed();
            for (int index = 0; index < encounterCreatures.Count; index++)
            {
                CreatureComponent creature = encounterCreatures[index];
                CreatureId id = new CreatureId($"health-creature-{index + 1}");
                creatureIds.Add(creature, id);
                creatures.Add(id, creature);
                seed.SeedHealth(id, creature.GetHealthInitializationState());
            }

            dispatcher = new RuleDispatcherBuilder(new InMemoryRulesStore(seed))
                .UseHealthRules()
                .Build();
            HealthProjectionObserver observer = new HealthProjectionObserver(creatures);
            dispatcher.RegisterFactObserver<HealthFact>(observer);
            dispatcher.RegisterFactObserver<CreatureReducedToZeroFact>(observer);

            foreach (KeyValuePair<CreatureComponent, CreatureId> entry in creatureIds)
                entry.Key.AttachHealthRules(this, entry.Value);
        }

        /// <summary>
        /// Creates one health composition root for the supplied encounter participants.
        /// </summary>
        /// <param name="encounterCreatures">The non-empty, unique creature sequence.</param>
        /// <returns>The initialized encounter health bridge.</returns>
        public static UnityHealthRulesBridge Create(
            IEnumerable<CreatureComponent> encounterCreatures
        )
        {
            if (encounterCreatures == null)
                throw new ArgumentNullException(nameof(encounterCreatures));
            CreatureComponent[] copied = encounterCreatures.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException(
                    "An encounter health bridge requires at least one creature.",
                    nameof(encounterCreatures)
                );
            if (copied.Any(creature => creature == null))
                throw new ArgumentException(
                    "An encounter health bridge cannot contain a null creature.",
                    nameof(encounterCreatures)
                );
            if (copied.Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "An encounter creature cannot be registered more than once.",
                    nameof(encounterCreatures)
                );
            return new UnityHealthRulesBridge(copied);
        }

        /// <summary>Gets the latest authoritative snapshot for bridge consumers and tests.</summary>
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
        /// <param name="creature">The encounter-stable rules identifier.</param>
        /// <returns>The latest committed health state.</returns>
        public HealthState GetHealth(CreatureId creature)
        {
            if (!dispatcher.Snapshot.Health.TryGet(creature, out HealthState health))
                throw new InvalidOperationException("Creature has no authoritative health state.");
            return health;
        }

        /// <summary>Looks up the minimal source retained for an encounter health origin.</summary>
        /// <param name="origin">The origin allocated for a health request.</param>
        /// <param name="source">Receives the existing rules source when found.</param>
        /// <returns><see langword="true"/> when the origin belongs to this encounter.</returns>
        public bool TryGetOriginSource(HealthChangeOriginId origin, out RuleSource source) =>
            origins.TryGetValue(origin, out source);

        /// <summary>Commits already-final damage and returns its exact applied breakdown.</summary>
        /// <param name="target">The registered creature to damage.</param>
        /// <param name="finalDamage">Damage after upstream calculations.</param>
        /// <param name="source">The existing rules source responsible for the damage.</param>
        /// <returns>The exact committed damage breakdown.</returns>
        public DamageOutcome ApplyFinalDamage(
            CreatureId target,
            int finalDamage,
            RuleSource source
        ) => DispatchNow(new ApplyDamageOp(target, finalDamage, AllocateOrigin(source), source));

        /// <summary>Commits healing and returns the amount that survived maximum-HP clamping.</summary>
        /// <param name="target">The registered creature to heal.</param>
        /// <param name="healing">The non-negative healing offered.</param>
        /// <param name="source">The existing rules source responsible for the healing.</param>
        /// <returns>The exact committed healing outcome.</returns>
        public HealingOutcome ApplyHealing(CreatureId target, int healing, RuleSource source) =>
            DispatchNow(new ApplyHealingOp(target, healing, AllocateOrigin(source), source));

        /// <summary>Attempts a source-owned, non-stacking temporary Hit Point grant.</summary>
        /// <param name="target">The registered creature receiving the offer.</param>
        /// <param name="amount">The non-negative temporary-HP pool offered.</param>
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

        /// <summary>Removes temporary Hit Points still owned by the supplied source.</summary>
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
            // This is a contract assertion, not sync-over-async. The dispatcher cannot be
            // extended outside this composition root, and every composed callback returns a
            // completed ValueTask. If that invariant changes, fail immediately instead of
            // blocking Unity's thread or allowing gameplay to continue before projection.
            ValueTask<OpResult<TResult>> pending = dispatcher.Dispatch(operation);
            if (!pending.IsCompleted)
            {
                throw new InvalidOperationException(
                    "Unity health requests must complete synchronously; an asynchronous observer "
                        + "must be driven by an async gameplay workflow before it is registered here."
                );
            }

            // GetResult cannot block after IsCompleted and preserves a completed dispatcher
            // failure or cancellation instead of replacing it with the async-observer assertion.
            OpResult<TResult> result = pending.GetAwaiter().GetResult();
            if (result is ResolvedOpResult<TResult> resolved)
                return resolved.Value;
            if (result is InvalidOpResult<TResult> invalid)
                throw new InvalidOperationException(invalid.Reason);
            throw new InvalidOperationException(
                "A health request cannot be interrupted or cancelled."
            );
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
                    creature.PresentCommittedDefeat();
                return default;
            }
        }
    }
}
