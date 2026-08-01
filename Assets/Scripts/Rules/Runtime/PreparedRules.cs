using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Stores the six immutable ability modifiers compiled for one creature.</summary>
    public readonly struct PreparedAbilityModifiers : IEquatable<PreparedAbilityModifiers>
    {
        public PreparedAbilityModifiers(
            int strength,
            int dexterity,
            int constitution,
            int intelligence,
            int wisdom,
            int charisma
        )
        {
            Strength = strength;
            Dexterity = dexterity;
            Constitution = constitution;
            Intelligence = intelligence;
            Wisdom = wisdom;
            Charisma = charisma;
        }

        public int Strength { get; }
        public int Dexterity { get; }
        public int Constitution { get; }
        public int Intelligence { get; }
        public int Wisdom { get; }
        public int Charisma { get; }

        /// <summary>Returns an ability modifier by its normalized PF2e abbreviation.</summary>
        public int Get(string ability) =>
            ability?.ToLowerInvariant() switch
            {
                "str" => Strength,
                "dex" => Dexterity,
                "con" => Constitution,
                "int" => Intelligence,
                "wis" => Wisdom,
                "cha" => Charisma,
                _ => 0,
            };

        public bool Equals(PreparedAbilityModifiers other) =>
            Strength == other.Strength
            && Dexterity == other.Dexterity
            && Constitution == other.Constitution
            && Intelligence == other.Intelligence
            && Wisdom == other.Wisdom
            && Charisma == other.Charisma;

        public override bool Equals(object obj) =>
            obj is PreparedAbilityModifiers other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma);
    }

    /// <summary>Describes one immutable weakness or resistance fact.</summary>
    public sealed class PreparedDefenseDescriptor : IEquatable<PreparedDefenseDescriptor>
    {
        public PreparedDefenseDescriptor(string type, int value)
        {
            Type = Pf2eSlug.FromName(type);
            if (string.IsNullOrWhiteSpace(Type))
                throw new ArgumentException("A defense type is required.", nameof(type));
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public string Type { get; }
        public int Value { get; }

        public bool Equals(PreparedDefenseDescriptor other) =>
            other != null && Type == other.Type && Value == other.Value;

        public override bool Equals(object obj) =>
            obj is PreparedDefenseDescriptor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Type, Value);
    }

    /// <summary>Describes an immunity while retaining effect-trait and death-effect semantics.</summary>
    public sealed class PreparedImmunityDescriptor : IEquatable<PreparedImmunityDescriptor>
    {
        public PreparedImmunityDescriptor(string type)
        {
            Type = Pf2eSlug.FromName(type);
            if (string.IsNullOrWhiteSpace(Type))
                throw new ArgumentException("An immunity type is required.", nameof(type));
            IsDeathEffect = Type == "death-effects" || Type == "death-effect";
            IsEffectTrait = IsDeathEffect || Type.EndsWith("-effects", StringComparison.Ordinal);
        }

        public string Type { get; }
        public bool IsEffectTrait { get; }
        public bool IsDeathEffect { get; }

        public bool Equals(PreparedImmunityDescriptor other) => other != null && Type == other.Type;

        public override bool Equals(object obj) =>
            obj is PreparedImmunityDescriptor other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Type);
    }

    /// <summary>Contains the immutable creature facts available to prepared predicates and collectors.</summary>
    public sealed class PreparedCreatureInputs
    {
        private readonly IReadOnlyDictionary<string, int> skillRanks;

        public PreparedCreatureInputs(
            int level,
            PreparedAbilityModifiers abilities,
            IEnumerable<KeyValuePair<string, int>> skillRanks,
            IEnumerable<string> equipment,
            string armorCategory,
            IEnumerable<string> traits,
            IEnumerable<PreparedDefenseDescriptor> weaknesses,
            IEnumerable<PreparedDefenseDescriptor> resistances,
            IEnumerable<PreparedImmunityDescriptor> immunities,
            IEnumerable<string> staticOptions
        )
        {
            if (level < 0)
                throw new ArgumentOutOfRangeException(nameof(level));
            Level = level;
            Abilities = abilities;
            this.skillRanks = new ReadOnlyDictionary<string, int>(
                (skillRanks ?? throw new ArgumentNullException(nameof(skillRanks))).ToDictionary(
                    pair => Pf2eSlug.FromName(pair.Key),
                    pair => pair.Value,
                    StringComparer.Ordinal
                )
            );
            if (this.skillRanks.Any(pair => pair.Value < 0))
                throw new ArgumentOutOfRangeException(nameof(skillRanks));
            Equipment = FreezeStrings(equipment, nameof(equipment));
            ArmorCategory = Pf2eSlug.FromName(armorCategory ?? string.Empty);
            Traits = FreezeStrings(traits, nameof(traits));
            Weaknesses = Freeze(weaknesses, nameof(weaknesses));
            Resistances = Freeze(resistances, nameof(resistances));
            Immunities = Freeze(immunities, nameof(immunities));
            StaticOptions = FreezeOptions(staticOptions);
        }

        public int Level { get; }
        public PreparedAbilityModifiers Abilities { get; }
        public IReadOnlyDictionary<string, int> SkillRanks => skillRanks;
        public IReadOnlyList<string> Equipment { get; }
        public string ArmorCategory { get; }
        public IReadOnlyList<string> Traits { get; }
        public IReadOnlyList<PreparedDefenseDescriptor> Weaknesses { get; }
        public IReadOnlyList<PreparedDefenseDescriptor> Resistances { get; }
        public IReadOnlyList<PreparedImmunityDescriptor> Immunities { get; }
        public IReadOnlyList<string> StaticOptions { get; }

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values, string parameter)
        {
            T[] copied = (values ?? throw new ArgumentNullException(parameter)).ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException(
                    "Immutable prepared inputs cannot contain null.",
                    parameter
                );
            return Array.AsReadOnly(copied);
        }

        private static IReadOnlyList<string> FreezeStrings(
            IEnumerable<string> values,
            string parameter
        ) =>
            Array.AsReadOnly(
                (values ?? throw new ArgumentNullException(parameter))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(Pf2eSlug.FromName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            );

        private static IReadOnlyList<string> FreezeOptions(IEnumerable<string> values) =>
            Array.AsReadOnly(
                (values ?? throw new ArgumentNullException(nameof(values)))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            );
    }

    /// <summary>Base type for the immutable predicate tree compiled from source JSON.</summary>
    public abstract class PreparedPredicate
    {
        internal PreparedPredicate() { }

        public abstract bool Evaluate(PreparedPredicateContext context);
        public static PreparedPredicate Always { get; } = new PreparedConstantPredicate(true);
        public static PreparedPredicate Never { get; } = new PreparedConstantPredicate(false);
    }

    /// <summary>Represents an unconditional compiled predicate.</summary>
    public sealed class PreparedConstantPredicate : PreparedPredicate
    {
        public PreparedConstantPredicate(bool value) => Value = value;

        public bool Value { get; }

        public override bool Evaluate(PreparedPredicateContext context) => Value;
    }

    /// <summary>Matches one normalized option from immutable static or current context.</summary>
    public sealed class PreparedOptionPredicate : PreparedPredicate
    {
        public PreparedOptionPredicate(string option)
        {
            Option = option?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(Option))
                throw new ArgumentException("A predicate option is required.", nameof(option));
        }

        public string Option { get; }

        public override bool Evaluate(PreparedPredicateContext context) =>
            context.HasOption(Option);
    }

    /// <summary>Identifies a numeric immutable input addressable by compiled predicates.</summary>
    public enum PreparedNumericFactKind
    {
        Level,
        SkillRank,
    }

    /// <summary>Requires one numeric immutable input to meet a minimum.</summary>
    public sealed class PreparedNumericAtLeastPredicate : PreparedPredicate
    {
        public PreparedNumericAtLeastPredicate(
            PreparedNumericFactKind kind,
            string key,
            int minimum
        )
        {
            Kind = kind;
            Key = Pf2eSlug.FromName(key ?? string.Empty);
            Minimum = minimum;
            if (kind == PreparedNumericFactKind.SkillRank && string.IsNullOrWhiteSpace(Key))
                throw new ArgumentException(
                    "A skill is required for a skill-rank predicate.",
                    nameof(key)
                );
        }

        public PreparedNumericFactKind Kind { get; }
        public string Key { get; }
        public int Minimum { get; }

        public override bool Evaluate(PreparedPredicateContext context) =>
            context.GetNumeric(Kind, Key) >= Minimum;
    }

    /// <summary>Requires every immutable child predicate to match.</summary>
    public sealed class PreparedAllPredicate : PreparedPredicate
    {
        public PreparedAllPredicate(IEnumerable<PreparedPredicate> children) =>
            Children = Freeze(children);

        public IReadOnlyList<PreparedPredicate> Children { get; }

        public override bool Evaluate(PreparedPredicateContext context) =>
            Children.All(child => child.Evaluate(context));

        private static IReadOnlyList<PreparedPredicate> Freeze(
            IEnumerable<PreparedPredicate> values
        )
        {
            PreparedPredicate[] copied = (
                values ?? throw new ArgumentNullException(nameof(values))
            ).ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException(
                    "Predicate children cannot contain null.",
                    nameof(values)
                );
            return Array.AsReadOnly(copied);
        }
    }

    /// <summary>Requires at least one immutable child predicate to match.</summary>
    public sealed class PreparedAnyPredicate : PreparedPredicate
    {
        public PreparedAnyPredicate(IEnumerable<PreparedPredicate> children)
        {
            PreparedPredicate[] copied = (
                children ?? throw new ArgumentNullException(nameof(children))
            ).ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException(
                    "Predicate children cannot contain null.",
                    nameof(children)
                );
            Children = Array.AsReadOnly(copied);
        }

        public IReadOnlyList<PreparedPredicate> Children { get; }

        public override bool Evaluate(PreparedPredicateContext context) =>
            Children.Any(child => child.Evaluate(context));
    }

    /// <summary>Negates one immutable child predicate.</summary>
    public sealed class PreparedNotPredicate : PreparedPredicate
    {
        public PreparedNotPredicate(PreparedPredicate child) =>
            Child = child ?? throw new ArgumentNullException(nameof(child));

        public PreparedPredicate Child { get; }

        public override bool Evaluate(PreparedPredicateContext context) => !Child.Evaluate(context);
    }

    /// <summary>Evaluates predicates from immutable inputs and one authoritative binding snapshot.</summary>
    public sealed class PreparedPredicateContext
    {
        private readonly HashSet<string> options;

        public PreparedPredicateContext(
            PreparedRulePackage package,
            RulesSnapshot snapshot,
            CreatureId owner,
            IEnumerable<string> currentOptions
        )
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            if (owner.IsEmpty)
                throw new ArgumentException("A predicate owner is required.", nameof(owner));
            Owner = owner;
            options = new HashSet<string>(
                Package.Inputs.StaticOptions,
                StringComparer.OrdinalIgnoreCase
            );
            foreach (string option in currentOptions ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(option))
                    options.Add(option);
            foreach (PreparedOptionSpec option in package.Options)
                if (IsDefinitionActive(option.DefinitionId))
                    options.Add(option.Option);
            foreach (KeyValuePair<BindingId, ActiveRuleBinding> pair in snapshot.RuleBindings)
            {
                ActiveRuleBinding binding = pair.Value;
                if (binding.Owner == owner && binding.IsEnabled && binding.EffectId.HasValue)
                    options.Add($"self:effect:{binding.Source.Slug}");
            }
        }

        public PreparedRulePackage Package { get; }
        public RulesSnapshot Snapshot { get; }
        public CreatureId Owner { get; }

        public bool HasOption(string option) =>
            !string.IsNullOrWhiteSpace(option) && options.Contains(option);

        public bool IsDefinitionActive(RuleDefinitionId definition) =>
            Snapshot.RuleBindings.Any(pair =>
                pair.Value.Owner == Owner
                && pair.Value.DefinitionId == definition
                && pair.Value.IsEnabled
            );

        internal int GetNumeric(PreparedNumericFactKind kind, string key) =>
            kind == PreparedNumericFactKind.Level ? Package.Inputs.Level
            : Package.Inputs.SkillRanks.TryGetValue(key, out int rank) ? rank
            : 0;
    }

    /// <summary>Immutable provenance for one compiled runtime definition.</summary>
    public sealed class PreparedRuleDefinitionSpec : IEquatable<PreparedRuleDefinitionSpec>
    {
        public PreparedRuleDefinitionSpec(
            RuleDefinitionId id,
            RuleSource source,
            string ruleKey,
            string provenance
        )
        {
            if (id.IsEmpty)
                throw new ArgumentException("A definition ID is required.", nameof(id));
            if (source.IsEmpty)
                throw new ArgumentException("A definition source is required.", nameof(source));
            Id = id;
            Source = source;
            RuleKey = ruleKey ?? string.Empty;
            Provenance = provenance ?? string.Empty;
        }

        public RuleDefinitionId Id { get; }
        public RuleSource Source { get; }
        public string RuleKey { get; }
        public string Provenance { get; }

        public bool Equals(PreparedRuleDefinitionSpec other) =>
            other != null
            && Id == other.Id
            && Source == other.Source
            && RuleKey == other.RuleKey
            && Provenance == other.Provenance;

        public override bool Equals(object obj) =>
            obj is PreparedRuleDefinitionSpec other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Id, Source, RuleKey, Provenance);
    }

    /// <summary>Creates a stateless binding with encounter creature identity added at enrollment.</summary>
    public sealed class PreparedBindingSeed
    {
        public PreparedBindingSeed(
            string stableKey,
            RuleDefinitionId definitionId,
            RuleSource source,
            long creationOrder
        )
        {
            if (string.IsNullOrWhiteSpace(stableKey))
                throw new ArgumentException("A stable binding key is required.", nameof(stableKey));
            if (definitionId.IsEmpty)
                throw new ArgumentException("A definition ID is required.", nameof(definitionId));
            if (source.IsEmpty)
                throw new ArgumentException("A rule source is required.", nameof(source));
            if (creationOrder < 0)
                throw new ArgumentOutOfRangeException(nameof(creationOrder));
            StableKey = stableKey;
            DefinitionId = definitionId;
            Source = source;
            CreationOrder = creationOrder;
        }

        public string StableKey { get; }
        public RuleDefinitionId DefinitionId { get; }
        public RuleSource Source { get; }
        public long CreationOrder { get; }

        public ActiveRuleBinding Create(CreatureId owner) =>
            new(
                new BindingId($"prepared:{owner.Value}:{StableKey}"),
                DefinitionId,
                owner,
                null,
                Source,
                CreationOrder
            );
    }

    /// <summary>Records one unsupported source key without retaining its JSON token.</summary>
    public sealed class PreparedUnsupportedDiagnostic
    {
        public PreparedUnsupportedDiagnostic(string key, RuleSource source, string provenance)
        {
            Key = key ?? string.Empty;
            Source = source;
            Provenance = provenance ?? string.Empty;
        }

        public string Key { get; }
        public RuleSource Source { get; }
        public string Provenance { get; }
    }

    /// <summary>Provides binding selection and predicate metadata shared by compiled contributions.</summary>
    public abstract class PreparedContributionSpec
    {
        protected PreparedContributionSpec(
            RuleDefinitionId definitionId,
            PreparedPredicate predicate
        )
        {
            if (definitionId.IsEmpty)
                throw new ArgumentException("A definition ID is required.", nameof(definitionId));
            DefinitionId = definitionId;
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public RuleDefinitionId DefinitionId { get; }
        public PreparedPredicate Predicate { get; }
    }

    /// <summary>Contributes one normalized option while its definition binding is enabled.</summary>
    public sealed class PreparedOptionSpec : PreparedContributionSpec
    {
        public PreparedOptionSpec(
            RuleDefinitionId definitionId,
            string option,
            PreparedPredicate predicate
        )
            : base(definitionId, predicate)
        {
            if (string.IsNullOrWhiteSpace(option))
                throw new ArgumentException("A normalized option is required.", nameof(option));
            Option = option.Trim().ToLowerInvariant();
        }

        public string Option { get; }
    }

    /// <summary>Describes one immutable compiled numeric modifier.</summary>
    public sealed class PreparedModifierSpec : PreparedContributionSpec
    {
        public PreparedModifierSpec(
            RuleDefinitionId id,
            string selector,
            string slug,
            int value,
            string type,
            string ability,
            PreparedPredicate predicate
        )
            : base(id, predicate)
        {
            Selector = selector ?? string.Empty;
            Slug = slug ?? string.Empty;
            Value = value;
            Type = type ?? string.Empty;
            Ability = ability ?? string.Empty;
        }

        public string Selector { get; }
        public string Slug { get; }
        public int Value { get; }
        public string Type { get; }
        public string Ability { get; }
    }

    /// <summary>Describes one immutable ordered modifier adjustment.</summary>
    public sealed class PreparedAdjustmentSpec : PreparedContributionSpec
    {
        public PreparedAdjustmentSpec(
            RuleDefinitionId id,
            string selector,
            string slug,
            string mode,
            float value,
            int priority,
            PreparedPredicate predicate
        )
            : base(id, predicate)
        {
            Selector = selector ?? string.Empty;
            Slug = slug ?? string.Empty;
            Mode = mode ?? string.Empty;
            Value = value;
            Priority = priority;
        }

        public string Selector { get; }
        public string Slug { get; }
        public string Mode { get; }
        public float Value { get; }
        public int Priority { get; }
    }

    /// <summary>Describes immutable additional damage dice.</summary>
    public sealed class PreparedDamageDiceSpec : PreparedContributionSpec
    {
        public PreparedDamageDiceSpec(
            RuleDefinitionId id,
            string selector,
            string category,
            int diceNumber,
            int dieSize,
            PreparedPredicate predicate
        )
            : base(id, predicate)
        {
            Selector = selector ?? string.Empty;
            Category = category ?? string.Empty;
            DiceNumber = diceNumber;
            DieSize = dieSize;
        }

        public string Selector { get; }
        public string Category { get; }
        public int DiceNumber { get; }
        public int DieSize { get; }
    }

    /// <summary>Describes an immutable item alteration without resolving or mutating inventory.</summary>
    public sealed class PreparedItemAlterationSpec : PreparedContributionSpec
    {
        public PreparedItemAlterationSpec(
            RuleDefinitionId id,
            string itemType,
            string mode,
            string property,
            string value,
            PreparedPredicate predicate
        )
            : base(id, predicate)
        {
            ItemType = itemType ?? string.Empty;
            Mode = mode ?? string.Empty;
            Property = property ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string ItemType { get; }
        public string Mode { get; }
        public string Property { get; }
        public string Value { get; }
    }

    /// <summary>The complete immutable output of prepared-rule compilation.</summary>
    public sealed class PreparedRulePackage
    {
        public PreparedRulePackage(
            PreparedCreatureInputs inputs,
            IEnumerable<PreparedRuleDefinitionSpec> definitions,
            IEnumerable<PreparedBindingSeed> bindings,
            IEnumerable<PreparedOptionSpec> options,
            IEnumerable<PreparedModifierSpec> modifiers,
            IEnumerable<PreparedAdjustmentSpec> adjustments,
            IEnumerable<PreparedDamageDiceSpec> damageDice,
            IEnumerable<PreparedItemAlterationSpec> itemAlterations,
            IEnumerable<PreparedUnsupportedDiagnostic> diagnostics
        )
        {
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Definitions = Freeze(definitions);
            Bindings = Freeze(bindings);
            Options = Freeze(options);
            Modifiers = Freeze(modifiers);
            Adjustments = Freeze(adjustments);
            DamageDice = Freeze(damageDice);
            ItemAlterations = Freeze(itemAlterations);
            Diagnostics = Freeze(diagnostics);
        }

        public PreparedCreatureInputs Inputs { get; }
        public IReadOnlyList<PreparedRuleDefinitionSpec> Definitions { get; }
        public IReadOnlyList<PreparedBindingSeed> Bindings { get; }
        public IReadOnlyList<PreparedOptionSpec> Options { get; }
        public IReadOnlyList<PreparedModifierSpec> Modifiers { get; }
        public IReadOnlyList<PreparedAdjustmentSpec> Adjustments { get; }
        public IReadOnlyList<PreparedDamageDiceSpec> DamageDice { get; }
        public IReadOnlyList<PreparedItemAlterationSpec> ItemAlterations { get; }
        public IReadOnlyList<PreparedUnsupportedDiagnostic> Diagnostics { get; }

        private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values)
        {
            T[] copied = (values ?? throw new ArgumentNullException(nameof(values))).ToArray();
            if (copied.Any(value => value == null))
                throw new ArgumentException(
                    "Prepared package collections cannot contain null.",
                    nameof(values)
                );
            return Array.AsReadOnly(copied);
        }
    }
}
