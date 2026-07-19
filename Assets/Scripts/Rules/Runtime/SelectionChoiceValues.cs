using System;

namespace Game.Rules.Runtime
{
    /// <summary>Contains one selected spell variant.</summary>
    public readonly struct SpellVariantSelection : IEquatable<SpellVariantSelection>
    {
        /// <summary>Gets the selected variant.</summary>
        public SpellVariantId Variant { get; }

        /// <summary>Creates a spell-variant selection.</summary>
        /// <param name="variant">The selected variant.</param>
        public SpellVariantSelection(SpellVariantId variant)
        {
            if (variant.IsEmpty)
                throw new ArgumentException("A spell variant is required.", nameof(variant));
            Variant = variant;
        }

        /// <inheritdoc/>
        public bool Equals(SpellVariantSelection other) => Variant.Equals(other.Variant);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is SpellVariantSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Variant.GetHashCode();
    }

    /// <summary>Contains one selected authoritative spell-slot pool.</summary>
    public readonly struct SpellSlotSelection : IEquatable<SpellSlotSelection>
    {
        /// <summary>Gets the selected spell-slot pool.</summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>Creates a spell-slot selection.</summary>
        /// <param name="pool">The selected spell-slot pool.</param>
        public SpellSlotSelection(SpellSlotPoolId pool)
        {
            if (pool.IsEmpty)
                throw new ArgumentException("A spell-slot pool is required.", nameof(pool));
            Pool = pool;
        }

        /// <inheritdoc/>
        public bool Equals(SpellSlotSelection other) => Pool.Equals(other.Pool);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellSlotSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Pool.GetHashCode();
    }

    /// <summary>Contains an explicit yes or no response that is distinct from cancellation.</summary>
    public readonly struct ConfirmationSelection : IEquatable<ConfirmationSelection>
    {
        /// <summary>Gets whether the caller explicitly confirmed the request.</summary>
        public bool IsConfirmed { get; }

        /// <summary>Creates an explicit confirmation or decline.</summary>
        /// <param name="isConfirmed"><see langword="true"/> to confirm; otherwise decline.</param>
        public ConfirmationSelection(bool isConfirmed) => IsConfirmed = isConfirmed;

        /// <inheritdoc/>
        public bool Equals(ConfirmationSelection other) => IsConfirmed == other.IsConfirmed;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ConfirmationSelection other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => IsConfirmed.GetHashCode();
    }

    /// <summary>Preserves the typed result and order of two composed workflow steps.</summary>
    /// <typeparam name="TFirst">The first completed selection type.</typeparam>
    /// <typeparam name="TSecond">The second completed selection type.</typeparam>
    public sealed class OrderedSelection<TFirst, TSecond>
    {
        /// <summary>Gets the first completed value.</summary>
        public TFirst First { get; }

        /// <summary>Gets the second completed value.</summary>
        public TSecond Second { get; }

        /// <summary>Creates a pair that preserves workflow order.</summary>
        /// <param name="first">The non-null first result.</param>
        /// <param name="second">The non-null second result.</param>
        public OrderedSelection(TFirst first, TSecond second)
        {
            if (ReferenceEquals(first, null))
                throw new ArgumentNullException(nameof(first));
            if (ReferenceEquals(second, null))
                throw new ArgumentNullException(nameof(second));
            First = first;
            Second = second;
        }
    }
}
