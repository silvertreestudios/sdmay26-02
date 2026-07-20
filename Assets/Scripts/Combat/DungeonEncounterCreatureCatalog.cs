using System;
using System.Collections.Generic;
using System.Linq;
using GridPublic;
using UnityEngine;

namespace Game.Combat.Encounters
{
    /// <summary>Maps one stable encounter content ID to its runtime JSON and prefab inputs.</summary>
    [Serializable]
    public sealed class DungeonEncounterCreatureCatalogEntry
    {
        [SerializeField]
        private string contentId = string.Empty;

        [SerializeField]
        private string resourcePath = string.Empty;

        [SerializeField]
        private GameObject prefab;

        /// <summary>Creates a serializable catalog entry.</summary>
        /// <param name="contentId">The stable creature content ID used by generated plans.</param>
        /// <param name="resourcePath">The extension-free path beneath a Resources folder.</param>
        /// <param name="prefab">The existing creature prefab instantiated at runtime.</param>
        /// <remarks>
        /// Asset deserialization bypasses this constructor. The containing catalog therefore owns
        /// complete validation through <see cref="DungeonEncounterCreatureCatalog.ValidateOrThrow"/>.
        /// </remarks>
        public DungeonEncounterCreatureCatalogEntry(
            string contentId,
            string resourcePath,
            GameObject prefab
        )
        {
            this.contentId = contentId;
            this.resourcePath = resourcePath;
            this.prefab = prefab;
        }

        /// <summary>Gets the stable content ID.</summary>
        public string ContentId => contentId;

        /// <summary>Gets the Resources-relative JSON path.</summary>
        public string ResourcePath => resourcePath;

        /// <summary>Gets the existing creature prefab reference.</summary>
        public GameObject Prefab => prefab;
    }

    /// <summary>
    /// Resolves generated encounter creature IDs to validated runtime JSON and prefab inputs.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Dungeon/Runtime Encounter Creature Catalog",
        fileName = "DungeonEncounterCreatureCatalog"
    )]
    public sealed class DungeonEncounterCreatureCatalog : ScriptableObject
    {
        /// <summary>The Resources path of the project-owned runtime encounter catalog.</summary>
        public const string DefaultResourcesPath =
            "DataFiles/dungeon/DungeonEncounterCreatureCatalog";

        /// <summary>The concrete hostile team assigned to every generated encounter enemy.</summary>
        public const string HostileTeamName = "Enemies";

        [SerializeField]
        private List<DungeonEncounterCreatureCatalogEntry> entries = new();

        private Dictionary<string, DungeonEncounterCreatureCatalogEntry> entriesByContentId;

        /// <summary>Gets a snapshot of the authored entries without validating them.</summary>
        public IReadOnlyList<DungeonEncounterCreatureCatalogEntry> Entries =>
            Array.AsReadOnly(entries.ToArray());

        /// <summary>
        /// Validates every entry and builds the exact ordinal lookup used by materialization.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// An entry is missing, duplicated, has blank metadata, references missing JSON, references
        /// no prefab, lacks a root <see cref="ActionController"/>, <see cref="Token"/>, or
        /// <see cref="Team"/>, or includes a preconfigured encounter-member component.
        /// </exception>
        public void ValidateOrThrow()
        {
            Dictionary<string, DungeonEncounterCreatureCatalogEntry> validated = new(
                StringComparer.Ordinal
            );
            for (int index = 0; index < entries.Count; index++)
            {
                DungeonEncounterCreatureCatalogEntry entry = entries[index];
                if (entry == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog entry {index} is missing."
                    );
                if (string.IsNullOrWhiteSpace(entry.ContentId))
                    throw new InvalidOperationException(
                        $"Encounter catalog entry {index} requires a non-empty content ID."
                    );
                if (
                    !string.Equals(
                        entry.ContentId,
                        entry.ContentId.Trim(),
                        StringComparison.Ordinal
                    )
                )
                    throw new InvalidOperationException(
                        $"Encounter catalog content ID '{entry.ContentId}' cannot contain leading or trailing whitespace."
                    );
                if (!validated.TryAdd(entry.ContentId, entry))
                    throw new InvalidOperationException(
                        $"Encounter catalog content ID '{entry.ContentId}' is duplicated."
                    );
                if (string.IsNullOrWhiteSpace(entry.ResourcePath))
                    throw new InvalidOperationException(
                        $"Encounter catalog entry '{entry.ContentId}' requires a Resources JSON path."
                    );
                if (Resources.Load<TextAsset>(entry.ResourcePath) == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog entry '{entry.ContentId}' cannot load creature JSON at Resources/{entry.ResourcePath}."
                    );
                if (entry.Prefab == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog entry '{entry.ContentId}' requires a creature prefab."
                    );
                if (entry.Prefab.GetComponent<ActionController>() == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog prefab '{entry.Prefab.name}' for '{entry.ContentId}' requires an ActionController on its root."
                    );
                if (entry.Prefab.GetComponent<Token>() == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog prefab '{entry.Prefab.name}' for '{entry.ContentId}' requires a Token on its root."
                    );
                if (entry.Prefab.GetComponent<Team>() == null)
                    throw new InvalidOperationException(
                        $"Encounter catalog prefab '{entry.Prefab.name}' for '{entry.ContentId}' requires a Team on its root."
                    );
                if (entry.Prefab.GetComponent<DungeonEncounterMember>() != null)
                    throw new InvalidOperationException(
                        $"Encounter catalog prefab '{entry.Prefab.name}' cannot contain instance-specific DungeonEncounterMember identity."
                    );
            }

            entriesByContentId = validated;
        }

        /// <summary>Resolves one required creature definition by exact stable content ID.</summary>
        /// <param name="contentId">The non-empty content ID from an encounter plan.</param>
        /// <returns>The complete validated runtime definition.</returns>
        /// <exception cref="ArgumentException"><paramref name="contentId"/> is blank.</exception>
        /// <exception cref="InvalidOperationException">The catalog is invalid.</exception>
        /// <exception cref="KeyNotFoundException">No entry uses the requested ID.</exception>
        public DungeonEncounterCreatureCatalogEntry Require(string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException(
                    "A creature content ID is required.",
                    nameof(contentId)
                );
            EnsureValidated();
            if (
                !entriesByContentId.TryGetValue(
                    contentId,
                    out DungeonEncounterCreatureCatalogEntry entry
                )
            )
                throw new KeyNotFoundException(
                    $"Encounter creature content ID '{contentId}' is not present in the runtime catalog."
                );
            return entry;
        }

        /// <summary>Loads and validates the project-owned default runtime catalog.</summary>
        /// <returns>The non-null validated catalog asset.</returns>
        /// <exception cref="InvalidOperationException">
        /// The asset is absent from its required Resources path or contains invalid entries.
        /// </exception>
        public static DungeonEncounterCreatureCatalog LoadDefaultOrThrow()
        {
            DungeonEncounterCreatureCatalog catalog =
                Resources.Load<DungeonEncounterCreatureCatalog>(DefaultResourcesPath);
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    $"The runtime encounter catalog is missing at Resources/{DefaultResourcesPath}."
                );
            }
            catalog.ValidateOrThrow();
            return catalog;
        }

        private void OnValidate()
        {
            entriesByContentId = null;
        }

        private void EnsureValidated()
        {
            if (entriesByContentId == null)
                ValidateOrThrow();
        }

#if UNITY_EDITOR
        /// <summary>Replaces serialized entries for editor authoring and validation tooling.</summary>
        /// <param name="replacement">The complete non-null replacement sequence.</param>
        /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is null.</exception>
        public void ReplaceEntries(IEnumerable<DungeonEncounterCreatureCatalogEntry> replacement)
        {
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));
            entries = replacement.ToList();
            entriesByContentId = null;
        }
#endif
    }
}
