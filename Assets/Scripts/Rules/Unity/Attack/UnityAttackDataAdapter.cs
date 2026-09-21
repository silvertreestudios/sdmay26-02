using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Attack
{
    /// <summary>Captures feature-agnostic Unity values used by typed attack resolution.</summary>
    internal static class UnityAttackDataAdapter
    {
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
    }
}
