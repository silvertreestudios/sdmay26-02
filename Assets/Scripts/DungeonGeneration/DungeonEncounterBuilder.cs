using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.DungeonGeneration
{
    /// <summary>Describes one existing creature definition eligible for encounter composition.</summary>
    public sealed class DungeonEncounterCandidate
    {
        /// <summary>Creates a rules-only candidate without content-loading metadata.</summary>
        /// <param name="id">The stable non-empty creature content ID.</param>
        /// <param name="level">The creature's PF2e level.</param>
        public DungeonEncounterCandidate(string id, int level)
            : this(id, level, string.Empty, string.Empty) { }

        /// <summary>Creates a candidate backed by an existing creature resource and prefab.</summary>
        /// <param name="id">The stable non-empty creature content ID.</param>
        /// <param name="level">The creature's PF2e level.</param>
        /// <param name="resourcePath">The project-relative Resources path without extension.</param>
        /// <param name="prefabPath">The project-relative prefab asset path.</param>
        /// <exception cref="ArgumentException"><paramref name="id"/> is empty or whitespace.</exception>
        public DungeonEncounterCandidate(
            string id,
            int level,
            string resourcePath,
            string prefabPath
        )
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A candidate requires a stable ID.", nameof(id));
            Id = id;
            Level = level;
            ResourcePath = resourcePath ?? string.Empty;
            PrefabPath = prefabPath ?? string.Empty;
        }

        /// <summary>Gets the stable creature content ID serialized into encounter plans.</summary>
        public string Id { get; }

        /// <summary>Gets the PF2e creature level used for XP lookup.</summary>
        public int Level { get; }

        /// <summary>Gets the Resources path used by later runtime activation.</summary>
        public string ResourcePath { get; }

        /// <summary>Gets the prefab asset path used to validate existing project content.</summary>
        public string PrefabPath { get; }
    }

    /// <summary>Represents one deterministic encounter composition before spawn cells are assigned.</summary>
    public sealed class DungeonEncounterBuildResult
    {
        /// <summary>Creates an immutable composition result.</summary>
        /// <param name="threat">The requested supported threat.</param>
        /// <param name="budget">The adjusted party-size XP budget.</param>
        /// <param name="spentXp">The XP spent by the selected composition.</param>
        /// <param name="creatureIds">The ordered creature IDs selected for spawning.</param>
        /// <exception cref="ArgumentNullException"><paramref name="creatureIds"/> is null.</exception>
        public DungeonEncounterBuildResult(
            DungeonEncounterThreat threat,
            int budget,
            int spentXp,
            IEnumerable<string> creatureIds
        )
        {
            Threat = threat;
            Budget = budget;
            SpentXp = spentXp;
            CreatureIds = Array.AsReadOnly(
                (creatureIds ?? throw new ArgumentNullException(nameof(creatureIds))).ToArray()
            );
        }

        /// <summary>Gets the requested threat.</summary>
        public DungeonEncounterThreat Threat { get; }

        /// <summary>Gets the adjusted XP budget, rather than only the amount spent.</summary>
        public int Budget { get; }

        /// <summary>Gets the XP spent by <see cref="CreatureIds"/>.</summary>
        public int SpentXp { get; }

        /// <summary>Gets the deterministic ordered creature composition.</summary>
        public IReadOnlyList<string> CreatureIds { get; }
    }

    /// <summary>Composes deterministic PF2e encounters independently of Unity and combat state.</summary>
    public interface IEncounterBuilder
    {
        /// <summary>
        /// Selects repeated candidates without exceeding the adjusted budget or room capacity,
        /// maximizing spent XP first and closeness to party size second.
        /// </summary>
        /// <param name="partyLevel">The party's PF2e level.</param>
        /// <param name="partySize">The positive player-character count.</param>
        /// <param name="threat">The supported encounter threat.</param>
        /// <param name="candidates">Unique existing creature candidates.</param>
        /// <param name="roomCapacity">The nonnegative number of available distinct spawn cells.</param>
        /// <param name="rng">The encounter substream used only for remaining deterministic ties.</param>
        /// <returns>An immutable composition; unsatisfiable inputs produce an empty composition.</returns>
        DungeonEncounterBuildResult Build(
            int partyLevel,
            int partySize,
            DungeonEncounterThreat threat,
            IReadOnlyList<DungeonEncounterCandidate> candidates,
            int roomCapacity,
            IDungeonRandom rng
        );
    }

    /// <summary>Uses bounded dynamic programming to implement <see cref="IEncounterBuilder"/>.</summary>
    public sealed class DungeonEncounterBuilder : IEncounterBuilder
    {
        /// <inheritdoc/>
        public DungeonEncounterBuildResult Build(
            int partyLevel,
            int partySize,
            DungeonEncounterThreat threat,
            IReadOnlyList<DungeonEncounterCandidate> candidates,
            int roomCapacity,
            IDungeonRandom rng
        )
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (roomCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(roomCapacity));

            int budget = DungeonEncounterRules.GetBudget(partySize, threat);
            DungeonEncounterCandidate[] ordered = candidates
                .OrderBy(candidate => candidate?.Id, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Any(candidate => candidate == null))
                throw new ArgumentException(
                    "Candidate collections cannot contain null entries.",
                    nameof(candidates)
                );
            if (
                ordered.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count()
                != ordered.Length
            )
            {
                throw new ArgumentException("Candidate IDs must be unique.", nameof(candidates));
            }

            List<Option> options = ordered
                .Select(candidate =>
                {
                    bool supported = DungeonEncounterRules.TryGetCreatureXp(
                        partyLevel,
                        candidate.Level,
                        out int xp
                    );
                    return new Option(candidate.Id, supported ? xp : 0, supported);
                })
                .Where(option => option.IsSupported && option.Xp <= budget)
                .ToList();
            if (roomCapacity == 0 || budget == 0 || options.Count == 0)
                return new DungeonEncounterBuildResult(threat, budget, 0, Array.Empty<string>());

            // The shuffled preference is derived after ordinal normalization, so candidate input
            // order cannot change either random consumption or tie outcomes.
            Shuffle(options, rng);
            int minimumXp = options.Min(option => option.Xp);
            int capacity = Math.Min(roomCapacity, budget / minimumXp);
            Dictionary<int, CompositionState>[] states = new Dictionary<int, CompositionState>[
                capacity + 1
            ];
            // Each state is keyed by exact (creature count, spent XP). Because every transition
            // adds one positive-XP creature, retaining the first predecessor for a key cannot
            // discard a composition that is better under either optimization criterion. The
            // normalized randomized option order decides only compositions tied on both values.
            states[0] = new Dictionary<int, CompositionState>
            {
                [0] = new CompositionState(null, null, 0, 0),
            };

            for (int count = 0; count < capacity; count++)
            {
                states[count + 1] = new Dictionary<int, CompositionState>();
                foreach (
                    KeyValuePair<int, CompositionState> state in states[count]
                        .OrderBy(pair => pair.Key)
                )
                {
                    foreach (Option option in options)
                    {
                        int spent = state.Key + option.Xp;
                        if (spent <= budget && !states[count + 1].ContainsKey(spent))
                        {
                            states[count + 1]
                                .Add(
                                    spent,
                                    new CompositionState(state.Value, option, count + 1, spent)
                                );
                        }
                    }
                }
            }

            int bestSpent = states.Max(group => group.Keys.Max());
            int bestDistance = states
                .Where(group => group.ContainsKey(bestSpent))
                .Min(group => Math.Abs(group[bestSpent].Count - partySize));
            CompositionState[] finalists = states
                .Where(group => group.ContainsKey(bestSpent))
                .Select(group => group[bestSpent])
                .Where(state => Math.Abs(state.Count - partySize) == bestDistance)
                .ToArray();
            CompositionState chosen =
                finalists.Length == 1 ? finalists[0] : finalists[rng.NextInt(finalists.Length)];

            List<string> ids = new();
            for (CompositionState state = chosen; state.Option != null; state = state.Previous)
                ids.Add(state.Option.Id);
            ids.Reverse();
            return new DungeonEncounterBuildResult(threat, budget, chosen.SpentXp, ids);
        }

        private static void Shuffle<T>(IList<T> values, IDungeonRandom random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.NextInt(index + 1);
                (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
            }
        }

        private sealed class Option
        {
            internal Option(string id, int xp, bool isSupported)
            {
                Id = id;
                Xp = xp;
                IsSupported = isSupported;
            }

            internal string Id { get; }
            internal int Xp { get; }
            internal bool IsSupported { get; }
        }

        private sealed class CompositionState
        {
            internal CompositionState(
                CompositionState previous,
                Option option,
                int count,
                int spentXp
            )
            {
                Previous = previous;
                Option = option;
                Count = count;
                SpentXp = spentXp;
            }

            internal CompositionState Previous { get; }
            internal Option Option { get; }
            internal int Count { get; }
            internal int SpentXp { get; }
        }
    }
}
