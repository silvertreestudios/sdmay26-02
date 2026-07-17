namespace Game.Creature.Rules
{
    /// <summary>
    /// Compatibility adapter for the Unity-free runtime's canonical PF2e slug normalization.
    /// </summary>
    public static class Pf2eSlug
    {
        /// <summary>
        /// Converts a name to a lowercase hyphenated slug compatible with the project catalog indexes.
        /// </summary>
        /// <param name="value">The human-readable PF2e item name.</param>
        /// <returns>A normalized slug, or an empty string for blank input.</returns>
        public static string FromName(string value)
        {
            return Game.Rules.Runtime.Pf2eSlug.FromName(value);
        }
    }
}
