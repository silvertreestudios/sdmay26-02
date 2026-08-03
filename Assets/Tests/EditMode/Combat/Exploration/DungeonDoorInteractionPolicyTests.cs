using Game.Combat.Exploration;
using Game.DungeonGeneration;
using NUnit.Framework;

/// <summary>Verifies pure dungeon-door geometry and mode validation.</summary>
public sealed class DungeonDoorInteractionPolicyTests
{
    /// <summary>Verifies all four cardinal neighbors may open a closed door in exploration.</summary>
    [TestCase(4, 5)]
    [TestCase(6, 5)]
    [TestCase(5, 4)]
    [TestCase(5, 6)]
    public void CardinallyAdjacentClosedDoorIsAllowed(int actorX, int actorZ)
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Exploration,
            actorCell: new DungeonCell(actorX, actorZ),
            doorCell: new DungeonCell(5, 5)
        );

        Assert.That(decision.IsAllowed, Is.True);
        Assert.That(decision.Rejection, Is.EqualTo(DungeonDoorInteractionRejection.None));
    }

    /// <summary>Verifies diagonal, same-cell, and distant actors cannot open the target door.</summary>
    [TestCase(4, 4)]
    [TestCase(5, 5)]
    [TestCase(3, 5)]
    [TestCase(5, 8)]
    public void ActorMustBeCardinallyAdjacent(int actorX, int actorZ)
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Exploration,
            actorCell: new DungeonCell(actorX, actorZ),
            doorCell: new DungeonCell(5, 5)
        );

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(
            decision.Rejection,
            Is.EqualTo(DungeonDoorInteractionRejection.ActorIsNotAdjacent)
        );
    }

    /// <summary>Verifies opening an already-open door is rejected explicitly.</summary>
    [Test]
    public void DoorMustBeClosed()
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Exploration,
            doorIsOpen: true
        );

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(
            decision.Rejection,
            Is.EqualTo(DungeonDoorInteractionRejection.DoorAlreadyOpen)
        );
    }

    /// <summary>Verifies undefined gameplay modes are rejected.</summary>
    [TestCase((DungeonDoorInteractionMode)0)]
    [TestCase((DungeonDoorInteractionMode)99)]
    public void ModeMustBeDefined(DungeonDoorInteractionMode mode)
    {
        DungeonDoorInteractionDecision decision = Evaluate(mode);

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.Rejection, Is.EqualTo(DungeonDoorInteractionRejection.InvalidMode));
    }

    private static DungeonDoorInteractionDecision Evaluate(
        DungeonDoorInteractionMode mode,
        DungeonCell actorCell = default,
        DungeonCell doorCell = default,
        bool doorIsOpen = false
    )
    {
        if (actorCell == default && doorCell == default)
            doorCell = new DungeonCell(1, 0);
        return DungeonDoorInteractionPolicy.Evaluate(
            new DungeonDoorInteractionRequest(mode, actorCell, doorCell, doorIsOpen)
        );
    }
}
