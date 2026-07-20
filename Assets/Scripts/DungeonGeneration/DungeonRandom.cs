using System;

namespace Game.DungeonGeneration
{
    /// <summary>Identifies a random stream reserved for one generation concern.</summary>
    public enum DungeonSeedSubstream
    {
        /// <summary>Controls rooms, doors, corridors, stairs, and topology retries.</summary>
        Topology = 0,

        /// <summary>Controls cosmetic and prop placement without changing topology.</summary>
        Decoration = 1,

        /// <summary>Controls encounter planning without changing topology or decoration.</summary>
        Encounter = 2,

        /// <summary>Controls deterministic retry derivation after rejected topology.</summary>
        Retry = 3,
    }

    /// <summary>Supplies seeded random values without depending on Unity's global random state.</summary>
    public interface IDungeonRandom
    {
        /// <summary>Returns an integer in the half-open interval starting at zero.</summary>
        /// <param name="exclusiveMaximum">The positive upper bound that is never returned.</param>
        /// <returns>A value greater than or equal to zero and less than <paramref name="exclusiveMaximum"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="exclusiveMaximum"/> is not positive.</exception>
        int NextInt(int exclusiveMaximum);

        /// <summary>Returns whether a draw falls below an integer percentage.</summary>
        /// <param name="percentage">The inclusive percentage from zero through one hundred.</param>
        /// <returns><see langword="true"/> when the draw succeeds; zero always fails and one hundred always succeeds.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="percentage"/> is outside zero through one hundred.</exception>
        bool NextPercent(int percentage);
    }

    /// <summary>Adapts a locally owned <see cref="Random"/> instance for dungeon generation.</summary>
    public sealed class SystemDungeonRandom : IDungeonRandom
    {
        private readonly Random random;

        /// <summary>Creates an isolated random source from the supplied seed.</summary>
        /// <param name="seed">The seed passed directly to <see cref="Random(int)"/>.</param>
        public SystemDungeonRandom(int seed)
        {
            random = new Random(seed);
        }

        /// <inheritdoc/>
        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            return random.Next(exclusiveMaximum);
        }

        /// <inheritdoc/>
        public bool NextPercent(int percentage)
        {
            if (percentage < 0 || percentage > 100)
                throw new ArgumentOutOfRangeException(nameof(percentage));
            return percentage == 100 || (percentage > 0 && random.Next(100) < percentage);
        }
    }

    /// <summary>
    /// Derives isolated <see cref="Random"/> seeds for dungeon depth, concern, and retry streams.
    /// </summary>
    /// <remarks>
    /// The project pins its Unity/.NET runtime and intentionally permits breaking regenerated data
    /// when that runtime changes, so generation uses the platform random implementation instead of
    /// maintaining a project-owned pseudo-random algorithm.
    /// </remarks>
    public static class DungeonSeedSequence
    {
        private const int SeedMultiplier = 397;

        /// <summary>Returns the seed assigned to a dungeon depth.</summary>
        /// <param name="runSeed">The run seed supplied to generation.</param>
        /// <param name="depth">The nonnegative dungeon depth.</param>
        /// <returns>A constant-time seed derived from the run seed and depth.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is negative.</exception>
        public static int ForDepth(int runSeed, int depth)
        {
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth));
            return Combine(runSeed, depth);
        }

        /// <summary>Derives a named concern stream without consuming any other stream.</summary>
        /// <param name="runSeed">The run seed shared by the dungeon run.</param>
        /// <param name="depth">The nonnegative dungeon depth.</param>
        /// <param name="substream">The reserved generation concern.</param>
        /// <returns>The initial seed for an independently consumable <see cref="Random"/> source.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is negative or <paramref name="substream"/> is undefined.</exception>
        public static int ForSubstream(int runSeed, int depth, DungeonSeedSubstream substream)
        {
            if (!Enum.IsDefined(typeof(DungeonSeedSubstream), substream))
                throw new ArgumentOutOfRangeException(nameof(substream));
            return new Random(Combine(ForDepth(runSeed, depth), (int)substream + 1)).Next();
        }

        /// <summary>Derives the topology seed for a zero-based retry attempt.</summary>
        /// <param name="runSeed">The run seed shared by the dungeon run.</param>
        /// <param name="depth">The nonnegative dungeon depth.</param>
        /// <param name="attempt">The nonnegative retry index.</param>
        /// <returns>The topology seed for exactly this retry attempt.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> or <paramref name="attempt"/> is negative.</exception>
        public static int ForTopologyAttempt(int runSeed, int depth, int attempt)
        {
            if (attempt < 0)
                throw new ArgumentOutOfRangeException(nameof(attempt));
            int topology = ForSubstream(runSeed, depth, DungeonSeedSubstream.Topology);
            if (attempt == 0)
                return topology;
            int retry = ForSubstream(runSeed, depth, DungeonSeedSubstream.Retry);
            return new Random(Combine(topology, Combine(retry, attempt))).Next();
        }

        private static int Combine(int left, int right)
        {
            return unchecked((left * SeedMultiplier) ^ right);
        }
    }
}
