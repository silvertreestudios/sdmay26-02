using Game.Combat.Exploration;
using Game.DungeonGeneration;
using NUnit.Framework;

/// <summary>Verifies pure dungeon-door interaction authorization and action costs.</summary>
public sealed class DungeonDoorInteractionPolicyTests
{
    /// <summary>Verifies all four cardinal neighbors may open a closed door in exploration.</summary>
    [TestCase(4, 5)]
    [TestCase(6, 5)]
    [TestCase(5, 4)]
    [TestCase(5, 6)]
    public void LivingPlayerCharacterMayOpenCardinallyAdjacentDoor(int actorX, int actorZ)
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

    /// <summary>Verifies non-player actors cannot open generated dungeon doors.</summary>
    [Test]
    public void ActorMustBePlayerCharacter()
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Exploration,
            actorIsPlayerCharacter: false
        );

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(
            decision.Rejection,
            Is.EqualTo(DungeonDoorInteractionRejection.ActorIsNotPlayerCharacter)
        );
    }

    /// <summary>Verifies dead player characters cannot open generated dungeon doors.</summary>
    [Test]
    public void ActorMustBeAlive()
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Exploration,
            actorIsAlive: false
        );

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.Rejection, Is.EqualTo(DungeonDoorInteractionRejection.ActorIsDead));
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

    /// <summary>Verifies exploration door opening is free even with no available action points.</summary>
    [Test]
    public void ExplorationIsFreeAndDoesNotRequireActionPoints()
    {
        DungeonDoorInteractionRequest request = Request(
            DungeonDoorInteractionMode.Exploration,
            availableActionPoints: 0
        );

        DungeonDoorInteractionDecision decision = DungeonDoorInteractionPolicy.Evaluate(request);

        Assert.That(decision.IsAllowed, Is.True);
        Assert.That(decision.ActionCost, Is.Zero);
        Assert.That(request.AvailableActionPoints, Is.Zero);
    }

    /// <summary>Verifies combat door opening is rejected when no action remains.</summary>
    [Test]
    public void CombatRequiresOneAvailableAction()
    {
        DungeonDoorInteractionDecision decision = Evaluate(
            DungeonDoorInteractionMode.Combat,
            availableActionPoints: 0
        );

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.ActionCost, Is.EqualTo(1));
        Assert.That(
            decision.Rejection,
            Is.EqualTo(DungeonDoorInteractionRejection.InsufficientActionPoints)
        );
    }

    /// <summary>Verifies one or more available combat actions authorize the exact one-action cost.</summary>
    [TestCase(1u)]
    [TestCase(2u)]
    [TestCase(3u)]
    public void CombatCostsExactlyOneAction(uint availableActionPoints)
    {
        DungeonDoorInteractionRequest request = Request(
            DungeonDoorInteractionMode.Combat,
            availableActionPoints: availableActionPoints
        );

        DungeonDoorInteractionDecision decision = DungeonDoorInteractionPolicy.Evaluate(request);

        Assert.That(decision.IsAllowed, Is.True);
        Assert.That(decision.ActionCost, Is.EqualTo(1));
        Assert.That(decision.Rejection, Is.EqualTo(DungeonDoorInteractionRejection.None));
        Assert.That(request.AvailableActionPoints, Is.EqualTo(availableActionPoints));
    }

    /// <summary>Verifies undefined and invalid gameplay modes are rejected without a cost.</summary>
    [TestCase((DungeonDoorInteractionMode)0)]
    [TestCase((DungeonDoorInteractionMode)99)]
    public void ModeMustBeDefined(DungeonDoorInteractionMode mode)
    {
        DungeonDoorInteractionDecision decision = Evaluate(mode);

        Assert.That(decision.IsAllowed, Is.False);
        Assert.That(decision.ActionCost, Is.Zero);
        Assert.That(decision.Rejection, Is.EqualTo(DungeonDoorInteractionRejection.InvalidMode));
    }

    private static DungeonDoorInteractionDecision Evaluate(
        DungeonDoorInteractionMode mode,
        bool actorIsPlayerCharacter = true,
        bool actorIsAlive = true,
        DungeonCell actorCell = default,
        DungeonCell doorCell = default,
        bool doorIsOpen = false,
        uint availableActionPoints = 3
    )
    {
        if (actorCell == default && doorCell == default)
            doorCell = new DungeonCell(1, 0);

        return DungeonDoorInteractionPolicy.Evaluate(
            new DungeonDoorInteractionRequest(
                mode,
                actorIsPlayerCharacter,
                actorIsAlive,
                actorCell,
                doorCell,
                doorIsOpen,
                availableActionPoints
            )
        );
    }

    private static DungeonDoorInteractionRequest Request(
        DungeonDoorInteractionMode mode,
        uint availableActionPoints
    ) =>
        new(
            mode,
            actorIsPlayerCharacter: true,
            actorIsAlive: true,
            actorCell: new DungeonCell(0, 0),
            doorCell: new DungeonCell(1, 0),
            doorIsOpen: false,
            availableActionPoints
        );
}
