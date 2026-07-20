using System;

namespace Game.Rules.Runtime
{
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
        /// <exception cref="ArgumentNullException">Either completed result is <see langword="null"/>.</exception>
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
