using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Unity;
using GridPrivate;
using GridPublic;
using UnityEngine;
using UnityEngine.Events;

public class CombatManager : CombatManagerInterface
{
    // Fields
    protected List<ActionController> Combatants = new();
    protected List<TurnStep> TurnQueue = new();
    protected ActionController TurnTaker = null;
    private readonly List<ActionController> activeCombatants = new();
    private readonly Dictionary<ActionController, uint> initiatives = new();
    private readonly HashSet<ActionController> actedThisRound = new();
    private bool combatActive;
    private bool dungeonDirectedCombat;
    private UnityCombatRulesBridge healthRules;

    // Events
    // See CombatEvents.cs for a list of events triggered by this class

    /// <summary>
    /// Raised when a dungeon-directed combat ends with the surviving team name, or an empty
    /// string when no team survives.
    /// </summary>
    public event Action<string> DungeonCombatEnded = delegate { };

    /// <summary>Raised whenever initiative starts or returns to exploration.</summary>
    public override event Action<bool> CombatActivityChanged = delegate { };

    /// <inheritdoc/>
    public override bool IsCombatActive => combatActive;

    public override void AddCombatant(ActionController combatant)
    {
        if (combatant == null)
            throw new ArgumentNullException(nameof(combatant));
        if (Combatants.Contains(combatant))
            return;

        OnCombatantJoin.Invoke(combatant.gameObject);
        Combatants.Add(combatant);
    }

    public override void AddEvent(TurnStep ts)
    {
        TurnQueue.Add(ts);
    }

    public override void Remove(ActionController combatant)
    {
        Combatants.Remove(combatant);
        activeCombatants.Remove(combatant);
        initiatives.Remove(combatant);
        actedThisRound.Remove(combatant);
        if (TurnTaker == combatant)
            TurnTaker = null;
        for (int i = TurnQueue.Count - 1; i >= 0; i--)
        {
            if (TurnQueue[i].Player == combatant)
                TurnQueue.RemoveAt(i);
        }
    }

    public override void Remove(TurnStep e)
    {
        for (int i = 0; i < TurnQueue.Count; i++)
        {
            if (TurnQueue[i] == e)
                TurnQueue.RemoveAt(i);
        }
    }

    public override void Remove(UnityAction e)
    {
        for (int i = 0; i < TurnQueue.Count; i++)
        {
            if (TurnQueue[i].Event == e)
                TurnQueue.RemoveAt(i);
        }
    }

    public override GameObject WhosTurn()
    {
        return TurnTaker == null ? null : TurnTaker.gameObject;
    }

    public override List<GameObject> GetCombatants()
    {
        List<GameObject> list = new();
        foreach (var c in TurnQueue)
        {
            if (c.Player != null && activeCombatants.Contains(c.Player))
                list.Add(c.Player.gameObject);
        }

        if (TurnTaker != null && list.Remove(TurnTaker.gameObject))
            list.Insert(0, TurnTaker.gameObject);
        return list;
    }

    [ContextMenu("StartCombat")]
    public override void StartCombat()
    {
        BeginCombat(Combatants.Where(CanTakeTurn).ToArray(), false);
    }

    /// <inheritdoc/>
    public override void StartDungeonCombat(IReadOnlyList<ActionController> participants)
    {
        BeginCombat(participants, true);
    }

    /// <inheritdoc/>
    public override void AddDungeonReinforcements(IReadOnlyList<ActionController> reinforcements)
    {
        if (reinforcements == null)
            throw new ArgumentNullException(nameof(reinforcements));
        if (!combatActive || !dungeonDirectedCombat)
        {
            throw new InvalidOperationException(
                "Dungeon reinforcements require an active dungeon-directed combat."
            );
        }

        ActionController[] additions = reinforcements.Distinct().ToArray();
        if (additions.Any(controller => controller == null))
            throw new ArgumentException(
                "Reinforcements cannot contain null.",
                nameof(reinforcements)
            );
        if (additions.Any(controller => !Combatants.Contains(controller)))
        {
            throw new InvalidOperationException(
                "Every reinforcement must register with the combat manager before activation."
            );
        }
        if (additions.Any(controller => activeCombatants.Contains(controller)))
        {
            throw new InvalidOperationException(
                "An active combatant cannot be added as a reinforcement twice."
            );
        }
        if (additions.Any(controller => !CanTakeTurn(controller)))
        {
            throw new InvalidOperationException(
                "Defeated or disabled combatants cannot join as reinforcements."
            );
        }

        foreach (ActionController reinforcement in additions)
        {
            activeCombatants.Add(reinforcement);
            uint initiative = reinforcement.GetInitiative();
            initiatives.Add(reinforcement, initiative);
            bool waitsForNextRound =
                TurnTaker != null
                && initiatives.TryGetValue(TurnTaker, out uint currentInitiative)
                && initiative > currentInitiative;
            if (waitsForNextRound)
                actedThisRound.Add(reinforcement);
            InsertReinforcement(reinforcement, waitsForNextRound);
        }

        healthRules.RegisterCombatants(additions);
        Pf2eRulesEngine.ApplyCombatStartRules(additions);
        LogInitiative(
            "Reinforcements",
            additions.OrderByDescending(controller => initiatives[controller]).ToArray()
        );
    }

    /// <inheritdoc/>
    public override void SuspendDungeonCombat()
    {
        if (!combatActive || !dungeonDirectedCombat)
        {
            throw new InvalidOperationException(
                "Only an active dungeon-directed combat can be suspended."
            );
        }

        Pf2eRulesEngine.EndEncounter(activeCombatants);
        StopCombatState();
    }

    private void BeginCombat(IReadOnlyList<ActionController> participants, bool dungeonDirected)
    {
        if (participants == null)
            throw new ArgumentNullException(nameof(participants));
        if (combatActive)
            throw new InvalidOperationException("Combat is already active.");

        ActionController[] selected = participants.Distinct().ToArray();
        if (selected.Length == 0)
            throw new ArgumentException(
                "Combat requires at least one participant.",
                nameof(participants)
            );
        if (selected.Any(controller => controller == null))
            throw new ArgumentException(
                "Combat participants cannot contain null.",
                nameof(participants)
            );
        if (selected.Any(controller => !Combatants.Contains(controller)))
        {
            throw new InvalidOperationException(
                "Every participant must register with the combat manager before combat starts."
            );
        }
        if (selected.Any(controller => !CanTakeTurn(controller)))
        {
            throw new InvalidOperationException(
                "Defeated or disabled controllers cannot begin combat."
            );
        }

        activeCombatants.Clear();
        activeCombatants.AddRange(selected);
        actedThisRound.Clear();
        initiatives.Clear();
        TurnTaker = null;
        dungeonDirectedCombat = dungeonDirected;
        combatActive = true;
        foreach (ActionController controller in activeCombatants)
            controller.ResetEncounterTurnState();

        RebuildCombatRulesBridge();
        RollInitiative(activeCombatants);
        Pf2eRulesEngine.ApplyCombatStartRules(activeCombatants);
        CombatActivityChanged.Invoke(true);
        OnCombatStart.Invoke();
        NextTurn();
    }

    private void RebuildCombatRulesBridge()
    {
        healthRules = UnityCombatRulesBridge.Create(activeCombatants, GetCurrentTilesOrFallback());
    }

    /// <inheritdoc/>
    public override void RefreshRulesTopology()
    {
        if (healthRules != null && TryGetCurrentTiles(out Tile[,] tiles))
        {
            healthRules.RefreshTopology(tiles);
        }
    }

    private static Tile[,] GetCurrentTilesOrFallback()
    {
        if (TryGetCurrentTiles(out Tile[,] tiles))
            return tiles;

        // Some focused combat tests intentionally construct no grid. Their actions never invoke
        // rules-native movement, so a single open cell keeps the shared non-movement state usable
        // without manufacturing a mutable or unbounded topology.
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

    /// <summary>
    /// Helper to order inititives.
    /// </summary>
    private void RollInitiative(IReadOnlyList<ActionController> participants)
    {
        List<ActionController> turnOrder = new();
        List<uint> initiativeValues = new();

        // Insert all AC's in sorted turnOrder
        foreach (ActionController ac in participants)
        {
            // Attempt to insert
            int i;
            uint initiative = ac.GetInitiative();
            for (i = 0; i < turnOrder.Count; i++)
            {
                if (initiativeValues[i] < initiative)
                {
                    initiativeValues.Insert(i, initiative);
                    turnOrder.Insert(i, ac);
                    break;
                }
            }
            // If no insertion, insert at end
            if (i == initiativeValues.Count)
            {
                initiativeValues.Add(initiative);
                turnOrder.Add(ac);
            }
        }
        this.initiatives.Clear();
        for (int index = 0; index < turnOrder.Count; index++)
            initiatives.Add(turnOrder[index], initiativeValues[index]);

        LogInitiative("Initiative Order", turnOrder);

        // Clear TurnQueue and add by initiative
        TurnQueue.Clear();
        foreach (ActionController ac in turnOrder)
            TurnQueue.Add(new TurnStep(ac));
    }

    private void LogInitiative(string heading, IReadOnlyList<ActionController> turnOrder)
    {
        string log = heading + ":\n";
        for (int i = 0; i < turnOrder.Count; i++)
        {
            ActionController controller = turnOrder[i];
            log +=
                "  "
                + (i + 1)
                + ". "
                + controller.gameObject.name
                + " (Initiative: "
                + initiatives[controller]
                + ")\n";
        }
        CombatLog.GetInstance().Log(log);
    }

    private void InsertReinforcement(ActionController reinforcement, bool waitsForNextRound)
    {
        uint reinforcementInitiative = initiatives[reinforcement];
        int insertionIndex = TurnQueue.Count;
        for (int index = 0; index < TurnQueue.Count; index++)
        {
            ActionController candidate = TurnQueue[index].Player;
            if (candidate == null)
                continue;

            bool candidateWaits = actedThisRound.Contains(candidate);
            if (!waitsForNextRound && candidateWaits)
            {
                insertionIndex = index;
                break;
            }
            if (waitsForNextRound != candidateWaits)
                continue;
            if (
                !initiatives.TryGetValue(candidate, out uint candidateInitiative)
                || reinforcementInitiative > candidateInitiative
            )
            {
                insertionIndex = index;
                break;
            }
        }

        TurnQueue.Insert(insertionIndex, new TurnStep(reinforcement));
    }

    public override bool CheckForEndOfGame()
    {
        if (!combatActive)
            return false;

        List<string> teams = new();
        // Check if a team has won yet.
        foreach (ActionController combatant in activeCombatants.Where(CanTakeTurn))
        {
            Team component = combatant.GetComponent<Team>();
            string team = component == null ? string.Empty : component.Name;
            if (!string.IsNullOrWhiteSpace(team) && !teams.Contains(team))
                teams.Add(team);
        }
        if (teams.Count < 2)
        {
            string winningTeam = teams.Count == 1 ? teams[0] : string.Empty;
            if (winningTeam.Length > 0)
                Debug.Log("Team " + winningTeam + " wins!");
            else
                Debug.LogWarning("Combat ended without a surviving team.");

            Pf2eRulesEngine.EndEncounter(activeCombatants);
            bool wasDungeonDirected = dungeonDirectedCombat;
            StopCombatState();
            if (wasDungeonDirected)
            {
                DungeonCombatEnded.Invoke(winningTeam);
                if (!string.Equals(winningTeam, "players", StringComparison.OrdinalIgnoreCase))
                {
                    OnCombatOutcome.Invoke(false);
                }
            }
            else
            {
                OnCombatEnd.Invoke(winningTeam);
                if (winningTeam.Length > 0)
                {
                    OnCombatOutcome.Invoke(
                        string.Equals(winningTeam, "players", StringComparison.OrdinalIgnoreCase)
                    );
                }
            }
            return true;
        }
        return false;
    }

    public override void NextTurn()
    {
        EnsureLegacyQueueState();
        if (!combatActive)
            return;
        if (CheckForEndOfGame() || TurnQueue.Count == 0)
            return;

        ActionController[] eligible = activeCombatants.Where(CanTakeTurn).ToArray();
        if (eligible.Length > 0 && eligible.All(actedThisRound.Contains))
            actedThisRound.Clear();

        int attempts = TurnQueue.Count;
        while (attempts-- > 0)
        {
            // Take the next turn.
            TurnStep e = TurnQueue[0];
            TurnQueue.RemoveAt(0);
            if (e.Player)
            {
                if (!activeCombatants.Contains(e.Player) || !CanTakeTurn(e.Player))
                    continue;
                if (actedThisRound.Contains(e.Player))
                {
                    TurnQueue.Add(e);
                    continue;
                }

                TurnTaker = e.Player;
                actedThisRound.Add(TurnTaker);
                OnNextTurn.Invoke(TurnTaker.gameObject);
                if (CanTakeTurn(e.Player))
                    ApplyTurnStartAuras(TurnTaker);
                if (CanTakeTurn(e.Player))
                    e.Trigger();

                if (activeCombatants.Contains(e.Player) && CanTakeTurn(e.Player))
                    TurnQueue.Add(e);
                else
                    NextTurn();
                return;
            }

            e.Trigger();
            TurnQueue.Add(e);
            return;
        }
    }

    private void EnsureLegacyQueueState()
    {
        if (combatActive || Combatants.Count == 0 || TurnQueue.Count == 0)
            return;

        activeCombatants.Clear();
        activeCombatants.AddRange(Combatants.Where(CanTakeTurn));
        actedThisRound.Clear();
        initiatives.Clear();
        foreach (ActionController controller in activeCombatants)
            initiatives[controller] = 0;
        dungeonDirectedCombat = false;
        combatActive = activeCombatants.Count > 0;
    }

    private void StopCombatState()
    {
        foreach (ActionController controller in activeCombatants)
            controller.ResetEncounterTurnState();
        if (healthRules != null)
        {
            healthRules.ReleaseOwnership();
            healthRules = null;
        }
        activeCombatants.Clear();
        initiatives.Clear();
        actedThisRound.Clear();
        TurnQueue.Clear();
        TurnTaker = null;
        combatActive = false;
        dungeonDirectedCombat = false;
        CombatActivityChanged.Invoke(false);
    }

    private static bool CanTakeTurn(ActionController actionController)
    {
        return actionController != null
            && actionController.gameObject.activeSelf
            && actionController.isActiveAndEnabled;
    }

    private void ApplyTurnStartAuras(ActionController acting)
    {
        if (acting == null)
            return;

        GridAPI grid = UnityEngine.Object.FindFirstObjectByType<GridAPI>();
        GridAPIPrivate gridPrivate = grid as GridAPIPrivate;
        if (gridPrivate == null)
            return;

        CreatureAuraResolver.ApplyTurnStartAuras(acting, activeCombatants, gridPrivate.GetTiles());
    }

    //added by Ryan Meyer 5/29/24, For cameraManager to get the positions of all tokens
    public Vector3[] getPoistions()
    {
        IReadOnlyList<ActionController> positionedCombatants = combatActive
            ? activeCombatants
            : Combatants;
        Vector3[] positions = new Vector3[positionedCombatants.Count];
        int i = 0;
        foreach (ActionController c in positionedCombatants)
        {
            positions[i] = c.gameObject.transform.position;
            i++;
        }
        return positions;
    }
}
