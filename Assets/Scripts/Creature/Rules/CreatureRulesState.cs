using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Provides the Unity-free creature facts that PF2e rules need when making decisions.
    /// </summary>
    public sealed class CreatureRulesState
    {
        public int Level { get; set; }
        public int ConstitutionModifier { get; set; }
        public string ArmorCategory { get; set; }
        public PreparedCharacter Prepared { get; set; }
        public IReadOnlyCollection<string> Conditions { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> TempHpImmunitySources { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Checks for an active condition by rules name while keeping condition storage outside rule implementations.
        /// </summary>
        /// <param name="condition">The PF2e condition name or slug to match.</param>
        /// <returns>True when the condition is present on this rules snapshot.</returns>
        public bool HasCondition(string condition)
        {
            return Conditions != null && Conditions.Contains(condition, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether a rules source is currently blocked from granting temporary Hit Points again.
        /// </summary>
        /// <param name="source">The stable source key used by the originating rule.</param>
        /// <returns>True when that source has temporary Hit Point immunity.</returns>
        public bool HasTempHpImmunity(string source)
        {
            return TempHpImmunitySources != null && TempHpImmunitySources.Contains(source, StringComparer.OrdinalIgnoreCase);
        }
    }
}
