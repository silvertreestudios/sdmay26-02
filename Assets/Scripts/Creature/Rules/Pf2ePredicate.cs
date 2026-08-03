using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;
using Newtonsoft.Json.Linq;

namespace Game.Creature.Rules
{
    /// <summary>Compiles the supported Foundry predicate JSON into the runtime's immutable AST.</summary>
    public static class Pf2ePredicate
    {
        /// <summary>Compiles a source predicate exactly once at the preparation boundary.</summary>
        public static PreparedPredicate Compile(JToken predicate)
        {
            if (predicate == null || predicate.Type == JTokenType.Null)
                return PreparedPredicate.Always;
            if (predicate is JArray array)
                return new PreparedAllPredicate(array.Select(Compile));
            if (predicate.Type == JTokenType.String)
                return CompileAtomic(predicate.Value<string>());
            if (predicate is not JObject value)
                return PreparedPredicate.Never;
            if (value.TryGetValue("and", out JToken andToken))
                return new PreparedAllPredicate(Children(andToken));
            if (value.TryGetValue("or", out JToken orToken))
                return new PreparedAnyPredicate(Children(orToken));
            if (value.TryGetValue("not", out JToken notToken))
                return new PreparedNotPredicate(Compile(notToken));
            if (
                value.TryGetValue("gte", out JToken gteToken)
                && gteToken is JArray gte
                && gte.Count == 2
            )
                return CompileNumeric(gte[0].Value<string>(), gte[1].Value<int>());
            return PreparedPredicate.Never;
        }

        internal static bool EvaluateStatic(
            PreparedPredicate predicate,
            IEnumerable<string> options,
            IReadOnlyDictionary<string, int> skillRanks,
            int level
        )
        {
            HashSet<string> values = new(
                options ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase
            );
            return Evaluate(predicate, values, skillRanks, level);
        }

        private static IEnumerable<PreparedPredicate> Children(JToken value) =>
            value is JArray array ? array.Select(Compile) : new[] { Compile(value) };

        private static PreparedPredicate CompileAtomic(string option)
        {
            if (string.IsNullOrWhiteSpace(option))
                return PreparedPredicate.Always;
            string normalized = option.Trim().ToLowerInvariant();
            if (
                normalized.StartsWith("skill:", StringComparison.Ordinal)
                && normalized.EndsWith(":rank", StringComparison.Ordinal)
            )
                return CompileNumeric(normalized, 1);
            int rankMarker = normalized.IndexOf(":rank:", StringComparison.Ordinal);
            if (
                normalized.StartsWith("skill:", StringComparison.Ordinal)
                && rankMarker > 6
                && int.TryParse(normalized.Substring(rankMarker + 6), out int rank)
            )
                return CompileNumeric(normalized.Substring(0, rankMarker + 5), rank);
            return new PreparedOptionPredicate(normalized);
        }

        private static PreparedPredicate CompileNumeric(string path, int minimum)
        {
            if (string.Equals(path, "self:level", StringComparison.OrdinalIgnoreCase))
                return new PreparedNumericAtLeastPredicate(
                    PreparedNumericFactKind.Level,
                    string.Empty,
                    minimum
                );
            const string prefix = "skill:";
            const string suffix = ":rank";
            if (
                path != null
                && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            )
                return new PreparedNumericAtLeastPredicate(
                    PreparedNumericFactKind.SkillRank,
                    path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length),
                    minimum
                );
            return PreparedPredicate.Never;
        }

        private static bool Evaluate(
            PreparedPredicate predicate,
            HashSet<string> options,
            IReadOnlyDictionary<string, int> skills,
            int level
        ) =>
            predicate switch
            {
                PreparedConstantPredicate value => value.Value,
                PreparedOptionPredicate value => options.Contains(value.Option),
                PreparedNumericAtLeastPredicate value => (
                    value.Kind == PreparedNumericFactKind.Level ? level
                    : skills.TryGetValue(value.Key, out int rank) ? rank
                    : 0
                ) >= value.Minimum,
                PreparedAllPredicate value => value.Children.All(child =>
                    Evaluate(child, options, skills, level)
                ),
                PreparedAnyPredicate value => value.Children.Any(child =>
                    Evaluate(child, options, skills, level)
                ),
                PreparedNotPredicate value => !Evaluate(value.Child, options, skills, level),
                _ => false,
            };
    }
}
