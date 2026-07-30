using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using CreatureSlug = Game.Creature.Rules.Pf2eSlug;

namespace Game.Rules.Unity.Attack
{
    /// <summary>Captures feature-agnostic Unity values used by typed attack resolution.</summary>
    internal static class UnityAttackDataAdapter
    {
        public static IReadOnlyList<Modifier> CaptureModifiers(CreatureComponent attacker)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            return attacker
                .ResolveAttackRoll(0)
                .AppliedModifiers.Where(modifier => modifier.Value != 0)
                .Select(ToRuntimeModifier)
                .ToArray();
        }

        public static IReadOnlyList<TypedDefenseAdjustment> CaptureWeaknesses(
            CreatureComponent defender
        )
        {
            if (defender == null)
                throw new ArgumentNullException(nameof(defender));
            return CaptureDefenses(defender.weaknesses);
        }

        public static IReadOnlyList<TypedDefenseAdjustment> CaptureResistances(
            CreatureComponent defender
        )
        {
            if (defender == null)
                throw new ArgumentNullException(nameof(defender));
            return CaptureDefenses(defender.resistances);
        }

        private static IReadOnlyList<TypedDefenseAdjustment> CaptureDefenses(
            IEnumerable<DamageValue> values
        ) =>
            (values ?? Enumerable.Empty<DamageValue>())
                .Select(value => new TypedDefenseAdjustment(
                    value.DamageType,
                    Math.Max(0, value.DamageAmount)
                ))
                .ToArray();

        private static Modifier ToRuntimeModifier(Pf2eModifier modifier) =>
            new(
                modifier.Value,
                modifier.Type switch
                {
                    Pf2eModifierType.Circumstance => ModifierType.Circumstance,
                    Pf2eModifierType.Item => ModifierType.Item,
                    Pf2eModifierType.Status => ModifierType.Status,
                    _ => ModifierType.Untyped,
                },
                RuleSource.FromSlug(
                    string.IsNullOrWhiteSpace(CreatureSlug.FromName(modifier.Source))
                        ? "unity-modifier"
                        : CreatureSlug.FromName(modifier.Source)
                ),
                Statistic.AttackRoll
            );
    }
}
