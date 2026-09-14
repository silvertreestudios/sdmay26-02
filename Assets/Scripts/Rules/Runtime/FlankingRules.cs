using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Describes one roster member's feature-local ability to help flank the selected target.
    /// </summary>
    /// <remarks>
    /// The Unity adapter captures action availability, team relationships, melee reach, and line
    /// of effect. Authoritative identity, health, and positions remain in the supplied
    /// <see cref="RulesSnapshot"/> and are never copied into this value.
    /// </remarks>
    public readonly struct FlankingParticipant : IEquatable<FlankingParticipant>
    {
        /// <summary>Creates one immutable participant capture for a specific target.</summary>
        /// <param name="creature">The roster member represented by this capture.</param>
        /// <param name="canFlank">Whether its Unity action boundary can currently threaten.</param>
        /// <param name="isFriendlyToAttacker">Whether it can cooperate with the attacker.</param>
        /// <param name="isFriendlyToTarget">Whether it treats the target as friendly.</param>
        /// <param name="threatensTarget">
        /// Whether its best melee reach has line of effect to the target in the captured topology.
        /// </param>
        public FlankingParticipant(
            CreatureId creature,
            bool canFlank,
            bool isFriendlyToAttacker,
            bool isFriendlyToTarget,
            bool threatensTarget
        )
        {
            if (creature.IsEmpty)
                throw new ArgumentException(
                    "A flanking participant is required.",
                    nameof(creature)
                );

            Creature = creature;
            CanFlank = canFlank;
            IsFriendlyToAttacker = isFriendlyToAttacker;
            IsFriendlyToTarget = isFriendlyToTarget;
            ThreatensTarget = threatensTarget;
        }

        /// <summary>Gets the roster member represented by this capture.</summary>
        public CreatureId Creature { get; }

        /// <summary>Gets whether the participant's Unity action boundary can currently threaten.</summary>
        public bool CanFlank { get; }

        /// <summary>Gets whether the participant can cooperate with the attacker.</summary>
        public bool IsFriendlyToAttacker { get; }

        /// <summary>Gets whether the participant treats the selected target as friendly.</summary>
        public bool IsFriendlyToTarget { get; }

        /// <summary>Gets whether captured reach and topology let this participant threaten the target.</summary>
        public bool ThreatensTarget { get; }

        /// <inheritdoc/>
        public bool Equals(FlankingParticipant other) =>
            Creature == other.Creature
            && CanFlank == other.CanFlank
            && IsFriendlyToAttacker == other.IsFriendlyToAttacker
            && IsFriendlyToTarget == other.IsFriendlyToTarget
            && ThreatensTarget == other.ThreatensTarget;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is FlankingParticipant other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(
                Creature,
                CanFlank,
                IsFriendlyToAttacker,
                IsFriendlyToTarget,
                ThreatensTarget
            );
    }

    /// <summary>
    /// Stores immutable feature inputs for one attacker-target flanking query.
    /// </summary>
    public sealed class FlankingContext
    {
        private readonly IReadOnlyList<FlankingParticipant> participants;

        /// <summary>Creates a target-specific context captured at the Unity boundary.</summary>
        /// <param name="attackerCanFlank">Whether the attacker can currently threaten.</param>
        /// <param name="targetIsAvailable">Whether the selected target is active in the host.</param>
        /// <param name="attackerThreatensTarget">
        /// Whether the selected melee reach has line of effect to the target.
        /// </param>
        /// <param name="participants">Roster members captured in deterministic identity order.</param>
        public FlankingContext(
            bool attackerCanFlank,
            bool targetIsAvailable,
            bool attackerThreatensTarget,
            IEnumerable<FlankingParticipant> participants
        )
        {
            if (participants == null)
                throw new ArgumentNullException(nameof(participants));

            FlankingParticipant[] copied = participants
                .OrderBy(participant => participant.Creature.Value, StringComparer.Ordinal)
                .ToArray();
            if (
                copied.Select(participant => participant.Creature).Distinct().Count()
                != copied.Length
            )
                throw new ArgumentException(
                    "A flanking context cannot contain duplicate participants.",
                    nameof(participants)
                );

            AttackerCanFlank = attackerCanFlank;
            TargetIsAvailable = targetIsAvailable;
            AttackerThreatensTarget = attackerThreatensTarget;
            this.participants = new ReadOnlyCollection<FlankingParticipant>(copied);
        }

        /// <summary>Gets whether the attacker can currently threaten.</summary>
        public bool AttackerCanFlank { get; }

        /// <summary>Gets whether the target remains available in the host.</summary>
        public bool TargetIsAvailable { get; }

        /// <summary>Gets whether the attacker's selected reach and topology threaten the target.</summary>
        public bool AttackerThreatensTarget { get; }

        /// <summary>Gets the deterministic target-specific participant captures.</summary>
        public IReadOnlyList<FlankingParticipant> Participants => participants;
    }

    /// <summary>Resolves the named Flanking rule from authoritative roster state and captured topology.</summary>
    public static class FlankingRules
    {
        /// <summary>Determines whether a melee attacker and one ally flank the selected target.</summary>
        /// <param name="snapshot">The exact authoritative snapshot for the Strike validation.</param>
        /// <param name="attacker">The creature making the melee Strike.</param>
        /// <param name="target">The creature that may become Off-Guard to that Strike.</param>
        /// <param name="context">Feature-owned immutable host and topology inputs.</param>
        /// <returns>
        /// <see langword="true"/> when the living attacker and a living, cooperating ally both
        /// threaten the target from opposite sides or corners; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="context"/> is null.</exception>
        public static bool IsFlanking(
            RulesSnapshot snapshot,
            CreatureId attacker,
            CreatureId target,
            FlankingContext context
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (
                attacker.IsEmpty
                || target.IsEmpty
                || attacker == target
                || !context.AttackerCanFlank
                || !context.TargetIsAvailable
                || !context.AttackerThreatensTarget
                || !HasLivingPosition(snapshot, attacker, out GridPosition attackerPosition)
                || !snapshot.Creatures.Contains(target)
                || !snapshot.Positions.TryGet(target, out GridPosition targetPosition)
            )
                return false;

            foreach (FlankingParticipant participant in context.Participants)
            {
                if (
                    participant.Creature == attacker
                    || participant.Creature == target
                    || !participant.CanFlank
                    || !participant.IsFriendlyToAttacker
                    || participant.IsFriendlyToTarget
                    || !participant.ThreatensTarget
                    || !HasLivingPosition(
                        snapshot,
                        participant.Creature,
                        out GridPosition allyPosition
                    )
                )
                    continue;

                if (IsOppositeSideOrCorner(attackerPosition, allyPosition, targetPosition))
                    return true;
            }

            return false;
        }

        private static bool HasLivingPosition(
            RulesSnapshot snapshot,
            CreatureId creature,
            out GridPosition position
        )
        {
            position = default;
            return snapshot.Creatures.Contains(creature)
                && snapshot.Health.IsAlive(creature)
                && snapshot.Positions.TryGet(creature, out position);
        }

        private static bool IsOppositeSideOrCorner(
            GridPosition attacker,
            GridPosition ally,
            GridPosition target
        )
        {
            int attackerX = Math.Sign(attacker.X - target.X);
            int attackerZ = Math.Sign(attacker.Z - target.Z);
            int allyX = Math.Sign(ally.X - target.X);
            int allyZ = Math.Sign(ally.Z - target.Z);
            return (attackerX != 0 || attackerZ != 0) && attackerX == -allyX && attackerZ == -allyZ;
        }
    }
}
