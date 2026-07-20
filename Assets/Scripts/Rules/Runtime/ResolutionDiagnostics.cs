using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Produces a human-readable view of traced operations, completion states, and direct facts.
    /// </summary>
    /// <remarks>
    /// Diagnostics are intended for logs and debugging rather than machine parsing. The compact
    /// representation is generated from the associated <see cref="ResolutionTrace"/> on demand.
    /// </remarks>
    public sealed class ResolutionDiagnostics
    {
        private readonly Dictionary<OpId, DiagnosticCompletion> completions =
            new Dictionary<OpId, DiagnosticCompletion>();
        private readonly ResolutionTrace trace;

        internal ResolutionDiagnostics(ResolutionTrace trace) =>
            this.trace = trace ?? throw new ArgumentNullException(nameof(trace));

        /// <summary>
        /// Gets an indented operation tree ordered by operation identifier.
        /// </summary>
        /// <remarks>
        /// Completed operations include their status and directly emitted facts. An operation that
        /// is still executing appears without a completion suffix.
        /// </remarks>
        public string Compact
        {
            get
            {
                List<string> lines = new List<string>();
                foreach (IOpFrameView frame in trace.OrderedFrames)
                {
                    int depth = Depth(frame);
                    string prefix = new string(' ', depth * 2);
                    string relation = frame.ParentId.HasValue
                        ? $" parent={frame.ParentId.Value.Value} cause={frame.CauseId.Value.Value}"
                        : " root";
                    completions.TryGetValue(frame.Id, out DiagnosticCompletion completion);
                    string result = completion == null ? string.Empty : $" -> {completion.Status}";
                    lines.Add(
                        $"{prefix}[op {frame.Id.Value}{relation}] {frame.OpType.Name}{result}"
                    );

                    if (frame.IsAction)
                        lines.Add($"{prefix}  profile: {frame.ActionProfile.ToDiagnosticString()}");

                    foreach (ResolutionRoll roll in trace.GetRolls(frame.Id))
                    {
                        lines.Add(
                            $"{prefix}  roll {roll.Sequence}: {roll.Dice} -> "
                                + $"[{string.Join(", ", roll.Result.Values)}] total={roll.Result.Total}"
                        );
                    }

                    if (completion == null)
                        continue;
                    foreach (RuleFact fact in completion.DirectFacts)
                    {
                        lines.Add(
                            $"{prefix}  [fact {fact.Id.Value}] {fact.GetType().Name} "
                                + $"source={fact.SourceOpId.Value} root={fact.RootOpId.Value}"
                        );
                    }
                }
                return string.Join("\n", lines);
            }
        }

        internal void Complete(OpId id, OpStatus status, IReadOnlyList<RuleFact> directFacts)
        {
            if (completions.ContainsKey(id))
                throw new InvalidOperationException(
                    $"Operation {id.Value} completed more than once."
                );
            completions.Add(id, new DiagnosticCompletion(status, directFacts));
        }

        private int Depth(IOpFrameView frame)
        {
            int depth = 0;
            HashSet<OpId> visited = new HashSet<OpId> { frame.Id };
            while (frame.ParentId.HasValue)
            {
                if (!visited.Add(frame.ParentId.Value))
                    throw new InvalidOperationException("A cycle exists in operation ancestry.");
                frame = trace.Require(frame.ParentId.Value);
                depth++;
            }
            return depth;
        }

        private sealed class DiagnosticCompletion
        {
            public OpStatus Status { get; }
            public IReadOnlyList<RuleFact> DirectFacts { get; }

            public DiagnosticCompletion(OpStatus status, IReadOnlyList<RuleFact> directFacts)
            {
                Status = status;
                DirectFacts = directFacts ?? Array.AsReadOnly(Array.Empty<RuleFact>());
            }
        }
    }
}
