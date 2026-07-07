using System.Collections.Generic;

namespace Game.Rules
{
    public interface IPf2eModifierProvider
    {
        IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic);
    }
}