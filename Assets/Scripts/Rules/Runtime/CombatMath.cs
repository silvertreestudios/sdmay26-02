using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Resolves the signed multiple attack penalty for a normal or agile attack.
    /// </summary>
    public static class MultipleAttackPenaltyResolver
    {
        /// <summary>
        /// Gets the penalty for the next attack from the number of attacks already made this turn.
        /// </summary>
        /// <param name="attackCount">The non-negative number of prior attacks.</param>
        /// <param name="isAgile">Whether the attack uses the reduced agile penalties.</param>
        /// <returns>Zero, -5/-10 for normal attacks, or -4/-8 for agile attacks.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="attackCount"/> is negative.</exception>
        public static int Resolve(int attackCount, bool isAgile)
        {
            if (attackCount < 0)
                throw new ArgumentOutOfRangeException(nameof(attackCount));
            if (attackCount == 0)
                return 0;
            if (attackCount == 1)
                return isAgile ? -4 : -5;
            return isAgile ? -8 : -10;
        }
    }

    /// <summary>
    /// Contains one pure damage roll before weakness, resistance, or authoritative HP mutation.
    /// </summary>
    public sealed class DamageRollOutcome
    {
        /// <summary>Gets the individual dice supplied by the injected roll source.</summary>
        public RollResult DiceRoll { get; }

        /// <summary>Gets the flat amount added before critical damage is applied.</summary>
        public int FlatModifier { get; }

        /// <summary>Gets the dice total plus the flat amount before critical adjustment.</summary>
        public int BaseDamage { get; }

        /// <summary>Gets the degree used to calculate final damage.</summary>
        public DegreeOfSuccess Degree { get; }

        /// <summary>Gets base damage doubled on a critical success, otherwise unchanged.</summary>
        public int TotalDamage { get; }

        private DamageRollOutcome(RollResult roll, int flatModifier, DegreeOfSuccess degree)
        {
            DiceRoll = roll ?? throw new ArgumentNullException(nameof(roll));
            FlatModifier = flatModifier;
            BaseDamage = checked(roll.Total + flatModifier);
            Degree = degree;
            TotalDamage =
                degree == DegreeOfSuccess.CriticalSuccess ? checked(BaseDamage * 2) : BaseDamage;
        }

        /// <summary>
        /// Rolls one damage component, adds flat damage, and doubles the combined value on a critical success.
        /// </summary>
        /// <param name="dice">The damage dice expression.</param>
        /// <param name="flatModifier">The flat damage added to the dice total.</param>
        /// <param name="degree">The attack or check degree controlling critical damage.</param>
        /// <param name="rolls">
        /// The required roll source, normally <see cref="OpCallbackContext.Rolls"/> so the result is traced.
        /// </param>
        /// <returns>A pure damage calculation with no state or presentation side effects.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rolls"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="degree"/> is undefined.</exception>
        /// <exception cref="OverflowException">
        /// The dice and flat modifier, or critical doubling, cannot be represented by an <see cref="int"/>.
        /// </exception>
        public static DamageRollOutcome Roll(
            DiceExpression dice,
            int flatModifier,
            DegreeOfSuccess degree,
            IRollService rolls
        )
        {
            if (!Enum.IsDefined(typeof(DegreeOfSuccess), degree))
                throw new ArgumentOutOfRangeException(nameof(degree));
            if (rolls == null)
                throw new ArgumentNullException(nameof(rolls));
            return new DamageRollOutcome(rolls.Roll(dice), flatModifier, degree);
        }
    }
}
