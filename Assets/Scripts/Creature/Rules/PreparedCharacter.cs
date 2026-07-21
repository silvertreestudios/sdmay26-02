using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Newtonsoft.Json.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Holds derived PF2e rule state for a creature after build choices and data item rules have been prepared.
    /// </summary>
    public sealed class PreparedCharacter
    {
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

        /// <summary>
        /// Atomically replaces active prepared effects and rebuilds their predicate roll options.
        /// </summary>
        /// <param name="effects">The complete active-effect set from validated persistence state.</param>
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
            if (
                copied
                    .Select(effect => effect.Slug + "\n" + effect.SourceSlug)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != copied.Length
            )
                throw new ArgumentException(
                    "Restored prepared-effect identities must be unique.",
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
        /// Atomically restores the mutable prepared-rule state persisted for a dungeon run.
        /// Catalog-derived modifiers and owned items remain untouched.
        /// </summary>
        /// <param name="rollOptions">
        /// The complete active roll-option set. Surrounding whitespace is removed before identity
        /// validation and storage.
        /// </param>
        /// <param name="effects">The complete active prepared-effect set.</param>
        public void RestorePersistentRuleState(
            IEnumerable<string> rollOptions,
            IEnumerable<ActivePf2eEffect> effects
        )
        {
            if (rollOptions == null)
                throw new ArgumentNullException(nameof(rollOptions));
            if (effects == null)
                throw new ArgumentNullException(nameof(effects));

            string[] copiedRollOptions = rollOptions.Select(option => option?.Trim()).ToArray();
            ActivePf2eEffect[] copiedEffects = effects.ToArray();
            if (copiedRollOptions.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException(
                    "Restored roll options cannot be blank.",
                    nameof(rollOptions)
                );
            if (
                copiedRollOptions.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != copiedRollOptions.Length
            )
                throw new ArgumentException(
                    "Restored roll options must be unique.",
                    nameof(rollOptions)
                );
            if (copiedEffects.Any(effect => effect == null))
                throw new ArgumentException(
                    "Restored prepared effects cannot contain null.",
                    nameof(effects)
                );
            if (
                copiedEffects
                    .Select(effect => effect.Slug + "\n" + effect.SourceSlug)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != copiedEffects.Length
            )
                throw new ArgumentException(
                    "Restored prepared-effect identities must be unique.",
                    nameof(effects)
                );

            RollOptions.Clear();
            foreach (string rollOption in copiedRollOptions)
                RollOptions.Add(rollOption);
            ActiveEffects.Clear();
            ActiveEffects.AddRange(copiedEffects);
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
        /// Gets the stable prepared-effect identity after its first persistence capture or restore.
        /// </summary>
        public string PersistentInstanceId { get; private set; }

        /// <summary>
        /// Creates an active effect entry with both runtime and source identifiers.
        /// </summary>
        /// <param name="name">The display name of the active effect.</param>
        /// <param name="slug">The runtime effect slug used for rule checks.</param>
        /// <param name="sourceSlug">The source item slug used for cleanup and predicate options.</param>
        /// <param name="persistentInstanceId">
        /// Stable restored instance identity, or empty for a newly activated effect.
        /// </param>
        public ActivePf2eEffect(
            string name,
            string slug,
            string sourceSlug,
            string persistentInstanceId = ""
        )
        {
            Name = name;
            Slug = slug;
            SourceSlug = sourceSlug;
            PersistentInstanceId = persistentInstanceId?.Trim() ?? string.Empty;
        }

        internal void EnsurePersistenceIdentity(string instanceId)
        {
            string normalized = instanceId?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
                throw new ArgumentException(
                    "A prepared-effect persistence identity is required.",
                    nameof(instanceId)
                );
            if (PersistentInstanceId.Length > 0 && PersistentInstanceId != normalized)
                throw new InvalidOperationException(
                    "A prepared-effect persistence identity cannot be replaced."
                );
            PersistentInstanceId = normalized;
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
