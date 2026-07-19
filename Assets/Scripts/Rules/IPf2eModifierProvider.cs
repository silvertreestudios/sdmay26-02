using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>
    /// Extension point for components that contribute PF2e modifiers without centralizing rule ownership in CreatureComponent.
    /// </summary>
    public interface IPf2eModifierProvider
    {
        /// <summary>
        /// Returns modifiers from this provider that can affect the requested statistic.
        /// Providers should keep their own source-specific state and avoid mutating the creature during resolution.
        /// </summary>
        /// <param name="statistic">The statistic currently being resolved.</param>
        /// <returns>Modifiers for the requested statistic.</returns>
        IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic);
    }
}
