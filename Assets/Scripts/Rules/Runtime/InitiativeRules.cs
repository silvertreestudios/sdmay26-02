using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>Owns the supported enrollment initiative calculation before initiative is rolled.</summary>
    public static class InitiativeRules
    {
        /// <summary>Uses the better imported initiative or Perception and resolves all typed candidates once.</summary>
        /// <param name="importedInitiative">The creature's immutable imported initiative modifier.</param>
        /// <param name="statistics">Prepared base skills and generic modifier definitions.</param>
        /// <param name="featureModifiers">Contributions from effects restored during enrollment.</param>
        public static int ResolveModifier(
            int importedInitiative,
            CreatureStatisticsState statistics,
            IEnumerable<Modifier> featureModifiers
        )
        {
            statistics.SkillModifiers.TryGetValue(Skill.FromName("perception"), out int perception);
            return new ModifierCollection(
                Statistic.Initiative,
                statistics
                    .Modifiers.Concat(featureModifiers)
                    .Append(
                        Modifier.Untyped(
                            Math.Max(importedInitiative, perception),
                            RuleSource.FromSlug("initiative"),
                            Statistic.Initiative
                        )
                    )
            ).Total;
        }
    }
}
