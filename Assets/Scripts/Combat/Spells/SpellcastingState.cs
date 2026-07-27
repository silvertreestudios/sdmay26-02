using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature.Rules;

namespace Game.Combat.Spells
{
    /// <summary>Identifies the legacy resource category for a spell-slot pool.</summary>
    public enum SpellSlotKind
    {
        Cantrip,
        Prepared,
        Font,
    }

    /// <summary>Stores mutable slot uses for the deprecated Unity-owned spellcasting path.</summary>
    public sealed class SpellSlotPool
    {
        /// <summary>Gets the stable legacy pool identifier.</summary>
        public string Id { get; }

        /// <summary>Gets the resource category.</summary>
        public SpellSlotKind Kind { get; }

        /// <summary>Gets the spell rank supported by the pool.</summary>
        public int Rank { get; }

        /// <summary>Gets the maximum number of uses.</summary>
        public int MaxUses { get; }

        /// <summary>Gets the remaining uses.</summary>
        public int UsesRemaining { get; private set; }

        /// <summary>Creates a legacy spell-slot pool.</summary>
        public SpellSlotPool(string id, SpellSlotKind kind, int rank, int maxUses)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Rank = Math.Max(0, rank);
            MaxUses = Math.Max(0, maxUses);
            UsesRemaining = MaxUses;
        }

        /// <summary>Spends one use, with cantrips remaining unlimited.</summary>
        public bool Spend()
        {
            if (Kind == SpellSlotKind.Cantrip)
                return true;
            if (UsesRemaining <= 0)
                return false;
            UsesRemaining--;
            return true;
        }

        /// <summary>Restores this pool to its prepared maximum.</summary>
        public void Restore() => UsesRemaining = MaxUses;
    }

    /// <summary>Describes one spell prepared for the deprecated legacy caster.</summary>
    public sealed class PreparedSpell
    {
        /// <summary>Gets the player-facing name.</summary>
        public string Name { get; }

        /// <summary>Gets the stable spell slug.</summary>
        public string Slug { get; }

        /// <summary>Gets the prepared rank.</summary>
        public int Rank { get; }

        /// <summary>Gets whether the spell spends no slot.</summary>
        public bool IsCantrip { get; }

        /// <summary>Gets whether the spell uses the legacy font pool.</summary>
        public bool IsFontSpell { get; }

        /// <summary>Gets the legacy slot-pool identifier.</summary>
        public string SlotPoolId { get; }

        /// <summary>Gets the supported action-cost variants.</summary>
        public IReadOnlyList<uint> ActionCosts { get; }

        /// <summary>Creates one legacy prepared-spell entry.</summary>
        public PreparedSpell(
            string name,
            int rank,
            bool isCantrip,
            bool isFontSpell,
            string slotPoolId,
            IEnumerable<uint> actionCosts
        )
        {
            Name = name ?? string.Empty;
            Slug = Pf2eSlug.FromName(Name);
            Rank = Math.Max(0, rank);
            IsCantrip = isCantrip;
            IsFontSpell = isFontSpell;
            SlotPoolId = slotPoolId ?? string.Empty;
            ActionCosts = (actionCosts ?? new[] { 1u }).Distinct().OrderBy(cost => cost).ToArray();
        }
    }

    /// <summary>
    /// Owns prepared spells and local resource spending for legacy non-Light spellcasting.
    /// </summary>
    /// <remarks>
    /// Migrated spells use <see cref="Game.Rules.Runtime.ISpellBook"/> and authoritative rules state.
    /// </remarks>
    [Obsolete(
        "Use ISpellBook and the rules runtime for migrated spells; SpellcastingState is retained only for legacy non-Light spells.",
        false
    )]
    public sealed class SpellcastingState
    {
        private readonly Dictionary<string, SpellSlotPool> pools = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<PreparedSpell> spells = new();

        /// <summary>Gets or sets the legacy spellcasting tradition slug.</summary>
        public string Tradition { get; set; } = "divine";

        /// <summary>Gets or sets the legacy spellcasting ability slug.</summary>
        public string Ability { get; set; } = "wis";

        /// <summary>Gets or sets the legacy spell attack modifier.</summary>
        public int SpellAttackModifier { get; set; }

        /// <summary>Gets the legacy spell DC.</summary>
        public int SpellDc => 10 + SpellAttackModifier;

        /// <summary>Gets the configured local slot pools.</summary>
        public IReadOnlyDictionary<string, SpellSlotPool> Pools => pools;

        /// <summary>Gets the prepared legacy spells; Light is intentionally absent.</summary>
        public IReadOnlyList<PreparedSpell> PreparedSpells => spells;

        /// <summary>Adds or replaces one local slot pool.</summary>
        public void AddPool(SpellSlotPool pool)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Id))
                return;
            pools[pool.Id] = pool;
        }

        /// <summary>Adds a prepared spell unless an equivalent action entry already exists.</summary>
        public void AddSpell(PreparedSpell spell)
        {
            if (
                spell == null
                || spells.Any(existing =>
                    string.Equals(existing.Name, spell.Name, StringComparison.OrdinalIgnoreCase)
                    && existing.ActionCosts.SequenceEqual(spell.ActionCosts)
                )
            )
                return;
            spells.Add(spell);
        }

        /// <summary>Finds a prepared spell by slug or display name.</summary>
        public PreparedSpell GetSpell(string slugOrName)
        {
            string slug = Pf2eSlug.FromName(slugOrName);
            return spells.FirstOrDefault(spell =>
                string.Equals(spell.Slug, slug, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>Checks whether the local legacy resource can pay for a spell.</summary>
        public bool CanCast(PreparedSpell spell)
        {
            if (spell == null)
                return false;
            if (spell.IsCantrip)
                return true;
            return pools.TryGetValue(spell.SlotPoolId, out SpellSlotPool pool)
                && pool.UsesRemaining > 0;
        }

        /// <summary>Spends the local legacy resource for a spell.</summary>
        public bool Spend(PreparedSpell spell)
        {
            if (spell == null)
                return false;
            if (spell.IsCantrip)
                return true;
            return pools.TryGetValue(spell.SlotPoolId, out SpellSlotPool pool) && pool.Spend();
        }
    }
}
