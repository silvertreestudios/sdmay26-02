using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Describes one player-visible active or derived rules effect without Unity references.
    /// </summary>
    public sealed class VisibleEffectProjection
    {
        /// <summary>
        /// Initializes one visible effect row for a creature presentation.
        /// </summary>
        /// <param name="definitionId">The stable rule definition that explains the effect.</param>
        /// <param name="source">The stable feat, spell, condition, item, or system source.</param>
        /// <param name="label">The concise player-facing label.</param>
        /// <param name="isDerived">
        /// Whether the effect is calculated from current state instead of stored as a target-owned instance.
        /// </param>
        /// <exception cref="ArgumentException">An ID, source, or label is empty.</exception>
        public VisibleEffectProjection(
            RuleDefinitionId definitionId,
            RuleSource source,
            string label,
            bool isDerived)
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException("A rule definition ID is required.", nameof(definitionId));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("A visible effect label is required.", nameof(label));
            DefinitionId = definitionId;
            Source = source;
            Label = label.Trim();
            IsDerived = isDerived;
        }

        /// <summary>
        /// Gets the stable definition responsible for the effect.
        /// </summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>
        /// Gets the stable content or system source responsible for the effect.
        /// </summary>
        public RuleSource Source { get; }

        /// <summary>
        /// Gets the concise player-facing effect label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Gets whether the effect is derived from current state rather than target-owned storage.
        /// </summary>
        public bool IsDerived { get; }
    }

    /// <summary>
    /// Contributes visible effect rows for one creature from an immutable snapshot.
    /// </summary>
    public interface IVisibleEffectProjectionSource
    {
        /// <summary>
        /// Selects this source's stored or derived visible effects for one creature.
        /// </summary>
        /// <param name="snapshot">The immutable current rules state.</param>
        /// <param name="creature">The creature whose display is being refreshed.</param>
        /// <returns>A required, caller-independent list of visible effects.</returns>
        IReadOnlyList<VisibleEffectProjection> Select(
            RulesSnapshot snapshot,
            CreatureId creature);
    }

    /// <summary>
    /// Combines registered stored and derived visible-effect selectors in registration order.
    /// </summary>
    public sealed class VisibleEffectProjectionSelector
    {
        private readonly List<IVisibleEffectProjectionSource> sources =
            new List<IVisibleEffectProjectionSource>();

        /// <summary>
        /// Registers one focused visible-effect projection source.
        /// </summary>
        /// <param name="source">The required selector contribution.</param>
        /// <returns>This selector so composition can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        public VisibleEffectProjectionSelector Register(
            IVisibleEffectProjectionSource source)
        {
            sources.Add(source ?? throw new ArgumentNullException(nameof(source)));
            return this;
        }

        /// <summary>
        /// Selects all visible effects for one creature without polling Unity objects.
        /// </summary>
        /// <param name="snapshot">The immutable current rules state.</param>
        /// <param name="creature">The creature whose display is being refreshed.</param>
        /// <returns>A read-only list in selector registration order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="creature"/> is empty.</exception>
        public IReadOnlyList<VisibleEffectProjection> Select(
            RulesSnapshot snapshot,
            CreatureId creature)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (creature.IsEmpty)
                throw new ArgumentException("A creature ID is required.", nameof(creature));

            List<VisibleEffectProjection> combined = new List<VisibleEffectProjection>();
            foreach (IVisibleEffectProjectionSource source in sources)
            {
                IReadOnlyList<VisibleEffectProjection> selected = source.Select(snapshot, creature) ??
                    throw new InvalidOperationException(
                        "A visible-effect projection source returned null.");
                foreach (VisibleEffectProjection effect in selected)
                {
                    combined.Add(effect ?? throw new InvalidOperationException(
                        "A visible-effect projection source returned a null effect."));
                }
            }
            return combined.AsReadOnly();
        }
    }

    /// <summary>
    /// Identifies creatures whose visible effects may have changed because one Fact committed.
    /// </summary>
    /// <typeparam name="TFact">The exact Fact type understood by the invalidator.</typeparam>
    public interface IVisibleEffectInvalidator<TFact>
        where TFact : RuleFact
    {
        /// <summary>
        /// Gets only the creatures whose visible projections require recomputation.
        /// </summary>
        /// <param name="fact">The typed committed Fact.</param>
        /// <param name="previousSnapshot">State immediately before the Fact committed.</param>
        /// <param name="currentSnapshot">State immediately after the Fact committed.</param>
        /// <returns>A required collection of affected creature IDs.</returns>
        IReadOnlyCollection<CreatureId> GetAffectedCreatures(
            TFact fact,
            RulesSnapshot previousSnapshot,
            RulesSnapshot currentSnapshot);
    }

    /// <summary>
    /// Combines typed visible-effect invalidators and returns deterministic distinct creature IDs.
    /// </summary>
    public sealed class VisibleEffectInvalidatorRegistry
    {
        private readonly Dictionary<Type, List<IInvalidatorRegistration>> registrations =
            new Dictionary<Type, List<IInvalidatorRegistration>>();

        /// <summary>
        /// Registers one invalidator for an exact concrete Fact type.
        /// </summary>
        /// <typeparam name="TFact">The Fact type understood by <paramref name="invalidator"/>.</typeparam>
        /// <param name="invalidator">The required typed invalidator.</param>
        /// <returns>This registry so composition can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="invalidator"/> is <see langword="null"/>.</exception>
        public VisibleEffectInvalidatorRegistry Register<TFact>(
            IVisibleEffectInvalidator<TFact> invalidator)
            where TFact : RuleFact
        {
            if (invalidator == null)
                throw new ArgumentNullException(nameof(invalidator));
            if (!registrations.TryGetValue(
                typeof(TFact),
                out List<IInvalidatorRegistration> typedRegistrations))
            {
                typedRegistrations = new List<IInvalidatorRegistration>();
                registrations.Add(typeof(TFact), typedRegistrations);
            }
            typedRegistrations.Add(new InvalidatorRegistration<TFact>(invalidator));
            return this;
        }

        /// <summary>
        /// Gets deterministic distinct creatures affected by one committed Fact.
        /// </summary>
        /// <param name="commit">The Fact and exact before/after snapshot pair.</param>
        /// <returns>A read-only collection sorted by stable creature ID.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="commit"/> is <see langword="null"/>.</exception>
        public IReadOnlyList<CreatureId> GetAffectedCreatures(CommittedRuleFact commit)
        {
            if (commit == null)
                throw new ArgumentNullException(nameof(commit));
            if (!registrations.TryGetValue(
                commit.Fact.GetType(),
                out List<IInvalidatorRegistration> typedRegistrations))
            {
                return Array.AsReadOnly(Array.Empty<CreatureId>());
            }

            HashSet<CreatureId> affected = new HashSet<CreatureId>();
            foreach (IInvalidatorRegistration registration in typedRegistrations)
            {
                IReadOnlyCollection<CreatureId> selected = registration.GetAffectedCreatures(commit);
                foreach (CreatureId creature in selected)
                {
                    if (creature.IsEmpty)
                    {
                        throw new InvalidOperationException(
                            "A visible-effect invalidator returned an empty creature ID.");
                    }
                    affected.Add(creature);
                }
            }

            List<CreatureId> ordered = new List<CreatureId>(affected);
            ordered.Sort((left, right) => string.Compare(
                left.Value,
                right.Value,
                StringComparison.Ordinal));
            return ordered.AsReadOnly();
        }

        private interface IInvalidatorRegistration
        {
            IReadOnlyCollection<CreatureId> GetAffectedCreatures(CommittedRuleFact commit);
        }

        private sealed class InvalidatorRegistration<TFact> : IInvalidatorRegistration
            where TFact : RuleFact
        {
            private readonly IVisibleEffectInvalidator<TFact> invalidator;

            public InvalidatorRegistration(IVisibleEffectInvalidator<TFact> invalidator) =>
                this.invalidator = invalidator;

            public IReadOnlyCollection<CreatureId> GetAffectedCreatures(CommittedRuleFact commit)
            {
                if (!(commit.Fact is TFact fact))
                {
                    throw new InvalidOperationException(
                        "A visible-effect invalidator received an incompatible registration.");
                }
                return invalidator.GetAffectedCreatures(
                    fact,
                    commit.PreviousSnapshot,
                    commit.CurrentSnapshot) ?? throw new InvalidOperationException(
                        $"The {typeof(TFact).Name} visible-effect invalidator returned null.");
            }
        }
    }

    /// <summary>
    /// Applies a recomputed visible-effect list to one creature's Unity presentation.
    /// </summary>
    public interface IVisibleEffectProjectionSink
    {
        /// <summary>
        /// Replaces the displayed visible effects for one affected creature.
        /// </summary>
        /// <param name="creature">The stable creature ID being refreshed.</param>
        /// <param name="effects">The complete current visible-effect projection.</param>
        void Refresh(
            CreatureId creature,
            IReadOnlyList<VisibleEffectProjection> effects);
    }
}
