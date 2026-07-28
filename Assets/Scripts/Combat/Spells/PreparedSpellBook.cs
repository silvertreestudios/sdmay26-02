using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;

namespace Game.Combat.Spells
{
    /// <summary>Describes how one exact prepared spell obtains its casting resource.</summary>
    public readonly struct PreparedSpellEntry : IEquatable<PreparedSpellEntry>
    {
        /// <summary>Creates a cantrip entry that never spends a slot.</summary>
        /// <param name="spell">The exact cantrip reference prepared in the book.</param>
        /// <returns>A preparation entry authorized without a slot pool.</returns>
        public static PreparedSpellEntry Cantrip(SpellReference spell) =>
            new PreparedSpellEntry(spell, default, true);

        /// <summary>Creates a ranked entry authorized to spend one exact slot pool.</summary>
        /// <param name="spell">The exact spell and rank prepared in the slot.</param>
        /// <param name="pool">The local prepared slot pool that authorizes the cast.</param>
        /// <returns>A preparation entry bound to the supplied pool.</returns>
        public static PreparedSpellEntry FromPool(SpellReference spell, SpellSlotPoolId pool) =>
            new PreparedSpellEntry(spell, pool, false);

        private PreparedSpellEntry(SpellReference spell, SpellSlotPoolId pool, bool isCantrip)
        {
            if (!isCantrip && pool.IsEmpty)
                throw new ArgumentException(
                    "A ranked preparation requires a slot pool.",
                    nameof(pool)
                );
            Spell = spell;
            Pool = pool;
            IsCantrip = isCantrip;
        }

        /// <summary>Gets the exact prepared spell and rank.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the local slot pool for a ranked spell.</summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>Gets whether this preparation spends no spell slot.</summary>
        public bool IsCantrip { get; }

        /// <inheritdoc/>
        public bool Equals(PreparedSpellEntry other) =>
            Spell == other.Spell && Pool == other.Pool && IsCantrip == other.IsCantrip;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PreparedSpellEntry other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Spell, Pool, IsCantrip);
    }

    /// <summary>Defines one local slot pool used to seed authoritative encounter state.</summary>
    public readonly struct PreparedSpellSlotPool : IEquatable<PreparedSpellSlotPool>
    {
        /// <summary>Creates a local prepared slot pool.</summary>
        /// <param name="id">The stable local ID that will be scoped to an encounter creature.</param>
        /// <param name="maximum">The maximum casts available after preparation.</param>
        public PreparedSpellSlotPool(SpellSlotPoolId id, int maximum)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A slot pool ID is required.", nameof(id));
            if (maximum < 0)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            Id = id;
            Maximum = maximum;
        }

        /// <summary>Gets the stable local pool identity.</summary>
        public SpellSlotPoolId Id { get; }

        /// <summary>Gets the maximum casts available from the pool.</summary>
        public int Maximum { get; }

        /// <inheritdoc/>
        public bool Equals(PreparedSpellSlotPool other) =>
            Id == other.Id && Maximum == other.Maximum;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is PreparedSpellSlotPool other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Id, Maximum);
    }

    /// <summary>
    /// Implements exact-rank preparation and binds resources owned by encounter rules state.
    /// </summary>
    public sealed class PreparedSpellBook : ISpellBook
    {
        private readonly IReadOnlyList<PreparedSpellEntry> entries;
        private readonly IReadOnlyList<SpellReference> castableSpells;
        private readonly Dictionary<SpellSlotPoolId, int> maximums;

        /// <summary>Creates a validated, deduplicated prepared spellbook.</summary>
        /// <param name="entries">Exact-rank preparations and their authorized resources.</param>
        /// <param name="pools">Local pools used for ranked prepared spells and divine font.</param>
        /// <param name="spellAttackModifier">The derived spell attack modifier for this caster.</param>
        public PreparedSpellBook(
            IEnumerable<PreparedSpellEntry> entries,
            IEnumerable<PreparedSpellSlotPool> pools,
            int spellAttackModifier
        )
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (pools == null)
                throw new ArgumentNullException(nameof(pools));
            PreparedSpellEntry[] uniqueEntries = entries.Distinct().ToArray();
            if (
                uniqueEntries
                    .GroupBy(entry => entry.Spell)
                    .Any(group => group.Select(entry => entry).Distinct().Count() > 1)
            )
                throw new ArgumentException(
                    "An exact spell reference cannot authorize multiple resources.",
                    nameof(entries)
                );
            PreparedSpellSlotPool[] uniquePools = pools.Distinct().ToArray();
            if (uniquePools.GroupBy(pool => pool.Id).Any(group => group.Count() > 1))
                throw new ArgumentException("Slot pool IDs must be unique.", nameof(pools));
            maximums = uniquePools.ToDictionary(pool => pool.Id, pool => pool.Maximum);
            if (uniqueEntries.Any(entry => !entry.IsCantrip && !maximums.ContainsKey(entry.Pool)))
                throw new ArgumentException(
                    "Every ranked preparation must reference a declared slot pool.",
                    nameof(entries)
                );
            this.entries = Array.AsReadOnly(uniqueEntries);
            castableSpells = Array.AsReadOnly(uniqueEntries.Select(entry => entry.Spell).ToArray());
            SpellAttackModifier = spellAttackModifier;
        }

        /// <summary>Gets the immutable, deduplicated preparation entries.</summary>
        public IReadOnlyList<PreparedSpellEntry> Entries => entries;

        /// <inheritdoc/>
        public IReadOnlyList<SpellReference> CastableSpells => castableSpells;

        /// <inheritdoc/>
        public int SpellAttackModifier { get; }

        /// <inheritdoc/>
        public int SpellDc => 10 + SpellAttackModifier;

        /// <inheritdoc/>
        public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
            maximums
                .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(pair => new SpellSlotState(
                    EncounterPool(owner, pair.Key),
                    owner,
                    pair.Value,
                    pair.Value
                ))
                .ToArray();

        /// <inheritdoc/>
        public SpellCastAuthorization Authorize(
            CreatureId owner,
            SpellReference spell,
            ISpellSlotStateReader slots
        )
        {
            if (!TryGetEntry(spell, out PreparedSpellEntry entry))
                return SpellCastAuthorization.Unavailable(
                    "The exact spell rank is not known or prepared."
                );
            if (entry.IsCantrip)
                return SpellCastAuthorization.Cantrip;
            SpellSlotPoolId authorizedPool = EncounterPool(owner, entry.Pool);
            if (slots == null || !slots.TryGet(authorizedPool, out SpellSlotState state))
                return SpellCastAuthorization.Unavailable(
                    "The authorized spell-slot pool is missing."
                );
            if (state.Owner != owner)
                return SpellCastAuthorization.Unavailable(
                    "The authorized spell-slot pool belongs to another creature."
                );
            if (state.Remaining <= 0)
                return SpellCastAuthorization.Unavailable(
                    "The authorized spell-slot pool is exhausted."
                );
            return SpellCastAuthorization.FromPool(authorizedPool);
        }

        /// <inheritdoc/>
        public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell)
        {
            if (!TryGetEntry(spell, out PreparedSpellEntry entry))
                return SpellCastAuthorization.Unavailable(
                    "The exact spell rank is not known or prepared."
                );
            return entry.IsCantrip
                ? SpellCastAuthorization.Cantrip
                : SpellCastAuthorization.FromPool(EncounterPool(owner, entry.Pool));
        }

        private bool TryGetEntry(SpellReference spell, out PreparedSpellEntry entry)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].Spell != spell)
                    continue;
                entry = entries[index];
                return true;
            }
            entry = default;
            return false;
        }

        private static SpellSlotPoolId EncounterPool(CreatureId owner, SpellSlotPoolId pool) =>
            new($"{owner.Value}:{pool.Value}");
    }

    /// <summary>Null Object spellbook used by every non-caster.</summary>
    public sealed class EmptySpellBook : ISpellBook
    {
        private EmptySpellBook() { }

        /// <summary>Gets the shared Null Object used for creatures without spellcasting.</summary>
        public static EmptySpellBook Instance { get; } = new EmptySpellBook();

        /// <inheritdoc/>
        public IReadOnlyList<SpellReference> CastableSpells { get; } =
            Array.Empty<SpellReference>();

        /// <inheritdoc/>
        public int SpellAttackModifier => 0;

        /// <inheritdoc/>
        public int SpellDc => 10;

        /// <inheritdoc/>
        public IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner) =>
            Array.Empty<SpellSlotState>();

        /// <inheritdoc/>
        public SpellCastAuthorization Authorize(
            CreatureId owner,
            SpellReference spell,
            ISpellSlotStateReader slots
        ) => SpellCastAuthorization.Unavailable("The creature has no spellbook.");

        /// <inheritdoc/>
        public SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell) =>
            SpellCastAuthorization.Unavailable("The creature has no spellbook.");
    }
}
