using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Combat.Rules;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
using Game.Rules.Unity.Composition;
using GridPrivate;
using GridPublic;
using UnityEngine;
using CreatureSlug = Game.Creature.Rules.Pf2eSlug;

namespace Game.Rules.Unity.Strike
{
    /// <summary>
    /// Owns encounter-stable Strike definitions, Unity identity maps, extraction, targeting, and
    /// projection adapters for one combat composition.
    /// </summary>
    public sealed class UnityStrikeContext
        : IStrikeActionCatalog,
            IStrikeTargetingProvider,
            IStrikeResolutionDataProvider,
            IFactObserver<AmmunitionSpentFact>,
            IFactObserver<StrikeItemLoadedChangedFact>
    {
        private readonly Dictionary<ItemId, StrikeItemDefinition> definitions = new();
        private readonly Dictionary<CreatureId, List<ItemId>> actorItems = new();
        private readonly Dictionary<ItemId, EquipmentWeapon> weapons = new();
        private readonly Dictionary<ItemId, CreatureComponent> itemOwners = new();
        private readonly Dictionary<ItemId, AmmunitionProjection> ammunition = new();
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private Tile[,] tiles;

        /// <summary>
        /// Creates an empty encounter-owned Strike context. Combatants are added through the
        /// shared enrollment pipeline before their state commits.
        /// </summary>
        /// <param name="creatures">Stable rules-to-Unity creature mappings.</param>
        /// <param name="tiles">The current live grid used only by the targeting adapter.</param>
        public UnityStrikeContext(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles
        )
        {
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        }

        /// <summary>Gets every Strike item registered for one creature.</summary>
        public IReadOnlyList<StrikeItemDefinition> GetItems(CreatureId actor)
        {
            if (!actorItems.TryGetValue(actor, out List<ItemId> items))
                return Array.Empty<StrikeItemDefinition>();
            return items.Select(item => definitions[item]).ToArray();
        }

        /// <summary>
        /// Prepares reversible Unity mappings and typed authoritative state for one combatant.
        /// </summary>
        internal IDisposable PrepareCombatant(
            CreatureId actor,
            CreatureComponent creature,
            out IReadOnlyList<EquipmentState> equipment,
            out IReadOnlyList<AmmunitionState> ammunition
        )
        {
            Register(actor, creature, out equipment, out ammunition);
            return new StrikeCombatantPreparation(this, actor);
        }

        /// <summary>Rolls back provisional Unity Strike mappings for a failed combatant addition.</summary>
        /// <param name="actor">The combatant whose uncommitted mappings are removed.</param>
        private void UnregisterCombatant(CreatureId actor)
        {
            if (!actorItems.TryGetValue(actor, out List<ItemId> items))
                return;
            foreach (ItemId item in items)
            {
                if (
                    definitions.TryGetValue(item, out StrikeItemDefinition definition)
                    && definition.Ammunition is RequiredStrikeAmmunitionRequirement required
                )
                    ammunition.Remove(required.Pool);
                definitions.Remove(item);
                itemOwners.Remove(item);
                weapons.Remove(item);
            }
            actorItems.Remove(actor);
        }

        /// <inheritdoc/>
        public StrikeItemDefinition GetStrikeItem(ItemId item)
        {
            if (!definitions.TryGetValue(item, out StrikeItemDefinition definition))
                throw new KeyNotFoundException($"Unknown Strike item '{item.Value}'.");
            return definition;
        }

        /// <summary>Gets the Unity weapon represented by an item, when it is not unarmed.</summary>
        public bool TryGetWeapon(ItemId item, out EquipmentWeapon weapon) =>
            weapons.TryGetValue(item, out weapon);

        /// <summary>Gets the Unity creature registered for a rules ID.</summary>
        public bool TryGetCreature(CreatureId creature, out CreatureComponent component) =>
            creatures.TryGetValue(creature, out component);

        /// <summary>Replaces the live grid boundary after encounter topology changes.</summary>
        public void ReplaceTiles(Tile[,] replacement) =>
            tiles = replacement ?? throw new ArgumentNullException(nameof(replacement));

        /// <inheritdoc/>
        public StrikeTargetingOutcome Evaluate(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target
        )
        {
            if (
                !creatures.TryGetValue(actor, out CreatureComponent attacker)
                || !creatures.TryGetValue(target, out CreatureComponent defender)
                || attacker == null
                || defender == null
            )
                return StrikeTargetingOutcome.Invalid("The selected creature is unavailable.");
            Team attackerTeam = attacker.GetComponent<Team>();
            Team defenderTeam = defender.GetComponent<Team>();
            bool hasTeamComponents = attackerTeam != null && defenderTeam != null;
            bool hasSameNamedTeam =
                hasTeamComponents
                && !string.IsNullOrWhiteSpace(attackerTeam.Name)
                && !string.IsNullOrWhiteSpace(defenderTeam.Name)
                && string.Equals(
                    attackerTeam.Name,
                    defenderTeam.Name,
                    StringComparison.OrdinalIgnoreCase
                );
            if (
                hasSameNamedTeam
                || (
                    hasTeamComponents
                    && TeamRules.TryGetInstance(out TeamRules teamRules)
                    && teamRules.Contains(attackerTeam.Name)
                    && teamRules.Contains(defenderTeam.Name)
                    && teamRules.IsFriendly(attackerTeam.Name, defenderTeam.Name)
                )
            )
                return StrikeTargetingOutcome.Invalid("The target is not a legal enemy.");

            StrikeTargetRequest request = new StrikeTargetRequest
            {
                ReachFeet = item.ReachFeet,
                RangeIncrementFeet = item.RangeIncrementFeet,
                IsRanged = item.IsRanged,
                RequiresLineOfEffect = true,
            };
            StrikeTargetResult result = GridPrivate.StrikeTargeting.Evaluate(
                attacker.gameObject,
                defender.gameObject,
                tiles,
                request
            );
            if (result == null)
                return StrikeTargetingOutcome.Invalid(
                    "The target is out of range or has no line of effect."
                );

            bool offGuard =
                !item.IsRanged
                && FlankingRule.IsFlanking(
                    attacker.gameObject,
                    defender.gameObject,
                    tiles,
                    Math.Max(5, item.ReachFeet)
                );
            Conditions conditions = defender.GetComponent<Conditions>();
            if (conditions != null)
            {
                offGuard |= conditions
                    .GetConditionNames()
                    .Select(CreatureSlug.FromName)
                    .Any(slug =>
                        string.Equals(slug, "flat-footed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(slug, "offguard", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(slug, "off-guard", StringComparison.OrdinalIgnoreCase)
                    );
            }
            return StrikeTargetingOutcome.Legal(
                result.DistanceFeet,
                result.RangePenalty,
                result.CoverAcBonus,
                offGuard
            );
        }

        /// <inheritdoc/>
        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target,
            LegalStrikeTargetingOutcome targeting
        )
        {
            if (!creatures.TryGetValue(target, out CreatureComponent defender) || defender == null)
                return ActionValidationResult.Invalid("The selected creature is unavailable.");
            return ResolveArmorClass(defender, targeting) > 0
                ? ActionValidationResult.Valid
                : ActionValidationResult.Invalid("The target's Armor Class must be positive.");
        }

        /// <inheritdoc/>
        public StrikeResolutionData Capture(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target,
            LegalStrikeTargetingOutcome targeting
        )
        {
            CreatureComponent attacker = RequireCreature(actor);
            CreatureComponent defender = RequireCreature(target);
            IReadOnlyList<Modifier> attackModifiers = UnityAttackDataAdapter.CaptureModifiers(
                attacker
            );

            PreparedStrikeContributions prepared = UnityPreparedStrikeDataAdapter.Capture(
                attacker,
                defender,
                item,
                targeting,
                snapshot,
                actor
            );
            if (
                attacker.TryGetComponent(out SpellEffectController spellEffects)
                && spellEffects.HasEffect<InfuseVitalitySpellEffect>()
            )
            {
                prepared.DamageDice.Add(
                    new TypedDamageDice(new DiceExpression(1, 4), "vitality", "Infuse Vitality")
                );
            }

            return new StrikeResolutionData(
                // Validation rejects an invalid AC before costs. If Unity-side presentation state
                // changes after the action begins, keep resolution non-failing instead of turning
                // that late adapter change into a partially committed Strike.
                Math.Max(1, ResolveArmorClass(defender, targeting)),
                attackModifiers,
                prepared.DamageDice,
                prepared.FlatDamage,
                UnityAttackDataAdapter.CaptureWeaknesses(defender),
                UnityAttackDataAdapter.CaptureResistances(defender)
            );
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(AmmunitionSpentFact fact, RulesSnapshot currentSnapshot)
        {
            if (ammunition.TryGetValue(fact.Item, out AmmunitionProjection projection))
                projection.Creature.SetAmmoQuantity(projection.AmmoName, fact.Remaining);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnFactCommitted(
            StrikeItemLoadedChangedFact fact,
            RulesSnapshot currentSnapshot
        )
        {
            if (
                itemOwners.TryGetValue(fact.Item, out CreatureComponent owner)
                && weapons.TryGetValue(fact.Item, out EquipmentWeapon weapon)
            )
            {
                owner.ProjectWeaponLoaded(weapon, fact.IsLoaded);
            }
            return default;
        }

        private void Register(
            CreatureId actor,
            CreatureComponent creature,
            out IReadOnlyList<EquipmentState> registeredEquipment,
            out IReadOnlyList<AmmunitionState> registeredAmmunition
        )
        {
            if (creature == null)
                throw new ArgumentException("A registered Strike creature cannot be null.");
            List<ItemId> items = new List<ItemId>();
            actorItems.Add(actor, items);
            List<EquipmentState> equipment = new List<EquipmentState>();
            List<AmmunitionState> pools = new List<AmmunitionState>();

            try
            {
                ItemId unarmedId = ItemIdFor(actor, "unarmed");
                int unarmedDie =
                    creature.passives != null
                    && creature.passives.Any(passive =>
                        string.Equals(
                            CreatureSlug.FromName(passive),
                            "zombie-fist",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        ? 6
                        : 3;
                StrikeItemDefinition unarmed = new StrikeItemDefinition(
                    unarmedId,
                    new ItemDefinitionId("unarmed"),
                    "Unarmed Strike",
                    "unarmed",
                    "unarmed",
                    new[]
                    {
                        Trait.FromSlug("agile"),
                        Trait.FromSlug("finesse"),
                        Trait.FromSlug("nonlethal"),
                        Trait.FromSlug("unarmed"),
                    },
                    creature.attackBonus,
                    new[]
                    {
                        new TypedDamageDice(
                            new DiceExpression(1, unarmedDie),
                            "bludgeoning",
                            "Unarmed Strike"
                        ),
                    },
                    new[] { new TypedFlatDamage(creature.strMod, "bludgeoning", "Strength") },
                    5,
                    0,
                    0,
                    StrikeAmmunitionRequirement.None
                );
                AddItem(actor, creature, unarmed, items, equipment, null);

                foreach (EquipmentWeapon weapon in EnumerateWeapons(creature))
                {
                    string slug = CreatureSlug.FromName(weapon.name);
                    ItemId itemId = ItemIdFor(actor, slug);
                    int reloadActions = creature.GetReloadCost(weapon);
                    StrikeAmmunitionRequirement ammoRequirement = string.IsNullOrWhiteSpace(
                        weapon.ammo
                    )
                        ? StrikeAmmunitionRequirement.None
                        : StrikeAmmunitionRequirement.Required(AmmunitionIdFor(actor, weapon.ammo));
                    List<TypedFlatDamage> flat = new List<TypedFlatDamage>();
                    if (weapon.range <= 0 || string.IsNullOrWhiteSpace(weapon.ammo))
                    {
                        flat.Add(
                            new TypedFlatDamage(
                                creature.damageBonus,
                                weapon.damage.damageType,
                                "Damage bonus"
                            )
                        );
                    }
                    StrikeItemDefinition definition = new StrikeItemDefinition(
                        itemId,
                        new ItemDefinitionId(slug),
                        weapon.name,
                        weapon.group,
                        weapon.category,
                        (weapon.traits ?? new List<string>())
                            .Where(trait => !string.IsNullOrWhiteSpace(trait))
                            .Select(Trait.FromSlug),
                        creature.GetAttackBonusForWeapon(weapon),
                        new[]
                        {
                            new TypedDamageDice(
                                new DiceExpression(
                                    weapon.damage.numberOfDice,
                                    weapon.damage.sidesPerDie
                                ),
                                weapon.damage.damageType,
                                weapon.name
                            ),
                        },
                        flat,
                        weapon.traits != null
                        && weapon.traits.Any(trait =>
                            string.Equals(trait, "reach", StringComparison.OrdinalIgnoreCase)
                        )
                            ? 10
                            : 5,
                        Math.Max(0, weapon.range),
                        reloadActions,
                        ammoRequirement
                    );
                    AddItem(actor, creature, definition, items, equipment, weapon);

                    if (ammoRequirement is RequiredStrikeAmmunitionRequirement required)
                    {
                        int quantity = creature.GetAmmoQuantity(weapon.ammo);
                        if (!ammunition.ContainsKey(required.Pool))
                        {
                            pools.Add(
                                new AmmunitionState(required.Pool, actor, Math.Max(0, quantity))
                            );
                            ammunition.Add(
                                required.Pool,
                                new AmmunitionProjection(creature, weapon.ammo)
                            );
                        }
                    }
                }
                registeredEquipment = Array.AsReadOnly(equipment.ToArray());
                registeredAmmunition = Array.AsReadOnly(pools.ToArray());
            }
            catch
            {
                UnregisterCombatant(actor);
                throw;
            }
        }

        private void AddItem(
            CreatureId actor,
            CreatureComponent creature,
            StrikeItemDefinition definition,
            ICollection<ItemId> items,
            ICollection<EquipmentState> equipment,
            EquipmentWeapon weapon
        )
        {
            definitions.Add(definition.Item, definition);
            items.Add(definition.Item);
            itemOwners.Add(definition.Item, creature);
            bool loaded = weapon == null || creature.IsWeaponLoaded(weapon);
            equipment.Add(
                new EquipmentState(definition.Item, definition.Definition, actor, true, loaded)
            );
            if (weapon != null)
                weapons.Add(definition.Item, weapon);
        }

        /// <summary>
        /// Owns provisional Strike mappings until enrollment transfers their cleanup lifetime.
        /// </summary>
        private sealed class StrikeCombatantPreparation : IDisposable
        {
            private readonly UnityStrikeContext owner;
            private readonly CreatureId actor;
            private bool isDisposed;

            internal StrikeCombatantPreparation(UnityStrikeContext owner, CreatureId actor)
            {
                this.owner = owner;
                this.actor = actor;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (isDisposed)
                    return;
                isDisposed = true;
                owner.UnregisterCombatant(actor);
            }
        }

        private CreatureComponent RequireCreature(CreatureId id)
        {
            if (!creatures.TryGetValue(id, out CreatureComponent creature) || creature == null)
                throw new InvalidOperationException($"Creature '{id.Value}' is unavailable.");
            return creature;
        }

        private static int ResolveArmorClass(
            CreatureComponent defender,
            LegalStrikeTargetingOutcome targeting
        )
        {
            List<Pf2eModifier> modifiers = new();
            if (targeting.CoverBonus != 0)
            {
                modifiers.Add(
                    new Pf2eModifier(
                        targeting.CoverBonus,
                        Pf2eModifierType.Circumstance,
                        "Cover",
                        Pf2eStatistic.ArmorClass
                    )
                );
            }
            if (targeting.OffGuard)
            {
                modifiers.Add(
                    new Pf2eModifier(
                        -2,
                        Pf2eModifierType.Circumstance,
                        "Off-guard",
                        Pf2eStatistic.ArmorClass
                    )
                );
            }
            return defender.ResolveArmorClass(modifiers).Total;
        }

        private static IEnumerable<EquipmentWeapon> EnumerateWeapons(CreatureComponent creature)
        {
            Dictionary<string, EquipmentWeapon> unique = new(StringComparer.OrdinalIgnoreCase);
            AddWeapon(unique, creature.equippedRightHand);
            AddWeapon(unique, creature.equippedLeftHand);
            foreach (EquipmentWeapon weapon in creature.weapons ?? new List<EquipmentWeapon>())
                AddWeapon(unique, weapon);
            foreach (string weaponName in creature.weaponsList ?? new List<string>())
                AddWeapon(unique, DataFileInterface.GetWeapon(weaponName));
            return unique.Values;
        }

        private static void AddWeapon(
            IDictionary<string, EquipmentWeapon> weapons,
            EquipmentWeapon weapon
        )
        {
            if (weapon == null || weapon.damage == null || string.IsNullOrWhiteSpace(weapon.name))
                return;
            string slug = CreatureSlug.FromName(weapon.name);
            if (!weapons.ContainsKey(slug))
                weapons.Add(slug, weapon);
        }

        private static ItemId ItemIdFor(CreatureId actor, string slug) =>
            new ItemId($"{actor.Value}-strike-{slug}");

        private static ItemId AmmunitionIdFor(CreatureId actor, string ammoName) =>
            new ItemId($"{actor.Value}-ammo-{CreatureSlug.FromName(ammoName)}");

        private sealed class AmmunitionProjection
        {
            public AmmunitionProjection(CreatureComponent creature, string ammoName)
            {
                Creature = creature;
                AmmoName = ammoName;
            }

            public CreatureComponent Creature { get; }
            public string AmmoName { get; }
        }
    }

    internal sealed class PreparedStrikeContributions
    {
        public List<TypedDamageDice> DamageDice { get; } = new();
        public List<TypedFlatDamage> FlatDamage { get; } = new();
    }

    internal static class UnityPreparedStrikeDataAdapter
    {
        public static PreparedStrikeContributions Capture(
            CreatureComponent attacker,
            CreatureComponent target,
            StrikeItemDefinition item,
            LegalStrikeTargetingOutcome targeting,
            RulesSnapshot snapshot,
            CreatureId actor
        )
        {
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(attacker);
            List<string> options = BuildOptions(prepared, target, item, targeting);
            foreach (string option in RageRules.GetActiveRollOptions(snapshot, actor))
                AddOption(options, option);
            PreparedStrikeContributions result = new PreparedStrikeContributions();

            List<RuleModifier> flatModifiers = prepared
                .Modifiers.Where(modifier =>
                    string.Equals(
                        modifier.Selector,
                        "strike-damage",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Where(modifier => Pf2ePredicate.Evaluate(modifier.Predicate, prepared, options))
                .GroupBy(modifier => modifier.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            foreach (
                RuleAdjustment adjustment in prepared
                    .Adjustments.Where(adjustment =>
                        string.Equals(
                            adjustment.Selector,
                            "strike-damage",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Where(adjustment =>
                        Pf2ePredicate.Evaluate(adjustment.Predicate, prepared, options)
                    )
                    .OrderBy(adjustment => adjustment.Priority)
            )
            {
                RuleModifier modifier = flatModifiers.LastOrDefault(candidate =>
                    string.Equals(
                        candidate.Slug,
                        adjustment.Slug,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                if (modifier == null)
                    continue;
                if (string.Equals(adjustment.Mode, "upgrade", StringComparison.OrdinalIgnoreCase))
                    modifier.Value = Math.Max(modifier.Value, Mathf.RoundToInt(adjustment.Value));
                else if (
                    string.Equals(adjustment.Mode, "multiply", StringComparison.OrdinalIgnoreCase)
                )
                    modifier.Value = Mathf.FloorToInt(modifier.Value * adjustment.Value);
            }

            string primaryType = item.DamageDice[0].DamageType;
            foreach (RuleModifier modifier in flatModifiers.Where(value => value.Value != 0))
                result.FlatDamage.Add(
                    new TypedFlatDamage(modifier.Value, primaryType, modifier.Slug)
                );

            if (!item.IsRanged)
            {
                RuleModifier ability = prepared.Modifiers.LastOrDefault(modifier =>
                    string.Equals(
                        modifier.Selector,
                        "melee-strike-damage",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !string.IsNullOrWhiteSpace(modifier.Ability)
                    && Pf2ePredicate.Evaluate(modifier.Predicate, prepared, options)
                );
                if (ability != null)
                {
                    int desired = GetAbilityModifier(attacker, ability.Ability);
                    int existing = item.FlatDamage.Count == 0 ? 0 : item.FlatDamage[0].Amount;
                    result.FlatDamage.Add(
                        new TypedFlatDamage(desired - existing, primaryType, ability.Ability)
                    );
                }
            }

            foreach (
                RuleDamageDice dice in prepared
                    .DamageDice.Where(value =>
                        string.Equals(
                            value.Selector,
                            "strike-damage",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Where(value => value.DiceNumber > 0 && value.DieSize > 0)
                    .Where(value => Pf2ePredicate.Evaluate(value.Predicate, prepared, options))
            )
            {
                result.DamageDice.Add(
                    new TypedDamageDice(
                        new DiceExpression(dice.DiceNumber, dice.DieSize),
                        dice.Category ?? "precision",
                        "Prepared damage dice"
                    )
                );
            }
            return result;
        }

        private static List<string> BuildOptions(
            PreparedCharacter prepared,
            CreatureComponent target,
            StrikeItemDefinition item,
            LegalStrikeTargetingOutcome targeting
        )
        {
            List<string> options = new List<string>();
            foreach (Trait trait in item.Traits)
            {
                AddOption(options, $"item:trait:{trait.Slug}");
                if (trait.Slug == "ranged")
                    AddOption(options, "item:ranged");
                if (trait.Slug.StartsWith("thrown", StringComparison.Ordinal))
                    AddOption(options, "item:thrown");
            }
            AddOption(options, $"item:slug:{item.Definition.Value}");
            if (!string.IsNullOrWhiteSpace(item.Category))
                AddOption(options, $"item:category:{item.Category}");
            AddOption(options, $"item:damage:die:faces:{item.DamageDice[0].Dice.Sides}");
            if (item.IsRanged)
                AddOption(options, "item:ranged");
            if (targeting.OffGuard)
                AddOption(options, "target:condition:off-guard");

            foreach (ItemAlterationRule alteration in prepared.ItemAlterations)
            {
                if (
                    string.Equals(alteration.ItemType, "weapon", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        alteration.Property,
                        "other-tags",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(alteration.Mode, "add", StringComparison.OrdinalIgnoreCase)
                    && Pf2ePredicate.Evaluate(alteration.Predicate, prepared, options)
                )
                    AddOption(options, $"item:tag:{alteration.Value}");
            }
            Conditions conditions = target.GetComponent<Conditions>();
            if (conditions != null)
            {
                foreach (string condition in conditions.GetConditionNames())
                {
                    string slug = CreatureSlug.FromName(condition);
                    if (!string.IsNullOrWhiteSpace(slug))
                        AddOption(options, $"target:condition:{slug}");
                }
            }
            return options;
        }

        private static void AddOption(ICollection<string> options, string option)
        {
            if (!options.Contains(option, StringComparer.OrdinalIgnoreCase))
                options.Add(option);
        }

        private static int GetAbilityModifier(CreatureComponent creature, string ability) =>
            ability?.ToLowerInvariant() switch
            {
                "str" => creature.strMod,
                "dex" => creature.dexMod,
                "con" => creature.conMod,
                "int" => creature.intMod,
                "wis" => creature.wisMod,
                "cha" => creature.chaMod,
                _ => 0,
            };
    }
}
