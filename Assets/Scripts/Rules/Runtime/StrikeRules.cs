using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Rules.Runtime
{
    /// <summary>Describes whether a Strike consumes an ammunition pool.</summary>
    public abstract class StrikeAmmunitionRequirement
    {
        private protected StrikeAmmunitionRequirement() { }

        /// <summary>Gets the shared value for attacks that do not consume ammunition.</summary>
        public static StrikeAmmunitionRequirement None { get; } =
            new NoStrikeAmmunitionRequirement();

        /// <summary>Creates a requirement for one authoritative ammunition pool.</summary>
        /// <param name="pool">The ammunition pool spent when the Strike begins.</param>
        /// <returns>A structural ammunition requirement.</returns>
        public static StrikeAmmunitionRequirement Required(ItemId pool) =>
            new RequiredStrikeAmmunitionRequirement(pool);
    }

    /// <summary>Represents a Strike with no ammunition cost.</summary>
    public sealed class NoStrikeAmmunitionRequirement : StrikeAmmunitionRequirement
    {
        internal NoStrikeAmmunitionRequirement() { }
    }

    /// <summary>Represents a Strike that spends one unit from a specific ammunition pool.</summary>
    public sealed class RequiredStrikeAmmunitionRequirement : StrikeAmmunitionRequirement
    {
        internal RequiredStrikeAmmunitionRequirement(ItemId pool)
        {
            if (pool.IsEmpty)
                throw new ArgumentException("An ammunition pool is required.", nameof(pool));
            Pool = pool;
        }

        /// <summary>Gets the authoritative ammunition pool.</summary>
        public ItemId Pool { get; }
    }

    /// <summary>Defines one encounter-stable weapon or unarmed Strike entry.</summary>
    public sealed class StrikeItemDefinition
    {
        private readonly IReadOnlyList<Trait> traits;
        private readonly IReadOnlyList<TypedDamageDice> damageDice;
        private readonly IReadOnlyList<TypedFlatDamage> flatDamage;

        /// <summary>Creates a complete immutable Strike item definition.</summary>
        public StrikeItemDefinition(
            ItemId item,
            ItemDefinitionId definition,
            string label,
            string group,
            string category,
            IEnumerable<Trait> traits,
            int attackModifier,
            IEnumerable<TypedDamageDice> damageDice,
            IEnumerable<TypedFlatDamage> flatDamage,
            int reachFeet,
            int rangeIncrementFeet,
            int reloadActions,
            StrikeAmmunitionRequirement ammunition
        )
        {
            if (item.IsEmpty)
                throw new ArgumentException("A Strike item ID is required.", nameof(item));
            if (definition.IsEmpty)
                throw new ArgumentException(
                    "A Strike item definition ID is required.",
                    nameof(definition)
                );
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("A Strike label is required.", nameof(label));
            if (traits == null)
                throw new ArgumentNullException(nameof(traits));
            if (damageDice == null)
                throw new ArgumentNullException(nameof(damageDice));
            if (flatDamage == null)
                throw new ArgumentNullException(nameof(flatDamage));
            if (reachFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(reachFeet));
            if (rangeIncrementFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(rangeIncrementFeet));
            if (reloadActions < 0 || reloadActions > 3)
                throw new ArgumentOutOfRangeException(nameof(reloadActions));

            Trait[] copiedTraits = traits.Distinct().ToArray();
            if (copiedTraits.Any(trait => trait.IsEmpty))
                throw new ArgumentException("Strike traits cannot be empty.", nameof(traits));
            TypedDamageDice[] copiedDice = damageDice.ToArray();
            TypedFlatDamage[] copiedFlats = flatDamage.ToArray();
            if (copiedDice.Any(component => component == null))
                throw new ArgumentException("Strike dice cannot contain null.", nameof(damageDice));
            if (copiedFlats.Any(component => component == null))
                throw new ArgumentException(
                    "Strike flat damage cannot contain null.",
                    nameof(flatDamage)
                );
            if (copiedDice.Length == 0)
                throw new ArgumentException(
                    "A Strike requires base damage dice.",
                    nameof(damageDice)
                );

            Item = item;
            Definition = definition;
            Label = label.Trim();
            Group = group?.Trim() ?? string.Empty;
            Category = category?.Trim() ?? string.Empty;
            this.traits = Array.AsReadOnly(copiedTraits);
            AttackModifier = attackModifier;
            this.damageDice = Array.AsReadOnly(copiedDice);
            this.flatDamage = Array.AsReadOnly(copiedFlats);
            ReachFeet = reachFeet;
            RangeIncrementFeet = rangeIncrementFeet;
            ReloadActions = reloadActions;
            Ammunition = ammunition ?? throw new ArgumentNullException(nameof(ammunition));
        }

        /// <summary>Gets the encounter-stable selected item.</summary>
        public ItemId Item { get; }

        /// <summary>Gets the stable content definition.</summary>
        public ItemDefinitionId Definition { get; }

        /// <summary>Gets the player-facing item label.</summary>
        public string Label { get; }

        /// <summary>Gets the weapon group, or an empty value for unarmed attacks.</summary>
        public string Group { get; }

        /// <summary>Gets the weapon category used by prepared rule predicates.</summary>
        public string Category { get; }

        /// <summary>Gets the immutable item traits.</summary>
        public IReadOnlyList<Trait> Traits => traits;

        /// <summary>Gets the weapon-specific base attack modifier.</summary>
        public int AttackModifier { get; }

        /// <summary>Gets the base damage dice.</summary>
        public IReadOnlyList<TypedDamageDice> DamageDice => damageDice;

        /// <summary>Gets the base flat damage.</summary>
        public IReadOnlyList<TypedFlatDamage> FlatDamage => flatDamage;

        /// <summary>Gets melee reach in feet.</summary>
        public int ReachFeet { get; }

        /// <summary>Gets the ranged increment in feet, or zero for melee.</summary>
        public int RangeIncrementFeet { get; }

        /// <summary>Gets the number of actions required to reload, or zero.</summary>
        public int ReloadActions { get; }

        /// <summary>Gets the structural ammunition requirement.</summary>
        public StrikeAmmunitionRequirement Ammunition { get; }

        /// <summary>Gets whether this is a ranged Strike.</summary>
        public bool IsRanged => RangeIncrementFeet > 0;

        /// <summary>Gets whether this Strike uses agile MAP.</summary>
        public bool IsAgile => Traits.Contains(Trait.FromSlug("agile"));

        /// <summary>Gets the mean base damage used by tactical selection.</summary>
        public double AverageDamage =>
            damageDice.Sum(component => component.Dice.Count * (component.Dice.Sides + 1) / 2.0)
            + flatDamage.Sum(component => component.Amount);
    }

    /// <summary>Supplies immutable Strike definitions to rules and Unity adapters.</summary>
    public interface IStrikeActionCatalog
    {
        /// <summary>Gets one selected item definition.</summary>
        StrikeItemDefinition GetStrikeItem(ItemId item);
    }

    /// <summary>Contains an authoritative target evaluation recomputed immediately before costs.</summary>
    public abstract class StrikeTargetingOutcome
    {
        private protected StrikeTargetingOutcome() { }

        /// <summary>Creates an invalid target result.</summary>
        public static InvalidStrikeTargetingOutcome Invalid(string reason) =>
            new InvalidStrikeTargetingOutcome(reason);

        /// <summary>Creates a legal target result.</summary>
        public static LegalStrikeTargetingOutcome Legal(
            int distanceFeet,
            int rangePenalty,
            int coverBonus,
            bool offGuard
        ) => new LegalStrikeTargetingOutcome(distanceFeet, rangePenalty, coverBonus, offGuard);
    }

    /// <summary>Explains why a selected target is currently illegal.</summary>
    public sealed class InvalidStrikeTargetingOutcome : StrikeTargetingOutcome
    {
        internal InvalidStrikeTargetingOutcome(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "A targeting failure reason is required.",
                    nameof(reason)
                );
            Reason = reason.Trim();
        }

        /// <summary>Gets the failure reason.</summary>
        public string Reason { get; }
    }

    /// <summary>Contains current range, cover, and off-guard facts for a legal target.</summary>
    public sealed class LegalStrikeTargetingOutcome : StrikeTargetingOutcome
    {
        internal LegalStrikeTargetingOutcome(
            int distanceFeet,
            int rangePenalty,
            int coverBonus,
            bool offGuard
        )
        {
            if (distanceFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(distanceFeet));
            if (rangePenalty > 0)
                throw new ArgumentOutOfRangeException(nameof(rangePenalty));
            if (coverBonus < 0)
                throw new ArgumentOutOfRangeException(nameof(coverBonus));
            DistanceFeet = distanceFeet;
            RangePenalty = rangePenalty;
            CoverBonus = coverBonus;
            OffGuard = offGuard;
        }

        /// <summary>Gets grid distance in feet.</summary>
        public int DistanceFeet { get; }

        /// <summary>Gets the non-positive ranged increment penalty.</summary>
        public int RangePenalty { get; }

        /// <summary>Gets the cover circumstance bonus to AC.</summary>
        public int CoverBonus { get; }

        /// <summary>Gets whether the target is off-guard to this Strike.</summary>
        public bool OffGuard { get; }
    }

    /// <summary>Recomputes Strike geometry and allegiance without making Unity authoritative.</summary>
    public interface IStrikeTargetingProvider
    {
        /// <summary>Evaluates the selected target against current encounter state.</summary>
        StrikeTargetingOutcome Evaluate(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target
        );
    }

    /// <summary>Contains immutable Unity-extracted values needed by pure Strike calculation.</summary>
    public sealed class StrikeResolutionData
    {
        private readonly IReadOnlyList<Modifier> attackModifiers;
        private readonly IReadOnlyList<TypedDamageDice> damageDice;
        private readonly IReadOnlyList<TypedFlatDamage> flatDamage;
        private readonly IReadOnlyList<TypedDamageImmunity> immunities;
        private readonly IReadOnlyList<TypedDefenseAdjustment> weaknesses;
        private readonly IReadOnlyList<TypedDefenseAdjustment> resistances;

        /// <summary>Creates one frozen resolution-data snapshot.</summary>
        /// <param name="armorClass">The target Armor Class for this resolution.</param>
        /// <param name="attackModifiers">Current non-item attack modifiers.</param>
        /// <param name="damageDice">Additional typed damage dice.</param>
        /// <param name="flatDamage">Additional typed flat damage.</param>
        /// <param name="immunities">Target damage-type immunities.</param>
        /// <param name="weaknesses">Target typed weaknesses.</param>
        /// <param name="resistances">Target typed resistances.</param>
        public StrikeResolutionData(
            int armorClass,
            IEnumerable<Modifier> attackModifiers,
            IEnumerable<TypedDamageDice> damageDice,
            IEnumerable<TypedFlatDamage> flatDamage,
            IEnumerable<TypedDamageImmunity> immunities,
            IEnumerable<TypedDefenseAdjustment> weaknesses,
            IEnumerable<TypedDefenseAdjustment> resistances
        )
        {
            if (armorClass <= 0)
                throw new ArgumentOutOfRangeException(nameof(armorClass));
            ArmorClass = armorClass;
            if (attackModifiers == null)
                throw new ArgumentNullException(nameof(attackModifiers));
            Modifier[] copiedModifiers = attackModifiers.ToArray();
            if (copiedModifiers.Any(modifier => modifier.IsEmpty))
                throw new ArgumentException(
                    "Attack modifiers cannot contain empty values.",
                    nameof(attackModifiers)
                );
            this.attackModifiers = Array.AsReadOnly(copiedModifiers);
            this.damageDice = Copy(damageDice, nameof(damageDice));
            this.flatDamage = Copy(flatDamage, nameof(flatDamage));
            this.immunities = Copy(immunities, nameof(immunities));
            this.weaknesses = Copy(weaknesses, nameof(weaknesses));
            this.resistances = Copy(resistances, nameof(resistances));
        }

        /// <summary>
        /// Gets target AC after the resolution provider applies Strike-specific cover and
        /// off-guard modifiers.
        /// </summary>
        public int ArmorClass { get; }

        /// <summary>Gets current actor modifiers excluding the item base, MAP, and range.</summary>
        public IReadOnlyList<Modifier> AttackModifiers => attackModifiers;

        /// <summary>Gets prepared extra damage dice.</summary>
        public IReadOnlyList<TypedDamageDice> DamageDice => damageDice;

        /// <summary>Gets prepared flat damage contributions.</summary>
        public IReadOnlyList<TypedFlatDamage> FlatDamage => flatDamage;

        /// <summary>Gets target damage-type immunities.</summary>
        public IReadOnlyList<TypedDamageImmunity> Immunities => immunities;

        /// <summary>Gets target weaknesses.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Weaknesses => weaknesses;

        /// <summary>Gets target resistances.</summary>
        public IReadOnlyList<TypedDefenseAdjustment> Resistances => resistances;

        internal StrikeResolutionData WithPreparedContributions(
            IEnumerable<TypedDamageDice> extraDice,
            IEnumerable<TypedFlatDamage> extraFlat,
            IEnumerable<TypedDamageImmunity> preparedImmunities,
            IEnumerable<TypedDefenseAdjustment> preparedWeaknesses,
            IEnumerable<TypedDefenseAdjustment> preparedResistances
        ) =>
            new(
                ArmorClass,
                AttackModifiers,
                DamageDice.Concat(extraDice ?? throw new ArgumentNullException(nameof(extraDice))),
                FlatDamage.Concat(extraFlat ?? throw new ArgumentNullException(nameof(extraFlat))),
                Immunities.Concat(
                    preparedImmunities
                        ?? throw new ArgumentNullException(nameof(preparedImmunities))
                ),
                Weaknesses.Concat(
                    preparedWeaknesses
                        ?? throw new ArgumentNullException(nameof(preparedWeaknesses))
                ),
                Resistances.Concat(
                    preparedResistances
                        ?? throw new ArgumentNullException(nameof(preparedResistances))
                )
            );

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            T[] copied = values.ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException("Values cannot contain null.", parameterName);
            return Array.AsReadOnly(copied);
        }
    }

    /// <summary>
    /// Validates and freezes prepared character, effect, and defense values for one legal Strike.
    /// </summary>
    public interface IStrikeResolutionDataProvider
    {
        /// <summary>
        /// Validates every resolution-data invariant that can reject the Strike before its costs
        /// commit.
        /// </summary>
        ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target,
            LegalStrikeTargetingOutcome targeting
        );

        /// <summary>
        /// Captures immutable calculation inputs after <see cref="Validate"/> has accepted them.
        /// </summary>
        StrikeResolutionData Capture(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item,
            CreatureId target,
            LegalStrikeTargetingOutcome targeting
        );
    }

    /// <summary>Contains the deterministic attack and damage result for one resolved Strike.</summary>
    public sealed class StrikeResolution
    {
        internal StrikeResolution(
            RollResult attackRoll,
            int attackModifier,
            int multipleAttackPenalty,
            int rangePenalty,
            int armorClass,
            int coverBonus,
            bool offGuard,
            DegreeOfSuccess degree,
            IReadOnlyList<TypedDamagePart> damage,
            int finalDamage
        )
        {
            AttackRoll = attackRoll;
            AttackModifier = attackModifier;
            MultipleAttackPenalty = multipleAttackPenalty;
            RangePenalty = rangePenalty;
            ArmorClass = armorClass;
            CoverBonus = coverBonus;
            OffGuard = offGuard;
            Degree = degree;
            Damage = damage;
            FinalDamage = finalDamage;
        }

        /// <summary>Gets the deterministic d20 result.</summary>
        public RollResult AttackRoll { get; }

        /// <summary>Gets the final signed attack modifier.</summary>
        public int AttackModifier { get; }

        /// <summary>Gets the signed MAP contribution.</summary>
        public int MultipleAttackPenalty { get; }

        /// <summary>Gets the signed range contribution.</summary>
        public int RangePenalty { get; }

        /// <summary>Gets the final target AC.</summary>
        public int ArmorClass { get; }

        /// <summary>Gets the cover bonus included in AC.</summary>
        public int CoverBonus { get; }

        /// <summary>Gets whether off-guard applied.</summary>
        public bool OffGuard { get; }

        /// <summary>Gets the final degree of success.</summary>
        public DegreeOfSuccess Degree { get; }

        /// <summary>Gets final damage by type after defenses.</summary>
        public IReadOnlyList<TypedDamagePart> Damage { get; }

        /// <summary>Gets final damage submitted to the sole HP write path.</summary>
        public int FinalDamage { get; }

        /// <summary>Gets whether the attack hit.</summary>
        public bool Hit =>
            Degree == DegreeOfSuccess.Success || Degree == DegreeOfSuccess.CriticalSuccess;
    }

    /// <summary>Represents one complete normal Strike selected by player or AI.</summary>
    public sealed class StrikeActionOp : ActionOp<StrikeResolution>
    {
        /// <summary>Creates a normal Strike root.</summary>
        public StrikeActionOp(CreatureId actor, ItemId item, CreatureId target)
            : base(actor, StrikeActionDefinition.DefinitionId)
        {
            if (item.IsEmpty)
                throw new ArgumentException("A Strike item is required.", nameof(item));
            if (target.IsEmpty)
                throw new ArgumentException("A Strike target is required.", nameof(target));
            Item = item;
            Target = target;
        }

        /// <summary>Gets the selected item.</summary>
        public ItemId Item { get; }

        /// <summary>Gets the selected creature target.</summary>
        public CreatureId Target { get; }

        /// <inheritdoc/>
        public override ActionProfile GetBaseProfile(IActionCatalog catalog)
        {
            if (catalog is not IStrikeActionCatalog strikeCatalog)
                throw new InvalidOperationException(
                    "Strike requires an action catalog that exposes Strike definitions."
                );
            StrikeItemDefinition item = strikeCatalog.GetStrikeItem(Item);
            List<Trait> traits = item.Traits.Concat(new[] { Trait.FromSlug("attack") }).ToList();
            return item.Ammunition is RequiredStrikeAmmunitionRequirement required
                ? ActionProfile.OneAction(traits, new[] { RuleCost.Ammunition(required.Pool) })
                : ActionProfile.OneAction(traits);
        }
    }

    /// <summary>Resolves the nested targeting, roll, degree, and damage calculation for a Strike.</summary>
    /// <remarks>
    /// This operation is nested-only. A validated <see cref="StrikeActionOp"/> dispatches it after
    /// action costs commit, and the dispatcher observes its resolved value before parent
    /// continuation can apply authoritative damage.
    /// </remarks>
    public sealed class ResolveStrikeOp : IRuleOp<StrikeResolution>
    {
        /// <summary>Creates the calculation request for one already-begun Strike.</summary>
        /// <param name="actor">The attacking creature.</param>
        /// <param name="item">The selected Strike item.</param>
        /// <param name="target">The selected target creature.</param>
        public ResolveStrikeOp(CreatureId actor, ItemId item, CreatureId target)
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Strike actor is required.", nameof(actor));
            if (item.IsEmpty)
                throw new ArgumentException("A Strike item is required.", nameof(item));
            if (target.IsEmpty)
                throw new ArgumentException("A Strike target is required.", nameof(target));
            Actor = actor;
            Item = item;
            Target = target;
        }

        /// <summary>Gets the attacking creature.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the selected Strike item.</summary>
        public ItemId Item { get; }

        /// <summary>Gets the selected target creature.</summary>
        public CreatureId Target { get; }
    }

    /// <summary>Owns normal Strike availability and stable action identity.</summary>
    public sealed class StrikeActionDefinition
    {
        /// <summary>Gets Strike's stable action definition ID.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("strike");

        /// <summary>Gets current availability for one actor/item entry.</summary>
        public ActionAvailability GetAvailability(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (!snapshot.Creatures.Contains(actor))
                return ActionAvailability.Unavailable("The actor is not registered.");
            if (!snapshot.Health.TryGet(actor, out HealthState health) || health.Current == 0)
                return ActionAvailability.Unavailable("The actor cannot act.");
            if (
                !snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < 1
            )
                return ActionAvailability.Unavailable("The actor does not have an action.");
            if (!snapshot.MultipleAttackPenalty.Contains(actor))
                return ActionAvailability.Unavailable(
                    "The actor has no multiple-attack-penalty state."
                );
            if (
                !snapshot.Equipment.TryGet(item.Item, out EquipmentState equipment)
                || equipment.Holder != actor
                || !equipment.IsWielded
            )
                return ActionAvailability.Unavailable("The Strike item is not wielded.");
            if (item.ReloadActions > 0 && !equipment.IsLoaded)
                return ActionAvailability.Unavailable("The weapon is not loaded.");
            if (
                item.Ammunition is RequiredStrikeAmmunitionRequirement required
                && (
                    !snapshot.Ammunition.TryGet(required.Pool, out AmmunitionState ammunition)
                    || ammunition.Owner != actor
                    || ammunition.Remaining == 0
                )
            )
                return ActionAvailability.Unavailable("The required ammunition is unavailable.");
            return ActionAvailability.Available;
        }

        /// <summary>
        /// Checks whether one fully selected Strike can legally begin without changing rules state.
        /// </summary>
        /// <param name="snapshot">The current rules snapshot to inspect.</param>
        /// <param name="operation">The actor, item, and target selected for the Strike.</param>
        /// <param name="catalog">The feature catalog that owns the selected item.</param>
        /// <param name="targeting">The feature targeting provider for current geometry.</param>
        /// <param name="resolutionData">
        /// The feature provider that validates prepared character and defense inputs.
        /// </param>
        /// <returns>A valid result or the first reason the Strike cannot legally begin.</returns>
        /// <remarks>
        /// Presentation may call this method immediately before starting an attack animation. The
        /// action lifecycle calls the same method again as the authoritative validator before it
        /// commits costs, so preview acceptance never grants permission or bypasses dispatch.
        /// </remarks>
        public ActionValidationResult Validate(
            RulesSnapshot snapshot,
            StrikeActionOp operation,
            IStrikeActionCatalog catalog,
            IStrikeTargetingProvider targeting,
            IStrikeResolutionDataProvider resolutionData
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (targeting == null)
                throw new ArgumentNullException(nameof(targeting));
            if (resolutionData == null)
                throw new ArgumentNullException(nameof(resolutionData));

            StrikeItemDefinition item;
            try
            {
                item = catalog.GetStrikeItem(operation.Item);
            }
            catch (KeyNotFoundException)
            {
                return ActionValidationResult.Invalid("The selected Strike item is unknown.");
            }
            ActionAvailability availability = GetAvailability(snapshot, operation.Actor, item);
            if (availability is UnavailableActionAvailability unavailable)
                return ActionValidationResult.Invalid(unavailable.Reason);
            if (
                !snapshot.Creatures.TryGet(operation.Actor, out CreatureState actor)
                || !snapshot.Creatures.TryGet(operation.Target, out CreatureState target)
                || actor.Player == target.Player
            )
                return ActionValidationResult.Invalid("The target is not a legal enemy.");
            if (
                !snapshot.Health.TryGet(operation.Target, out HealthState health)
                || health.Current == 0
            )
                return ActionValidationResult.Invalid("The target is defeated.");
            StrikeTargetingOutcome result = targeting.Evaluate(
                snapshot,
                operation.Actor,
                item,
                operation.Target
            );
            if (result is InvalidStrikeTargetingOutcome invalid)
                return ActionValidationResult.Invalid(invalid.Reason);
            return resolutionData.Validate(
                snapshot,
                operation.Actor,
                item,
                operation.Target,
                (LegalStrikeTargetingOutcome)result
            );
        }
    }

    /// <summary>Reports a committed loaded-state transition.</summary>
    public sealed class StrikeItemLoadedChangedFact : RuleFact
    {
        internal StrikeItemLoadedChangedFact(CreatureId actor, ItemId item, bool isLoaded)
        {
            Actor = actor;
            Item = item;
            IsLoaded = isLoaded;
        }

        /// <summary>Gets the item holder.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the changed item.</summary>
        public ItemId Item { get; }

        /// <summary>Gets the committed loaded state.</summary>
        public bool IsLoaded { get; }
    }

    internal sealed class SetStrikeItemLoadedOp : IRuleOp<EquipmentState>
    {
        public SetStrikeItemLoadedOp(CreatureId actor, ItemId item, bool isLoaded)
        {
            Actor = actor;
            Item = item;
            IsLoaded = isLoaded;
        }

        public CreatureId Actor { get; }
        public ItemId Item { get; }
        public bool IsLoaded { get; }
    }

    /// <summary>Contains Strike-owned state added for one encounter reinforcement.</summary>
    public sealed class StrikeCombatantRegistration
    {
        private readonly IReadOnlyList<EquipmentState> equipment;
        private readonly IReadOnlyList<AmmunitionState> ammunition;

        /// <summary>Creates the complete Strike state for one already-registered combatant.</summary>
        public StrikeCombatantRegistration(
            CreatureId actor,
            IEnumerable<EquipmentState> equipment,
            IEnumerable<AmmunitionState> ammunition
        )
        {
            if (actor.IsEmpty)
                throw new ArgumentException("A Strike combatant is required.", nameof(actor));
            if (equipment == null)
                throw new ArgumentNullException(nameof(equipment));
            if (ammunition == null)
                throw new ArgumentNullException(nameof(ammunition));
            EquipmentState[] copiedEquipment = equipment.ToArray();
            AmmunitionState[] copiedAmmunition = ammunition.ToArray();
            if (copiedEquipment.Any(item => item == null || item.Holder != actor))
                throw new ArgumentException(
                    "Every Strike item must belong to the registered actor.",
                    nameof(equipment)
                );
            if (copiedAmmunition.Any(pool => pool.Owner != actor))
                throw new ArgumentException(
                    "Every ammunition pool must belong to the registered actor.",
                    nameof(ammunition)
                );
            if (
                copiedEquipment.Select(item => item.Id).Distinct().Count() != copiedEquipment.Length
            )
                throw new ArgumentException("Strike item IDs must be unique.", nameof(equipment));
            if (
                copiedAmmunition.Select(pool => pool.Item).Distinct().Count()
                != copiedAmmunition.Length
            )
                throw new ArgumentException(
                    "Strike ammunition pool IDs must be unique.",
                    nameof(ammunition)
                );
            Actor = actor;
            this.equipment = Array.AsReadOnly(copiedEquipment);
            this.ammunition = Array.AsReadOnly(copiedAmmunition);
        }

        /// <summary>Gets the registered actor.</summary>
        public CreatureId Actor { get; }

        /// <summary>Gets the actor's Strike equipment.</summary>
        public IReadOnlyList<EquipmentState> Equipment => equipment;

        /// <summary>Gets the actor's ammunition pools.</summary>
        public IReadOnlyList<AmmunitionState> Ammunition => ammunition;
    }

    /// <summary>Adds Strike-owned state after generic reinforcement registration.</summary>
    public sealed class RegisterStrikeCombatantOp : IRuleOp<bool>
    {
        /// <summary>Creates a feature registration request.</summary>
        public RegisterStrikeCombatantOp(StrikeCombatantRegistration registration) =>
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));

        /// <summary>Gets the complete Strike state being registered.</summary>
        public StrikeCombatantRegistration Registration { get; }
    }

    /// <summary>Records installation of the feature-owned Strike state for a reinforcement.</summary>
    public sealed class StrikeCombatantRegisteredFact : RuleFact
    {
        internal StrikeCombatantRegisteredFact(CreatureId actor) => Actor = actor;

        /// <summary>Gets the newly Strike-enabled combatant.</summary>
        public CreatureId Actor { get; }
    }

    /// <summary>Represents the minimal feature-owned Reload needed by ranged Strike.</summary>
    public sealed class ReloadActionOp : ActionOp<EquipmentState>
    {
        /// <summary>Creates a Reload root for one selected weapon.</summary>
        public ReloadActionOp(CreatureId actor, ItemId item)
            : base(actor, ReloadActionDefinition.DefinitionId)
        {
            if (item.IsEmpty)
                throw new ArgumentException("A Reload item is required.", nameof(item));
            Item = item;
        }

        /// <summary>Gets the selected weapon.</summary>
        public ItemId Item { get; }

        /// <inheritdoc/>
        public override ActionProfile GetBaseProfile(IActionCatalog catalog)
        {
            if (catalog is not IStrikeActionCatalog strikeCatalog)
                throw new InvalidOperationException(
                    "Reload requires a catalog that exposes Strike definitions."
                );
            StrikeItemDefinition item = strikeCatalog.GetStrikeItem(Item);
            if (item.ReloadActions == 0)
                throw new InvalidOperationException("The selected item does not require Reload.");
            return ActionProfile.Create(
                ActionCost.FromActions(item.ReloadActions),
                new[] { Trait.FromSlug("manipulate") }
            );
        }
    }

    /// <summary>Owns Reload's stable identity and availability selector.</summary>
    public sealed class ReloadActionDefinition
    {
        /// <summary>Gets Reload's stable definition ID.</summary>
        public static ActionDefinitionId DefinitionId { get; } = new ActionDefinitionId("reload");

        /// <summary>Gets current Reload availability for one eligible actor and weapon.</summary>
        public ActionAvailability GetAvailability(
            RulesSnapshot snapshot,
            CreatureId actor,
            StrikeItemDefinition item
        )
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (!snapshot.Creatures.Contains(actor))
                return ActionAvailability.Unavailable("The actor is not registered.");
            if (!snapshot.Health.TryGet(actor, out HealthState health) || health.Current == 0)
                return ActionAvailability.Unavailable("The actor cannot act.");
            if (item.ReloadActions == 0)
                return ActionAvailability.Unavailable("The item does not require Reload.");
            if (
                !snapshot.Equipment.TryGet(item.Item, out EquipmentState equipment)
                || equipment.Holder != actor
                || !equipment.IsWielded
            )
                return ActionAvailability.Unavailable("The weapon is not wielded.");
            if (equipment.IsLoaded)
                return ActionAvailability.Unavailable("The weapon is already loaded.");
            if (
                !snapshot.ActionEconomy.TryGet(actor, out ActionEconomyState economy)
                || economy.ActionsRemaining < item.ReloadActions
            )
                return ActionAvailability.Unavailable("The actor cannot afford Reload.");
            if (
                item.Ammunition is RequiredStrikeAmmunitionRequirement required
                && (
                    !snapshot.Ammunition.TryGet(required.Pool, out AmmunitionState ammunition)
                    || ammunition.Owner != actor
                    || ammunition.Remaining == 0
                )
            )
                return ActionAvailability.Unavailable("No ammunition remains.");
            return ActionAvailability.Available;
        }
    }

    /// <summary>Registers normal Strike and minimal Reload behavior.</summary>
    public static class StrikeRuleDispatcherExtensions
    {
        private static readonly RuleSource Source = RuleSource.FromSlug("strike");

        /// <summary>Adds feature-owned Strike and Reload handlers to a dispatcher.</summary>
        public static RuleDispatcherBuilder UseStrikeRules(
            this RuleDispatcherBuilder builder,
            IStrikeActionCatalog catalog,
            IStrikeTargetingProvider targeting,
            IStrikeResolutionDataProvider resolutionData
        )
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (targeting == null)
                throw new ArgumentNullException(nameof(targeting));
            if (resolutionData == null)
                throw new ArgumentNullException(nameof(resolutionData));

            StrikeActionDefinition strike = new StrikeActionDefinition();
            ReloadActionDefinition reload = new ReloadActionDefinition();
            return builder
                .RegisterHandler<StrikeActionOp, StrikeResolution>(new StrikeActionHandler(catalog))
                .RegisterHandler<ResolveStrikeOp, StrikeResolution>(
                    new ResolveStrikeHandler(catalog, targeting, resolutionData),
                    InvocationPolicy.NestedOnly
                )
                .RegisterActionValidator(
                    new StrikeActionValidator(strike, catalog, targeting, resolutionData)
                )
                .RegisterHandler<ReloadActionOp, EquipmentState>(new ReloadActionHandler(catalog))
                .RegisterActionValidator(new ReloadActionValidator(reload, catalog))
                .RegisterHandler<RegisterStrikeCombatantOp, bool>(
                    new RegisterStrikeCombatantHandler()
                )
                .RegisterHandler<SetStrikeItemLoadedOp, EquipmentState>(
                    new SetStrikeItemLoadedHandler(),
                    InvocationPolicy.NestedOnly
                )
                .RegisterReducer<CommitStrikeItemLoadedOp, EquipmentState>(
                    new CommitStrikeItemLoadedReducer(),
                    Source
                )
                .RegisterReducer<CommitStrikeCombatantRegistrationOp, bool>(
                    new CommitStrikeCombatantRegistrationReducer(),
                    Source
                );
        }
    }

    internal sealed class StrikeActionValidator : IActionValidator<StrikeActionOp>
    {
        private readonly StrikeActionDefinition definition;
        private readonly IStrikeActionCatalog catalog;
        private readonly IStrikeTargetingProvider targeting;
        private readonly IStrikeResolutionDataProvider resolutionData;

        public StrikeActionValidator(
            StrikeActionDefinition definition,
            IStrikeActionCatalog catalog,
            IStrikeTargetingProvider targeting,
            IStrikeResolutionDataProvider resolutionData
        )
        {
            this.definition = definition;
            this.catalog = catalog;
            this.targeting = targeting;
            this.resolutionData = resolutionData;
        }

        public ActionValidationResult Validate(
            OpFrame<StrikeActionOp> frame,
            RulesSnapshot snapshot
        ) => definition.Validate(snapshot, frame.Op, catalog, targeting, resolutionData);
    }

    internal sealed class StrikeActionHandler : IOpHandler<StrikeActionOp, StrikeResolution>
    {
        private readonly IStrikeActionCatalog catalog;

        public StrikeActionHandler(IStrikeActionCatalog catalog)
        {
            this.catalog = catalog;
        }

        public async ValueTask<StrikeResolution> Handle(
            OpFrame<StrikeActionOp> frame,
            OpHandlerContext context
        )
        {
            StrikeItemDefinition item = catalog.GetStrikeItem(frame.Op.Item);
            OpResult<StrikeResolution> resolvedStrike = await context.Dispatch(
                new ResolveStrikeOp(frame.Op.Actor, frame.Op.Item, frame.Op.Target)
            );
            if (resolvedStrike is not ResolvedOpResult<StrikeResolution> resolved)
                throw new InvalidOperationException("Strike resolution did not resolve.");
            StrikeResolution resolution = resolved.Value;

            if (resolution.Hit && resolution.FinalDamage > 0)
            {
                OpResult<DamageOutcome> damage = await context.Dispatch(
                    new ApplyDamageOp(
                        frame.Op.Target,
                        resolution.FinalDamage,
                        new HealthChangeOriginId($"strike-{frame.RootId.Value}"),
                        RuleSource.FromSlug(item.Definition.Value)
                    )
                );
                if (damage is not ResolvedOpResult<DamageOutcome>)
                    throw new InvalidOperationException("Strike damage did not resolve.");
            }

            if (item.ReloadActions > 0)
            {
                OpResult<EquipmentState> unloaded = await context.Dispatch(
                    new SetStrikeItemLoadedOp(frame.Op.Actor, item.Item, false)
                );
                if (unloaded is not ResolvedOpResult<EquipmentState>)
                    throw new InvalidOperationException("Strike load state did not resolve.");
            }

            OpResult<MultipleAttackPenaltyState> advanced = await context.Dispatch(
                new AdvanceMultipleAttackPenaltyOp(frame.Op.Actor)
            );
            if (advanced is not ResolvedOpResult<MultipleAttackPenaltyState>)
                throw new InvalidOperationException("Strike MAP advancement did not resolve.");
            return resolution;
        }
    }

    internal sealed class ResolveStrikeHandler : IOpHandler<ResolveStrikeOp, StrikeResolution>
    {
        private readonly IStrikeActionCatalog catalog;
        private readonly IStrikeTargetingProvider targeting;
        private readonly IStrikeResolutionDataProvider resolutionData;

        public ResolveStrikeHandler(
            IStrikeActionCatalog catalog,
            IStrikeTargetingProvider targeting,
            IStrikeResolutionDataProvider resolutionData
        )
        {
            this.catalog = catalog;
            this.targeting = targeting;
            this.resolutionData = resolutionData;
        }

        public async ValueTask<StrikeResolution> Handle(
            OpFrame<ResolveStrikeOp> frame,
            OpHandlerContext context
        )
        {
            StrikeItemDefinition item = catalog.GetStrikeItem(frame.Op.Item);
            StrikeTargetingOutcome target = targeting.Evaluate(
                context.Snapshot,
                frame.Op.Actor,
                item,
                frame.Op.Target
            );
            if (target is not LegalStrikeTargetingOutcome legal)
                throw new InvalidOperationException(
                    "Strike targeting changed after action validation."
                );
            StrikeResolutionData data = resolutionData.Capture(
                context.Snapshot,
                frame.Op.Actor,
                item,
                frame.Op.Target,
                legal
            );
            data = await AddPreparedContributions(frame, context, item, legal, data);
            if (
                !context.Snapshot.MultipleAttackPenalty.TryGet(
                    frame.Op.Actor,
                    out MultipleAttackPenaltyState map
                )
            )
                throw new InvalidOperationException(
                    "The strike actor has no authoritative multiple-attack-penalty state."
                );
            int priorAttacks = map.AttackCount;
            int mapPenalty = MultipleAttackPenaltyResolver.Resolve(priorAttacks, item.IsAgile);
            return await Resolve(frame, item, data, legal, mapPenalty, context);
        }

        private static async ValueTask<StrikeResolutionData> AddPreparedContributions(
            OpFrame<ResolveStrikeOp> frame,
            OpHandlerContext context,
            StrikeItemDefinition item,
            LegalStrikeTargetingOutcome targeting,
            StrikeResolutionData data
        )
        {
            if (
                !context.Snapshot.PreparedInputs.TryGet(
                    frame.Op.Actor,
                    out PreparedCreatureInputs actorInputs
                )
            )
                throw new InvalidOperationException("The Strike actor has no prepared inputs.");
            if (
                !context.Snapshot.PreparedInputs.TryGet(
                    frame.Op.Target,
                    out PreparedCreatureInputs targetInputs
                )
            )
                throw new InvalidOperationException("The Strike target has no prepared inputs.");
            string[] targetConditions = targeting.OffGuard
                ? new[] { "off-guard" }
                : Array.Empty<string>();
            PreparedContributionContext baseContext = new(
                item.Definition.Value,
                item.Category,
                item.IsRanged,
                item.DamageDice[0].Dice.Sides,
                item.Traits.Select(trait => trait.Slug),
                Array.Empty<string>(),
                targetConditions
            );
            OpResult<IReadOnlyList<PreparedItemAlterationSpec>> alterationResult =
                await context.Dispatch(
                    new CollectPreparedItemAlterationsOp(
                        frame.Op.Actor,
                        "weapon",
                        "other-tags",
                        baseContext
                    )
                );
            if (
                alterationResult
                is not ResolvedOpResult<IReadOnlyList<PreparedItemAlterationSpec>> alterations
            )
                throw new InvalidOperationException("Prepared item alterations did not resolve.");
            string[] tags = alterations
                .Value.Where(value =>
                    string.Equals(value.Mode, "add", StringComparison.OrdinalIgnoreCase)
                )
                .Select(value => value.Value)
                .ToArray();
            PreparedContributionContext finalContext = new(
                item.Definition.Value,
                item.Category,
                item.IsRanged,
                item.DamageDice[0].Dice.Sides,
                item.Traits.Select(trait => trait.Slug),
                tags,
                targetConditions
            );
            IReadOnlyList<PreparedModifierValue> flat = RequireResolved(
                await context.Dispatch(
                    new CollectPreparedModifiersOp(frame.Op.Actor, "strike-damage", finalContext)
                ),
                "Prepared Strike modifiers"
            );
            List<TypedFlatDamage> extraFlat = flat.Where(value => value.Value != 0)
                .Select(value => new TypedFlatDamage(
                    value.Value,
                    item.DamageDice[0].DamageType,
                    value.Slug
                ))
                .ToList();
            if (!item.IsRanged)
            {
                PreparedModifierValue ability = RequireResolved(
                        await context.Dispatch(
                            new CollectPreparedModifiersOp(
                                frame.Op.Actor,
                                "melee-strike-damage",
                                finalContext
                            )
                        ),
                        "Prepared melee modifiers"
                    )
                    .LastOrDefault(value => !string.IsNullOrWhiteSpace(value.Ability));
                if (ability != null)
                {
                    int current = item.FlatDamage.Count == 0 ? 0 : item.FlatDamage[0].Amount;
                    extraFlat.Add(
                        new TypedFlatDamage(
                            actorInputs.Abilities.Get(ability.Ability) - current,
                            item.DamageDice[0].DamageType,
                            ability.Ability
                        )
                    );
                }
            }
            IReadOnlyList<PreparedDamageDiceSpec> dice = RequireResolved(
                await context.Dispatch(
                    new CollectPreparedDamageDiceOp(frame.Op.Actor, "strike-damage", finalContext)
                ),
                "Prepared damage dice"
            );
            return data.WithPreparedContributions(
                dice.Select(value => new TypedDamageDice(
                    new DiceExpression(value.DiceNumber, value.DieSize),
                    value.Category,
                    "Prepared damage dice"
                )),
                extraFlat,
                targetInputs
                    .Immunities.Where(value => value.Kind == PreparedImmunityKind.Damage)
                    .Select(value => new TypedDamageImmunity(value.Type)),
                targetInputs.Weaknesses.Select(value => new TypedDefenseAdjustment(
                    value.Type,
                    value.Value
                )),
                targetInputs.Resistances.Select(value => new TypedDefenseAdjustment(
                    value.Type,
                    value.Value
                ))
            );
        }

        private static IReadOnlyList<T> RequireResolved<T>(
            OpResult<IReadOnlyList<T>> result,
            string operation
        ) =>
            result is ResolvedOpResult<IReadOnlyList<T>> resolved
                ? resolved.Value
                : throw new InvalidOperationException($"{operation} did not resolve.");

        private static async ValueTask<StrikeResolution> Resolve(
            OpFrame<ResolveStrikeOp> frame,
            StrikeItemDefinition item,
            StrikeResolutionData data,
            LegalStrikeTargetingOutcome targeting,
            int mapPenalty,
            OpHandlerContext context
        )
        {
            List<Modifier> attackCandidates = new List<Modifier>
            {
                Modifier.Untyped(
                    item.AttackModifier,
                    RuleSource.FromSlug(item.Definition.Value),
                    Statistic.AttackRoll
                ),
                Modifier.Untyped(
                    mapPenalty,
                    RuleSource.FromSlug("multiple-attack-penalty"),
                    Statistic.AttackRoll
                ),
                Modifier.Untyped(
                    targeting.RangePenalty,
                    RuleSource.FromSlug("range-penalty"),
                    Statistic.AttackRoll
                ),
            };
            attackCandidates.AddRange(data.AttackModifiers);
            List<Modifier> defenseCandidates = new List<Modifier>
            {
                Modifier.Untyped(
                    data.ArmorClass,
                    RuleSource.FromSlug("base-armor-class"),
                    Statistic.ArmorClass
                ),
            };
            if (targeting.CoverBonus != 0)
                defenseCandidates.Add(
                    new Modifier(
                        targeting.CoverBonus,
                        ModifierType.Circumstance,
                        RuleSource.FromSlug("cover"),
                        Statistic.ArmorClass
                    )
                );
            if (targeting.OffGuard)
                defenseCandidates.Add(
                    new Modifier(
                        -2,
                        ModifierType.Circumstance,
                        RuleSource.FromSlug("flanking-off-guard"),
                        Statistic.ArmorClass
                    )
                );
            OpResult<ModifierCollection> defenseResult = await context.Dispatch(
                new CollectDefenseModifiersOp(
                    frame.Op.Target,
                    defenseCandidates,
                    CheckSource.From(frame.Id)
                )
            );
            if (defenseResult is not ResolvedOpResult<ModifierCollection> resolvedDefense)
                throw new InvalidOperationException("Strike defense collection did not resolve.");
            int armorClass = Math.Max(1, resolvedDefense.Value.Total);
            bool offGuard =
                targeting.OffGuard
                || ConditionSelectors.HasMarker(
                    context.Snapshot,
                    frame.Op.Target,
                    ConditionRuleDefinitions.OffGuard
                );
            OpResult<CheckOutcome> attackResult = await context.Dispatch(
                new AttackCheckOp(
                    frame.Op.Actor,
                    frame.Op.Target,
                    attackCandidates,
                    armorClass,
                    CheckSource.From(frame.Id)
                )
            );
            if (attackResult is not ResolvedOpResult<CheckOutcome> resolvedAttack)
                throw new InvalidOperationException("Strike attack check did not resolve.");
            CheckOutcome attack = resolvedAttack.Value;
            DegreeOfSuccess degree = attack.Degree;
            bool hit =
                degree == DegreeOfSuccess.Success || degree == DegreeOfSuccess.CriticalSuccess;
            IReadOnlyList<TypedDamagePart> damage = Array.Empty<TypedDamagePart>();
            int finalDamage = 0;
            if (hit)
            {
                damage = ResolveDamage(item, data, degree, context.Rolls);
                finalDamage = damage.Sum(part => part.Amount);
            }
            return new StrikeResolution(
                attack.Roll,
                attack.Modifiers.Total,
                mapPenalty,
                targeting.RangePenalty,
                armorClass,
                targeting.CoverBonus,
                offGuard,
                degree,
                damage,
                finalDamage
            );
        }

        private static IReadOnlyList<TypedDamagePart> ResolveDamage(
            StrikeItemDefinition item,
            StrikeResolutionData data,
            DegreeOfSuccess degree,
            IRollService rolls
        )
        {
            List<TypedDamageDice> dice = item.DamageDice.Concat(data.DamageDice).ToList();
            int deadlySides = FindTraitDie(item.Traits, "deadly-d");
            int fatalSides = FindTraitDie(item.Traits, "fatal-d");
            if (
                degree == DegreeOfSuccess.CriticalSuccess
                && fatalSides > 0
                && dice[0].Dice.Sides < fatalSides
            )
            {
                TypedDamageDice primary = dice[0];
                dice[0] = new TypedDamageDice(
                    new DiceExpression(primary.Dice.Count, fatalSides),
                    primary.DamageType,
                    primary.Source
                );
            }

            int criticalTraitSides = fatalSides > 0 ? fatalSides : deadlySides;
            IReadOnlyList<TypedDamageDice> criticalOnlyDice =
                degree == DegreeOfSuccess.CriticalSuccess && criticalTraitSides > 0
                    ? new[]
                    {
                        new TypedDamageDice(
                            new DiceExpression(1, criticalTraitSides),
                            dice[0].DamageType,
                            fatalSides > 0 ? "fatal" : "deadly"
                        ),
                    }
                    : Array.Empty<TypedDamageDice>();
            return TypedDamageResolver.Resolve(
                dice,
                item.FlatDamage.Concat(data.FlatDamage),
                criticalOnlyDice,
                degree,
                data.Immunities,
                data.Weaknesses,
                data.Resistances,
                rolls
            );
        }

        private static int FindTraitDie(IEnumerable<Trait> traits, string prefix)
        {
            foreach (Trait trait in traits)
            {
                if (
                    trait.Slug.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(trait.Slug.Substring(prefix.Length), out int sides)
                    && sides > 0
                )
                    return sides;
            }
            return 0;
        }
    }

    internal sealed class ReloadActionValidator : IActionValidator<ReloadActionOp>
    {
        private readonly ReloadActionDefinition definition;
        private readonly IStrikeActionCatalog catalog;

        public ReloadActionValidator(
            ReloadActionDefinition definition,
            IStrikeActionCatalog catalog
        )
        {
            this.definition = definition;
            this.catalog = catalog;
        }

        public ActionValidationResult Validate(
            OpFrame<ReloadActionOp> frame,
            RulesSnapshot snapshot
        )
        {
            StrikeItemDefinition item;
            try
            {
                item = catalog.GetStrikeItem(frame.Op.Item);
            }
            catch (KeyNotFoundException)
            {
                return ActionValidationResult.Invalid("The selected Reload item is unknown.");
            }
            ActionAvailability availability = definition.GetAvailability(
                snapshot,
                frame.Op.Actor,
                item
            );
            return availability is UnavailableActionAvailability unavailable
                ? ActionValidationResult.Invalid(unavailable.Reason)
                : ActionValidationResult.Valid;
        }
    }

    internal sealed class ReloadActionHandler : IOpHandler<ReloadActionOp, EquipmentState>
    {
        private readonly IStrikeActionCatalog catalog;

        public ReloadActionHandler(IStrikeActionCatalog catalog) => this.catalog = catalog;

        public async ValueTask<EquipmentState> Handle(
            OpFrame<ReloadActionOp> frame,
            OpHandlerContext context
        )
        {
            _ = catalog.GetStrikeItem(frame.Op.Item);
            OpResult<EquipmentState> loaded = await context.Dispatch(
                new SetStrikeItemLoadedOp(frame.Op.Actor, frame.Op.Item, true)
            );
            if (loaded is not ResolvedOpResult<EquipmentState> resolved)
                throw new InvalidOperationException("Reload load state did not resolve.");
            return resolved.Value;
        }
    }

    internal sealed class SetStrikeItemLoadedHandler
        : IOpHandler<SetStrikeItemLoadedOp, EquipmentState>
    {
        public async ValueTask<EquipmentState> Handle(
            OpFrame<SetStrikeItemLoadedOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<EquipmentState> result = await context.Dispatch(
                new CommitStrikeItemLoadedOp(frame.Op.Actor, frame.Op.Item, frame.Op.IsLoaded)
            );
            if (result is ResolvedOpResult<EquipmentState> resolved)
                return resolved.Value;
            throw new InvalidOperationException("Loaded-state commitment did not resolve.");
        }
    }

    internal sealed class CommitStrikeItemLoadedOp : IRuleOp<EquipmentState>
    {
        public CommitStrikeItemLoadedOp(CreatureId actor, ItemId item, bool isLoaded)
        {
            Actor = actor;
            Item = item;
            IsLoaded = isLoaded;
        }

        public CreatureId Actor { get; }
        public ItemId Item { get; }
        public bool IsLoaded { get; }
    }

    internal sealed class CommitStrikeItemLoadedReducer
        : IOpReducer<CommitStrikeItemLoadedOp, EquipmentState>
    {
        public ReductionResult<EquipmentState> Reduce(
            ReductionContext<CommitStrikeItemLoadedOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            if (
                !state.Equipment.TryGet(context.Op.Item, out EquipmentState equipment)
                || equipment.Holder != context.Op.Actor
            )
                return ReductionResult<EquipmentState>.Reject(
                    "The actor does not hold the selected item."
                );
            EquipmentState changed = new EquipmentState(
                equipment.Id,
                equipment.DefinitionId,
                equipment.Holder,
                equipment.IsWielded,
                context.Op.IsLoaded
            );
            state.Equipment.Set(changed.Id, changed);
            if (equipment.IsLoaded != changed.IsLoaded)
            {
                facts.Stage(
                    new StrikeItemLoadedChangedFact(
                        context.Op.Actor,
                        context.Op.Item,
                        changed.IsLoaded
                    )
                );
            }
            return ReductionResult<EquipmentState>.Accept(changed);
        }
    }

    internal sealed class RegisterStrikeCombatantHandler
        : IOpHandler<RegisterStrikeCombatantOp, bool>
    {
        public async ValueTask<bool> Handle(
            OpFrame<RegisterStrikeCombatantOp> frame,
            OpHandlerContext context
        )
        {
            OpResult<bool> result = await context.Dispatch(
                new CommitStrikeCombatantRegistrationOp(frame.Op.Registration)
            );
            if (result is ResolvedOpResult<bool> resolved)
                return resolved.Value;
            throw new InvalidOperationException("Strike combatant registration did not resolve.");
        }
    }

    internal sealed class CommitStrikeCombatantRegistrationOp : IRuleOp<bool>
    {
        public CommitStrikeCombatantRegistrationOp(StrikeCombatantRegistration registration) =>
            Registration = registration;

        public StrikeCombatantRegistration Registration { get; }
    }

    internal sealed class CommitStrikeCombatantRegistrationReducer
        : IOpReducer<CommitStrikeCombatantRegistrationOp, bool>
    {
        public ReductionResult<bool> Reduce(
            ReductionContext<CommitStrikeCombatantRegistrationOp> context,
            RulesStateDraft state,
            FactSink facts
        )
        {
            StrikeCombatantRegistration registration = context.Op.Registration;
            if (!state.Creatures.Contains(registration.Actor))
                return ReductionResult<bool>.Reject("The Strike combatant is not registered.");
            MultipleAttackPenaltyState expectedPenalty = new MultipleAttackPenaltyState(0);
            if (
                !state.MultipleAttackPenalty.TryGet(
                    registration.Actor,
                    out MultipleAttackPenaltyState committedPenalty
                ) || !committedPenalty.Equals(expectedPenalty)
            )
                return ReductionResult<bool>.Reject(
                    "Strike combatant MAP conflicts with the committed registration."
                );

            EquipmentState[] committedEquipment = state
                .Equipment.Select(pair => pair.Value)
                .Where(item => item.Holder == registration.Actor)
                .ToArray();
            AmmunitionState[] committedAmmunition = state
                .Ammunition.Select(pair => pair.Value)
                .Where(pool => pool.Owner == registration.Actor)
                .ToArray();
            bool equipmentExact = CompleteCollectionMatches(
                registration.Equipment,
                committedEquipment,
                item => item.Id
            );
            bool ammunitionExact = CompleteCollectionMatches(
                registration.Ammunition,
                committedAmmunition,
                pool => pool.Item
            );
            if (equipmentExact && ammunitionExact)
                return ReductionResult<bool>.Accept(false);
            bool anyRegistered =
                committedEquipment.Length > 0
                || committedAmmunition.Length > 0
                || registration.Equipment.Any(item => state.Equipment.Contains(item.Id))
                || registration.Ammunition.Any(pool => state.Ammunition.Contains(pool.Item));
            if (anyRegistered)
                return ReductionResult<bool>.Reject(
                    "Strike combatant state conflicts with the committed registration."
                );
            foreach (EquipmentState item in registration.Equipment)
                state.Equipment.Set(item.Id, item);
            foreach (AmmunitionState pool in registration.Ammunition)
                state.Ammunition.Set(pool.Item, pool);
            facts.Stage(new StrikeCombatantRegisteredFact(registration.Actor));
            return ReductionResult<bool>.Accept(true);
        }

        private static bool CompleteCollectionMatches<TValue, TId>(
            IEnumerable<TValue> expected,
            IEnumerable<TValue> committed,
            Func<TValue, TId> identify
        )
        {
            Dictionary<TId, TValue> expectedById = expected.ToDictionary(identify);
            Dictionary<TId, TValue> committedById = committed.ToDictionary(identify);
            return expectedById.Count == committedById.Count
                && expectedById.All(pair =>
                    committedById.TryGetValue(pair.Key, out TValue value)
                    && EqualityComparer<TValue>.Default.Equals(pair.Value, value)
                );
        }
    }
}
