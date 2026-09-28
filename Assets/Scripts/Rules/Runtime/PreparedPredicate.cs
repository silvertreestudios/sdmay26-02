using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Rules.Runtime
{
    /// <summary>An immutable supported prepared-rule predicate, independent of JSON and Unity.</summary>
    public abstract class PreparedPredicate
    {
        private protected PreparedPredicate() { }

        /// <summary>Tests the current roll options without retaining or changing them.</summary>
        public abstract bool Matches(IEnumerable<string> options);

        /// <summary>Creates an option membership test. Empty options match unconditionally.</summary>
        public static PreparedPredicate Option(string option) => new OptionPredicate(option);

        /// <summary>Requires all supplied clauses; an empty conjunction matches.</summary>
        public static PreparedPredicate All(IEnumerable<PreparedPredicate> clauses) =>
            new GroupPredicate(clauses, true);

        /// <summary>Requires at least one supplied clause; an empty disjunction fails.</summary>
        public static PreparedPredicate Any(IEnumerable<PreparedPredicate> clauses) =>
            new GroupPredicate(clauses, false);

        /// <summary>Negates a clause.</summary>
        public static PreparedPredicate Not(PreparedPredicate clause) => new NotPredicate(clause);

        /// <summary>Compares an immutable prepared numeric fact against its required minimum.</summary>
        public static PreparedPredicate AtLeast(int actual, int required) =>
            new NumericPredicate(actual, required);

        private sealed class OptionPredicate : PreparedPredicate
        {
            private readonly string option;

            internal OptionPredicate(string option) => this.option = option;

            public override bool Matches(IEnumerable<string> options) =>
                string.IsNullOrWhiteSpace(option)
                || options.Contains(option, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class GroupPredicate : PreparedPredicate
        {
            private readonly PreparedPredicate[] clauses;
            private readonly bool all;

            internal GroupPredicate(IEnumerable<PreparedPredicate> clauses, bool all)
            {
                this.clauses = (
                    clauses ?? throw new ArgumentNullException(nameof(clauses))
                ).ToArray();
                if (this.clauses.Any(clause => clause == null))
                    throw new ArgumentException(
                        "Predicate clauses cannot contain null.",
                        nameof(clauses)
                    );
                this.all = all;
            }

            public override bool Matches(IEnumerable<string> options) =>
                all
                    ? clauses.All(clause => clause.Matches(options))
                    : clauses.Any(clause => clause.Matches(options));
        }

        private sealed class NotPredicate : PreparedPredicate
        {
            private readonly PreparedPredicate clause;

            internal NotPredicate(PreparedPredicate clause) =>
                this.clause = clause ?? throw new ArgumentNullException(nameof(clause));

            public override bool Matches(IEnumerable<string> options) => !clause.Matches(options);
        }

        private sealed class NumericPredicate : PreparedPredicate
        {
            private readonly int actual;
            private readonly int required;

            internal NumericPredicate(int actual, int required)
            {
                this.actual = actual;
                this.required = required;
            }

            public override bool Matches(IEnumerable<string> options) => actual >= required;
        }
    }
}
