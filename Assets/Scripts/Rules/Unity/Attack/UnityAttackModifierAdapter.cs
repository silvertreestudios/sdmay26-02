using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using CreatureSlug = Game.Creature.Rules.Pf2eSlug;

namespace Game.Rules.Unity.Attack
{
    /// <summary>Converts current prepared Unity attack modifiers into rules-runtime values.</summary>
    internal static class UnityAttackModifierAdapter
    {
        public static IReadOnlyList<Modifier> Capture(CreatureComponent attacker)
        {
            if (attacker == null)
                throw new ArgumentNullException(nameof(attacker));
            return attacker
                .ResolveAttackRoll(0)
                .AppliedModifiers.Where(modifier => modifier.Value != 0)
                .Select(ToRuntimeModifier)
                .ToArray();
        }

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
