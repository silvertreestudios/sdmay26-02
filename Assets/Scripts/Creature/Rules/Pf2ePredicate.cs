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
            var options = (
                prepared == null ? Enumerable.Empty<string>() : prepared.RollOptions
            ).Concat(itemOptions ?? Enumerable.Empty<string>());
            return Compile(predicate, prepared).Matches(options);
        }

        /// <summary>Copies JSON clauses and prepared numeric facts into a Unity-free immutable predicate.</summary>
        public static Game.Rules.Runtime.PreparedPredicate Compile(
            JToken predicate,
            PreparedCharacter prepared
        )
        {
            if (predicate == null || predicate.Type == JTokenType.Null)
                return Game.Rules.Runtime.PreparedPredicate.All(
                    Array.Empty<Game.Rules.Runtime.PreparedPredicate>()
                );
            if (predicate is JArray array)
                return Game.Rules.Runtime.PreparedPredicate.All(
                    array.Select(entry => Compile(entry, prepared))
                );
            if (predicate.Type == JTokenType.String)
            {
                string option = predicate.Value<string>();
                if (
                    option != null
                    && option.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    if (option.EndsWith(":rank", StringComparison.OrdinalIgnoreCase))
                        return Game.Rules.Runtime.PreparedPredicate.AtLeast(
                            GetNumeric(option, prepared),
                            1
                        );
                    if (option.Contains(":rank:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = option.Split(':');
                        return parts.Length == 4 && int.TryParse(parts[3], out int rank)
                            ? Game.Rules.Runtime.PreparedPredicate.AtLeast(
                                GetNumeric($"skill:{parts[1]}:rank", prepared),
                                rank
                            )
                            : Game.Rules.Runtime.PreparedPredicate.Any(
                                Array.Empty<Game.Rules.Runtime.PreparedPredicate>()
                            );
                    }
                }
                return Game.Rules.Runtime.PreparedPredicate.Option(option);
            }
            if (predicate is JObject obj)
            {
                if (obj.TryGetValue("and", out JToken andToken))
                    return Compile(andToken, prepared);
                if (obj.TryGetValue("or", out JToken orToken))
                    return orToken is JArray alternatives
                        ? Game.Rules.Runtime.PreparedPredicate.Any(
                            alternatives.Select(entry => Compile(entry, prepared))
                        )
                        : Compile(orToken, prepared);
                if (obj.TryGetValue("not", out JToken notToken))
                    return Game.Rules.Runtime.PreparedPredicate.Not(Compile(notToken, prepared));
                if (
                    obj.TryGetValue("gte", out JToken gteToken)
                    && gteToken is JArray gte
                    && gte.Count == 2
                )
                    return Game.Rules.Runtime.PreparedPredicate.AtLeast(
                        GetNumeric(gte[0].Value<string>(), prepared),
                        gte[1].Value<int>()
                    );
            }
            return Game.Rules.Runtime.PreparedPredicate.Any(
                Array.Empty<Game.Rules.Runtime.PreparedPredicate>()
            );
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
