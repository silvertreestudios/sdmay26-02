using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Rules;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity.Attack;
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
            IStrikePresentationCatalog,
            IFactObserver<AmmunitionSpentFact>,
            IFactObserver<StrikeItemLoadedChangedFact>
    {
        private readonly Dictionary<ItemId, StrikeItemDefinition> definitions = new();
        private readonly Dictionary<CreatureId, PreparedStrikeDefinition> preparedDefinitions =
            new();
        private readonly Dictionary<CreatureId, List<ItemId>> actorItems = new();
        private readonly Dictionary<ItemId, EquipmentWeapon> weapons = new();
        private readonly Dictionary<ItemId, CreatureComponent> itemOwners = new();
        private readonly Dictionary<ItemId, AmmunitionProjection> ammunition = new();
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private Tile[,] tiles;
        private readonly ICombatantFriendshipProvider friendships;

        /// <summary>
        /// Creates an empty encounter-owned Strike context. Combatants are added through the
        /// shared enrollment pipeline before their state commits.
        /// </summary>
        /// <param name="creatures">Stable rules-to-Unity creature mappings.</param>
        /// <param name="tiles">The current live grid used only by the targeting adapter.</param>
        /// <param name="friendships">Explicit encounter relationships for enrolled factions.</param>
        public UnityStrikeContext(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            Tile[,] tiles,
            ICombatantFriendshipProvider friendships
        )
        {
            this.friendships = friendships ?? throw new ArgumentNullException(nameof(friendships));
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
            preparedDefinitions.Remove(actor);
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
            if (!StrikeTargetingRules.IsEnemy(snapshot, actor, target, friendships))
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
                    snapshot,
                    actor,
                    target,
                    creatures,
                    tiles,
                    Math.Max(5, item.ReachFeet)
                );
            offGuard |= OffGuardRules.IsOffGuard(snapshot, target);
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
            return
                snapshot.Statistics.TryGet(target, out CreatureStatisticsState statistics)
                && statistics.ArmorClass > 0
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
            CreatureComponent defender = RequireCreature(target);
            PreparedStrikeContributions prepared = PreparedStrikeRules.Evaluate(
                preparedDefinitions[actor],
                item,
                targeting,
                snapshot,
                actor,
                target
            );
            if (!snapshot.Statistics.TryGet(target, out CreatureStatisticsState statistics))
                throw new InvalidOperationException(
                    $"Creature '{target.Value}' has no enrolled statistics."
                );
            return new StrikeResolutionData(
                // The rules slice owns the untyped base AC. Live targeting contributes cover and
                // off-guard as typed candidates below so same-type stacking is resolved once.
                Math.Max(1, statistics.ArmorClass),
                StrikeTargetingRules.ArmorClassModifiers(targeting),
                Array.Empty<Modifier>(),
                prepared.DamageDice,
                prepared.FlatDamage,
                UnityAttackDataAdapter.CaptureWeaknesses(defender),
                UnityAttackDataAdapter.CaptureResistances(defender)
            );
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            AmmunitionSpentFact fact,
            OpId rootId,
            RulesSnapshot currentSnapshot
        )
        {
            if (ammunition.TryGetValue(fact.Item, out AmmunitionProjection projection))
                projection.Creature.SetAmmoQuantity(projection.AmmoName, fact.Remaining);
        }

        /// <inheritdoc/>
        public void OnFactCommitted(
            StrikeItemLoadedChangedFact fact,
            OpId rootId,
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
                preparedDefinitions.Add(actor, UnityPreparedStrikeDataAdapter.Capture(creature));
                ItemId unarmedId = ItemIdFor(actor, "unarmed");
                StrikeItemDefinition unarmed = PreparedStrikeRules.CreateUnarmed(
                    unarmedId,
                    (creature.passives ?? new List<string>()).Select(CreatureSlug.FromName),
                    creature.attackBonus,
                    creature.strMod
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
                    StrikeItemDefinition definition = PreparedStrikeRules.CreateWeapon(
                        itemId,
                        new ItemDefinitionId(slug),
                        weapon.name,
                        weapon.group,
                        weapon.category,
                        (weapon.traits ?? new List<string>())
                            .Where(trait => !string.IsNullOrWhiteSpace(trait))
                            .Select(Trait.FromSlug),
                        creature.GetAttackBonusForWeapon(weapon),
                        new TypedDamageDice(
                            new DiceExpression(
                                weapon.damage.numberOfDice,
                                weapon.damage.sidesPerDie
                            ),
                            weapon.damage.damageType,
                            weapon.name
                        ),
                        creature.damageBonus,
                        weapon.range,
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

    /// <summary>Copies prepared definitions and ability values without evaluating contextual rules.</summary>
    internal static class UnityPreparedStrikeDataAdapter
    {
        internal static PreparedStrikeDefinition Capture(CreatureComponent creature)
        {
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);
            return new PreparedStrikeDefinition(
                prepared.RollOptions,
                new Dictionary<string, int>
                {
                    ["str"] = creature.strMod,
                    ["dex"] = creature.dexMod,
                    ["con"] = creature.conMod,
                    ["int"] = creature.intMod,
                    ["wis"] = creature.wisMod,
                    ["cha"] = creature.chaMod,
                },
                prepared.Modifiers.Select(value => new PreparedStrikeModifier(
                    value.Selector,
                    value.Slug,
                    value.Value,
                    value.Ability ?? string.Empty,
                    Pf2ePredicate.Compile(value.Predicate, prepared)
                )),
                prepared.Adjustments.Select(value => new PreparedStrikeAdjustment(
                    value.Selector,
                    value.Slug,
                    value.Mode,
                    value.Value,
                    value.Priority,
                    Pf2ePredicate.Compile(value.Predicate, prepared)
                )),
                prepared.DamageDice.Select(value => new PreparedStrikeDice(
                    value.Selector,
                    value.Category ?? "precision",
                    value.DiceNumber,
                    value.DieSize,
                    Pf2ePredicate.Compile(value.Predicate, prepared)
                )),
                prepared.ItemAlterations.Select(value => new PreparedStrikeAlteration(
                    value.ItemType,
                    value.Property,
                    value.Mode,
                    value.Value,
                    Pf2ePredicate.Compile(value.Predicate, prepared)
                ))
            );
        }
    }
}
