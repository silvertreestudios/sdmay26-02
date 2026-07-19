using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Evaluates the supported subset of Foundry PF2e predicates against prepared roll options and item options.
    /// </summary>
    public static class Pf2ePredicate
    {
        /// <summary>
        /// Determines whether a predicate matches the current prepared character and transient item context.
        /// </summary>
        /// <param name="predicate">The predicate token from a PF2e rule element.</param>
        /// <param name="prepared">The prepared character supplying roll options and numeric rule facts.</param>
        /// <param name="itemOptions">Optional item-scoped options such as traits for a Strike or item alteration.</param>
        /// <returns>True when the predicate is empty or all supported clauses match.</returns>
        public static bool Evaluate(
            JToken predicate,
            PreparedCharacter prepared,
            IEnumerable<string> itemOptions = null
        )
        {
            if (predicate == null || predicate.Type == JTokenType.Null)
                return true;

            if (predicate is JArray array)
                return array.All(entry => Evaluate(entry, prepared, itemOptions));

            if (predicate.Type == JTokenType.String)
                return EvaluateAtomic(predicate.Value<string>(), prepared, itemOptions);

            if (predicate is JObject obj)
            {
                if (obj.TryGetValue("and", out JToken andToken))
                    return (andToken as JArray)?.All(entry =>
                            Evaluate(entry, prepared, itemOptions)
                        )
                        ?? Evaluate(andToken, prepared, itemOptions);
                if (obj.TryGetValue("or", out JToken orToken))
                    return (orToken as JArray)?.Any(entry => Evaluate(entry, prepared, itemOptions))
                        ?? Evaluate(orToken, prepared, itemOptions);
                if (obj.TryGetValue("not", out JToken notToken))
                    return !Evaluate(notToken, prepared, itemOptions);
                if (obj.TryGetValue("gte", out JToken gteToken))
                    return EvaluateGte(gteToken as JArray, prepared);
            }

            return false;
        }

        private static bool EvaluateAtomic(
            string option,
            PreparedCharacter prepared,
            IEnumerable<string> itemOptions
        )
        {
            if (string.IsNullOrWhiteSpace(option))
                return true;

            if (
                option.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)
                && option.EndsWith(":rank", StringComparison.OrdinalIgnoreCase)
            )
                return GetNumeric(option, prepared) > 0;

            if (
                option.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)
                && option.Contains(":rank:", StringComparison.OrdinalIgnoreCase)
            )
            {
                string[] parts = option.Split(':');
                return parts.Length == 4
                    && int.TryParse(parts[3], out int rank)
                    && GetNumeric($"skill:{parts[1]}:rank", prepared) >= rank;
            }

            if (
                itemOptions != null
                && itemOptions.Contains(option, StringComparer.OrdinalIgnoreCase)
            )
                return true;

            return prepared?.RollOptions.Contains(option) ?? false;
        }

        private static bool EvaluateGte(JArray gte, PreparedCharacter prepared)
        {
            if (gte == null || gte.Count != 2)
                return false;

            int left = GetNumeric(gte[0].Value<string>(), prepared);
            int right = gte[1].Value<int>();
            return left >= right;
        }

        private static int GetNumeric(string path, PreparedCharacter prepared)
        {
            if (string.Equals(path, "self:level", StringComparison.OrdinalIgnoreCase))
            {
                string levelOption = prepared.RollOptions.FirstOrDefault(option =>
                    option.StartsWith("self:level:", StringComparison.OrdinalIgnoreCase)
                );
                if (
                    levelOption != null
                    && int.TryParse(levelOption.Substring("self:level:".Length), out int level)
                )
                    return level;
            }

            if (
                path != null
                && path.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(":rank", StringComparison.OrdinalIgnoreCase)
            )
            {
                string skill = path.Substring(
                    "skill:".Length,
                    path.Length - "skill:".Length - ":rank".Length
                );
                return prepared.SkillRanks.TryGetValue(skill, out int rank) ? rank : 0;
            }

            return 0;
        }
    }
}
