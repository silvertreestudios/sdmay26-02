using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Rules.Runtime;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Quarantines spellcasting and persisted legacy effects that have not yet moved to rules
    /// state. Prepared ownership, options, skill ranks, and contributions never live here.
    /// </summary>
    public sealed class PreparedCharacter
    {
        private ISpellBook spellBook = EmptySpellBook.Instance;

        /// <summary>Creates an empty quarantine state for loading and persistence.</summary>
        public PreparedCharacter() { }

        /// <summary>Gets persisted legacy effects excluded from the prepared-rules cutover.</summary>
        public List<ActivePf2eEffect> ActiveEffects { get; } = new();

        /// <summary>Gets or sets legacy spellcasting state pending its dedicated migration.</summary>
        public SpellcastingState Spellcasting { get; set; }

        /// <summary>Gets or replaces the quarantined spellbook.</summary>
        public ISpellBook SpellBook
        {
            get => spellBook;
            set => spellBook = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Checks only the deferred persisted-effect collection.</summary>
        public bool HasActiveEffect(string slug) =>
            ActiveEffects.Any(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );

        /// <summary>Replaces the complete deferred persisted-effect collection.</summary>
        public void RestoreActiveEffects(IEnumerable<ActivePf2eEffect> effects)
        {
            ActivePf2eEffect[] copied = (
                effects ?? throw new ArgumentNullException(nameof(effects))
            ).ToArray();
            if (copied.Any(effect => effect == null))
                throw new ArgumentException(
                    "Restored effects cannot contain null.",
                    nameof(effects)
                );
            ActiveEffects.Clear();
            ActiveEffects.AddRange(copied);
        }

        /// <summary>Adds one deferred persisted effect when it is not already present.</summary>
        public ActivePf2eEffect AddActiveEffect(Pf2eItem item, string sourceSlug)
        {
            string slug = string.IsNullOrWhiteSpace(sourceSlug) ? item?.Slug : sourceSlug;
            ActivePf2eEffect existing = ActiveEffects.FirstOrDefault(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );
            if (existing != null)
                return existing;
            ActivePf2eEffect active = new(item?.Name ?? slug, slug, item?.Slug ?? slug);
            ActiveEffects.Add(active);
            return active;
        }

        /// <summary>Removes matching deferred persisted effects.</summary>
        public void RemoveActiveEffect(string slug) =>
            ActiveEffects.RemoveAll(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );
    }

    /// <summary>Deferred persisted effect descriptor; it is not runtime rules authority.</summary>
    public sealed class ActivePf2eEffect
    {
        public ActivePf2eEffect(string name, string slug, string sourceSlug)
        {
            Name = name ?? string.Empty;
            Slug = slug ?? string.Empty;
            SourceSlug = sourceSlug ?? string.Empty;
        }

        public string Name { get; }
        public string Slug { get; }
        public string SourceSlug { get; }
    }
}
