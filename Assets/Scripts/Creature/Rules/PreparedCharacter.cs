using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Rules.Runtime;
using Newtonsoft.Json.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Holds derived PF2e rule state for a creature after build choices and data item rules have been prepared.
    /// </summary>
    public sealed class PreparedCharacter
    {
        private ISpellBook spellBook = EmptySpellBook.Instance;
        public CharacterBuild Build { get; }
        public List<OwnedPf2eItem> OwnedItems { get; } = new();
        public HashSet<string> RollOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RuleModifier> Modifiers { get; } = new();
        public List<RuleAdjustment> Adjustments { get; } = new();
        public List<RuleDamageDice> DamageDice { get; } = new();
        public List<ItemAlterationRule> ItemAlterations { get; } = new();
        public List<ActivePf2eEffect> ActiveEffects { get; } = new();
        public Dictionary<string, int> SkillRanks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> RuleValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> RuleReferences { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> UnsupportedRuleKeys { get; } = new();

        public SpellcastingState Spellcasting { get; set; }

        /// <summary>Gets or replaces the creature's required spellbook.</summary>
        public ISpellBook SpellBook
        {
            get => spellBook;
            set => spellBook = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Creates prepared state around saved build choices while leaving all derivation to the preparer.
        /// </summary>
        /// <param name="build">The durable character build choices that seed preparation.</param>
        public PreparedCharacter(CharacterBuild build)
        {
            Build = build ?? new CharacterBuild();
        }

        /// <summary>
        /// Adds a PF2e item once and records its roll options so later predicates can match it.
        /// </summary>
        /// <param name="item">The catalog item to add.</param>
        /// <param name="grantedBy">Optional slug of the item or rule that granted this item.</param>
        /// <returns>True when the item was newly added.</returns>
        public bool AddOwnedItem(Pf2eItem item, string grantedBy = null)
        {
            if (item == null || OwnedItems.Any(owned => owned.Item.Slug == item.Slug))
                return false;

            OwnedItems.Add(new OwnedPf2eItem(item, grantedBy));
            AddRollOptionsForItem(item);
            return true;
        }

        /// <summary>
        /// Checks whether the character owns an item by slug.
        /// </summary>
        /// <param name="slug">The item slug to match.</param>
        /// <returns>True when an owned item has the supplied slug.</returns>
        public bool HasOwnedItem(string slug)
        {
            return OwnedItems.Any(item =>
                string.Equals(item.Item.Slug, slug, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Checks whether an effect is currently active by either its effect slug or source slug.
        /// </summary>
        /// <param name="slug">The active effect slug or source slug to match.</param>
        /// <returns>True when the prepared character currently has that effect.</returns>
        public bool HasActiveEffect(string slug)
        {
            return ActiveEffects.Any(effect =>
                string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(effect.SourceSlug, slug, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Records an active effect and exposes the Foundry-style effect roll options used by predicates.
        /// </summary>
        /// <param name="item">The effect item from the catalog, when one can be resolved.</param>
        /// <param name="sourceSlug">The stable source slug used by the rule that activated the effect.</param>
        /// <returns>The existing or newly created active effect entry.</returns>
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
            AddEffectRollOptions(active);
            return active;
        }

        /// <summary>Replaces active prepared effects and only their derived roll options.</summary>
        /// <param name="effects">The complete validated active-effect membership.</param>
        public void RestoreActiveEffects(IEnumerable<ActivePf2eEffect> effects)
        {
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));
            ActivePf2eEffect[] copied = effects.ToArray();
            if (copied.Any(effect => effect == null))
                throw new ArgumentException(
                    "Restored prepared effects cannot contain null.",
                    nameof(effects)
                );

            foreach (ActivePf2eEffect existing in ActiveEffects)
            {
                RollOptions.Remove($"self:effect:{existing.Slug}");
                RollOptions.Remove($"self:effect:{existing.SourceSlug}");
            }
            ActiveEffects.Clear();
            foreach (ActivePf2eEffect effect in copied)
            {
                ActiveEffects.Add(effect);
                AddEffectRollOptions(effect);
            }
        }

        /// <summary>
        /// Removes active effect state and the roll options that came from it.
        /// </summary>
        /// <param name="slug">The effect slug or source slug to remove.</param>
        public void RemoveActiveEffect(string slug)
        {
            foreach (
                ActivePf2eEffect effect in ActiveEffects
                    .Where(effect =>
                        string.Equals(effect.Slug, slug, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            effect.SourceSlug,
                            slug,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToArray()
            )
            {
                RollOptions.Remove($"self:effect:{effect.Slug}");
                RollOptions.Remove($"self:effect:{effect.SourceSlug}");
                ActiveEffects.Remove(effect);
            }
        }

        private void AddRollOptionsForItem(Pf2eItem item)
        {
            if (item.Type == "class")
                RollOptions.Add($"class:{item.Slug}");

            if (item.Type == "feat")
                RollOptions.Add($"feat:{item.Slug}");

            string category = item.System?.SelectToken("category")?.Value<string>();
            if (
                item.Type == "feat"
                && string.Equals(category, "classfeature", StringComparison.OrdinalIgnoreCase)
            )
                RollOptions.Add($"feature:{item.Slug}");

            if (item.Type == "action")
                RollOptions.Add($"action:{item.Slug}");
        }

        private void AddEffectRollOptions(ActivePf2eEffect effect)
        {
            RollOptions.Add($"self:effect:{effect.Slug}");
            RollOptions.Add($"self:effect:{effect.SourceSlug}");
        }
    }

    /// <summary>
    /// Records a prepared item and the optional source that granted it so future rules can reason about provenance.
    /// </summary>
    public sealed class OwnedPf2eItem
    {
        public Pf2eItem Item { get; }
        public string GrantedBy { get; }

        /// <summary>
        /// Creates a prepared ownership record for a PF2e item.
        /// </summary>
        /// <param name="item">The owned PF2e catalog item.</param>
        /// <param name="grantedBy">The slug of the item or rule that granted it, when known.</param>
        public OwnedPf2eItem(Pf2eItem item, string grantedBy)
        {
            Item = item;
            GrantedBy = grantedBy;
        }
    }

    /// <summary>
    /// Tracks an active PF2e effect in prepared state without requiring Unity components to know its source JSON.
    /// </summary>
    public sealed class ActivePf2eEffect
    {
        public string Name { get; }
        public string Slug { get; }
        public string SourceSlug { get; }

        /// <summary>
        /// Creates an active effect entry with both runtime and source identifiers.
        /// </summary>
        /// <param name="name">The display name of the active effect.</param>
        /// <param name="slug">The runtime effect slug used for rule checks.</param>
        /// <param name="sourceSlug">The source item slug used for cleanup and predicate options.</param>
        public ActivePf2eEffect(string name, string slug, string sourceSlug)
        {
            Name = name;
            Slug = slug;
            SourceSlug = sourceSlug;
        }
    }

    /// <summary>
    /// Represents a supported FlatModifier rule element after item preparation.
    /// </summary>
    public sealed class RuleModifier
    {
        public string Selector;
        public string Slug;
        public int Value;
        public string Type;
        public string Ability;
        public JToken Predicate;
    }

    /// <summary>
    /// Represents a supported AdjustModifier rule element that can alter a prepared modifier by selector and slug.
    /// </summary>
    public sealed class RuleAdjustment
    {
        public string Selector;
        public string Slug;
        public string Mode;
        public float Value;
        public int Priority;
        public JToken Predicate;
    }

    /// <summary>
    /// Represents a supported DamageDice rule element after item preparation.
    /// </summary>
    public sealed class RuleDamageDice
    {
        public string Selector;
        public string Category;
        public int DiceNumber;
        public int DieSize;
        public JToken Predicate;
    }

    /// <summary>
    /// Represents a supported ItemAlteration rule element while keeping upstream item JSON unchanged.
    /// </summary>
    public sealed class ItemAlterationRule
    {
        public string ItemType;
        public string Mode;
        public string Property;
        public string Value;
        public JToken Predicate;
    }
}
