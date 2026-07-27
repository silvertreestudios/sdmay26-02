using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature.Rules;

namespace Game.Combat.Spells
{
    public enum SpellSlotKind
    {
        Cantrip,
        Prepared,
        Font,
    }

    public sealed class SpellSlotPool
    {
        public string Id { get; }
        public SpellSlotKind Kind { get; }
        public int Rank { get; }
        public int MaxUses { get; }
        public int UsesRemaining { get; private set; }

        public SpellSlotPool(string id, SpellSlotKind kind, int rank, int maxUses)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Rank = Math.Max(0, rank);
            MaxUses = Math.Max(0, maxUses);
            UsesRemaining = MaxUses;
        }

        public bool Spend()
        {
            if (Kind == SpellSlotKind.Cantrip)
                return true;
            if (UsesRemaining <= 0)
                return false;
            UsesRemaining--;
            return true;
        }

        public void Restore() => UsesRemaining = MaxUses;
    }

    public sealed class PreparedSpell
    {
        public string Name { get; }
        public string Slug { get; }
        public int Rank { get; }
        public bool IsCantrip { get; }
        public bool IsFontSpell { get; }
        public string SlotPoolId { get; }
        public IReadOnlyList<uint> ActionCosts { get; }

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

    public sealed class SpellcastingState
    {
        private readonly Dictionary<string, SpellSlotPool> pools = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<PreparedSpell> spells = new();

        public string Tradition { get; set; } = "divine";
        public string Ability { get; set; } = "wis";
        public int SpellAttackModifier { get; set; }
        public int SpellDc => 10 + SpellAttackModifier;
        public IReadOnlyDictionary<string, SpellSlotPool> Pools => pools;
        public IReadOnlyList<PreparedSpell> PreparedSpells => spells;

        public void AddPool(SpellSlotPool pool)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Id))
                return;
            pools[pool.Id] = pool;
        }

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

        public PreparedSpell GetSpell(string slugOrName)
        {
            string slug = Pf2eSlug.FromName(slugOrName);
            return spells.FirstOrDefault(spell =>
                string.Equals(spell.Slug, slug, StringComparison.OrdinalIgnoreCase)
            );
        }

        public bool CanCast(PreparedSpell spell)
        {
            if (spell == null)
                return false;
            if (spell.IsCantrip)
                return true;
            return pools.TryGetValue(spell.SlotPoolId, out SpellSlotPool pool)
                && pool.UsesRemaining > 0;
        }

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
