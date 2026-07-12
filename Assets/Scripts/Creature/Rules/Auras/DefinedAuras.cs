using System;
using System.Collections.Generic;

namespace Game.Creature.Rules
{
    public static class DefinedAuras
    {
        private static readonly Dictionary<string, ICreatureAuraRule> Auras = new(StringComparer.OrdinalIgnoreCase)
        {
            { RottingAuraRule.RuleSlug, new RottingAuraRule() }
        };

        public static ICreatureAuraRule TryGet(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return Auras.TryGetValue(slug, out ICreatureAuraRule rule) ? rule : null;
        }
    }
}
