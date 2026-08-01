using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Rules.Runtime;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Owns the immutable compiled rules package plus the explicitly deferred spell and persisted
    /// active-effect boundaries. Migrated rule participation is never mutable through this type.
    /// </summary>
    public sealed class PreparedCharacter
    {
        private ISpellBook spellBook = EmptySpellBook.Instance;

        /// <summary>Creates an empty prepared shell for persistence-only restoration.</summary>
        public PreparedCharacter(CharacterBuild build)
            : this(build, EmptyPackage(), Array.Empty<OwnedPf2eItem>()) { }

        internal PreparedCharacter(
            CharacterBuild build,
            PreparedRulePackage rules,
            IEnumerable<OwnedPf2eItem> ownedItems
        )
        {
            Build = build ?? new CharacterBuild();
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            OwnedItems = Array.AsReadOnly(
                (ownedItems ?? throw new ArgumentNullException(nameof(ownedItems))).ToArray()
            );
        }

        public CharacterBuild Build { get; }

        /// <summary>Gets the frozen, single-authority output of rule preparation.</summary>
        public PreparedRulePackage Rules { get; }

        /// <summary>Gets immutable source ownership records retained for display and persistence.</summary>
        public IReadOnlyList<OwnedPf2eItem> OwnedItems { get; }

        /// <summary>Gets immutable normalized options for diagnostics and presentation only.</summary>
        public IReadOnlyList<string> RollOptions =>
            Array.AsReadOnly(
                Rules
                    .Inputs.StaticOptions.Concat(Rules.Options.Select(value => value.Option))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            );

        /// <summary>Gets immutable compiled skill ranks.</summary>
        public IReadOnlyDictionary<string, int> SkillRanks => Rules.Inputs.SkillRanks;

        /// <summary>
        /// Gets persisted effects that remain quarantined until the later active-effect persistence
        /// slice. Rules evaluation does not read this collection.
        /// </summary>
        public List<ActivePf2eEffect> ActiveEffects { get; } = new();

        public SpellcastingState Spellcasting { get; set; }

        public ISpellBook SpellBook
        {
            get => spellBook;
            set => spellBook = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Checks compile-time ownership without reading a mutable prepared collection.</summary>
        public bool HasOwnedItem(string slug)
        {
            string option = $"item:owned:{Pf2eSlug.FromName(slug)}";
            return Rules.Options.Any(value =>
                string.Equals(value.Option, option, StringComparison.Ordinal)
            );
        }

        /// <summary>Checks only the deferred persistence collection; runtime rules use bindings.</summary>
        public bool HasActiveEffect(string slug) =>
            ActiveEffects.Any(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );

        public void RestoreActiveEffects(IEnumerable<ActivePf2eEffect> effects)
        {
            ActivePf2eEffect[] copied = (
                effects ?? throw new ArgumentNullException(nameof(effects))
            ).ToArray();
            if (copied.Any(effect => effect == null))
                throw new ArgumentException(
                    "Restored prepared effects cannot contain null.",
                    nameof(effects)
                );
            ActiveEffects.Clear();
            ActiveEffects.AddRange(copied);
        }

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

        public void RemoveActiveEffect(string slug) =>
            ActiveEffects.RemoveAll(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );

        private static PreparedRulePackage EmptyPackage() =>
            new(
                new PreparedCreatureInputs(
                    0,
                    default,
                    Array.Empty<KeyValuePair<string, int>>(),
                    Array.Empty<string>(),
                    string.Empty,
                    Array.Empty<string>(),
                    Array.Empty<PreparedDefenseDescriptor>(),
                    Array.Empty<PreparedDefenseDescriptor>(),
                    Array.Empty<PreparedImmunityDescriptor>(),
                    Array.Empty<string>()
                ),
                Array.Empty<PreparedRuleDefinitionSpec>(),
                Array.Empty<PreparedBindingSeed>(),
                Array.Empty<PreparedOptionSpec>(),
                Array.Empty<PreparedModifierSpec>(),
                Array.Empty<PreparedAdjustmentSpec>(),
                Array.Empty<PreparedDamageDiceSpec>(),
                Array.Empty<PreparedItemAlterationSpec>(),
                Array.Empty<PreparedUnsupportedDiagnostic>()
            );
    }

    /// <summary>Records immutable prepared ownership and its grant provenance.</summary>
    public sealed class OwnedPf2eItem
    {
        public OwnedPf2eItem(Pf2eItem item, string grantedBy)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            GrantedBy = grantedBy ?? string.Empty;
        }

        public Pf2eItem Item { get; }
        public string GrantedBy { get; }
    }

    /// <summary>Deferred persisted effect descriptor; it is not a runtime rule authority.</summary>
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
