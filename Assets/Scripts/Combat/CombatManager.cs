using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using UnityEngine;

/// <summary>Hosts and projects the single rules-owned encounter lifecycle.</summary>
public class CombatManager : CombatManagerInterface
{
    protected List<ActionController> Combatants = new();
    private readonly List<ActionController> activeCombatants = new();
    private bool combatActive;
    private bool dungeonDirectedCombat;
    private bool encounterEnded;
    private EncounterOutcome? pendingOutcomePresentation;
    private UnityCombatRulesBridge combatRules;

    /// <summary>Raised with the surviving team display name when dungeon combat ends.</summary>
    public event Action<string> DungeonCombatEnded = delegate { };

    /// <inheritdoc/>
    public override event Action<bool> CombatActivityChanged = delegate { };

    /// <inheritdoc/>
    public override bool IsCombatActive => combatActive;

    /// <inheritdoc/>
    public override void AddCombatant(ActionController combatant)
    {
        if (combatant == null)
            throw new ArgumentNullException(nameof(combatant));
        if (Combatants.Contains(combatant))
            return;
        Combatants.Add(combatant);
        OnCombatantJoin.Invoke(combatant.gameObject);
    }

    /// <inheritdoc/>
    public override void Remove(ActionController combatant)
    {
        if (combatant == null)
            return;
        // Defeat presentation retires this controller from future host registrations. The active
        // rules roster stays immutable and retains the creature as a timing boundary.
        Combatants.Remove(combatant);
    }

    /// <inheritdoc/>
    public override GameObject WhosTurn()
    {
        if (
            !combatActive
            || combatRules == null
            || !combatRules.GetEncounter().CurrentTurn.HasValue
        )
            return null;
        CreatureId actor = combatRules.GetEncounter().CurrentTurn.Value.Actor;
        return combatRules.TryGetController(actor, out ActionController controller)
            ? controller.gameObject
            : null;
    }

    /// <inheritdoc/>
    public override List<GameObject> GetCombatants()
    {
        if (!combatActive || combatRules == null)
            return Combatants
                .Where(controller => controller != null)
                .Select(c => c.gameObject)
                .ToList();

        return combatRules
            .GetEncounter()
            .Roster.Where(entry =>
                combatRules.Snapshot.Health.TryGet(entry.Creature, out HealthState health)
                && health.IsLiving
            )
            .Select(entry =>
                combatRules.TryGetController(entry.Creature, out ActionController controller)
                    ? controller.gameObject
                    : null
            )
            .Where(gameObject => gameObject != null)
            .ToList();
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat() =>
        BeginCombat(Combatants.Where(CanTakeTurn).ToArray(), false);

    /// <inheritdoc/>
    public override void StartDungeonCombat(IReadOnlyList<ActionController> participants) =>
        BeginCombat(participants, true);

    /// <inheritdoc/>
    public override void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements)
    {
        if (reinforcements == null)
            throw new ArgumentNullException(nameof(reinforcements));
        if (!combatActive || !dungeonDirectedCombat || combatRules == null)
            throw new InvalidOperationException(
                "Dungeon reinforcements require an active dungeon encounter."
            );

        ActionController[] additions = reinforcements.Distinct().ToArray();
        if (
            additions.Length == 0
            || additions.Any(controller => controller == null)
            || additions.Any(controller => !Combatants.Contains(controller))
            || additions.Any(activeCombatants.Contains)
        )
            throw new ArgumentException(
                "Reinforcements must be new, registered, unique controllers.",
                nameof(reinforcements)
            );
        if (additions.Any(controller => !CanTakeTurn(controller)))
            throw new InvalidOperationException(
                "Inactive or disabled controllers cannot join combat."
            );

        Pf2eRulesEngine.ApplyCombatStartRules(additions);
        combatRules.RegisterCombatants(additions);
        activeCombatants.AddRange(additions);
    }

    /// <inheritdoc/>
    public override void SuspendDungeonCombat()
    {
        if (!combatActive || !dungeonDirectedCombat || combatRules == null)
            throw new InvalidOperationException(
                "Only an active dungeon encounter can be suspended."
            );
        combatRules.SuspendEncounter();
        StopCombatState();
    }

    /// <inheritdoc/>
    public override void EndCurrentTurn(ActionController expectedActor)
    {
        if (expectedActor == null)
            throw new ArgumentNullException(nameof(expectedActor));
        if (!combatActive || combatRules == null)
            return;
        CreatureId actor = combatRules.GetCreatureId(expectedActor);
        combatRules.EndTurn(actor);
        OnGameplayStateCommitted.Invoke();
    }

    /// <inheritdoc/>
    public override void NextTurn()
    {
        GameObject current = WhosTurn();
        if (current != null)
            EndCurrentTurn(current.GetComponent<ActionController>());
    }

    /// <inheritdoc/>
    public override bool CheckForEndOfGame()
    {
        if (
            pendingOutcomePresentation.HasValue
            && !activeCombatants.Any(controller => controller != null && controller.IsTakingAction)
        )
        {
            EncounterOutcome pending = pendingOutcomePresentation.Value;
            pendingOutcomePresentation = null;
            CompleteEncounterOutcomePresentation(pending);
        }
        return encounterEnded
            || (combatRules != null && combatRules.GetEncounter().Phase == EncounterPhase.Ended);
    }

    /// <inheritdoc/>
    public override void RefreshRulesTopology()
    {
        if (combatRules != null && TryGetCurrentTiles(out Tile[,] tiles))
            combatRules.RefreshTopology(tiles);
    }

    private void BeginCombat(IReadOnlyList<ActionController> participants, bool dungeonDirected)
    {
        if (participants == null)
            throw new ArgumentNullException(nameof(participants));
        if (combatActive)
            throw new InvalidOperationException("Combat is already active.");

        ActionController[] selected = participants.Distinct().ToArray();
        if (
            selected.Length == 0
            || selected.Any(controller => controller == null)
            || selected.Any(controller => !Combatants.Contains(controller))
        )
            throw new ArgumentException(
                "Combat requires registered, unique, non-null participants.",
                nameof(participants)
            );
        if (selected.Any(controller => !CanTakeTurn(controller)))
            throw new InvalidOperationException(
                "Inactive or disabled controllers cannot begin combat."
            );

        Pf2eRulesEngine.ApplyCombatStartRules(selected);
        activeCombatants.Clear();
        activeCombatants.AddRange(selected);
        dungeonDirectedCombat = dungeonDirected;
        encounterEnded = false;
        pendingOutcomePresentation = null;
        combatRules = UnityCombatRulesBridge.Create(activeCombatants, GetCurrentTilesOrFallback());
        combatRules.EncounterStarted += PresentEncounterStarted;
        combatRules.TurnBegan += PresentTurnBegan;
        combatRules.EncounterEnded += PresentEncounterOutcome;
        combatActive = true;
        combatRules.StartEncounter("players");
    }

    private void PresentEncounterStarted()
    {
        LogInitiative();
        CombatActivityChanged.Invoke(true);
        OnCombatStart.Invoke();
    }

    private void LogInitiative()
    {
        if (combatRules == null || !CombatLog.TryGetInstance(out CombatLogInterface log))
            return;
        EncounterState encounter = combatRules.GetEncounter();
        string message = "Initiative Order:\n";
        for (int index = 0; index < encounter.Roster.Count; index++)
        {
            InitiativeEntry entry = encounter.Roster[index];
            string displayName = combatRules.TryGetController(
                entry.Creature,
                out ActionController controller
            )
                ? controller.gameObject.name
                : entry.Creature.Value;
            message += $"  {index + 1}. {displayName} (Initiative: {entry.Total})\n";
        }
        log.Log(message);
    }

    private void PresentTurnBegan(TurnIdentity turn)
    {
        if (
            combatRules != null
            && combatRules.TryGetController(turn.Actor, out ActionController controller)
        )
            OnNextTurn.Invoke(controller.gameObject);
    }

    private void PresentEncounterOutcome(EncounterOutcome outcome)
    {
        encounterEnded = true;
        if (activeCombatants.Any(controller => controller != null && controller.IsTakingAction))
        {
            pendingOutcomePresentation = outcome;
            return;
        }
        CompleteEncounterOutcomePresentation(outcome);
    }

    private void CompleteEncounterOutcomePresentation(EncounterOutcome outcome)
    {
        bool playerWon = outcome == EncounterOutcome.PlayerVictory;
        string winningTeam = playerWon ? FindProtagonistTeam() : FindLivingOppositionTeam();
        bool wasDungeonDirected = dungeonDirectedCombat;

        if (wasDungeonDirected)
            DungeonCombatEnded.Invoke(winningTeam);
        else
            OnCombatEnd.Invoke(winningTeam);
        if (!wasDungeonDirected || !playerWon)
            OnCombatOutcome.Invoke(playerWon);
        StopCombatState();
    }

    private string FindLivingOppositionTeam()
    {
        if (combatRules == null)
            return string.Empty;
        EncounterState encounter = combatRules.GetEncounter();
        foreach (InitiativeEntry entry in encounter.Roster)
        {
            if (
                entry.Team == encounter.ProtagonistTeam
                || !combatRules.Snapshot.Health.TryGet(entry.Creature, out HealthState health)
                || !health.IsLiving
                || !combatRules.TryGetController(entry.Creature, out ActionController controller)
                || controller == null
            )
                continue;
            Team team = controller.GetComponent<Team>();
            if (team != null && !string.IsNullOrWhiteSpace(team.Name))
                return team.Name;
        }
        return string.Empty;
    }

    private string FindProtagonistTeam()
    {
        if (combatRules == null)
            return string.Empty;
        PlayerId protagonist = combatRules.GetEncounter().ProtagonistTeam;
        foreach (ActionController controller in activeCombatants)
        {
            if (
                controller != null
                && combatRules.Snapshot.Creatures.TryGet(
                    combatRules.GetCreatureId(controller),
                    out CreatureState creature
                )
                && creature.Player == protagonist
            )
            {
                Team team = controller.GetComponent<Team>();
                if (team != null && !string.IsNullOrWhiteSpace(team.Name))
                    return team.Name;
            }
        }
        return string.Empty;
    }

    private void StopCombatState()
    {
        if (combatRules != null)
        {
            combatRules.EncounterStarted -= PresentEncounterStarted;
            combatRules.TurnBegan -= PresentTurnBegan;
            combatRules.EncounterEnded -= PresentEncounterOutcome;
            combatRules.ReleaseOwnership();
            combatRules = null;
        }
        foreach (ActionController controller in activeCombatants)
            controller?.ResetEncounterTurnState();
        activeCombatants.Clear();
        combatActive = false;
        dungeonDirectedCombat = false;
        pendingOutcomePresentation = null;
        CombatActivityChanged.Invoke(false);
    }

    private static Tile[,] GetCurrentTilesOrFallback()
    {
        if (TryGetCurrentTiles(out Tile[,] tiles))
            return tiles;
        Tile[,] fallback = new Tile[1, 1];
        fallback[0, 0] = new Tile();
        return fallback;
    }

    private static bool TryGetCurrentTiles(out Tile[,] tiles)
    {
        if (
            GridPublic.GridAPI.TryGetInstance(out GridPublic.GridAPI publicGrid)
            && publicGrid is GridAPIPrivate grid
            && grid.GetTiles() is Tile[,] current
            && current.GetLength(0) > 0
            && current.GetLength(1) > 0
        )
        {
            tiles = current;
            return true;
        }
        tiles = new Tile[0, 0];
        return false;
    }

    private static bool CanTakeTurn(ActionController controller) =>
        controller != null && controller.gameObject.activeSelf && controller.isActiveAndEnabled;

    // Camera projection for legacy callers.
    public Vector3[] getPoistions() =>
        GetCombatants().Select(combatant => combatant.transform.position).ToArray();
}
