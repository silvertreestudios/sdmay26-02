using System;
using System.Globalization;

namespace Game.DungeonGeneration
{
    /// <summary>Identifies a stable random stream reserved for one generation concern.</summary>
    public enum DungeonSeedSubstream
    {
        /// <summary>Controls rooms, doors, corridors, stairs, and topology retries.</summary>
        Topology = 0,
        /// <summary>Controls cosmetic and prop placement without changing topology.</summary>
        Decoration = 1,
        /// <summary>Controls encounter planning without changing topology or decoration.</summary>
        Encounter = 2,
        /// <summary>Controls deterministic retry derivation after rejected topology.</summary>
        Retry = 3
    }

    /// <summary>Supplies deterministic unsigned values without depending on a game engine.</summary>
    public interface IDungeonRandom
    {
        /// <summary>Returns the next uniformly distributed 64-bit value.</summary>
        ulong NextUInt64();

        /// <summary>Returns a value greater than or equal to zero and less than <paramref name="exclusiveMaximum"/>.</summary>
        int NextInt(int exclusiveMaximum);

        /// <summary>Returns whether a draw falls below an integer percentage from zero through one hundred.</summary>
        bool NextPercent(int percentage);
    }

    /// <summary>SplitMix64 random source used by dungeon generation and its tests.</summary>
    public sealed class SplitMix64DungeonRandom : IDungeonRandom
    {
        private ulong state;

        /// <summary>Creates a source at the exact supplied 64-bit state.</summary>
        /// <param name="state">The stable unsigned state; every bit is significant.</param>
        public SplitMix64DungeonRandom(ulong state)
        {
            this.state = state;
        }

        /// <inheritdoc/>
        public ulong NextUInt64()
        {
            state += DungeonSeedSequence.SplitMixIncrement;
            return DungeonSeedSequence.Mix(state);
        }

        /// <inheritdoc/>
        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));

            // Rejection avoids modulo bias and is stable on every .NET platform.
            ulong bound = (ulong)exclusiveMaximum;
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong value = NextUInt64();
                if (value >= threshold)
                    return (int)(value % bound);
            }
        }

        /// <inheritdoc/>
        public bool NextPercent(int percentage)
        {
            if (percentage < 0 || percentage > 100)
                throw new ArgumentOutOfRangeException(nameof(percentage));
            return percentage == 100 || (percentage > 0 && NextInt(100) < percentage);
        }
    }

    /// <summary>
    /// Derives depth, concern, and retry states from a signed run seed while preserving its exact bit pattern.
    /// </summary>
    public static class DungeonSeedSequence
    {
        internal const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;

        private static readonly ulong[] SubstreamSalts =
        {
            0xD1B54A32D192ED03UL,
            0xABC98388FB8FAC03UL,
            0x8CB92BA72F3D8DD7UL,
            0xDB4F0B9175AE2165UL
        };

        /// <summary>Returns the stable state for a dungeon depth.</summary>
        /// <remarks>Depth zero is exactly the signed seed's 64-bit representation. Each later depth advances SplitMix64 once.</remarks>
        public static ulong ForDepth(long runSeed, int depth)
        {
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth));

            ulong state = unchecked((ulong)runSeed);
            for (int index = 0; index < depth; index++)
            {
                state += SplitMixIncrement;
                state = Mix(state);
            }

            return state;
        }

        /// <summary>Derives a named concern stream for a depth without consuming any other stream.</summary>
        public static ulong ForSubstream(long runSeed, int depth, DungeonSeedSubstream substream)
        {
            int index = (int)substream;
            if (index < 0 || index >= SubstreamSalts.Length)
                throw new ArgumentOutOfRangeException(nameof(substream));
            return Mix(ForDepth(runSeed, depth) ^ SubstreamSalts[index]);
        }

        /// <summary>Derives the topology stream for a zero-based retry attempt.</summary>
        /// <remarks>Attempt zero uses the named topology stream. Later attempts combine the reserved retry stream and attempt number.</remarks>
        public static ulong ForTopologyAttempt(long runSeed, int depth, int attempt)
        {
            if (attempt < 0)
                throw new ArgumentOutOfRangeException(nameof(attempt));
            ulong topology = ForSubstream(runSeed, depth, DungeonSeedSubstream.Topology);
            if (attempt == 0)
                return topology;
            ulong retry = ForSubstream(runSeed, depth, DungeonSeedSubstream.Retry);
            return Mix(topology ^ retry ^ (SplitMixIncrement * (ulong)attempt));
        }

        /// <summary>Formats an unsigned state as fixed-width lowercase hexadecimal for metadata and diagnostics.</summary>
        public static string FormatState(ulong state)
        {
            return state.ToString("x16", CultureInfo.InvariantCulture);
        }

        internal static ulong Mix(ulong value)
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
