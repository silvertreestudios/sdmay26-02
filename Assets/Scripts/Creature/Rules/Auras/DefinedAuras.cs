using System;
using System.Collections.Generic;
using Game.Rules.Runtime;

namespace Game.Creature.Rules
{
    public static class DefinedAuras
    {
        private static readonly Dictionary<string, ICreatureAuraRule> Auras = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { RottingAuraRules.Slug, new RottingAuraVisualization() },
        };

        public static ICreatureAuraRule TryGet(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return Auras.TryGetValue(slug, out ICreatureAuraRule rule) ? rule : null;
        }
    }
}
