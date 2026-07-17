using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Provides the canonical runtime normalization for open PF2e data slugs.
    /// </summary>
    public static class Pf2eSlug
    {
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
