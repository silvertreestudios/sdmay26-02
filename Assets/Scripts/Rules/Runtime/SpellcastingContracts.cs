using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies one spell definition independently of display text.</summary>
    public readonly struct SpellId : IEquatable<SpellId>
    {
        /// <summary>Gets the stable spell slug.</summary>
        public string Value { get; }

        /// <summary>Gets whether this is the uninitialized identifier.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        /// <summary>Creates a spell identifier from a stable slug.</summary>
        public SpellId(string value) => Value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(SpellId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Value ?? string.Empty;

        /// <summary>Tests two spell identifiers for ordinal equality.</summary>
        public static bool operator ==(SpellId left, SpellId right) => left.Equals(right);

        /// <summary>Tests two spell identifiers for ordinal inequality.</summary>
        public static bool operator !=(SpellId left, SpellId right) => !left.Equals(right);
    }

    /// <summary>Identifies one spell cast at one exact requested rank.</summary>
    public readonly struct SpellReference : IEquatable<SpellReference>
    {
        /// <summary>Creates an exact spell reference.</summary>
        public SpellReference(SpellId spell, int rank)
        {
            if (spell.IsEmpty)
                throw new ArgumentException("A spell ID is required.", nameof(spell));
            if (rank <= 0)
                throw new ArgumentOutOfRangeException(nameof(rank));
            Spell = spell;
            Rank = rank;
        }

        /// <summary>Gets the stable spell identity.</summary>
        public SpellId Spell { get; }

        /// <summary>Gets the exact requested cast rank.</summary>
        public int Rank { get; }

        /// <inheritdoc/>
        public bool Equals(SpellReference other) => Spell == other.Spell && Rank == other.Rank;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellReference other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Spell, Rank);

        /// <inheritdoc/>
        public override string ToString() => $"{Spell}@{Rank}";

        /// <summary>Tests two exact spell-and-rank references for equality.</summary>
        public static bool operator ==(SpellReference left, SpellReference right) =>
            left.Equals(right);

        /// <summary>Tests two exact spell-and-rank references for inequality.</summary>
        public static bool operator !=(SpellReference left, SpellReference right) =>
            !left.Equals(right);
    }

    /// <summary>Reads authoritative slot state without exposing a mutable store.</summary>
    public interface ISpellSlotStateReader
    {
        /// <summary>Attempts to read one stable spell-slot pool.</summary>
        bool TryGet(SpellSlotPoolId pool, out SpellSlotState state);
    }

    /// <summary>Adapts one immutable rules snapshot to spellbook resource queries.</summary>
    public sealed class SnapshotSpellSlotStateReader : ISpellSlotStateReader
    {
        private readonly RulesSnapshot snapshot;

        /// <summary>Creates a reader over an immutable rules snapshot.</summary>
        public SnapshotSpellSlotStateReader(RulesSnapshot snapshot) =>
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        /// <inheritdoc/>
        public bool TryGet(SpellSlotPoolId pool, out SpellSlotState state) =>
            snapshot.SpellSlots.TryGet(pool, out state);
    }

    /// <summary>Describes the spellbook-authorized resource for one exact cast.</summary>
    public enum SpellCastResourceKind
    {
        Unavailable,
        Cantrip,
        SpellSlot,
    }

    /// <summary>Returns either an authorized resource or a concrete rejection reason.</summary>
    public readonly struct SpellCastAuthorization : IEquatable<SpellCastAuthorization>
    {
        private SpellCastAuthorization(
            SpellCastResourceKind kind,
            SpellSlotPoolId pool,
            string reason
        )
        {
            Kind = kind;
            Pool = pool;
            Reason = reason ?? string.Empty;
        }

        /// <summary>Gets the resource category authorized for the cast.</summary>
        public SpellCastResourceKind Kind { get; }

        /// <summary>Gets the authoritative pool to spend for a slotted cast.</summary>
        public SpellSlotPoolId Pool { get; }

        /// <summary>Gets the user-facing rejection reason when authorization failed.</summary>
        public string Reason { get; }

        /// <summary>Gets whether the spellbook authorized the requested exact cast.</summary>
        public bool IsAuthorized => Kind != SpellCastResourceKind.Unavailable;

        /// <summary>Gets successful authorization for a cantrip that spends no slot.</summary>
        public static SpellCastAuthorization Cantrip { get; } =
            new SpellCastAuthorization(SpellCastResourceKind.Cantrip, default, string.Empty);

        /// <summary>Creates successful authorization for one authoritative slot pool.</summary>
        /// <param name="pool">The non-empty pool that action-cost handling must spend.</param>
        /// <returns>An authorization bound to the supplied pool.</returns>
        public static SpellCastAuthorization FromPool(SpellSlotPoolId pool)
        {
            if (pool.IsEmpty)
                throw new ArgumentException("An authorized slot pool is required.", nameof(pool));
            return new SpellCastAuthorization(SpellCastResourceKind.SpellSlot, pool, string.Empty);
        }

        /// <summary>Creates a failed authorization with a concrete explanation.</summary>
        /// <param name="reason">The non-empty reason the cast cannot proceed.</param>
        /// <returns>An unavailable authorization that spends no resource.</returns>
        public static SpellCastAuthorization Unavailable(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "An unavailable cast requires a reason.",
                    nameof(reason)
                );
            return new SpellCastAuthorization(
                SpellCastResourceKind.Unavailable,
                default,
                reason.Trim()
            );
        }

        /// <inheritdoc/>
        public bool Equals(SpellCastAuthorization other) =>
            Kind == other.Kind && Pool == other.Pool && Reason == other.Reason;

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is SpellCastAuthorization other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Kind, Pool, Reason);
    }

    /// <summary>Owns exact spell preparation and casting-resource authorization.</summary>
    public interface ISpellBook
    {
        /// <summary>Gets all exact cast references, without duplicate preparation entries.</summary>
        IReadOnlyList<SpellReference> CastableSpells { get; }

        /// <summary>Gets the spell attack modifier derived for this book.</summary>
        int SpellAttackModifier { get; }

        /// <summary>Gets the spell DC derived for this book.</summary>
        int SpellDc { get; }

        /// <summary>Creates authoritative encounter slot state owned by the supplied creature.</summary>
        IReadOnlyList<SpellSlotState> CreateInitialSlotStates(CreatureId owner);

        /// <summary>Resolves whether an exact cast is prepared and has an available authorized resource.</summary>
        SpellCastAuthorization Authorize(
            CreatureId owner,
            SpellReference spell,
            ISpellSlotStateReader slots
        );

        /// <summary>
        /// Binds an exact prepared spell to its encounter-scoped cantrip or slot resource.
        /// </summary>
        /// <remarks>
        /// This does not inspect remaining uses. <see cref="Authorize"/> performs the authoritative
        /// live-state check immediately before the action lifecycle commits the bound cost.
        /// </remarks>
        SpellCastAuthorization BindResource(CreatureId owner, SpellReference spell);
    }

    /// <summary>Supplies the current spellbook for one registered creature.</summary>
    public interface ISpellBookProvider
    {
        /// <summary>Gets a book, returning an empty book for creatures without spellcasting.</summary>
        ISpellBook GetSpellBook(CreatureId creature);
    }

    /// <summary>Identifies a definition-owned Cast a Spell action variant.</summary>
    public readonly struct SpellActionVariant : IEquatable<SpellActionVariant>
    {
        /// <summary>Creates a combat action-cost variant for a spell.</summary>
        /// <param name="actions">The number of actions, from one through three.</param>
        /// <exception cref="ArgumentOutOfRangeException">The cost is outside one through three.</exception>
        public SpellActionVariant(int actions)
        {
            if (actions <= 0 || actions > 3)
                throw new ArgumentOutOfRangeException(nameof(actions));
            Actions = actions;
        }

        /// <summary>Gets the action count spent by this variant.</summary>
        public int Actions { get; }

        /// <inheritdoc/>
        public bool Equals(SpellActionVariant other) => Actions == other.Actions;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellActionVariant other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Actions;

        /// <inheritdoc/>
        public override string ToString() => $"{Actions}A";

        /// <summary>Tests two action variants for equality.</summary>
        public static bool operator ==(SpellActionVariant left, SpellActionVariant right) =>
            left.Equals(right);

        /// <summary>Tests two action variants for inequality.</summary>
        public static bool operator !=(SpellActionVariant left, SpellActionVariant right) =>
            !left.Equals(right);
    }

    /// <summary>Declares one generic active effect created by a spell definition.</summary>
    public sealed class SpellEffectDirective
    {
        /// <summary>Creates a request to apply one registered active-effect definition.</summary>
        /// <param name="definitionId">The definition registered in the active-rule registry.</param>
        /// <param name="duration">The lifecycle duration copied to each created effect.</param>
        /// <param name="target">The data-owned target selector, currently <c>self</c>.</param>
        public SpellEffectDirective(
            RuleDefinitionId definitionId,
            EffectDuration duration,
            string target
        )
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "An effect definition ID is required.",
                    nameof(definitionId)
                );
            if (!string.Equals(target, "self", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Only the self spell-effect target is currently supported.",
                    nameof(target)
                );
            DefinitionId = definitionId;
            Duration = duration;
            Target = target;
        }

        /// <summary>Gets the generic active-effect definition to instantiate.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets the duration assigned to each created effect instance.</summary>
        public EffectDuration Duration { get; }

        /// <summary>
        /// Gets the definition-owned target selector used during rules resolution.
        /// </summary>
        public string Target { get; }
    }

    /// <summary>Identifies a data-backed area shape without depending on Unity grid types.</summary>
    public enum SpellAreaShape
    {
        /// <summary>A cone projected from the caster.</summary>
        Cone,

        /// <summary>A burst centered on a selected grid location.</summary>
        Burst,

        /// <summary>An emanation centered on the caster.</summary>
        Emanation,

        /// <summary>A line projected from the caster.</summary>
        Line,
    }

    /// <summary>Defines one rules spell's area-selection geometry.</summary>
    public sealed class SpellAreaTarget
    {
        /// <summary>Creates a positive-foot area target.</summary>
        public SpellAreaTarget(SpellAreaShape shape, int sizeFeet, int rangeFeet = 0)
        {
            if (!Enum.IsDefined(typeof(SpellAreaShape), shape))
                throw new ArgumentOutOfRangeException(nameof(shape));
            if (sizeFeet <= 0)
                throw new ArgumentOutOfRangeException(nameof(sizeFeet));
            if (rangeFeet < 0)
                throw new ArgumentOutOfRangeException(nameof(rangeFeet));
            if (shape != SpellAreaShape.Burst && rangeFeet != 0)
                throw new ArgumentException(
                    "Only burst placements currently use a separate range.",
                    nameof(rangeFeet)
                );
            Shape = shape;
            SizeFeet = sizeFeet;
            RangeFeet = rangeFeet;
        }

        /// <summary>Gets the grid-independent area shape.</summary>
        public SpellAreaShape Shape { get; }

        /// <summary>Gets the area's size in feet.</summary>
        public int SizeFeet { get; }

        /// <summary>Gets the caster-to-placement range for a burst, or zero for caster-origin areas.</summary>
        public int RangeFeet { get; }
    }

    /// <summary>Identifies one of the eight authored horizontal area directions.</summary>
    public enum SpellAreaDirection
    {
        /// <summary>Positive X.</summary>
        East,

        /// <summary>Positive X and positive Z.</summary>
        NorthEast,

        /// <summary>Positive Z.</summary>
        North,

        /// <summary>Negative X and positive Z.</summary>
        NorthWest,

        /// <summary>Negative X.</summary>
        West,

        /// <summary>Negative X and negative Z.</summary>
        SouthWest,

        /// <summary>Negative Z.</summary>
        South,

        /// <summary>Positive X and negative Z.</summary>
        SouthEast,
    }

    /// <summary>Stores an immutable, Unity-free authored area placement.</summary>
    public readonly struct SpellAreaPlacement : IEquatable<SpellAreaPlacement>
    {
        /// <summary>Creates one exact area placement from grid coordinates and direction.</summary>
        public SpellAreaPlacement(
            SpellAreaShape shape,
            GridPosition originCell,
            int originCornerX,
            int originCornerZ,
            SpellAreaDirection direction
        )
        {
            if (!Enum.IsDefined(typeof(SpellAreaShape), shape))
                throw new ArgumentOutOfRangeException(nameof(shape));
            if (!Enum.IsDefined(typeof(SpellAreaDirection), direction))
                throw new ArgumentOutOfRangeException(nameof(direction));
            Shape = shape;
            OriginCell = originCell;
            OriginCornerX = originCornerX;
            OriginCornerZ = originCornerZ;
            Direction = direction;
        }

        /// <summary>Gets the authored shape copied from the selection.</summary>
        public SpellAreaShape Shape { get; }

        /// <summary>Gets the selected origin cell.</summary>
        public GridPosition OriginCell { get; }

        /// <summary>Gets the selected grid-corner X coordinate.</summary>
        public int OriginCornerX { get; }

        /// <summary>Gets the selected grid-corner Z coordinate.</summary>
        public int OriginCornerZ { get; }

        /// <summary>Gets the selected horizontal direction.</summary>
        public SpellAreaDirection Direction { get; }

        /// <inheritdoc/>
        public bool Equals(SpellAreaPlacement other) =>
            Shape == other.Shape
            && OriginCell == other.OriginCell
            && OriginCornerX == other.OriginCornerX
            && OriginCornerZ == other.OriginCornerZ
            && Direction == other.Direction;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellAreaPlacement other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(Shape, OriginCell, OriginCornerX, OriginCornerZ, Direction);
    }

    /// <summary>Revalidates an exact area placement and affected creature set before costs.</summary>
    public interface ISpellSaveTargetingProvider
    {
        /// <summary>
        /// Checks authored geometry, live positions, topology, line of effect, and exact targets.
        /// </summary>
        ActionValidationResult Validate(
            RulesSnapshot snapshot,
            CreatureId actor,
            SpellSaveDefinition save,
            SpellAreaPlacement placement,
            IReadOnlyList<CreatureId> selectedCreatures
        );
    }

    /// <summary>Declares one condition applied on an exact saving-throw degree.</summary>
    public sealed class SpellSaveConditionDirective
    {
        /// <summary>Creates a canonical condition directive with typed state.</summary>
        public SpellSaveConditionDirective(
            RuleDefinitionId definitionId,
            DegreeOfSuccess degree,
            EffectDuration duration,
            IEffectState state
        )
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException(
                    "A condition definition is required.",
                    nameof(definitionId)
                );
            if (!Enum.IsDefined(typeof(DegreeOfSuccess), degree))
                throw new ArgumentOutOfRangeException(nameof(degree));
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (!ConditionRuleDefinitions.Accepts(definitionId, State))
                throw new ArgumentException(
                    "The condition state does not match its canonical definition.",
                    nameof(state)
                );
            DefinitionId = definitionId;
            Degree = degree;
            Duration = duration;
        }

        /// <summary>Gets the canonical condition definition.</summary>
        public RuleDefinitionId DefinitionId { get; }

        /// <summary>Gets the save degree that applies the condition.</summary>
        public DegreeOfSuccess Degree { get; }

        /// <summary>Gets the authored condition duration.</summary>
        public EffectDuration Duration { get; }

        /// <summary>Gets the immutable typed condition state.</summary>
        public IEffectState State { get; }
    }

    /// <summary>Defines one generic area basic-save workflow owned by a spell definition.</summary>
    public sealed class SpellSaveDefinition
    {
        private readonly IReadOnlyList<TypedDamageDice> damage;
        private readonly IReadOnlyList<SpellSaveConditionDirective> conditions;

        /// <summary>Creates an immutable basic-save definition.</summary>
        public SpellSaveDefinition(
            SaveKind save,
            bool isBasic,
            SpellAreaTarget target,
            IEnumerable<TypedDamageDice> damage,
            IEnumerable<SpellSaveConditionDirective> conditions
        )
        {
            if (!Enum.IsDefined(typeof(SaveKind), save))
                throw new ArgumentOutOfRangeException(nameof(save));
            if (!isBasic)
                throw new ArgumentException(
                    "Only basic saving-throw spell damage is currently supported.",
                    nameof(isBasic)
                );
            Save = save;
            IsBasic = true;
            Target = target ?? throw new ArgumentNullException(nameof(target));
            this.damage = new ReadOnlyCollection<TypedDamageDice>(
                (damage ?? throw new ArgumentNullException(nameof(damage))).ToArray()
            );
            if (this.damage.Count == 0 || this.damage.Any(value => value == null))
                throw new ArgumentException(
                    "A save definition requires non-null damage dice.",
                    nameof(damage)
                );
            this.conditions = new ReadOnlyCollection<SpellSaveConditionDirective>(
                (conditions ?? throw new ArgumentNullException(nameof(conditions))).ToArray()
            );
            if (this.conditions.Any(value => value == null))
                throw new ArgumentException(
                    "Save condition directives cannot contain null.",
                    nameof(conditions)
                );
        }

        /// <summary>Gets the saving throw rolled by each selected creature.</summary>
        public SaveKind Save { get; }

        /// <summary>Gets whether the definition uses PF2e basic-save damage scaling.</summary>
        public bool IsBasic { get; }

        /// <summary>Gets the area-selection contract.</summary>
        public SpellAreaTarget Target { get; }

        /// <summary>Gets the typed damage dice rolled once and shared by every target.</summary>
        public IReadOnlyList<TypedDamageDice> Damage => damage;

        /// <summary>Gets secondary conditions keyed to exact save degrees.</summary>
        public IReadOnlyList<SpellSaveConditionDirective> Conditions => conditions;
    }

    /// <summary>Stores immutable data shared by rules and Unity spell presentation.</summary>
    public sealed class SpellDefinition
    {
        private readonly IReadOnlyList<SpellActionVariant> variants;
        private readonly IReadOnlyList<Trait> traits;
        private readonly IReadOnlyList<SpellEffectDirective> effects;
        private readonly IReadOnlyList<SpellAttackDefinition> attacks;
        private readonly IReadOnlyList<SpellSaveDefinition> saves;

        /// <summary>Creates an immutable, data-backed spell definition.</summary>
        /// <param name="id">The stable spell identity shared with prepared spellbooks.</param>
        /// <param name="displayName">The player-facing spell name.</param>
        /// <param name="minimumRank">The lowest rank at which the spell can be cast.</param>
        /// <param name="variants">The supported one-to-three-action casting variants.</param>
        /// <param name="traits">The rules traits frozen into the action profile.</param>
        /// <param name="effects">Generic active effects created when the cast resolves.</param>
        /// <param name="attacks">Generic spell attacks resolved when the cast completes.</param>
        /// <param name="saves">Generic area basic saves resolved when the cast completes.</param>
        public SpellDefinition(
            SpellId id,
            string displayName,
            int minimumRank,
            IEnumerable<SpellActionVariant> variants,
            IEnumerable<Trait> traits,
            IEnumerable<SpellEffectDirective> effects,
            IEnumerable<SpellAttackDefinition> attacks,
            IEnumerable<SpellSaveDefinition> saves
        )
        {
            if (id.IsEmpty)
                throw new ArgumentException("A spell ID is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));
            if (minimumRank <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumRank));
            Id = id;
            DisplayName = displayName.Trim();
            MinimumRank = minimumRank;
            this.variants = Array.AsReadOnly(
                (variants ?? throw new ArgumentNullException(nameof(variants))).Distinct().ToArray()
            );
            if (this.variants.Count == 0)
                throw new ArgumentException(
                    "A spell requires an action variant.",
                    nameof(variants)
                );
            this.traits = Array.AsReadOnly(
                (traits ?? throw new ArgumentNullException(nameof(traits))).Distinct().ToArray()
            );
            this.effects = new ReadOnlyCollection<SpellEffectDirective>(
                (effects ?? throw new ArgumentNullException(nameof(effects))).ToArray()
            );
            if (this.effects.Any(effect => effect == null))
                throw new ArgumentException(
                    "Effect directives cannot contain null.",
                    nameof(effects)
                );
            this.attacks = new ReadOnlyCollection<SpellAttackDefinition>(
                (attacks ?? throw new ArgumentNullException(nameof(attacks))).ToArray()
            );
            if (this.attacks.Any(attack => attack == null))
                throw new ArgumentException(
                    "Attack directives cannot contain null.",
                    nameof(attacks)
                );
            this.saves = new ReadOnlyCollection<SpellSaveDefinition>(
                (saves ?? throw new ArgumentNullException(nameof(saves))).ToArray()
            );
            if (this.saves.Any(save => save == null))
                throw new ArgumentException("Save directives cannot contain null.", nameof(saves));
            if (this.attacks.Count > 0 && this.saves.Count > 0)
                throw new ArgumentException(
                    "A spell cannot combine the current attack and area-save target workflows."
                );
            int resolutionCategories =
                (this.effects.Count > 0 ? 1 : 0)
                + (this.attacks.Count > 0 ? 1 : 0)
                + (this.saves.Count > 0 ? 1 : 0);
            if (resolutionCategories != 1)
                throw new ArgumentException(
                    "A rules-ready spell requires exactly one modeled resolution category."
                );
        }

        /// <summary>Gets the stable spell identity.</summary>
        public SpellId Id { get; }

        /// <summary>Gets the player-facing spell name.</summary>
        public string DisplayName { get; }

        /// <summary>Gets the lowest legal cast rank.</summary>
        public int MinimumRank { get; }

        /// <summary>Gets every action-cost variant supported by the definition.</summary>
        public IReadOnlyList<SpellActionVariant> Variants => variants;

        /// <summary>Gets the immutable rules traits for the spell action.</summary>
        public IReadOnlyList<Trait> Traits => traits;

        /// <summary>Gets generic active effects created by a resolved cast.</summary>
        public IReadOnlyList<SpellEffectDirective> Effects => effects;

        /// <summary>Gets generic spell attacks resolved by this definition.</summary>
        public IReadOnlyList<SpellAttackDefinition> Attacks => attacks;

        /// <summary>Gets generic area basic saves resolved by this definition.</summary>
        public IReadOnlyList<SpellSaveDefinition> Saves => saves;
    }

    /// <summary>Resolves generic spell definitions by exact cast reference.</summary>
    public interface ISpellDefinitionCatalog
    {
        /// <summary>Attempts to resolve definition data for an exact spell and cast rank.</summary>
        /// <param name="reference">The requested stable spell identity and rank.</param>
        /// <param name="definition">The matching definition when the rank is legal.</param>
        /// <returns><see langword="true"/> when a matching definition exists.</returns>
        bool TryGetSpell(SpellReference reference, out SpellDefinition definition);
    }

    /// <summary>Extends action-profile lookup with selected spell definition data.</summary>
    public interface ISpellActionCatalog : IActionCatalog, ISpellDefinitionCatalog
    {
        /// <summary>Gets the immutable prepared spellbook owned by one encounter creature.</summary>
        ISpellBook GetSpellBook(CreatureId creature);
    }

    /// <summary>Generic immutable state carried by a spell-created active effect.</summary>
    public sealed class SpellEffectState : IEffectState, IEquatable<SpellEffectState>
    {
        /// <summary>Records which exact spell affected which creature.</summary>
        /// <param name="spell">The spell identity and rank that created the effect.</param>
        /// <param name="target">The creature that presentation and expiration apply to.</param>
        public SpellEffectState(SpellReference spell, CreatureId target)
        {
            if (target.IsEmpty)
                throw new ArgumentException("A spell effect target is required.", nameof(target));
            Spell = spell;
            Target = target;
        }

        /// <summary>Gets the exact spell and rank that created the effect.</summary>
        public SpellReference Spell { get; }

        /// <summary>Gets the creature receiving the effect.</summary>
        public CreatureId Target { get; }

        /// <inheritdoc/>
        public bool Equals(SpellEffectState other) =>
            other != null && Spell == other.Spell && Target == other.Target;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellEffectState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Spell, Target);
    }
}
