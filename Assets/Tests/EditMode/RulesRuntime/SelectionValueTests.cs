using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies selection values own their data and reject structurally absent input.</summary>
    public sealed class SelectionValueTests
    {
        private static readonly CreatureId First = new CreatureId("selection-first");
        private static readonly CreatureId Second = new CreatureId("selection-second");

        /// <summary>Verifies requests and completed action payloads defensively copy collections.</summary>
        [Test]
        public void CollectionsAreDefensivelyCopiedAndRemainImmutable()
        {
            List<CreatureId> candidates = new List<CreatureId> { First, Second };
            MultipleCreatureSelectionRequest request = new MultipleCreatureSelectionRequest(
                new SelectionRequestId("copy-creatures"),
                candidates,
                1,
                2
            );
            MultipleCreatureSelection selected = new MultipleCreatureSelection(candidates);
            GridPosition start = new GridPosition(0, 0, 0);
            GridPosition destination = new GridPosition(1, 0, 0);
            List<GridPosition> suppliedPath = new List<GridPosition> { start, destination };
            PathSelection path = new PathSelection(suppliedPath);
            TumbleThroughSelection tumble = new TumbleThroughSelection(
                suppliedPath,
                First,
                new MovementMode("land")
            );

            candidates[0] = new CreatureId("changed");
            candidates.Clear();
            suppliedPath[1] = new GridPosition(9, 9, 9);
            suppliedPath.Clear();

            Assert.That(request.Candidates, Is.EqualTo(new[] { First, Second }));
            Assert.That(selected.Creatures, Is.EqualTo(new[] { First, Second }));
            Assert.That(path.Positions, Is.EqualTo(new[] { start, destination }));
            Assert.That(tumble.Path, Is.EqualTo(new[] { start, destination }));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CreatureId>)request.Candidates).Add(new CreatureId("later"))
            );
        }

        /// <summary>Verifies every primitive rejects missing IDs, candidates, or required shape.</summary>
        [Test]
        public void RequiredInputsCannotBeAbsent()
        {
            SelectionRequestId id = new SelectionRequestId("absence-test");
            ItemId item = new ItemId("test-item");
            SpellVariantId variant = new SpellVariantId("test-variant");
            SpellSlotPoolId pool = new SpellSlotPoolId("test-slot-pool");

            Assert.Throws<ArgumentException>(() => new SelectionRequestId(" "));
            Assert.Throws<ArgumentException>(() => new AreaTemplateId(""));
            Assert.Throws<ArgumentException>(() => new SpellVariantId(" "));
            Assert.Throws<ArgumentException>(() => new MovementMode(""));
            Assert.Throws<ArgumentException>(() => ActionAvailability.Unavailable(" "));
            Assert.Throws<ArgumentException>(() => SelectionOutcome<int>.Invalid(""));
            Assert.Throws<ArgumentNullException>(() => SelectionOutcome<string>.Completed(null));
            Assert.Throws<ArgumentException>(() => new CreatureSelection(default));
            Assert.Throws<ArgumentException>(() => new ItemSelection(default));
            Assert.Throws<ArgumentException>(() => new WeaponSelection(default));
            Assert.Throws<ArgumentException>(() => new SpellVariantSelection(default));
            Assert.Throws<ArgumentException>(() => new SpellSlotSelection(default));
            Assert.Throws<ArgumentException>(() => new StrikeSelection(default, First));
            Assert.Throws<ArgumentException>(() => new StrikeSelection(item, default));
            Assert.Throws<ArgumentException>(() =>
                new CreatureSelectionRequest(id, Array.Empty<CreatureId>())
            );
            Assert.Throws<ArgumentException>(() =>
                new CreatureSelectionRequest(id, new[] { First, First })
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MultipleCreatureSelectionRequest(id, new[] { First }, 0, 1)
            );
            Assert.Throws<ArgumentException>(() =>
                new ItemSelectionRequest(id, Array.Empty<ItemId>())
            );
            Assert.Throws<ArgumentException>(() =>
                new WeaponSelectionRequest(id, new[] { default(ItemId) })
            );
            Assert.Throws<ArgumentException>(() =>
                new GridCellSelectionRequest(id, Array.Empty<GridPosition>())
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PathSelectionRequest(id, First, default, new[] { new GridPosition(1, 0, 0) }, 0)
            );
            Assert.Throws<ArgumentException>(() => new AreaOrientation(default, default));
            Assert.Throws<ArgumentException>(() =>
                new AreaSelection(default, new AreaOrientation(default, new GridPosition(1, 0, 0)))
            );
            Assert.Throws<ArgumentException>(() =>
                new SpellVariantSelectionRequest(id, Array.Empty<SpellVariantId>())
            );
            Assert.Throws<ArgumentException>(() =>
                new SpellSlotSelectionRequest(id, Array.Empty<SpellSlotPoolId>())
            );
            Assert.Throws<ArgumentException>(() =>
                new TumbleThroughSelection(
                    new[] { default(GridPosition) },
                    First,
                    new MovementMode("land")
                )
            );
            Assert.Throws<ArgumentNullException>(() => new CastSpellSelection(pool, variant, null));
        }

        /// <summary>Verifies spell targets are separate shapes without unrelated optional fields.</summary>
        [Test]
        public void SpellTargetsAreStructurallyDistinct()
        {
            AreaTemplateId template = new AreaTemplateId("burst-ten");
            AreaSelection area = new AreaSelection(
                template,
                new AreaOrientation(default, new GridPosition(1, 0, 0))
            );
            ISpellTargetSelection self = SelfSpellTargetSelection.Instance;
            ISpellTargetSelection single = new SingleCreatureSpellTargetSelection(First);
            ISpellTargetSelection multiple = new MultipleCreatureSpellTargetSelection(
                new[] { First, Second }
            );
            ISpellTargetSelection cell = new GridCellSpellTargetSelection(
                new GridPosition(3, 0, 2)
            );
            ISpellTargetSelection orientedArea = new AreaSpellTargetSelection(area);

            Assert.That(self, Is.TypeOf<SelfSpellTargetSelection>());
            Assert.That(single, Is.TypeOf<SingleCreatureSpellTargetSelection>());
            Assert.That(multiple, Is.TypeOf<MultipleCreatureSpellTargetSelection>());
            Assert.That(cell, Is.TypeOf<GridCellSpellTargetSelection>());
            Assert.That(orientedArea, Is.TypeOf<AreaSpellTargetSelection>());
            Assert.That(
                typeof(ISpellTargetSelection).GetProperties(),
                Is.Empty,
                "The common target contract must not become a nullable mega-selection."
            );
        }
    }
}
