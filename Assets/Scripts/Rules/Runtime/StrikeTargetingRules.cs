using System;
using System.Collections.Generic;

namespace Game.Rules.Runtime
{
    /// <summary>Cover categories shared by scene extraction and pure Strike policy.</summary>
    public enum StrikeCover
    {
        None,
        Lesser,
        Standard,
        Greater,
    }

    /// <summary>Owns range and cover policy for both Strike previews and execution.</summary>
    public static class StrikeTargetingRules
    {
        /// <summary>Checks enemy eligibility using enrolled factions and explicit encounter relationships.</summary>
        public static bool IsEnemy(
            RulesSnapshot snapshot,
            CreatureId actor,
            CreatureId target,
            ICombatantFriendshipProvider friendships
        ) =>
            actor != target
            && snapshot.Creatures.TryGet(actor, out CreatureState attacker)
            && snapshot.Creatures.TryGet(target, out CreatureState defender)
            && attacker.Player != defender.Player
            && !friendships.IsFriendly(attacker.Player, defender.Player);

        /// <summary>Gets the reach, fixed spell range, or six-increment weapon range.</summary>
        public static int MaximumRangeFeet(
            bool isRanged,
            int reachFeet,
            int incrementFeet,
            int fixedRangeFeet = 0
        ) =>
            isRanged
                ? fixedRangeFeet > 0
                    ? fixedRangeFeet
                    : incrementFeet * 6
                : reachFeet;

        /// <summary>Checks the inclusive range boundary, rejecting ranged weapons without an increment.</summary>
        public static bool IsWithinRange(
            int distanceFeet,
            bool isRanged,
            int reachFeet,
            int incrementFeet,
            int fixedRangeFeet = 0
        ) =>
            (!isRanged || fixedRangeFeet > 0 || incrementFeet > 0)
            && distanceFeet <= MaximumRangeFeet(isRanged, reachFeet, incrementFeet, fixedRangeFeet);

        /// <summary>Calculates the untyped penalty beyond the first weapon increment.</summary>
        public static int RangePenalty(int distanceFeet, int incrementFeet)
        {
            if (incrementFeet <= 0 || distanceFeet <= incrementFeet)
                return 0;
            int increment = (int)Math.Ceiling((double)distanceFeet / incrementFeet);
            if (increment > 6)
                throw new ArgumentOutOfRangeException(
                    nameof(distanceFeet),
                    "Ranged Strikes cannot target beyond six range increments."
                );
            return -2 * (increment - 1);
        }

        /// <summary>Interprets the existing sixteen-ray scene sample as ranged cover.</summary>
        public static StrikeCover CoverFromRays(bool isRanged, int clearRays) =>
            isRanged && clearRays > 0 && clearRays < 16 ? StrikeCover.Standard : StrikeCover.None;

        /// <summary>Gets cover's circumstance bonus to Armor Class.</summary>
        public static int CoverArmorClassBonus(StrikeCover cover) =>
            cover switch
            {
                StrikeCover.Lesser => 1,
                StrikeCover.Standard => 2,
                StrikeCover.Greater => 4,
                _ => 0,
            };

        /// <summary>Creates typed cover and Off-Guard candidates; the check runtime owns stacking.</summary>
        public static IReadOnlyList<Modifier> ArmorClassModifiers(
            LegalStrikeTargetingOutcome targeting
        )
        {
            List<Modifier> modifiers = new();
            if (targeting.CoverBonus != 0)
                modifiers.Add(
                    new Modifier(
                        targeting.CoverBonus,
                        ModifierType.Circumstance,
                        RuleSource.FromSlug("cover"),
                        Statistic.ArmorClass
                    )
                );
            if (targeting.OffGuard)
                modifiers.Add(
                    new Modifier(
                        -2,
                        ModifierType.Circumstance,
                        RuleSource.FromSlug("off-guard"),
                        Statistic.ArmorClass
                    )
                );
            return modifiers.AsReadOnly();
        }
    }
}
