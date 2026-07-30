using System.Globalization;

namespace Game.Rules.Runtime
{
    /// <summary>Parses simple distance values shared by data-backed rules adapters.</summary>
    public static class DistanceValues
    {
        private const string FeetSuffix = " feet";

        /// <summary>Parses a positive whole-number distance ending in <c> feet</c>.</summary>
        /// <param name="value">The candidate data value, such as <c>60 feet</c>.</param>
        /// <param name="feet">The positive number of feet when successful.</param>
        /// <returns><see langword="true"/> only for the supported numeric-foot shape.</returns>
        public static bool TryParseFeet(string value, out int feet)
        {
            feet = 0;
            string normalized = value?.Trim() ?? string.Empty;
            return normalized.EndsWith(FeetSuffix, System.StringComparison.Ordinal)
                && int.TryParse(
                    normalized.Substring(0, normalized.Length - FeetSuffix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out feet
                )
                && feet > 0;
        }
    }
}
