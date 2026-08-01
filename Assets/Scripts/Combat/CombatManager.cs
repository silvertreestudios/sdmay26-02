using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using GridPrivate;
using UnityEngine;

/// <summary>Hosts and projects the single rules-owned encounter lifecycle.</summary>
public class CombatManager : CombatManagerInterface
{
    private const string DungeonProtagonistTeamName = "Players";
    protected List<ActionController> Combatants = new();
    private readonly List<ActionController> activeCombatants = new();
    private bool dungeonDirectedCombat;
    private int pendingCombatStops;
    private EncounterOutcome? pendingOutcomePresentation;
    private UnityCombatRulesBridge combatRules;

    /// <summary>Raised with the surviving team display name when dungeon combat ends.</summary>
    public event Action<string> DungeonCombatEnded = delegate { };

    /// <inheritdoc/>
    public override event Action<bool> CombatActivityChanged = delegate { };

    /// <inheritdoc/>
    public override bool IsCombatActive =>
        combatRules != null && combatRules.GetEncounter().Phase == EncounterPhase.Active;

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
            combatRules == null
            || combatRules.GetEncounter().Phase != EncounterPhase.Active
            || !combatRules.GetEncounter().CurrentTurn.HasValue
        )
            return null;
        CreatureId actor = combatRules.GetEncounter().CurrentTurn.Value.Actor;
        return combatRules.GetController(actor).gameObject;
    }

    /// <inheritdoc/>
    public override List<GameObject> GetCombatants()
    {
        if (combatRules == null)
            return Combatants
                .Where(controller => controller != null)
                .Select(c => c.gameObject)
                .ToList();

        return combatRules
            .GetEncounter()
            .Roster.Where(entry => combatRules.GetHealth(entry.Creature).IsLiving)
            .Select(entry => combatRules.GetController(entry.Creature).gameObject)
            .ToList();
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat()
    {
        ActionController[] participants = Combatants.Where(CanTakeTurn).ToArray();
        BeginCombat(
            participants,
            false,
            FindFirstRegisteredTeamName(participants),
            EncounterConclusionPolicy.VictoryOrDefeat
        );
    }

    /// <inheritdoc/>
    public override void EnterTactics()
    {
        ActionController[] participants = Combatants
            .Where(CanTakeTacticsTurn)
            .Where(controller =>
                string.Equals(
                    controller.GetComponent<Team>()?.Name,
                    DungeonProtagonistTeamName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        BeginCombat(
            participants,
            false,
            FindRegisteredTeamName(participants, DungeonProtagonistTeamName),
            EncounterConclusionPolicy.ProtagonistDefeatOnly
        );
    }

    /// <inheritdoc/>
    public override bool TryReturnToExploration()
    {
        if (combatRules == null || combatRules.GetEncounter().Phase != EncounterPhase.Active)
            return false;

        EncounterState encounter = combatRules.GetEncounter();
        bool livingOpposition = encounter.Roster.Any(entry =>
            entry.Team != encounter.ProtagonistTeam
            && combatRules.GetHealth(entry.Creature).IsLiving
        );
        if (
            livingOpposition
            || combatRules.IsResolutionActive
            || activeCombatants.Any(controller => controller != null && controller.IsTakingAction)
        )
            return false;

        combatRules.SuspendEncounter();
        StopCombatState();
        return true;
    }

    /// <inheritdoc/>
    public override void StartDungeonCombat(IReadOnlyList<ActionController> participants)
    {
        if (participants == null)
            throw new ArgumentNullException(nameof(participants));
        BeginCombat(
            participants,
            true,
            FindRegisteredTeamName(participants, DungeonProtagonistTeamName),
            EncounterConclusionPolicy.VictoryOrDefeat
        );
    }

    /// <inheritdoc/>
    public override void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements)
    {
        if (reinforcements == null)
            throw new ArgumentNullException(nameof(reinforcements));
        if (combatRules == null || combatRules.GetEncounter().Phase != EncounterPhase.Active)
            throw new InvalidOperationException("Dungeon reinforcements require active Tactics.");

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

        bool[] explorationStates = additions
            .Select(controller => controller.IsInDungeonExploration)
            .ToArray();
        try
        {
            foreach (ActionController addition in additions)
                addition.SetDungeonExploration(false);
            Pf2eRulesEngine.ApplyCombatStartRules(additions);
            combatRules.RegisterCombatants(additions);
            activeCombatants.AddRange(additions);
        }
        catch
        {
            for (int index = 0; index < additions.Length; index++)
            {
                if (explorationStates[index] && !additions[index].TryGetCombatRules(out _, out _))
                    additions[index].SetDungeonExploration(true);
            }
            throw;
        }
    }

    /// <summary>
    /// Releases encounter ownership for test and floor teardown after gameplay has already stopped.
    /// This is not a player-facing Tactics exit and must not be used by encounter flow.
    /// </summary>
    internal void ReleaseTacticsForTeardown()
    {
        if (combatRules == null || combatRules.GetEncounter().Phase != EncounterPhase.Active)
            return;
        combatRules.SuspendEncounter();
        StopCombatState();
    }

    /// <inheritdoc/>
    public override void EndCurrentTurn(ActionController expectedActor)
    {
        if (expectedActor == null)
            throw new ArgumentNullException(nameof(expectedActor));
        if (combatRules == null || combatRules.GetEncounter().Phase != EncounterPhase.Active)
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
        bool ended =
            combatRules != null && combatRules.GetEncounter().Phase == EncounterPhase.Ended;
        if (
            pendingOutcomePresentation.HasValue
            && !activeCombatants.Any(controller => controller != null && controller.IsTakingAction)
        )
        {
            EncounterOutcome pending = pendingOutcomePresentation.Value;
            pendingOutcomePresentation = null;
            CompleteEncounterOutcomePresentation(pending);
        }
        return ended;
    }

    /// <inheritdoc/>
    public override void RefreshRulesTopology()
    {
        if (combatRules == null)
            return;
        combatRules.RefreshTopology(GetCurrentTiles());
    }

    private void BeginCombat(
        IReadOnlyList<ActionController> participants,
        bool dungeonDirected,
        string protagonistTeamName,
        EncounterConclusionPolicy conclusionPolicy
    )
    {
        if (participants == null)
            throw new ArgumentNullException(nameof(participants));
        if (combatRules != null)
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

        Tile[,] tiles = GetCurrentTiles();
        bool[] explorationStates = selected
            .Select(controller => controller.IsInDungeonExploration)
            .ToArray();
        try
        {
            foreach (ActionController controller in selected)
                controller.SetDungeonExploration(false);
            activeCombatants.Clear();
            activeCombatants.AddRange(selected);
            Pf2eRulesEngine.ApplyCombatStartRules(selected);
            dungeonDirectedCombat = dungeonDirected;
            pendingOutcomePresentation = null;
            combatRules = UnityCombatRulesBridge.Create(activeCombatants, tiles);
            combatRules.EncounterStarted += PresentEncounterStarted;
            combatRules.TurnBegan += PresentTurnBegan;
            combatRules.EncounterEnded += PresentEncounterOutcome;
            combatRules.StartEncounter(protagonistTeamName, conclusionPolicy);
        }
        catch
        {
            StopCombatState(() =>
            {
                for (int index = 0; index < selected.Length; index++)
                {
                    if (explorationStates[index])
                        selected[index].SetDungeonExploration(true);
                }
            });
            throw;
        }
    }

    private static string FindFirstRegisteredTeamName(IReadOnlyList<ActionController> participants)
    {
        string firstRegisteredTeamName = string.Empty;
        foreach (ActionController participant in participants)
        {
            Team team = participant == null ? null : participant.GetComponent<Team>();
            if (team == null || string.IsNullOrWhiteSpace(team.Name))
                continue;
            if (
                string.Equals(
                    team.Name,
                    DungeonProtagonistTeamName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return team.Name;
            if (string.IsNullOrEmpty(firstRegisteredTeamName))
                firstRegisteredTeamName = team.Name;
        }
        if (!string.IsNullOrEmpty(firstRegisteredTeamName))
            return firstRegisteredTeamName;
        throw new InvalidOperationException(
            "Legacy combat requires at least one participant with a registered team name."
        );
    }

    private static string FindRegisteredTeamName(
        IReadOnlyList<ActionController> participants,
        string requiredTeamName
    )
    {
        foreach (ActionController participant in participants)
        {
            Team team = participant == null ? null : participant.GetComponent<Team>();
            if (
                team != null
                && string.Equals(team.Name, requiredTeamName, StringComparison.OrdinalIgnoreCase)
            )
                return team.Name;
        }
        throw new InvalidOperationException(
            $"Dungeon combat requires a registered {requiredTeamName} participant."
        );
    }

    private void PresentEncounterStarted()
    {
        LogInitiative();
        CombatActivityChanged.Invoke(true);
        OnCombatStart.Invoke();
    }

    private void LogInitiative()
    {
        if (combatRules == null)
            throw new InvalidOperationException(
                "Initiative presentation requires the active encounter bridge."
            );
        if (!CombatLog.TryGetInstance(out CombatLogInterface log))
            return;
        EncounterState encounter = combatRules.GetEncounter();
        string message = "Initiative Order:\n";
        for (int index = 0; index < encounter.Roster.Count; index++)
        {
            InitiativeEntry entry = encounter.Roster[index];
            string displayName = combatRules.GetController(entry.Creature).gameObject.name;
            message += $"  {index + 1}. {displayName} (Initiative: {entry.Total})\n";
        }
        log.Log(message);
    }

    private void PresentTurnBegan(TurnIdentity turn)
    {
        if (combatRules == null)
            throw new InvalidOperationException(
                "Turn presentation requires the active encounter bridge."
            );
        OnNextTurn.Invoke(combatRules.GetController(turn.Actor).gameObject);
    }

    private void PresentEncounterOutcome(EncounterOutcome outcome)
    {
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
        string winningTeam;
        try
        {
            winningTeam = playerWon ? FindProtagonistTeam() : FindLivingOppositionTeam();
        }
        catch
        {
            StopCombatState();
            throw;
        }
        bool wasDungeonDirected = dungeonDirectedCombat;

        StopCombatState();
        if (wasDungeonDirected)
            DungeonCombatEnded.Invoke(winningTeam);
        else
            OnCombatEnd.Invoke(winningTeam);
        if (!wasDungeonDirected || !playerWon)
            OnCombatOutcome.Invoke(playerWon);
    }

    private string FindLivingOppositionTeam()
    {
        if (combatRules == null)
            throw new InvalidOperationException(
                "Outcome presentation requires the encounter bridge."
            );
        EncounterState encounter = combatRules.GetEncounter();
        foreach (InitiativeEntry entry in encounter.Roster)
        {
            if (
                entry.Team == encounter.ProtagonistTeam
                || !combatRules.GetHealth(entry.Creature).IsLiving
            )
                continue;
            ActionController controller = combatRules.GetController(entry.Creature);
            Team team = controller.GetComponent<Team>();
            if (team != null && !string.IsNullOrWhiteSpace(team.Name))
                return team.Name;
        }
        if (encounter.ConclusionPolicy == EncounterConclusionPolicy.ProtagonistDefeatOnly)
            return "Opposition";
        throw new InvalidOperationException(
            "Player defeat requires a mapped living opposition team."
        );
    }

    private string FindProtagonistTeam()
    {
        if (combatRules == null)
            throw new InvalidOperationException(
                "Outcome presentation requires the encounter bridge."
            );
        PlayerId protagonist = combatRules.GetEncounter().ProtagonistTeam;
        foreach (InitiativeEntry entry in combatRules.GetEncounter().Roster)
        {
            CreatureState creature = combatRules.Snapshot.Creatures[entry.Creature];
            if (creature.Player == protagonist)
            {
                ActionController controller = combatRules.GetController(entry.Creature);
                Team team = controller.GetComponent<Team>();
                if (team != null && !string.IsNullOrWhiteSpace(team.Name))
                    return team.Name;
            }
        }
        throw new InvalidOperationException("Player victory requires a mapped protagonist team.");
    }

    private void StopCombatState(Action afterOwnershipReleased = null)
    {
        if (combatRules != null)
        {
            UnityCombatRulesBridge releasing = combatRules;
            ActionController[] stoppingControllers = activeCombatants.ToArray();
            releasing.EncounterStarted -= PresentEncounterStarted;
            releasing.TurnBegan -= PresentTurnBegan;
            releasing.EncounterEnded -= PresentEncounterOutcome;
            combatRules = null;
            activeCombatants.Clear();
            dungeonDirectedCombat = false;
            pendingOutcomePresentation = null;
            pendingCombatStops++;
            releasing.ReleaseOwnership(() =>
            {
                CompleteCombatStateStop(stoppingControllers);
                afterOwnershipReleased?.Invoke();
            });
            return;
        }
        if (pendingCombatStops > 0)
            return;
        ActionController[] detachedControllers = activeCombatants.ToArray();
        activeCombatants.Clear();
        dungeonDirectedCombat = false;
        pendingOutcomePresentation = null;
        CompleteCombatStateStop(detachedControllers);
        afterOwnershipReleased?.Invoke();
    }

    private void CompleteCombatStateStop(IReadOnlyList<ActionController> stoppingControllers)
    {
        foreach (ActionController controller in stoppingControllers)
            controller?.ResetEncounterTurnState();
        if (pendingCombatStops > 0)
            pendingCombatStops--;
        if (combatRules == null && pendingCombatStops == 0)
            CombatActivityChanged.Invoke(false);
    }

    private static Tile[,] GetCurrentTiles()
    {
        if (TryGetCurrentTiles(out Tile[,] tiles))
            return tiles;
        throw new InvalidOperationException(
            "Combat requires an initialized compatible grid and topology."
        );
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

    private static bool CanTakeTacticsTurn(ActionController controller)
    {
        if (!CanTakeTurn(controller))
            return false;
        CreatureComponent creature = controller.GetComponent<CreatureComponent>();
        return creature == null || !creature.IsDefeated && creature.hp > 0;
    }

    // Camera projection for legacy callers.
    public Vector3[] getPoistions() =>
        GetCombatants().Select(combatant => combatant.transform.position).ToArray();
}
