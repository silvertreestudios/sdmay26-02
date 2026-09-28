using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

namespace Game.Combat.Spells
{
    /// <summary>Keeps prepared slot resources stable across detached dungeon exploration.</summary>
    internal sealed class SpellSlotResourceSeed : MonoBehaviour
    {
        private PreparedSpellSlotResource[] resources = Array.Empty<PreparedSpellSlotResource>();
        private CreatureId owner;
        private PreparedSpellBook book;

        internal IReadOnlyList<PreparedSpellSlotResource> Resources => resources;

        internal void Initialize(IEnumerable<PreparedSpellSlotResource> restoredResources)
        {
            resources =
                restoredResources?.ToArray()
                ?? throw new ArgumentNullException(nameof(restoredResources));
            owner = default;
            book = null;
        }

        internal void Configure(CreatureId configuredOwner, PreparedSpellBook configuredBook)
        {
            if (configuredOwner.IsEmpty)
                throw new ArgumentException(
                    "A spell-slot owner is required.",
                    nameof(configuredOwner)
                );
            owner = configuredOwner;
            book = configuredBook ?? throw new ArgumentNullException(nameof(configuredBook));
        }

        internal IReadOnlyList<PreparedSpellSlotResource> Capture(ActionController controller)
        {
            if (
                book != null
                && controller != null
                && controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId actor
                )
                && actor == owner
            )
                return book.CaptureSlotResources(
                    owner,
                    new SnapshotSpellSlotStateReader(bridge.Snapshot)
                );
            return resources;
        }

        internal void Project(RulesSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (book == null || owner.IsEmpty)
                return;
            resources = book.CaptureSlotResources(owner, new SnapshotSpellSlotStateReader(snapshot))
                .ToArray();
        }
    }
}
