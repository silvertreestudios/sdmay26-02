using System.Collections.Generic;
using UnityEngine;

namespace Game.Rules
{
    public class Pf2eModifierCollection : MonoBehaviour, IPf2eModifierProvider
    {
        private readonly List<Pf2eModifier> modifiers = new();

        public IReadOnlyList<Pf2eModifier> Modifiers => modifiers;

        public void Add(Pf2eModifier modifier)
        {
            modifiers.Add(modifier);
        }

        public void RemoveFromSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return;

            modifiers.RemoveAll(modifier => string.Equals(modifier.Source, source, System.StringComparison.OrdinalIgnoreCase));
        }

        public void Clear()
        {
            modifiers.Clear();
        }

        public IEnumerable<Pf2eModifier> GetModifiers(Pf2eStatistic statistic)
        {
            foreach (Pf2eModifier modifier in modifiers)
            {
                if (modifier.TargetStatistic == statistic)
                    yield return modifier;
            }
        }
    }
}