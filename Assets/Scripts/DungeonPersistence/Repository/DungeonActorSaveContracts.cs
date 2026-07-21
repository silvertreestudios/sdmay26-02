using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Represents an integer grid coordinate without depending on a live Unity object.</summary>
    public readonly struct DungeonSaveCell : IEquatable<DungeonSaveCell>
    {
        /// <summary>Creates a saved horizontal grid coordinate.</summary>
        /// <param name="x">The horizontal X coordinate.</param>
        /// <param name="z">The horizontal Z coordinate.</param>
        public DungeonSaveCell(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>Gets the horizontal X coordinate.</summary>
        public int X { get; }

        /// <summary>Gets the horizontal Z coordinate.</summary>
        public int Z { get; }

        /// <inheritdoc/>
        public bool Equals(DungeonSaveCell other) => X == other.X && Z == other.Z;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DungeonSaveCell other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked((X * 397) ^ Z);

        /// <summary>Tests two saved cells for value equality.</summary>
        public static bool operator ==(DungeonSaveCell left, DungeonSaveCell right) =>
            left.Equals(right);

        /// <summary>Tests two saved cells for value inequality.</summary>
        public static bool operator !=(DungeonSaveCell left, DungeonSaveCell right) =>
            !left.Equals(right);
    }

    /// <summary>Records one source-aware condition application.</summary>
    public sealed class DungeonConditionSaveState
    {
        /// <summary>Creates a condition application that can coexist with other sources.</summary>
        /// <param name="applicationId">The stable identity of this distinct application.</param>
        /// <param name="conditionId">The stable condition identifier.</param>
        /// <param name="sourceInstanceId">The stable source-instance identifier, or an empty string for an intrinsic condition.</param>
        /// <param name="value">The condition value when the condition is valued, otherwise zero.</param>
        public DungeonConditionSaveState(
            string applicationId,
            string conditionId,
            string sourceInstanceId,
            int value
        )
        {
            ApplicationId = DungeonSaveContractGuard.RequiredId(
                applicationId,
                nameof(applicationId)
            );
            ConditionId = DungeonSaveContractGuard.RequiredId(conditionId, nameof(conditionId));
            SourceInstanceId = DungeonSaveContractGuard.Normalized(sourceInstanceId);
            if (value < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Condition values cannot be negative."
                );
            Value = value;
        }

        /// <summary>Gets the stable identity of this distinct application.</summary>
        public string ApplicationId { get; }

        /// <summary>Gets the stable condition identifier.</summary>
        public string ConditionId { get; }

        /// <summary>Gets the source-instance identifier, or an empty string for an intrinsic condition.</summary>
        public string SourceInstanceId { get; }

        /// <summary>Gets the nonnegative valued-condition amount.</summary>
        public int Value { get; }
    }

    /// <summary>
    /// Records an active, non-consumed timed effect and its binding. Consumed effects are
    /// canonically omitted because they no longer contribute restorable state. The payload is owned by the
    /// registered discriminator codec; persistence deliberately does not serialize Unity objects.
    /// </summary>
    public sealed class DungeonTimedEffectSaveState
    {
        /// <summary>Creates a durable timed-effect binding record.</summary>
        /// <param name="instanceId">The stable effect instance identifier.</param>
        /// <param name="kind">The registered effect-kind identifier.</param>
        /// <param name="stateDiscriminator">The registered codec discriminator for <paramref name="stateJson"/>.</param>
        /// <param name="sourceCreatureId">The stable source creature identifier, or an empty string.</param>
        /// <param name="ownerCreatureId">The stable binding owner creature identifier.</param>
        /// <param name="targetCreatureId">The stable effect target creature identifier.</param>
        /// <param name="bindingCreationOrder">The nonnegative order used to rebuild deterministic bindings.</param>
        /// <param name="remainingTargetTurnStarts">The remaining target-turn-start expirations.</param>
        /// <param name="stateJson">Effect-kind state as a JSON value, or an empty string when none is required.</param>
        public DungeonTimedEffectSaveState(
            string instanceId,
            string kind,
            string stateDiscriminator,
            string sourceCreatureId,
            string ownerCreatureId,
            string targetCreatureId,
            long bindingCreationOrder,
            int remainingTargetTurnStarts,
            string stateJson
        )
        {
            InstanceId = DungeonSaveContractGuard.RequiredId(instanceId, nameof(instanceId));
            Kind = DungeonSaveContractGuard.RequiredId(kind, nameof(kind));
            StateDiscriminator = DungeonSaveContractGuard.RequiredId(
                stateDiscriminator,
                nameof(stateDiscriminator)
            );
            SourceCreatureId = DungeonSaveContractGuard.Normalized(sourceCreatureId);
            OwnerCreatureId = DungeonSaveContractGuard.RequiredId(
                ownerCreatureId,
                nameof(ownerCreatureId)
            );
            TargetCreatureId = DungeonSaveContractGuard.RequiredId(
                targetCreatureId,
                nameof(targetCreatureId)
            );
            if (bindingCreationOrder < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(bindingCreationOrder),
                    "Binding order cannot be negative."
                );
            if (remainingTargetTurnStarts < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(remainingTargetTurnStarts),
                    "Remaining duration cannot be negative."
                );
            BindingCreationOrder = bindingCreationOrder;
            RemainingTargetTurnStarts = remainingTargetTurnStarts;
            StateJson = DungeonSaveContractGuard.CanonicalJson(stateJson, nameof(stateJson));
        }

        /// <summary>Gets the stable effect instance identifier.</summary>
        public string InstanceId { get; }

        /// <summary>Gets the registered effect-kind identifier.</summary>
        public string Kind { get; }

        /// <summary>Gets the registered state codec discriminator.</summary>
        public string StateDiscriminator { get; }

        /// <summary>Gets the stable source creature identifier, or an empty string.</summary>
        public string SourceCreatureId { get; }

        /// <summary>Gets the stable binding owner creature identifier.</summary>
        public string OwnerCreatureId { get; }

        /// <summary>Gets the stable effect target creature identifier.</summary>
        public string TargetCreatureId { get; }

        /// <summary>Gets the deterministic binding creation order.</summary>
        public long BindingCreationOrder { get; }

        /// <summary>Gets the remaining target-turn-start expirations.</summary>
        public int RemainingTargetTurnStarts { get; }

        /// <summary>Gets canonical effect-kind JSON, or an empty string.</summary>
        public string StateJson { get; }
    }

    /// <summary>
    /// Records authoritative health, including temporary-HP ownership and immunities required to
    /// restore later rule behavior exactly.
    /// </summary>
    public sealed class DungeonHealthSaveState
    {
        /// <summary>Creates a complete health record.</summary>
        /// <param name="currentHitPoints">Current nonnegative Hit Points.</param>
        /// <param name="maximumHitPoints">Maximum positive Hit Points.</param>
        /// <param name="temporaryHitPoints">Current nonnegative temporary Hit Points.</param>
        /// <param name="temporaryHitPointSourceId">The owning rule source, or an empty string when temporary HP is zero.</param>
        /// <param name="temporaryHitPointImmunitySourceIds">Unique rule sources currently immune to granting temporary HP.</param>
        public DungeonHealthSaveState(
            int currentHitPoints,
            int maximumHitPoints,
            int temporaryHitPoints,
            string temporaryHitPointSourceId,
            IEnumerable<string> temporaryHitPointImmunitySourceIds
        )
        {
            if (maximumHitPoints <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumHitPoints),
                    "Maximum HP must be positive."
                );
            if (currentHitPoints < 0 || currentHitPoints > maximumHitPoints)
                throw new ArgumentOutOfRangeException(
                    nameof(currentHitPoints),
                    "Current HP must be within zero and maximum HP."
                );
            if (temporaryHitPoints < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(temporaryHitPoints),
                    "Temporary HP cannot be negative."
                );
            CurrentHitPoints = currentHitPoints;
            MaximumHitPoints = maximumHitPoints;
            TemporaryHitPoints = temporaryHitPoints;
            TemporaryHitPointSourceId = DungeonSaveContractGuard.Normalized(
                temporaryHitPointSourceId
            );
            if (temporaryHitPoints == 0 && TemporaryHitPointSourceId.Length > 0)
            {
                throw new ArgumentException(
                    "Temporary HP cannot retain an owning source when its value is zero.",
                    nameof(temporaryHitPointSourceId)
                );
            }
            TemporaryHitPointImmunitySourceIds = DungeonSaveContractGuard.UniqueStrings(
                temporaryHitPointImmunitySourceIds,
                nameof(temporaryHitPointImmunitySourceIds)
            );
        }

        /// <summary>Gets current Hit Points.</summary>
        public int CurrentHitPoints { get; }

        /// <summary>Gets maximum Hit Points.</summary>
        public int MaximumHitPoints { get; }

        /// <summary>Gets current temporary Hit Points.</summary>
        public int TemporaryHitPoints { get; }

        /// <summary>Gets the temporary-HP owner source, or an empty string.</summary>
        public string TemporaryHitPointSourceId { get; }

        /// <summary>Gets temporary-HP immunity sources in ordinal order.</summary>
        public IReadOnlyList<string> TemporaryHitPointImmunitySourceIds { get; }
    }
}
