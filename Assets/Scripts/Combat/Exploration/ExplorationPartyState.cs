using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Game.DungeonGeneration;

namespace Game.Combat.Exploration
{
    /// <summary>Identifies one living exploration party member independently of roster position.</summary>
    public readonly struct ExplorationMemberId : IEquatable<ExplorationMemberId>
    {
        private readonly string value;

        /// <summary>Creates a stable member identifier from non-blank text.</summary>
        /// <param name="value">The identifier text, normalized by trimming outer whitespace.</param>
        /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
        public ExplorationMemberId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "An exploration member requires a stable identifier.",
                    nameof(value)
                );

            this.value = value.Trim();
        }

        /// <summary>Gets the normalized identifier text, or an empty string for a default value.</summary>
        public string Value => value ?? string.Empty;

        /// <summary>Gets whether this is an uninitialized default identifier.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(value);

        /// <inheritdoc/>
        public bool Equals(ExplorationMemberId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ExplorationMemberId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;

        /// <summary>Compares two identifiers using ordinal text equality.</summary>
        public static bool operator ==(ExplorationMemberId left, ExplorationMemberId right) =>
            left.Equals(right);

        /// <summary>Compares two identifiers using ordinal text inequality.</summary>
        public static bool operator !=(ExplorationMemberId left, ExplorationMemberId right) =>
            !left.Equals(right);
    }

    /// <summary>Stores one living party member's stable identity and current grid cell.</summary>
    public readonly struct ExplorationPartyMember : IEquatable<ExplorationPartyMember>
    {
        /// <summary>Creates an immutable member position.</summary>
        /// <param name="id">The initialized stable member identifier.</param>
        /// <param name="cell">The member's unique current cell.</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> is uninitialized.</exception>
        public ExplorationPartyMember(ExplorationMemberId id, DungeonCell cell)
        {
            if (id.IsEmpty)
                throw new ArgumentException(
                    "An exploration party member requires an initialized identifier.",
                    nameof(id)
                );

            Id = id;
            Cell = cell;
        }

        /// <summary>Gets the member's stable identity.</summary>
        public ExplorationMemberId Id { get; }

        /// <summary>Gets the member's current grid cell.</summary>
        public DungeonCell Cell { get; }

        /// <inheritdoc/>
        public bool Equals(ExplorationPartyMember other) => Id == other.Id && Cell == other.Cell;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ExplorationPartyMember other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Id.GetHashCode() * 397) ^ Cell.GetHashCode();
            }
        }

        /// <summary>Compares two member positions for identity and cell equality.</summary>
        public static bool operator ==(ExplorationPartyMember left, ExplorationPartyMember right) =>
            left.Equals(right);

        /// <summary>Compares two member positions for identity or cell inequality.</summary>
        public static bool operator !=(ExplorationPartyMember left, ExplorationPartyMember right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// Captures a non-empty living roster, its stable order, unique occupied cells, and selected
    /// exploration leader.
    /// </summary>
    public sealed class ExplorationPartyState
    {
        private readonly ReadOnlyCollection<ExplorationPartyMember> members;
        private readonly IReadOnlyDictionary<ExplorationMemberId, int> indexById;

        /// <summary>Creates a validated immutable party state.</summary>
        /// <param name="members">The non-empty living roster in stable follower order.</param>
        /// <param name="selectedLeaderId">The identity of any member in the supplied roster.</param>
        /// <exception cref="ArgumentNullException"><paramref name="members"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// The roster is empty, contains an uninitialized or duplicate identity, contains
        /// overlapping cells, or does not contain <paramref name="selectedLeaderId"/>.
        /// </exception>
        public ExplorationPartyState(
            IEnumerable<ExplorationPartyMember> members,
            ExplorationMemberId selectedLeaderId
        )
        {
            if (members == null)
                throw new ArgumentNullException(nameof(members));

            ExplorationPartyMember[] copied = members.ToArray();
            if (copied.Length == 0)
                throw new ArgumentException(
                    "An exploration party requires at least one living member.",
                    nameof(members)
                );
            if (copied.Any(member => member.Id.IsEmpty))
                throw new ArgumentException(
                    "Exploration party identities must be initialized.",
                    nameof(members)
                );
            if (copied.Select(member => member.Id).Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "Exploration party identities must be unique.",
                    nameof(members)
                );
            if (copied.Select(member => member.Cell).Distinct().Count() != copied.Length)
                throw new ArgumentException(
                    "Exploration party members cannot occupy the same cell.",
                    nameof(members)
                );

            Dictionary<ExplorationMemberId, int> copiedIndex = new();
            for (int index = 0; index < copied.Length; index++)
                copiedIndex.Add(copied[index].Id, index);
            if (selectedLeaderId.IsEmpty || !copiedIndex.ContainsKey(selectedLeaderId))
            {
                throw new ArgumentException(
                    "The selected exploration leader must belong to the living roster.",
                    nameof(selectedLeaderId)
                );
            }

            this.members = Array.AsReadOnly(copied);
            indexById = new ReadOnlyDictionary<ExplorationMemberId, int>(copiedIndex);
            SelectedLeaderId = selectedLeaderId;
        }

        /// <summary>Gets the living roster in stable follower order.</summary>
        public IReadOnlyList<ExplorationPartyMember> Members => members;

        /// <summary>Gets the selected leader's stable identity.</summary>
        public ExplorationMemberId SelectedLeaderId { get; }

        /// <summary>Gets the selected leader's current immutable member state.</summary>
        public ExplorationPartyMember SelectedLeader => members[indexById[SelectedLeaderId]];

        /// <summary>Selects any living roster member without changing order or positions.</summary>
        /// <param name="leaderId">The identity of the new leader.</param>
        /// <returns>A new party state with the requested leader.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="leaderId"/> is uninitialized or absent from the living roster.
        /// </exception>
        public ExplorationPartyState SelectLeader(ExplorationMemberId leaderId)
        {
            if (leaderId.IsEmpty || !indexById.ContainsKey(leaderId))
                throw new ArgumentException(
                    "The selected exploration leader must belong to the living roster.",
                    nameof(leaderId)
                );

            return leaderId == SelectedLeaderId
                ? this
                : new ExplorationPartyState(members, leaderId);
        }
    }
}
