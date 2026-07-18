using System;

namespace Game.DungeonGeneration
{
    /// <summary>
    /// Provides the PF2e encounter budgets, creature XP values, and depth-based threat mix used
    /// by deterministic dungeon encounter planning.
    /// </summary>
    public static class DungeonEncounterRules
    {
        private static readonly int[] CreatureXpByRelativeLevel =
        {
            10, 15, 20, 30, 40, 60, 80, 120, 160
        };

        /// <summary>Calculates the adjusted XP budget for a party and supported threat.</summary>
        /// <param name="partySize">The positive number of player characters.</param>
        /// <param name="threat">The supported Trivial, Low, or Moderate threat.</param>
        /// <returns>The nonnegative PF2e budget after adjusting from the four-character baseline.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="partySize"/> is not positive, <paramref name="threat"/> is undefined,
        /// or the adjusted budget cannot be represented by <see cref="int"/>.
        /// </exception>
        public static int GetBudget(int partySize, DungeonEncounterThreat threat)
        {
            if (partySize <= 0)
                throw new ArgumentOutOfRangeException(nameof(partySize));

            int baseline;
            int adjustment;
            switch (threat)
            {
                case DungeonEncounterThreat.Trivial:
                    baseline = 40;
                    adjustment = 10;
                    break;
                case DungeonEncounterThreat.Low:
                    baseline = 60;
                    // Remastered GM Core Table 10-1 uses a 20-XP character adjustment.
                    adjustment = 20;
                    break;
                case DungeonEncounterThreat.Moderate:
                    baseline = 80;
                    adjustment = 20;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(threat));
            }

            long adjusted = baseline + ((long)partySize - 4L) * adjustment;
            if (adjusted > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(partySize));
            return (int)Math.Max(0L, adjusted);
        }

        /// <summary>Looks up a creature's PF2e encounter XP from its level difference.</summary>
        /// <param name="partyLevel">The party's level.</param>
        /// <param name="creatureLevel">The candidate creature's level.</param>
        /// <param name="xp">Receives the creature XP when the difference is supported.</param>
        /// <returns>
        /// <see langword="true"/> for differences from party level -4 through +4; otherwise
        /// <see langword="false"/> and zero XP.
        /// </returns>
        public static bool TryGetCreatureXp(int partyLevel, int creatureLevel, out int xp)
        {
            long difference = (long)creatureLevel - partyLevel;
            if (difference < -4 || difference > 4)
            {
                xp = 0;
                return false;
            }

            xp = CreatureXpByRelativeLevel[(int)difference + 4];
            return true;
        }

        /// <summary>Selects the dungeon threat distribution assigned to a depth.</summary>
        /// <param name="depth">The nonnegative zero-based dungeon depth.</param>
        /// <param name="random">The encounter substream used for the percentile draw.</param>
        /// <returns>A threat drawn from the depth's Trivial/Low/Moderate distribution.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is negative.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
        public static DungeonEncounterThreat SelectThreat(int depth, IDungeonRandom random)
        {
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            int trivialPercent = Math.Max(10, 50 - Math.Min(depth, 8) * 5);
            int draw = random.NextInt(100);
            if (draw < trivialPercent)
                return DungeonEncounterThreat.Trivial;
            if (draw < trivialPercent + 35)
                return DungeonEncounterThreat.Low;
            return DungeonEncounterThreat.Moderate;
        }
    }
}
