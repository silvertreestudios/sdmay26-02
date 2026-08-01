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
        internal IUnityCombatantStateContribution PrepareCombatant(
            CreatureId actor,
            CreatureComponent creature
        )
        {
            return new StrikeCombatantPreparation(this, Register(actor, creature));
        }

        /// <summary>Rolls back provisional Unity Strike mappings for a failed reinforcement join.</summary>
        /// <param name="actor">The reinforcement whose uncommitted mappings are removed.</param>
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
            if (snapshot.PreparedInputs.TryGet(target, out PreparedCreatureInputs targetInputs))
                offGuard |= targetInputs.StaticOptions.Any(option =>
                    option == "self:condition:flat-footed"
                    || option == "self:condition:offguard"
                    || option == "self:condition:off-guard"
                );
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

            List<TypedDamageDice> extraDice = new();
            if (
                attacker.TryGetComponent(out SpellEffectController spellEffects)
                && spellEffects.HasEffect<InfuseVitalitySpellEffect>()
            )
            {
                extraDice.Add(
                    new TypedDamageDice(new DiceExpression(1, 4), "vitality", "Infuse Vitality")
                );
            }

            return new StrikeResolutionData(
                // Validation rejects an invalid AC before costs. If Unity-side presentation state
                // changes after the action begins, keep resolution non-failing instead of turning
                // that late adapter change into a partially committed Strike.
                Math.Max(1, ResolveArmorClass(defender, targeting)),
                attackModifiers,
                extraDice,
                Array.Empty<TypedFlatDamage>(),
                Array.Empty<TypedDefenseAdjustment>(),
                Array.Empty<TypedDefenseAdjustment>()
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

        private StrikeCombatantRegistration Register(CreatureId actor, CreatureComponent creature)
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
                return new StrikeCombatantRegistration(actor, equipment, pools);
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
        /// Owns provisional Strike mappings and exposes the same typed state to initial seeding and
        /// reinforcement registration.
        /// </summary>
        private sealed class StrikeCombatantPreparation
            : IUnityCombatantStateContribution,
                IDisposable
        {
            private readonly UnityStrikeContext owner;
            private readonly StrikeCombatantRegistration registration;
            private bool isDisposed;

            internal StrikeCombatantPreparation(
                UnityStrikeContext owner,
                StrikeCombatantRegistration registration
            )
            {
                this.owner = owner;
                this.registration = registration;
            }

            /// <inheritdoc/>
            public void Seed(RulesStateSeed seed)
            {
                if (seed == null)
                    throw new ArgumentNullException(nameof(seed));
                foreach (EquipmentState item in registration.Equipment)
                    seed.SeedEquipment(item);
                foreach (AmmunitionState pool in registration.Ammunition)
                    seed.SeedAmmunition(pool);
            }

            /// <inheritdoc/>
            public void Register(UnityCombatRulesBridge bridge)
            {
                if (bridge == null)
                    throw new ArgumentNullException(nameof(bridge));
                OpResult<bool> result = bridge.Dispatch(
                    new RegisterStrikeCombatantOp(registration)
                );
                if (result is ResolvedOpResult<bool>)
                    return;
                if (result is InvalidOpResult<bool> invalid)
                    throw new InvalidOperationException(invalid.Reason);
                throw new InvalidOperationException(
                    "Strike combatant registration did not resolve."
                );
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (isDisposed)
                    return;
                isDisposed = true;
                owner.UnregisterCombatant(registration.Actor);
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
}
