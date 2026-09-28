using System.Collections.Generic;
using UnityEngine;

namespace Game.Rules
{
    /// <summary>
    /// Simple component-backed modifier store for generic sources that do not have a dedicated
    /// rules feature.
    /// </summary>
    /// <remarks>
    /// Encounter enrollment copies this collection into immutable rules statistics. Changes made
    /// afterward do not affect that active encounter. Use a feature-owned binding or effect for
    /// conditions, spells, auras, equipment, and other contributions that can change in combat.
    /// </remarks>
    public class Pf2eModifierCollection : MonoBehaviour, IPf2eModifierProvider
    {
        private readonly List<Pf2eModifier> modifiers = new();

        /// <summary>
        /// Read-only view of stored modifiers for diagnostics, tests, and UI inspection.
        /// </summary>
        public IReadOnlyList<Pf2eModifier> Modifiers => modifiers;

        /// <summary>
        /// Adds a modifier exactly as supplied; validation and rule interpretation belong to the source creating it.
        /// </summary>
        /// <param name="modifier">The modifier to expose through this provider.</param>
        public void Add(Pf2eModifier modifier)
        {
            modifiers.Add(modifier);
        }

        /// <summary>
        /// Removes all modifiers with a matching source label, ignoring blank labels to avoid accidental broad removals.
        /// </summary>
        /// <param name="source">The case-insensitive source label to remove.</param>
        public void RemoveFromSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return;

            modifiers.RemoveAll(modifier =>
                string.Equals(modifier.Source, source, System.StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Removes every modifier from this collection.
        /// </summary>
        public void Clear()
        {
            modifiers.Clear();
        }

        /// <summary>
        /// Returns modifiers in this collection that target the requested statistic.
        /// </summary>
        /// <param name="statistic">The statistic currently being resolved.</param>
        /// <returns>Stored modifiers for the requested statistic.</returns>
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
