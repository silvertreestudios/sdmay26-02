using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the PF2e stacking category of a rules modifier.
    /// </summary>
    public enum ModifierType
    {
        /// <summary>
        /// An untyped modifier; every untyped bonus and penalty stacks.
        /// </summary>
        Untyped,

        /// <summary>
        /// A circumstance modifier; only the greatest bonus and worst penalty apply.
        /// </summary>
        Circumstance,

        /// <summary>
        /// An item modifier; only the greatest bonus and worst penalty apply.
        /// </summary>
        Item,

        /// <summary>
        /// A status modifier; only the greatest bonus and worst penalty apply.
        /// </summary>
        Status
    }

    /// <summary>
    /// Identifies the statistic affected by a normalized modifier.
    /// </summary>
    public enum Statistic
    {
        /// <summary>An attack roll total.</summary>
        AttackRoll,
        /// <summary>Armor Class.</summary>
        ArmorClass,
        /// <summary>A Fortitude saving throw.</summary>
        FortitudeSave,
        /// <summary>A Reflex saving throw.</summary>
        ReflexSave,
        /// <summary>A Will saving throw.</summary>
        WillSave,
        /// <summary>A skill check.</summary>
        SkillCheck,
        /// <summary>An initiative roll.</summary>
        Initiative,
        /// <summary>A general difficulty class.</summary>
        DifficultyClass
    }

    /// <summary>
    /// Identifies one of the three PF2e saving throws.
    /// </summary>
    public enum SaveKind
    {
        /// <summary>Fortitude.</summary>
        Fortitude,
        /// <summary>Reflex.</summary>
        Reflex,
        /// <summary>Will.</summary>
        Will
    }

    /// <summary>
    /// Describes one normalized modifier with stable provenance and a single target statistic.
    /// </summary>
    /// <remarks>
    /// Feature-specific eligibility belongs in selectors or middleware. Once eligible, a rule
    /// contributes this plain value so the shared resolver can apply typed stacking consistently.
    /// </remarks>
    public readonly struct Modifier : IEquatable<Modifier>
    {
        /// <summary>
        /// Gets the signed amount contributed when this modifier applies.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Gets the stacking category.
        /// </summary>
        public ModifierType Type { get; }

        /// <summary>
        /// Gets the stable rule source that contributed the modifier.
        /// </summary>
        public RuleSource Source { get; }

        /// <summary>
        /// Gets the statistic this modifier can affect.
        /// </summary>
        public Statistic Statistic { get; }

        /// <summary>
        /// Gets whether this is the uninitialized default modifier.
        /// </summary>
        public bool IsEmpty => Source.IsEmpty;

        /// <summary>
        /// Creates one normalized modifier.
        /// </summary>
        /// <param name="value">The signed modifier amount.</param>
        /// <param name="type">The PF2e stacking category.</param>
        /// <param name="source">The stable source responsible for the modifier.</param>
        /// <param name="statistic">The only statistic this modifier affects.</param>
        /// <exception cref="ArgumentException"><paramref name="source"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="type"/> or <paramref name="statistic"/> is not a defined value.
        /// </exception>
        public Modifier(int value, ModifierType type, RuleSource source, Statistic statistic)
        {
            if (!Enum.IsDefined(typeof(ModifierType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            if (!Enum.IsDefined(typeof(Statistic), statistic))
                throw new ArgumentOutOfRangeException(nameof(statistic));
            if (source.IsEmpty)
                throw new ArgumentException("A modifier requires a stable rule source.", nameof(source));

            Value = value;
            Type = type;
            Source = source;
            Statistic = statistic;
        }

        /// <summary>
        /// Creates an untyped modifier for a base statistic or another stacking source.
        /// </summary>
        /// <param name="value">The signed amount.</param>
        /// <param name="source">The stable source responsible for the value.</param>
        /// <param name="statistic">The affected statistic.</param>
        /// <returns>An untyped modifier.</returns>
        public static Modifier Untyped(int value, RuleSource source, Statistic statistic) =>
            new Modifier(value, ModifierType.Untyped, source, statistic);

        /// <summary>
        /// Creates a positive status bonus while rejecting a penalty accidentally passed as a bonus.
        /// </summary>
        /// <param name="value">The positive bonus amount.</param>
        /// <param name="source">The stable source responsible for the bonus.</param>
        /// <param name="statistic">The affected statistic.</param>
        /// <returns>A status bonus.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive.</exception>
        public static Modifier StatusBonus(int value, RuleSource source, Statistic statistic)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "A status bonus must be positive.");
            return new Modifier(value, ModifierType.Status, source, statistic);
        }

        /// <inheritdoc/>
        public bool Equals(Modifier other) =>
            Value == other.Value && Type == other.Type && Source == other.Source &&
            Statistic == other.Statistic;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Modifier other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Value, Type, Source, Statistic);

        /// <summary>
        /// Compares every normalized modifier field.
        /// </summary>
        public static bool operator ==(Modifier left, Modifier right) => left.Equals(right);

        /// <summary>
        /// Compares every normalized modifier field.
        /// </summary>
        public static bool operator !=(Modifier left, Modifier right) => !left.Equals(right);
    }

    /// <summary>
    /// Resolves an immutable set of candidate modifiers for one statistic.
    /// </summary>
    /// <remarks>
    /// Untyped values all apply. For each typed category, the greatest bonus and worst penalty
    /// apply while weaker values are retained in <see cref="Suppressed"/> for diagnostics. Input
    /// order breaks equal-value ties, preserving deterministic middleware and seed ordering.
    /// </remarks>
    public sealed class ModifierCollection : IEquatable<ModifierCollection>
    {
        private readonly IReadOnlyList<Modifier> candidates;
        private readonly IReadOnlyList<Modifier> applied;
        private readonly IReadOnlyList<Modifier> suppressed;

        /// <summary>
        /// Gets the statistic resolved by this collection.
        /// </summary>
        public Statistic Statistic { get; }

        /// <summary>
        /// Gets every supplied candidate in deterministic collection order.
        /// </summary>
        public IReadOnlyList<Modifier> Candidates => candidates;

        /// <summary>
        /// Gets the candidates that contribute to <see cref="Total"/>.
        /// </summary>
        public IReadOnlyList<Modifier> Applied => applied;

        /// <summary>
        /// Gets relevant typed candidates suppressed by stronger same-type values.
        /// </summary>
        public IReadOnlyList<Modifier> Suppressed => suppressed;

        /// <summary>
        /// Gets the sum of every applied modifier.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Resolves the supplied candidates for one statistic.
        /// </summary>
        /// <param name="statistic">The statistic to resolve.</param>
        /// <param name="candidates">Candidate modifiers in deterministic collection order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="statistic"/> is undefined.</exception>
        public ModifierCollection(Statistic statistic, IEnumerable<Modifier> candidates)
        {
            if (!Enum.IsDefined(typeof(Statistic), statistic))
                throw new ArgumentOutOfRangeException(nameof(statistic));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            Statistic = statistic;
            Modifier[] copied = candidates.ToArray();
            if (copied.Any(modifier => modifier.IsEmpty))
                throw new ArgumentException("Modifier candidates cannot contain an empty value.", nameof(candidates));
            this.candidates = Array.AsReadOnly(copied);

            List<Modifier> relevant = copied
                .Where(modifier => modifier.Statistic == statistic && modifier.Value != 0)
                .ToList();
            List<Modifier> appliedValues = relevant
                .Where(modifier => modifier.Type == ModifierType.Untyped)
                .ToList();
            List<Modifier> suppressedValues = new List<Modifier>();

            ResolveTyped(relevant, ModifierType.Circumstance, appliedValues, suppressedValues);
            ResolveTyped(relevant, ModifierType.Item, appliedValues, suppressedValues);
            ResolveTyped(relevant, ModifierType.Status, appliedValues, suppressedValues);

            applied = Array.AsReadOnly(appliedValues.ToArray());
            suppressed = Array.AsReadOnly(suppressedValues.ToArray());
            Total = appliedValues.Sum(modifier => modifier.Value);
        }

        /// <summary>
        /// Returns a new collection with one candidate appended after existing candidates.
        /// </summary>
        /// <param name="modifier">The modifier contributed by a selector or middleware binding.</param>
        /// <returns>A newly resolved immutable collection.</returns>
        public ModifierCollection Add(Modifier modifier) =>
            new ModifierCollection(Statistic, candidates.Concat(new[] { modifier }));

        /// <inheritdoc/>
        public bool Equals(ModifierCollection other) =>
            other != null && Statistic == other.Statistic &&
            candidates.SequenceEqual(other.candidates);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ModifierCollection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Statistic, Total, candidates.Count);

        private static void ResolveTyped(
            IEnumerable<Modifier> relevant,
            ModifierType type,
            ICollection<Modifier> applied,
            ICollection<Modifier> suppressed)
        {
            Modifier[] bonuses = relevant
                .Where(modifier => modifier.Type == type && modifier.Value > 0)
                .OrderByDescending(modifier => modifier.Value)
                .ToArray();
            if (bonuses.Length > 0)
            {
                applied.Add(bonuses[0]);
                for (int index = 1; index < bonuses.Length; index++)
                    suppressed.Add(bonuses[index]);
            }

            Modifier[] penalties = relevant
                .Where(modifier => modifier.Type == type && modifier.Value < 0)
                .OrderBy(modifier => modifier.Value)
                .ToArray();
            if (penalties.Length > 0)
            {
                applied.Add(penalties[0]);
                for (int index = 1; index < penalties.Length; index++)
                    suppressed.Add(penalties[index]);
            }
        }
    }
}
