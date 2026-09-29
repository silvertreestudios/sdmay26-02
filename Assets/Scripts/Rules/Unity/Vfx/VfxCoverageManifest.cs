using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Rules.Unity.Vfx
{
    /// <summary>One machine-readable gallery and production-coverage record.</summary>
    [Serializable]
    public sealed class VfxCoverageEntry
    {
        /// <summary>Stable copyable feedback identifier.</summary>
        public string id;

        /// <summary>Player-facing name.</summary>
        public string displayName;

        /// <summary>Spell variant, Strike outcome, or lifecycle phase.</summary>
        public string variant;

        /// <summary>Committed outcome represented by the fixture.</summary>
        public string outcome;

        /// <summary>Whether normal production content can reach this exact profile.</summary>
        public bool productionReachable;

        /// <summary>Exact Resources-relative production prefab cue.</summary>
        public string cue;

        /// <summary>Production presenter or committed Fact that selects this cue.</summary>
        public string gameplayTrigger;

        /// <summary>Automated verification covering the mapping or lifecycle.</summary>
        public string evidence;

        /// <summary>Grouping used by gallery filtering and coverage reconciliation.</summary>
        public string category;
    }

    /// <summary>Committed coverage manifest for the production VFX and review gallery.</summary>
    [Serializable]
    public sealed class VfxCoverageManifest
    {
        /// <summary>All supported gallery entries.</summary>
        public List<VfxCoverageEntry> entries = new();

        /// <summary>Explicitly unsupported content that must not be counted as implemented.</summary>
        public List<string> unsupported = new();

        /// <summary>Loads and validates the committed Resources manifest.</summary>
        public static VfxCoverageManifest Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("Vfx/vfx-coverage-manifest");
            if (asset == null)
                throw new InvalidOperationException("The VFX coverage manifest is missing.");
            VfxCoverageManifest manifest = JsonUtility.FromJson<VfxCoverageManifest>(asset.text);
            if (manifest == null || manifest.entries == null || manifest.entries.Count == 0)
                throw new InvalidOperationException(
                    "The VFX coverage manifest contains no entries."
                );
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (VfxCoverageEntry entry in manifest.entries)
            {
                if (
                    entry == null
                    || string.IsNullOrWhiteSpace(entry.id)
                    || string.IsNullOrWhiteSpace(entry.cue)
                    || string.IsNullOrWhiteSpace(entry.gameplayTrigger)
                    || string.IsNullOrWhiteSpace(entry.evidence)
                )
                    throw new InvalidOperationException(
                        "Every VFX coverage entry requires an ID, cue, gameplay trigger, and evidence."
                    );
                if (!ids.Add(entry.id))
                    throw new InvalidOperationException(
                        $"The VFX coverage manifest repeats gallery ID '{entry.id}'."
                    );
            }
            return manifest;
        }
    }
}
