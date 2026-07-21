using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    internal static class HealthOperationValidation
    {
        // Every stable-ID string constructor rejects blank input. These guards still protect the
        // operation boundary because C# can create default value-type IDs without invoking those
        // constructors (for example, default(CreatureId)).
        public static CreatureId RequireCreature(CreatureId value)
        {
            if (value.IsEmpty)
                throw new ArgumentException("A target creature ID is required.", nameof(value));
            return value;
        }

        public static HealthChangeOriginId RequireOrigin(HealthChangeOriginId value)
        {
            if (value.IsEmpty)
                throw new ArgumentException(
                    "A health-change origin ID is required.",
                    nameof(value)
                );
            return value;
        }

        public static RuleSource RequireSource(RuleSource value)
        {
            if (value.IsEmpty)
                throw new ArgumentException("A health rule source is required.", nameof(value));
            return value;
        }

        public static int RequireAmount(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    /// <summary>Identifies whether one entry in a health batch deals damage or restores health.</summary>
    public enum HealthBatchChangeKind
    {
        /// <summary>Applies an already-final damage amount.</summary>
        Damage,

        /// <summary>Applies healing subject to maximum-HP clamping.</summary>
        Healing,
    }

    /// <summary>
    /// Describes one ordered health change that belongs to a larger completed effect.
    /// </summary>
    /// <remarks>
    /// Higher-level actions should finish targeting, rolls, and damage calculation before creating
    /// these entries. The batch handler preserves entry order while placing every resulting health
    /// Fact under one causal root, so outcome listeners observe the complete multi-target effect.
    /// </remarks>
    public sealed class HealthBatchChange
    {
        /// <summary>Gets whether this entry deals damage or restores health.</summary>
        public HealthBatchChangeKind Kind { get; }

        /// <summary>Gets the creature whose authoritative health changes.</summary>
        public CreatureId Target { get; }

        /// <summary>Gets the non-negative already-final damage or requested healing amount.</summary>
        public int Amount { get; }

        /// <summary>Gets the stable identity of this individual health change.</summary>
        public HealthChangeOriginId Origin { get; }

        /// <summary>Gets the rules source responsible for this individual health change.</summary>
        public RuleSource Source { get; }

        /// <summary>Initializes one validated entry in a multi-target health effect.</summary>
        /// <param name="kind">Whether the amount is damage or healing.</param>
        /// <param name="target">The creature whose authoritative health changes.</param>
        /// <param name="amount">The non-negative already-final damage or requested healing.</param>
        /// <param name="origin">The stable identity of this individual health change.</param>
        /// <param name="source">The rules source responsible for this change.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="kind"/> is undefined or <paramref name="amount"/> is negative.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="target"/>, <paramref name="origin"/>, or <paramref name="source"/> is empty.
        /// </exception>
        public HealthBatchChange(
            HealthBatchChangeKind kind,
            CreatureId target,
            int amount,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            if (kind != HealthBatchChangeKind.Damage && kind != HealthBatchChangeKind.Healing)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            Target = HealthOperationValidation.RequireCreature(target);
            Amount = HealthOperationValidation.RequireAmount(amount, nameof(amount));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Reports the amount applied by one ordered health-batch entry.</summary>
    public sealed class HealthBatchChangeOutcome
    {
        /// <summary>Gets the request whose result this value describes.</summary>
        public HealthBatchChange Change { get; }

        /// <summary>
        /// Gets damage absorbed by temporary/current HP or healing restored after clamping.
        /// </summary>
        public int Applied { get; }

        internal HealthBatchChangeOutcome(HealthBatchChange change, int applied)
        {
            Change = change ?? throw new ArgumentNullException(nameof(change));
            Applied = applied;
        }
    }

    /// <summary>Reports every health result in the same deterministic order as its batch request.</summary>
    public sealed class HealthBatchOutcome
    {
        /// <summary>Gets the immutable ordered results for the completed effect.</summary>
        public IReadOnlyList<HealthBatchChangeOutcome> Changes { get; }

        internal HealthBatchOutcome(IEnumerable<HealthBatchChangeOutcome> changes) =>
            Changes = Array.AsReadOnly(changes.ToArray());
    }

    /// <summary>
    /// Applies a completed multi-target health effect under one dispatcher causal root.
    /// </summary>
    /// <remarks>
    /// Each entry still uses the normal damage or healing workflow, including its middleware and
    /// Facts. Binding-scoped Fact listeners run only after every entry has committed, preventing
    /// encounter outcome from depending on target iteration order.
    /// </remarks>
    public sealed class ApplyHealthBatchOp : IRuleOp<HealthBatchOutcome>
    {
        /// <summary>Gets the immutable health changes in action-defined processing order.</summary>
        public IReadOnlyList<HealthBatchChange> Changes { get; }

        /// <summary>Initializes one non-empty completed health-effect batch.</summary>
        /// <param name="changes">The validated health changes in deterministic processing order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The sequence is empty or contains a null entry.
        /// </exception>
        public ApplyHealthBatchOp(IEnumerable<HealthBatchChange> changes)
        {
            HealthBatchChange[] copied =
                changes?.ToArray() ?? throw new ArgumentNullException(nameof(changes));
            if (copied.Length == 0 || copied.Any(change => change == null))
                throw new ArgumentException(
                    "A health batch requires at least one non-null change.",
                    nameof(changes)
                );
            Changes = Array.AsReadOnly(copied);
        }
    }

    /// <summary>
    /// Starts the externally dispatchable damage workflow for an already-final amount.
    /// </summary>
    /// <remarks>
    /// This operation is a narrow direct-damage seam, not the usual entry point for completed
    /// rules-engine actions. It also bridges Unity-driven features that already calculate final
    /// damage while those workflows are migrated. Higher-level action operations for Strikes,
    /// spells, and similar features should own their complete workflow and apply damage from
    /// within it, so direct dispatch of this operation should become rare.
    ///
    /// Handlers and middleware receive this public request before its handler dispatches the
    /// nested-only <see cref="CommitDamageOp"/>. Keeping the state-writing reducer behind that
    /// nested operation prevents callers from bypassing workflow authorization and provenance.
    /// </remarks>
    public sealed class ApplyDamageOp : IRuleOp<DamageOutcome>
    {
        public CreatureId Target { get; }
        public int FinalDamage { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes one externally dispatchable final-damage request.</summary>
        /// <param name="target">The creature whose health is authoritative.</param>
        /// <param name="finalDamage">Damage after critical, weakness, and resistance calculation.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rules source used for dispatcher provenance.</param>
        public ApplyDamageOp(
            CreatureId target,
            int finalDamage,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            FinalDamage = HealthOperationValidation.RequireAmount(finalDamage, nameof(finalDamage));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>
    /// Performs the sole authoritative health-state mutation for an accepted
    /// <see cref="ApplyDamageOp"/>.
    /// </summary>
    /// <remarks>
    /// Only <c>ApplyDamageHandler</c> dispatches this operation as a child frame. It is registered
    /// as a reducer, not a root handler, so external callers use <see cref="ApplyDamageOp"/> while
    /// this type preserves atomic state and Fact commitment at the trusted nested boundary.
    /// </remarks>
    public sealed class CommitDamageOp : IRuleOp<DamageOutcome>, IRuleSourcedOp
    {
        public CreatureId Target { get; }
        public int FinalDamage { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes the nested reducer request for one final damage commit.</summary>
        /// <param name="target">The creature whose health is authoritative.</param>
        /// <param name="finalDamage">Damage after all upstream calculation.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rules source stamped onto committed Facts.</param>
        public CommitDamageOp(
            CreatureId target,
            int finalDamage,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            FinalDamage = HealthOperationValidation.RequireAmount(finalDamage, nameof(finalDamage));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Requests healing through the authoritative health reducer.</summary>
    public sealed class ApplyHealingOp : IRuleOp<HealingOutcome>
    {
        public CreatureId Target { get; }
        public int Healing { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes one externally dispatchable healing request.</summary>
        /// <param name="target">The creature to heal.</param>
        /// <param name="healing">The non-negative healing offered.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rules source used for dispatcher provenance.</param>
        public ApplyHealingOp(
            CreatureId target,
            int healing,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Healing = HealthOperationValidation.RequireAmount(healing, nameof(healing));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Nested-only reducer operation for one healing commit.</summary>
    public sealed class CommitHealingOp : IRuleOp<HealingOutcome>, IRuleSourcedOp
    {
        public CreatureId Target { get; }
        public int Healing { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes the nested reducer request for one healing commit.</summary>
        /// <param name="target">The creature to heal.</param>
        /// <param name="healing">The non-negative healing offered.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rules source stamped onto committed Facts.</param>
        public CommitHealingOp(
            CreatureId target,
            int healing,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Healing = HealthOperationValidation.RequireAmount(healing, nameof(healing));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>
    /// Requests durable defeat commitment after a zero-HP creature's reactions have settled.
    /// </summary>
    /// <remarks>
    /// Unity presentation dispatches this operation immediately before irreversible defeat work.
    /// Damage does not commit defeat itself because Reaction listeners must retain their causal
    /// opportunity to heal the creature first.
    /// </remarks>
    public sealed class FinalizeCreatureDefeatOp : IRuleOp<bool>
    {
        /// <summary>Gets the zero-HP creature whose defeat should become irreversible.</summary>
        public CreatureId Target { get; }

        /// <summary>Initializes a settled zero-HP defeat request.</summary>
        /// <param name="target">The authoritative creature identity to finalize.</param>
        public FinalizeCreatureDefeatOp(CreatureId target) =>
            Target = HealthOperationValidation.RequireCreature(target);
    }

    internal sealed class CommitCreatureDefeatOp : IRuleOp<bool>
    {
        internal CreatureId Target { get; }

        internal CommitCreatureDefeatOp(CreatureId target) =>
            Target = HealthOperationValidation.RequireCreature(target);
    }

    /// <summary>Requests a non-stacking temporary Hit Point grant owned by one source.</summary>
    public sealed class GrantTemporaryHitPointsOp : IRuleOp<TemporaryHitPointsGrantOutcome>
    {
        public CreatureId Target { get; }
        public int Amount { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes one externally dispatchable temporary-HP offer.</summary>
        /// <param name="target">The creature receiving the offer.</param>
        /// <param name="amount">The non-negative pool offered.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rule source that will own an accepted pool.</param>
        public GrantTemporaryHitPointsOp(
            CreatureId target,
            int amount,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Amount = HealthOperationValidation.RequireAmount(amount, nameof(amount));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Nested-only reducer operation for a source-owned temporary Hit Point grant.</summary>
    public sealed class CommitTemporaryHitPointsGrantOp
        : IRuleOp<TemporaryHitPointsGrantOutcome>,
            IRuleSourcedOp
    {
        public CreatureId Target { get; }
        public int Amount { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes the nested reducer request for a temporary-HP offer.</summary>
        /// <param name="target">The creature receiving the offer.</param>
        /// <param name="amount">The non-negative pool offered.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The rule source that will own an accepted pool.</param>
        public CommitTemporaryHitPointsGrantOp(
            CreatureId target,
            int amount,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Amount = HealthOperationValidation.RequireAmount(amount, nameof(amount));
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Requests removal of temporary Hit Points still owned by one source.</summary>
    public sealed class RemoveTemporaryHitPointsOp : IRuleOp<TemporaryHitPointsRemovalOutcome>
    {
        public CreatureId Target { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes a request to remove a source's remaining temporary HP.</summary>
        /// <param name="target">The creature whose pool may be removed.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The source that must own the active pool.</param>
        public RemoveTemporaryHitPointsOp(
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Nested-only reducer operation for source-owned temporary Hit Point removal.</summary>
    public sealed class CommitTemporaryHitPointsRemovalOp
        : IRuleOp<TemporaryHitPointsRemovalOutcome>,
            IRuleSourcedOp
    {
        public CreatureId Target { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes the nested reducer request for source-owned pool removal.</summary>
        /// <param name="target">The creature whose pool may be removed.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The source that must own the active pool.</param>
        public CommitTemporaryHitPointsRemovalOp(
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Requests immunity to future temporary Hit Point grants from one source.</summary>
    public sealed class AddTemporaryHitPointImmunityOp : IRuleOp<TemporaryHitPointImmunityOutcome>
    {
        public CreatureId Target { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes a request to block one source's future temporary-HP grants.</summary>
        /// <param name="target">The creature receiving the immunity.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The source whose grants will be blocked.</param>
        public AddTemporaryHitPointImmunityOp(
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }

    /// <summary>Nested-only reducer operation for temporary Hit Point immunity.</summary>
    public sealed class CommitTemporaryHitPointImmunityOp
        : IRuleOp<TemporaryHitPointImmunityOutcome>,
            IRuleSourcedOp
    {
        public CreatureId Target { get; }
        public HealthChangeOriginId Origin { get; }
        public RuleSource Source { get; }

        /// <summary>Initializes the nested reducer request for source-specific immunity.</summary>
        /// <param name="target">The creature receiving the immunity.</param>
        /// <param name="origin">The encounter-stable cause identifier.</param>
        /// <param name="source">The source whose grants will be blocked.</param>
        public CommitTemporaryHitPointImmunityOp(
            CreatureId target,
            HealthChangeOriginId origin,
            RuleSource source
        )
        {
            Target = HealthOperationValidation.RequireCreature(target);
            Origin = HealthOperationValidation.RequireOrigin(origin);
            Source = HealthOperationValidation.RequireSource(source);
        }
    }
}
