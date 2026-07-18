using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies the four ordered PF2e check outcomes.
    /// </summary>
    public enum DegreeOfSuccess
    {
        /// <summary>The total fails the difficulty class by at least 10.</summary>
        CriticalFailure,
        /// <summary>The total is below the difficulty class.</summary>
        Failure,
        /// <summary>The total meets or exceeds the difficulty class.</summary>
        Success,
        /// <summary>The total exceeds the difficulty class by at least 10.</summary>
        CriticalSuccess
    }

    /// <summary>
    /// Resolves PF2e degree-of-success thresholds and natural d20 adjustments.
    /// </summary>
    public static class DegreeOfSuccessResolver
    {
        /// <summary>
        /// Resolves the total against a difficulty class, then adjusts one degree for a natural 20 or 1.
        /// </summary>
        /// <param name="naturalRoll">The unmodified d20 value from 1 through 20.</param>
        /// <param name="total">The d20 value plus every applied modifier.</param>
        /// <param name="difficultyClass">The positive target difficulty class.</param>
        /// <returns>The final ordered degree of success.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="naturalRoll"/> is outside 1 through 20 or
        /// <paramref name="difficultyClass"/> is not positive.
        /// </exception>
        public static DegreeOfSuccess Resolve(
            int naturalRoll,
            int total,
            int difficultyClass)
        {
            if (naturalRoll < 1 || naturalRoll > 20)
                throw new ArgumentOutOfRangeException(nameof(naturalRoll));
            if (difficultyClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(difficultyClass));

            long margin = (long)total - difficultyClass;
            DegreeOfSuccess degree;
            if (margin >= 10)
                degree = DegreeOfSuccess.CriticalSuccess;
            else if (margin >= 0)
                degree = DegreeOfSuccess.Success;
            else if (margin <= -10)
                degree = DegreeOfSuccess.CriticalFailure;
            else
                degree = DegreeOfSuccess.Failure;

            if (naturalRoll == 20 && degree < DegreeOfSuccess.CriticalSuccess)
                degree++;
            else if (naturalRoll == 1 && degree > DegreeOfSuccess.CriticalFailure)
                degree--;
            return degree;
        }
    }

    /// <summary>
    /// Identifies the trusted ancestor operation whose workflow requested a check.
    /// </summary>
    /// <remarks>
    /// Check operations are nested-only. Their handlers verify that this ID belongs to the
    /// current frame's parent chain, preventing callers from attaching unrelated provenance.
    /// </remarks>
    public readonly struct CheckSource : IEquatable<CheckSource>
    {
        /// <summary>
        /// Gets the ancestor operation responsible for requesting the check.
        /// </summary>
        public OpId OperationId { get; }

        /// <summary>
        /// Gets whether this is the uninitialized default value.
        /// </summary>
        public bool IsEmpty => OperationId.IsEmpty;

        private CheckSource(OpId operationId)
        {
            if (operationId.IsEmpty)
                throw new ArgumentException("A check source requires an operation ID.", nameof(operationId));
            OperationId = operationId;
        }

        /// <summary>
        /// Creates trusted check provenance from the current or another ancestor frame.
        /// </summary>
        /// <param name="operationId">The operation that requested the check.</param>
        /// <returns>A check source that handlers validate against the live trace.</returns>
        public static CheckSource From(OpId operationId) => new CheckSource(operationId);

        /// <inheritdoc/>
        public bool Equals(CheckSource other) => OperationId == other.OperationId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CheckSource other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => OperationId.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => OperationId.ToString();

        /// <summary>Compares the responsible operation identity.</summary>
        public static bool operator ==(CheckSource left, CheckSource right) => left.Equals(right);

        /// <summary>Compares the responsible operation identity.</summary>
        public static bool operator !=(CheckSource left, CheckSource right) => !left.Equals(right);
    }

    /// <summary>
    /// Contains one legally resolved check, including its roll, modifiers, source, and degree.
    /// </summary>
    /// <remarks>
    /// A failed check is still a resolved operation and therefore produces this same outcome type.
    /// Callers branch on <see cref="Degree"/> rather than treating ordinary failure as invalid work.
    /// </remarks>
    public sealed class CheckOutcome
    {
        /// <summary>Gets the creature that attempted the check.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the ancestor operation that requested the check.</summary>
        public CheckSource Source { get; }

        /// <summary>Gets the recorded d20 result.</summary>
        public RollResult Roll { get; }

        /// <summary>Gets applied, suppressed, and candidate modifier details.</summary>
        public ModifierCollection Modifiers { get; }

        /// <summary>Gets the target difficulty class.</summary>
        public int DifficultyClass { get; }

        /// <summary>Gets the natural d20 plus the applied modifier total.</summary>
        public int Total { get; }

        /// <summary>Gets the final degree after natural 20 or 1 adjustment.</summary>
        public DegreeOfSuccess Degree { get; }

        internal CheckOutcome(
            CreatureId actor,
            CheckSource source,
            RollResult roll,
            ModifierCollection modifiers,
            int difficultyClass)
        {
            Actor = actor;
            Source = source;
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            Modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            if (roll.Dice != DiceExpressions.D20)
                throw new ArgumentException("A check outcome requires exactly one d20.", nameof(roll));
            DifficultyClass = difficultyClass;
            Total = checked(roll.Total + modifiers.Total);
            Degree = DegreeOfSuccessResolver.Resolve(roll.Values[0], Total, difficultyClass);
        }
    }

    /// <summary>
    /// Requests a deterministic d20 check for one typed skill against a fixed difficulty class.
    /// </summary>
    public sealed class SkillCheckOp : IRuleOp<CheckOutcome>
    {
        /// <summary>Gets the creature attempting the check.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the skill whose current modifier is resolved.</summary>
        public Skill Skill { get; }

        /// <summary>Gets the target difficulty class.</summary>
        public int DifficultyClass { get; }

        /// <summary>Gets the trusted ancestor that requested this check.</summary>
        public CheckSource Source { get; }

        /// <summary>
        /// Creates a nested skill-check request containing IDs and plain rules data only.
        /// </summary>
        /// <param name="actor">The creature attempting the check.</param>
        /// <param name="skill">The typed skill to resolve.</param>
        /// <param name="difficultyClass">The positive target DC.</param>
        /// <param name="source">The ancestor operation responsible for the check.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="actor"/>, <paramref name="skill"/>, or <paramref name="source"/> is empty.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="difficultyClass"/> is not positive.
        /// </exception>
        public SkillCheckOp(
            CreatureId actor,
            Skill skill,
            int difficultyClass,
            CheckSource source)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A skill check requires an actor.", nameof(actor));
            if (skill.IsEmpty)
                throw new ArgumentException("A skill check requires a skill.", nameof(skill));
            if (difficultyClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(difficultyClass));
            if (source.IsEmpty)
                throw new ArgumentException("A skill check requires trusted source provenance.", nameof(source));

            Actor = actor;
            Skill = skill;
            DifficultyClass = difficultyClass;
            Source = source;
        }
    }

    /// <summary>
    /// Requests a deterministic Fortitude, Reflex, or Will save against a fixed difficulty class.
    /// </summary>
    public sealed class SavingThrowOp : IRuleOp<CheckOutcome>
    {
        /// <summary>Gets the creature attempting the save.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the saving throw to resolve.</summary>
        public SaveKind Save { get; }

        /// <summary>Gets the target difficulty class.</summary>
        public int DifficultyClass { get; }

        /// <summary>Gets the trusted ancestor that requested this save.</summary>
        public CheckSource Source { get; }

        /// <summary>
        /// Creates a nested saving-throw request containing IDs and plain rules data only.
        /// </summary>
        /// <param name="actor">The creature attempting the save.</param>
        /// <param name="save">The saving throw identity.</param>
        /// <param name="difficultyClass">The positive target DC.</param>
        /// <param name="source">The ancestor operation responsible for the save.</param>
        public SavingThrowOp(
            CreatureId actor,
            SaveKind save,
            int difficultyClass,
            CheckSource source)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A saving throw requires an actor.", nameof(actor));
            if (!Enum.IsDefined(typeof(SaveKind), save))
                throw new ArgumentOutOfRangeException(nameof(save));
            if (difficultyClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(difficultyClass));
            if (source.IsEmpty)
                throw new ArgumentException("A saving throw requires trusted source provenance.", nameof(source));

            Actor = actor;
            Save = save;
            DifficultyClass = difficultyClass;
            Source = source;
        }
    }

}
