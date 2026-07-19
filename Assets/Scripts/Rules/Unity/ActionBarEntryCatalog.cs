using System;
using System.Collections.Generic;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Builds a deterministic, immutable view of legacy and definition-backed action-bar entries.
    /// </summary>
    public sealed class ActionBarEntryCatalog
    {
        /// <summary>
        /// Initializes a merged catalog while replacing only explicit stable-key matches.
        /// </summary>
        /// <param name="legacyEntries">Legacy entries in their existing display order.</param>
        /// <param name="definitionEntries">Definition entries in their registration order.</param>
        /// <exception cref="ArgumentNullException">A collection or one of its entries is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">One input category repeats a stable key.</exception>
        /// <remarks>
        /// A matching definition occupies the legacy entry's position. Unmatched definitions are
        /// appended in definition order. Equal display names with different keys remain separate.
        /// </remarks>
        public ActionBarEntryCatalog(
            IEnumerable<LegacyActionBarEntry> legacyEntries,
            IEnumerable<IDefinitionActionBarEntry> definitionEntries
        )
        {
            LegacyActionBarEntry[] legacy = CopyEntries(legacyEntries, nameof(legacyEntries));
            IDefinitionActionBarEntry[] definitions = CopyEntries(
                definitionEntries,
                nameof(definitionEntries)
            );
            EnsureUniqueKeys(legacy, nameof(legacyEntries));
            EnsureUniqueKeys(definitions, nameof(definitionEntries));

            Dictionary<ActionBarEntryKey, IDefinitionActionBarEntry> definitionsByKey =
                new Dictionary<ActionBarEntryKey, IDefinitionActionBarEntry>();
            foreach (IDefinitionActionBarEntry definition in definitions)
                definitionsByKey.Add(definition.Key, definition);

            HashSet<ActionBarEntryKey> includedDefinitions = new HashSet<ActionBarEntryKey>();
            List<IActionBarEntry> merged = new List<IActionBarEntry>(
                legacy.Length + definitions.Length
            );
            foreach (LegacyActionBarEntry legacyEntry in legacy)
            {
                if (
                    definitionsByKey.TryGetValue(
                        legacyEntry.Key,
                        out IDefinitionActionBarEntry replacement
                    )
                )
                {
                    merged.Add(replacement);
                    includedDefinitions.Add(replacement.Key);
                }
                else
                {
                    merged.Add(legacyEntry);
                }
            }

            foreach (IDefinitionActionBarEntry definition in definitions)
            {
                if (!includedDefinitions.Contains(definition.Key))
                    merged.Add(definition);
            }

            Entries = Array.AsReadOnly(merged.ToArray());
        }

        /// <summary>
        /// Gets the immutable merged entry order.
        /// </summary>
        public IReadOnlyList<IActionBarEntry> Entries { get; }

        private static TEntry[] CopyEntries<TEntry>(
            IEnumerable<TEntry> entries,
            string parameterName
        )
            where TEntry : class, IActionBarEntry
        {
            if (entries == null)
                throw new ArgumentNullException(parameterName);

            List<TEntry> copy = new List<TEntry>();
            foreach (TEntry entry in entries)
            {
                if (entry == null)
                    throw new ArgumentNullException(
                        parameterName,
                        "An action-bar catalog cannot contain a null entry."
                    );
                if (entry.Key.IsEmpty)
                    throw new ArgumentException(
                        "An action-bar catalog entry has an empty key.",
                        parameterName
                    );
                copy.Add(entry);
            }

            return copy.ToArray();
        }

        private static void EnsureUniqueKeys<TEntry>(
            IReadOnlyList<TEntry> entries,
            string parameterName
        )
            where TEntry : IActionBarEntry
        {
            HashSet<ActionBarEntryKey> keys = new HashSet<ActionBarEntryKey>();
            for (int index = 0; index < entries.Count; index++)
            {
                if (!keys.Add(entries[index].Key))
                {
                    throw new ArgumentException(
                        $"Action-bar key '{entries[index].Key}' appears more than once in the same entry category.",
                        parameterName
                    );
                }
            }
        }
    }
}
