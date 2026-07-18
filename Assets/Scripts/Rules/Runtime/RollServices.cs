using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes a homogeneous group of dice without performing a roll.
    /// </summary>
    /// <remarks>
    /// Rules calculations pass this plain value to <see cref="IRollService"/> so production,
    /// replay, and test callers can provide randomness without depending on Unity global state.
    /// </remarks>
    public readonly struct DiceExpression : IEquatable<DiceExpression>
    {
        /// <summary>
        /// Gets the number of dice rolled.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets the number of faces on each die.
        /// </summary>
        public int Sides { get; }

        /// <summary>
        /// Gets whether this is the uninitialized default expression.
        /// </summary>
        public bool IsEmpty => Count == 0 || Sides == 0;

        /// <summary>
        /// Creates a validated dice expression such as two six-sided dice.
        /// </summary>
        /// <param name="count">The positive number of dice.</param>
        /// <param name="sides">The positive number of faces on each die.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count"/> or <paramref name="sides"/> is not positive.
        /// </exception>
        public DiceExpression(int count, int sides)
        {
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "A roll requires at least one die.");
            if (sides <= 0)
                throw new ArgumentOutOfRangeException(nameof(sides), "A die requires at least one side.");

            Count = count;
            Sides = sides;
        }

        /// <inheritdoc/>
        public bool Equals(DiceExpression other) => Count == other.Count && Sides == other.Sides;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DiceExpression other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Count, Sides);

        /// <inheritdoc/>
        public override string ToString() => $"{Count}d{Sides}";

        /// <summary>
        /// Compares two dice expressions by count and die size.
        /// </summary>
        public static bool operator ==(DiceExpression left, DiceExpression right) => left.Equals(right);

        /// <summary>
        /// Compares two dice expressions by count and die size.
        /// </summary>
        public static bool operator !=(DiceExpression left, DiceExpression right) => !left.Equals(right);
    }

    /// <summary>
    /// Provides shared immutable expressions for dice that rules workflows use repeatedly.
    /// </summary>
    public static class DiceExpressions
    {
        /// <summary>One twenty-sided die used by PF2e checks and saving throws.</summary>
        public static readonly DiceExpression D20 = new DiceExpression(1, 20);
    }

    /// <summary>
    /// Contains the immutable individual values and total produced for one dice expression.
    /// </summary>
    public sealed class RollResult : IEquatable<RollResult>
    {
        private readonly IReadOnlyList<int> values;

        /// <summary>
        /// Gets the expression that was rolled.
        /// </summary>
        public DiceExpression Dice { get; }

        /// <summary>
        /// Gets each die value in roll order.
        /// </summary>
        public IReadOnlyList<int> Values => values;

        /// <summary>
        /// Gets the sum of all individual die values.
        /// </summary>
        public int Total { get; }

        /// <summary>
        /// Creates a result and validates that every supplied value belongs to the expression.
        /// </summary>
        /// <param name="dice">The expression that produced the values.</param>
        /// <param name="values">Exactly one in-range value for each die.</param>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// The value count differs from the dice count or a value is outside the die range.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The sum of the individual die values cannot be represented by an <see cref="int"/>.
        /// </exception>
        public RollResult(DiceExpression dice, IEnumerable<int> values)
        {
            if (dice.IsEmpty)
                throw new ArgumentException("A roll result requires a valid dice expression.", nameof(dice));
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            int[] copied = values.ToArray();
            if (copied.Length != dice.Count)
            {
                throw new ArgumentException(
                    $"{dice} requires {dice.Count} results, but {copied.Length} were supplied.",
                    nameof(values));
            }

            int total = 0;
            for (int index = 0; index < copied.Length; index++)
            {
                if (copied[index] < 1 || copied[index] > dice.Sides)
                {
                    throw new ArgumentException(
                        $"Roll value {copied[index]} is outside the 1-{dice.Sides} range for {dice}.",
                        nameof(values));
                }
                total = checked(total + copied[index]);
            }

            Dice = dice;
            this.values = Array.AsReadOnly(copied);
            Total = total;
        }

        /// <inheritdoc/>
        public bool Equals(RollResult other) =>
            other != null && Dice == other.Dice && values.SequenceEqual(other.values);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RollResult other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Dice, Total);
    }

    /// <summary>
    /// Supplies dice values to pure rules calculations.
    /// </summary>
    /// <remarks>
    /// Handlers obtain the callback-scoped implementation from <see cref="OpCallbackContext.Rolls"/>.
    /// Passing that same interface into attack and damage calculators keeps every random input on
    /// one injected source and lets the dispatcher record each roll against its operation frame.
    /// </remarks>
    public interface IRollService
    {
        /// <summary>
        /// Rolls the requested dice expression.
        /// </summary>
        /// <param name="dice">The validated dice to roll.</param>
        /// <returns>The individual die values and their total.</returns>
        RollResult Roll(DiceExpression dice);
    }

    /// <summary>
    /// Produces runtime dice values from an instance-owned <see cref="Random"/> source.
    /// </summary>
    /// <remarks>
    /// This production implementation never reads or changes <c>UnityEngine.Random</c>. A dispatcher
    /// owns one instance by default, while deterministic callers should inject
    /// <see cref="ScriptedRollService"/> instead.
    /// </remarks>
    public sealed class RandomRollService : IRollService
    {
        private readonly object gate = new object();
        private readonly Random random;

        /// <summary>
        /// Creates a production source with framework-provided entropy.
        /// </summary>
        public RandomRollService() => random = new Random();

        /// <summary>
        /// Creates an instance-owned seeded source for reproducible simulations.
        /// </summary>
        /// <param name="seed">The seed used by this source only.</param>
        public RandomRollService(int seed) => random = new Random(seed);

        /// <inheritdoc/>
        public RollResult Roll(DiceExpression dice)
        {
            int[] values = new int[dice.Count];
            lock (gate)
            {
                for (int index = 0; index < values.Length; index++)
                    values[index] = random.Next(dice.Sides) + 1;
            }
            return new RollResult(dice, values);
        }
    }

    /// <summary>
    /// Consumes caller-supplied die values in order for deterministic tests, replays, and simulations.
    /// </summary>
    /// <remarks>
    /// A request is atomic: exhaustion or an out-of-range upcoming value fails before any scripted
    /// value is consumed. This makes a bad fixture easy to diagnose and safe to correct and retry.
    /// </remarks>
    public sealed class ScriptedRollService : IRollService
    {
        private readonly object gate = new object();
        private readonly int[] values;
        private int nextIndex;

        /// <summary>
        /// Creates a source that returns the supplied individual die values in order.
        /// </summary>
        /// <param name="values">Individual die results, not pre-summed roll totals.</param>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
        public ScriptedRollService(params int[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            this.values = (int[])values.Clone();
        }

        /// <summary>
        /// Gets how many scripted individual die values remain unconsumed.
        /// </summary>
        public int Remaining
        {
            get
            {
                lock (gate)
                    return values.Length - nextIndex;
            }
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// The script is exhausted or an upcoming value is invalid for the requested die.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The requested values cannot be represented by the roll result's integer total.
        /// </exception>
        public RollResult Roll(DiceExpression dice)
        {
            lock (gate)
            {
                if (values.Length - nextIndex < dice.Count)
                {
                    throw new InvalidOperationException(
                        $"The scripted roll source needs {dice.Count} values for {dice}, but only " +
                        $"{values.Length - nextIndex} remain.");
                }

                for (int offset = 0; offset < dice.Count; offset++)
                {
                    int value = values[nextIndex + offset];
                    if (value < 1 || value > dice.Sides)
                    {
                        throw new InvalidOperationException(
                            $"Scripted value {value} is outside the 1-{dice.Sides} range for {dice}.");
                    }
                }

                int[] consumed = new int[dice.Count];
                Array.Copy(values, nextIndex, consumed, 0, dice.Count);
                RollResult result = new RollResult(dice, consumed);
                nextIndex += dice.Count;
                return result;
            }
        }
    }
}
