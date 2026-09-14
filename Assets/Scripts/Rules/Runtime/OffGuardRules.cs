using System;

namespace Game.Rules.Runtime
{
    /// <summary>Owns the named Off-Guard condition query used by attack features.</summary>
    public static class OffGuardRules
    {
        /// <summary>Gets the current condition identity.</summary>
        public static ConditionId ConditionId { get; } = new("Off-Guard");

        /// <summary>
        /// Gets the retained Flat-Footed condition alias used by existing imported and saved data.
        /// </summary>
        public static ConditionId FlatFootedConditionId { get; } = new("Flat-Footed");

        /// <summary>Determines whether either supported condition identity affects a creature.</summary>
        /// <param name="snapshot">The authoritative snapshot containing condition applications.</param>
        /// <param name="creature">The creature whose defense is being queried.</param>
        /// <returns><see langword="true"/> when an enabled application has a positive value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
        public static bool IsOffGuard(RulesSnapshot snapshot, CreatureId creature)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (creature.IsEmpty || !snapshot.Creatures.Contains(creature))
                return false;
            return ConditionRules.GetValue(snapshot, creature, ConditionId) > 0
                || ConditionRules.GetValue(snapshot, creature, FlatFootedConditionId) > 0;
        }
    }
}
