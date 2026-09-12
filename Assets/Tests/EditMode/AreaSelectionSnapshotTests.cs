using System.Linq;
using GridPrivate;
using GridPublic;
using NUnit.Framework;
using UnityEngine;

namespace TestsCombat
{
    public class AreaSelectionSnapshotTests
    {
        [Test]
        public void DestroyedOriginalIsNeverResolvedToReplacementOccupant()
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
            };
            GameObject original = new("original");
            GameObject replacement = new("replacement");
            tiles[1, 0].Occupants.Add(original);
            try
            {
                AreaTargetCapture capture = AreaTargetCapture.Capture(
                    new AreaTargetSource(Vector3Int.zero),
                    tiles,
                    new AreaTargetRequest { Shape = AreaShape.Line, SizeFeet = 5 },
                    new AreaPlacement { Shape = AreaShape.Line }
                );
                int id = original.GetInstanceID();
                AreaTargetResult resolved = capture.Resolve();
                Object.DestroyImmediate(original);
                tiles[1, 0].Occupants.Clear();
                tiles[1, 0].Occupants.Add(replacement);
                AreaSelectionSnapshot historical = AreaTargeting.Evaluate(capture.Snapshot);
                Assert.AreEqual(id, historical.Creatures.Single().EntityId);
                Assert.IsTrue(historical.Creatures.Single().IsAffected);
                Assert.IsFalse(
                    resolved.Creatures.Single().IsAffected,
                    "Unity adapters reject destroyed originals."
                );
                Assert.IsEmpty(capture.Resolve().Creatures);
                resolved.Cells.Clear();
                Assert.IsNotEmpty(AreaTargeting.Evaluate(capture.Snapshot).Cells);
                Assert.Throws<System.NotSupportedException>(() =>
                    ((System.Collections.Generic.IList<Vector3Int>)historical.Cells).Clear()
                );
            }
            finally
            {
                if (original != null)
                    Object.DestroyImmediate(original);
                Object.DestroyImmediate(replacement);
            }
        }

        [TestCase(AreaShape.Line, true, 16)]
        [TestCase(AreaShape.Cone, true, 16)]
        [TestCase(AreaShape.Emanation, true, 16)]
        [TestCase(AreaShape.Burst, true, 4)]
        [TestCase(AreaShape.Line, false, 16)]
        [TestCase(AreaShape.Burst, false, 4)]
        public void SelectionCombinesGeometryWithCapturedPhysics(
            AreaShape shape,
            bool requiresLineOfEffect,
            int rays
        )
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            GameObject target = new("inside");
            GameObject outside = new("outside");
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.AddComponent<Game.KayKit.MapLineOfSightBlocker>();
            wall.transform.position = new Vector3(1.5f, 0.75f, 0.5f);
            wall.transform.localScale = new Vector3(0.2f, 2, 4);
            tiles[2, 0].Occupants.Add(target);
            tiles[3, 0].Occupants.Add(outside);
            AreaTargetSource source = new(Vector3Int.zero);
            AreaTargetRequest request = new()
            {
                Shape = shape,
                SizeFeet = 10,
                RequiresLineOfEffect = requiresLineOfEffect,
            };
            AreaPlacement placement = new() { Shape = shape };
            try
            {
                AreaTargetCapture capture = AreaTargetCapture.Capture(
                    source,
                    tiles,
                    request,
                    placement
                );
                AreaSelectionSnapshot blocked = AreaTargeting.Evaluate(capture.Snapshot);
                Assert.AreEqual(target.GetInstanceID(), blocked.Creatures.Single().EntityId);
                Assert.AreEqual(0, blocked.Creatures.Single().ClearRays);
                Assert.AreEqual(!requiresLineOfEffect, blocked.Creatures.Single().IsAffected);
                Object.DestroyImmediate(wall);
                Assert.AreEqual(
                    0,
                    AreaTargeting.Evaluate(capture.Snapshot).Creatures.Single().ClearRays
                );
                AreaTargetResult clear = AreaTargetCapture
                    .Capture(source, tiles, request, placement)
                    .Resolve();
                Assert.AreEqual(rays, clear.Creatures.Single().ClearRays);
                Assert.IsTrue(clear.Creatures.Single().IsAffected);
                Assert.AreEqual(
                    shape == AreaShape.Burst ? StrikeCover.Standard : StrikeCover.None,
                    clear.Creatures.Single().Cover
                );
            }
            finally
            {
                if (wall != null)
                    Object.DestroyImmediate(wall);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(outside);
            }
        }

        [Test]
        public void SelectionUsesCapturedMembershipAndObstructionUntilRecaptured()
        {
            Tile[,] tiles =
            {
                { new Tile() },
                { new Tile() },
                { new Tile() },
                { new Tile() },
            };
            GameObject target = new("target");
            tiles[3, 0].Occupants.Add(target);
            AreaTargetSource source = new(Vector3Int.zero);
            AreaTargetRequest request = new() { Shape = AreaShape.Line, SizeFeet = 15 };
            AreaPlacement placement = new()
            {
                Shape = AreaShape.Line,
                Direction = AreaDirection.East,
            };
            try
            {
                AreaTargetCapture capture = AreaTargetCapture.Capture(
                    source,
                    tiles,
                    request,
                    placement
                );
                AreaSelectionSnapshot before = AreaTargeting.Evaluate(capture.Snapshot);
                Assert.AreEqual(target.GetInstanceID(), before.Creatures.Single().EntityId);
                Assert.AreEqual(16, before.Creatures.Single().ClearRays);
                tiles[1, 0] = null;
                tiles[3, 0].Occupants.Clear();
                tiles[2, 0].Occupants.Add(target);
                target.transform.position = new Vector3(2, 0, 0);
                AreaSelectionSnapshot old = AreaTargeting.Evaluate(capture.Snapshot);
                CollectionAssert.AreEqual(before.Cells, old.Cells);
                Assert.AreEqual(new Vector3Int(3, 0, 0), old.Creatures.Single().Cell);
                Assert.AreEqual(16, old.Creatures.Single().ClearRays);
                Assert.AreSame(target, capture.Resolve().Creatures.Single().Creature);
                AreaTargetCapture fresh = AreaTargetCapture.Capture(
                    source,
                    tiles,
                    request,
                    placement
                );
                AreaSelectionSnapshot current = AreaTargeting.Evaluate(fresh.Snapshot);
                Assert.AreEqual(new Vector3Int(2, 0, 0), current.Creatures.Single().Cell);
                Assert.AreEqual(0, current.Creatures.Single().ClearRays);
                Assert.IsFalse(current.Creatures.Single().IsAffected);
                Assert.AreNotEqual(before.Snapshot.Id, current.Snapshot.Id);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
