using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Game.Rules.Unity.Tests
{
    /// <summary>
    /// Verifies explicit stable-key replacement and immutable action-bar catalog ordering.
    /// </summary>
    public sealed class ActionBarEntryCatalogTests
    {
        /// <summary>
        /// Verifies a definition replaces only the legacy entry with the same explicit key.
        /// </summary>
        [Test]
        public void DefinitionSuppressesOnlyExplicitStableKeyMatch()
        {
            GameObject actor = new GameObject("action-bar-catalog-actor");
            try
            {
                TestActionController controller = actor.AddComponent<TestActionController>();
                LegacyActionBarEntry replacedLegacy = new LegacyActionBarEntry(
                    new ActionBarEntryKey("shared-key"),
                    new TestLegacyAction("Same display"),
                    controller
                );
                LegacyActionBarEntry retainedLegacy = new LegacyActionBarEntry(
                    new ActionBarEntryKey("legacy-only"),
                    new TestLegacyAction("Same display"),
                    controller
                );
                StubDefinitionEntry replacement = new StubDefinitionEntry(
                    new ActionBarEntryKey("shared-key"),
                    "Definition replacement"
                );
                StubDefinitionEntry sameNameDifferentKey = new StubDefinitionEntry(
                    new ActionBarEntryKey("definition-only"),
                    "Same display"
                );
                List<LegacyActionBarEntry> legacy = new List<LegacyActionBarEntry>
                {
                    replacedLegacy,
                    retainedLegacy,
                };
                List<IDefinitionActionBarEntry> definitions = new List<IDefinitionActionBarEntry>
                {
                    sameNameDifferentKey,
                    replacement,
                };

                ActionBarEntryCatalog catalog = new ActionBarEntryCatalog(legacy, definitions);
                legacy.Clear();
                definitions.Clear();

                Assert.That(
                    catalog.Entries,
                    Is.EqualTo(
                        new IActionBarEntry[] { replacement, retainedLegacy, sameNameDifferentKey }
                    )
                );
                Assert.That(catalog.Entries, Has.Count.EqualTo(3));
                Assert.That(catalog.Entries[1].DisplayName, Is.EqualTo("Same display"));
                Assert.That(catalog.Entries[2].DisplayName, Is.EqualTo("Same display"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
            }
        }

        /// <summary>
        /// Verifies duplicate keys within one implementation category are rejected as ambiguous configuration.
        /// </summary>
        [Test]
        public void DuplicateDefinitionKeysAreRejected()
        {
            StubDefinitionEntry first = new StubDefinitionEntry(
                new ActionBarEntryKey("duplicate"),
                "First"
            );
            StubDefinitionEntry second = new StubDefinitionEntry(
                new ActionBarEntryKey("duplicate"),
                "Second"
            );

            Assert.Throws<ArgumentException>(() =>
                new ActionBarEntryCatalog(
                    Array.Empty<LegacyActionBarEntry>(),
                    new IDefinitionActionBarEntry[] { first, second }
                )
            );
        }

        private sealed class StubDefinitionEntry : IDefinitionActionBarEntry
        {
            public StubDefinitionEntry(ActionBarEntryKey key, string displayName)
            {
                Key = key;
                DisplayName = displayName;
            }

            public ActionBarEntryKey Key { get; }

            public string DisplayName { get; }

            public ActionAvailability GetAvailability() => ActionAvailability.Available;

            public ValueTask<ActionBarExecutionOutcome> Execute() =>
                throw new NotSupportedException("Catalog tests do not execute entries.");
        }

        private sealed class TestLegacyAction : EntityAction
        {
            private readonly string actionName;

            public TestLegacyAction(string actionName)
                : base(0) => this.actionName = actionName;

            public override string ActionName => actionName;
        }

        private sealed class TestActionController : ActionController
        {
            public override void EndTurn() { }
        }
    }
}
