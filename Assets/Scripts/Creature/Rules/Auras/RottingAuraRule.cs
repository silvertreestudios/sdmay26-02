using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Creature.Rules
{
    public sealed class RottingAuraRule : ICreatureAuraRule
    {
        public const string RuleSlug = "rotting-aura";

        public string Slug => RuleSlug;
        public CreatureAuraTiming Timing => CreatureAuraTiming.TurnStart;

        public bool HasVisual(CreatureAura aura)
        {
            return aura != null && aura.radiusFeet > 0;
        }

        public bool CanAffect(CreatureAuraContext context)
        {
            CreatureComponent target = context?.TargetCreature;
            return target != null
                && target.hp > 0
                && target.maxHp > 0
                && target.hp < target.maxHp
                && !HasTrait(target, "undead")
                && !HasTrait(target, "construct");
        }

        /// <summary>Calculates one aura damage result without mutating health or presentation.</summary>
        public CreatureAuraEffectResult Resolve(CreatureAuraContext context)
        {
            CreatureComponent source = context.SourceCreature;
            CreatureComponent target = context.TargetCreature;
            int diceCount = Math.Max(1, 1 + Math.Max(0, source.level) / 6);
            int rolled = Math.Max(0, context.DiceRoller.Roll(diceCount, 6));
            DamageRollResolution resolution = DamageRoller.StartDamageResolution(
                new List<Dice>(),
                new List<DamageValue> { new DamageValue("void", rolled) }
            );
            DamageRoller.ApplyWeaknessAndResistance(
                resolution,
                target.weaknesses,
                target.resistances
            );
            DamageRoller.FinalizeDamageResolution(resolution);
            CreatureAuraEffectResult result = new()
            {
                Source = context.SourceObject,
                Target = context.TargetObject,
                Aura = context.Aura,
                RuleSlug = RuleSlug,
                RolledDamage = rolled,
                AppliedDamage = resolution.TotalDamage,
                DamageResolution = resolution,
            };
            return result;
        }

        internal static void Present(CreatureAuraEffectResult result) => LogAuraDamage(result);

        private static bool HasTrait(CreatureComponent creature, string trait)
        {
            return creature?.traits != null
                && creature.traits.Any(value =>
                    string.Equals(value, trait, StringComparison.OrdinalIgnoreCase)
                );
        }

        private static void LogAuraDamage(CreatureAuraEffectResult result)
        {
            if (!Application.isPlaying)
                return;

            CombatLogInterface log = UnityEngine.Object.FindFirstObjectByType<CombatLogInterface>();
            if (log == null || result == null)
                return;

            CombatLogEntry entry = new()
            {
                Kind = CombatLogEntryKind.Damage,
                Outcome = CombatLogOutcome.Damage,
                Actor = result.Source != null ? result.Source.name : string.Empty,
                Target = result.Target != null ? result.Target.name : string.Empty,
                Action = result.Aura?.name ?? "Rotting Aura",
                Message =
                    (result.Target != null ? result.Target.name : "Target")
                    + " takes "
                    + result.AppliedDamage
                    + " void damage from Rotting Aura.",
                Damage = result.DamageResolution?.Damage,
            };
            entry.Tags.Add("aura");
            entry.Tags.Add(RuleSlug);
            entry.Tags.Add("void");
            entry.Details.Add(new CombatLogDetail("Rolled", result.RolledDamage + " void"));
            if (result.DamageResolution != null)
            {
                foreach (CombatLogDetail detail in result.DamageResolution.Details)
                    entry.Details.Add(detail);
            }
            log.LogEntry(entry);
        }
    }
}
