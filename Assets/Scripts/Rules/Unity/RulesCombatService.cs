using System;
using System.Threading.Tasks;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Gives Unity adapters one rules runtime and one encounter-scoped identity map.
    /// </summary>
    /// <remarks>
    /// The service deliberately exposes no state mutation API. Registering a Unity object records
    /// only its adapter identity; it does not seed or update a <see cref="RulesState"/> slice.
    /// Every current gameplay field therefore remains legacy-owned until its vertical migration
    /// explicitly supplies authoritative seed data and retires the old writer.
    /// </remarks>
    public sealed class RulesCombatService : IRulesRuntime
    {
        private readonly IRulesRuntime runtime;

        /// <summary>
        /// Initializes the combat adapter over an already configured rules runtime.
        /// </summary>
        /// <param name="runtime">The required Unity-free runtime.</param>
        /// <param name="identities">The required encounter-scoped Unity identity map.</param>
        /// <exception cref="ArgumentNullException">Either dependency is <see langword="null"/>.</exception>
        public RulesCombatService(
            IRulesRuntime runtime,
            UnityRulesIdentityMap identities)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Identities = identities ?? throw new ArgumentNullException(nameof(identities));
        }

        /// <summary>
        /// Gets the explicit mappings between stable rules IDs and presentation objects.
        /// </summary>
        public UnityRulesIdentityMap Identities { get; }

        /// <inheritdoc/>
        public RulesSnapshot Snapshot => runtime.Snapshot;

        /// <inheritdoc/>
        public event Action<CommittedRuleFact> FactCommitted
        {
            add => runtime.FactCommitted += value;
            remove => runtime.FactCommitted -= value;
        }

        /// <inheritdoc/>
        public ValueTask<OpResult<TResult>> Dispatch<TResult>(IRuleOp<TResult> op) =>
            runtime.Dispatch(op);
    }

    /// <summary>
    /// Builds the encounter rules service owned by the current <see cref="GameManager"/>.
    /// </summary>
    public static class RulesCombatComposition
    {
        /// <summary>
        /// Creates the foundation runtime with no authoritative gameplay slices seeded.
        /// </summary>
        /// <returns>
        /// A service ready for identity registration and future externally allowed operations.
        /// </returns>
        /// <remarks>
        /// Empty seed data is intentional while HP, position, actions, equipment, conditions, and
        /// effects still belong to legacy Unity components. A feature migration must replace this
        /// construction path with an explicitly reviewed seed for only the slice it takes over.
        /// </remarks>
        public static RulesCombatService CreateFoundation()
        {
            RuleDispatcher dispatcher = new RuleDispatcherBuilder(
                new InMemoryRulesStore(new RulesStateSeed())).Build();
            return new RulesCombatService(dispatcher, new UnityRulesIdentityMap());
        }
    }
}
