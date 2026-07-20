using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>
    /// Returns predeclared typed outcomes in call order so workflow tests can prove sequencing and
    /// short-circuit behavior without introducing a production player or AI selection contract.
    /// </summary>
    internal sealed class ScriptedSelectionResolver : ISelectionResolver
    {
        private readonly Queue<object> outcomes;
        private readonly List<object> requests = new List<object>();

        public ScriptedSelectionResolver(params object[] outcomes)
        {
            if (outcomes == null)
                throw new ArgumentNullException(nameof(outcomes));
            this.outcomes = new Queue<object>(outcomes);
        }

        public IReadOnlyList<object> Requests => requests;

        public int Remaining => outcomes.Count;

        public ValueTask<SelectionOutcome<TSelection>> Select<TSelection>(
            ActionSelectionRequest<TSelection> request,
            CancellationToken cancellationToken
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (outcomes.Count == 0)
                throw new InvalidOperationException("No scripted selection outcome remains.");

            object outcome = outcomes.Dequeue();
            requests.Add(request);
            if (outcome is Task<SelectionOutcome<TSelection>> pending)
                return new ValueTask<SelectionOutcome<TSelection>>(pending);
            if (outcome == null)
                return new ValueTask<SelectionOutcome<TSelection>>(
                    (SelectionOutcome<TSelection>)null
                );
            if (!(outcome is SelectionOutcome<TSelection> typed))
                throw new InvalidOperationException(
                    $"Scripted outcome does not produce {typeof(TSelection).Name}."
                );
            return new ValueTask<SelectionOutcome<TSelection>>(typed);
        }
    }

    /// <summary>
    /// Supplies a named test-only validation predicate for the generic request contract.
    /// </summary>
    /// <typeparam name="TSelection">The value validated by the request.</typeparam>
    internal sealed class TestActionSelectionRequest<TSelection>
        : ActionSelectionRequest<TSelection>
    {
        private readonly Func<TSelection, bool> accepts;

        public TestActionSelectionRequest(string name, Func<TSelection, bool> accepts)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A test request name is required.", nameof(name));
            Name = name;
            this.accepts = accepts ?? throw new ArgumentNullException(nameof(accepts));
        }

        public string Name { get; }

        public override bool Accepts(TSelection selection) => accepts(selection);
    }
}
