using System.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Normalizes display names into PF2e-style slugs for lookups when data omits an explicit slug.
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
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string lower = value.Trim().ToLowerInvariant().Replace("'", string.Empty);
            char[] chars = lower.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            string slug = new string(chars);
            while (slug.Contains("--"))
                slug = slug.Replace("--", "-");
            return slug.Trim('-');
        }
    }
}
