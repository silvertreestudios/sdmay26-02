using System;
using System.Collections.Generic;
using System.Linq;
using Game.Rules.Runtime;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>Feature-owned cue selection for committed Strike outcomes.</summary>
    public static class StrikeVfxCueSelector
    {
        private static readonly IReadOnlyDictionary<string, string> Families = new Dictionary<
            string,
            string
        >(StringComparer.OrdinalIgnoreCase)
        {
            ["Unarmed Strike"] = "unarmed",
            ["Dogslicer"] = "slash",
            ["Scimitar"] = "slash",
            ["Longsword"] = "slash",
            ["Greataxe"] = "heavy-slash",
            ["Spear"] = "piercing",
            ["Mace"] = "bludgeoning",
            ["Shortbow"] = "bow",
            ["Sling"] = "sling",
        };

        /// <summary>Gets every supported named Strike profile.</summary>
        public static IReadOnlyCollection<string> SupportedProfiles => Families.Keys.ToArray();

        /// <summary>Gets the wind-up or projectile cue for a supported profile.</summary>
        public static VfxCueId GetTravel(string label) =>
            new("strike/" + GetFamily(label) + "/travel");

        /// <summary>Gets the committed contact cue, or no value for a miss.</summary>
        public static bool TryGetImpact(string label, DegreeOfSuccess degree, out VfxCueId cue)
        {
            if (degree is DegreeOfSuccess.Failure or DegreeOfSuccess.CriticalFailure)
            {
                cue = default;
                return false;
            }
            cue = new VfxCueId(
                "strike/"
                    + GetFamily(label)
                    + (degree == DegreeOfSuccess.CriticalSuccess ? "/critical" : "/hit")
            );
            return true;
        }

        /// <summary>Gets conditional accents only when resolved typed damage retained their source.</summary>
        public static IReadOnlyList<VfxCueId> GetContributionAccents(StrikeResolution resolution)
        {
            if (resolution == null)
                throw new ArgumentNullException(nameof(resolution));
            if (!resolution.Hit)
                return Array.Empty<VfxCueId>();
            HashSet<string> sources = resolution
                .Damage.SelectMany(part => part.Sources)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return GetContributionAccents(sources, resolution.Hit);
        }

        /// <summary>
        /// Gets conditional accents from committed contribution sources without recalculating damage.
        /// </summary>
        public static IReadOnlyList<VfxCueId> GetContributionAccents(
            IEnumerable<string> committedSources,
            bool hit
        )
        {
            if (committedSources == null)
                throw new ArgumentNullException(nameof(committedSources));
            if (!hit)
                return Array.Empty<VfxCueId>();
            HashSet<string> sources = committedSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<VfxCueId> cues = new();
            if (sources.Contains("sneak-attack"))
                cues.Add(new VfxCueId("auxiliary/sneak-attack"));
            if (sources.Contains("infuse-vitality"))
                cues.Add(new VfxCueId("auxiliary/infuse-vitality-strike"));
            return cues;
        }

        private static string GetFamily(string label) =>
            Families.TryGetValue(label ?? string.Empty, out string family)
                ? family
                : throw new KeyNotFoundException($"Strike profile '{label}' has no VFX family.");
    }

    /// <summary>Feature-owned cue selection for the eight production-supported spells.</summary>
    public static class SpellVfxCueSelector
    {
        private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
        {
            "light",
            "divine-lance",
            "shield",
            "guidance",
            "haunting-hymn",
            "bless",
            "infuse-vitality",
            "heal",
        };

        /// <summary>Gets every supported spell identifier.</summary>
        public static IReadOnlyCollection<string> SupportedSpells => Supported;

        /// <summary>Gets the cast wind-up cue for one supported spell.</summary>
        public static VfxCueId GetCast(SpellId spell)
        {
            Require(spell);
            return new VfxCueId("spell/" + spell.Value + "/cast");
        }

        /// <summary>Gets the result cue for an action variant and optional save outcome.</summary>
        public static VfxCueId GetResult(
            SpellId spell,
            int actions,
            DegreeOfSuccess? degree = null,
            bool livingTarget = true
        )
        {
            Require(spell);
            string suffix = spell.Value switch
            {
                "heal" => actions
                    + "-action-"
                    + (livingTarget ? "living" : "undead-" + Degree(degree)),
                "haunting-hymn" => Degree(degree),
                "divine-lance" => "projectile",
                "light" or "shield" or "guidance" or "bless" => "persistent",
                _ => actions + "-action",
            };
            return new VfxCueId("spell/" + spell.Value + "/" + suffix);
        }

        /// <summary>Gets the contact cue for a successful spell attack.</summary>
        public static VfxCueId GetAttackImpact(SpellId spell, DegreeOfSuccess degree)
        {
            Require(spell);
            if (degree is not (DegreeOfSuccess.Success or DegreeOfSuccess.CriticalSuccess))
                throw new ArgumentOutOfRangeException(
                    nameof(degree),
                    "A miss has no target impact cue."
                );
            return new VfxCueId(
                "spell/"
                    + spell.Value
                    + (degree == DegreeOfSuccess.CriticalSuccess ? "/critical" : "/hit")
            );
        }

        /// <summary>Gets the persistent cue for a supported lasting spell.</summary>
        public static bool TryGetPersistent(SpellId spell, out VfxCueId cue)
        {
            string id = spell.Value;
            if (id is "light" or "shield" or "guidance" or "bless" or "infuse-vitality")
            {
                cue = new VfxCueId("spell/" + id + "/persistent");
                return true;
            }
            cue = default;
            return false;
        }

        private static string Degree(DegreeOfSuccess? degree) =>
            degree switch
            {
                DegreeOfSuccess.CriticalSuccess => "critical-success",
                DegreeOfSuccess.Success => "success",
                DegreeOfSuccess.Failure => "failure",
                DegreeOfSuccess.CriticalFailure => "critical-failure",
                _ => "success",
            };

        private static void Require(SpellId spell)
        {
            if (!Supported.Contains(spell.Value))
                throw new KeyNotFoundException(
                    $"Spell '{spell.Value}' has no production VFX mapping."
                );
        }
    }
}
