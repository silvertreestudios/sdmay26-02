using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TeamRules : SingletonMonoBehaviour<TeamRules>
{
    //[SerializeField]
    private List<string> TeamList = new();

    private Dictionary<string, List<string>> Friendly = new();
    private Dictionary<string, List<string>> Neutral = new();
    private Dictionary<string, List<string>> Hostile = new();

    /// <summary>
    /// Defines a team. Relationships start as friendly to all teams.
    /// </summary>
    public void AddFriendlyTeam(string team)
    {
        Friendly.Add(team, new());
        Neutral.Add(team, new());
        Hostile.Add(team, new());

        foreach (string t in TeamList)
            MutualFriendly(team, t);

        TeamList.Add(team);
    }

    /// <summary>
    /// Defines a team. Relationships start as neutral to all teams.
    /// </summary>
    public void AddNeutralTeam(string team)
    {
        Friendly.Add(team, new());
        Neutral.Add(team, new());
        Hostile.Add(team, new());

        foreach (string t in TeamList)
            MutualNeutral(team, t);

        TeamList.Add(team);
    }

    /// <summary>
    /// Defines a team. Relationships start as hostile to all teams.
    /// </summary>
    public void AddHostileTeam(string team)
    {
        Friendly.Add(team, new());
        Neutral.Add(team, new());
        Hostile.Add(team, new());

        foreach (string t in TeamList)
            MutualHostile(team, t);

        TeamList.Add(team);
    }

    /// <summary>
    /// Private helper function
    /// </summary>
    protected void SetRelation(
        Dictionary<string, List<string>> target,
        Dictionary<string, List<string>> other1,
        Dictionary<string, List<string>> other2,
        string team1,
        string team2)
    {
        // Remove from other dictionaries
        List<string> list = new();
        if(other1.TryGetValue(team1, out list))
            list.Remove(team2);
        if (other2.TryGetValue(team1, out list))
            list.Remove(team2);

        // Add/overwrite in correct dictionary
        if(!target[team1].Contains(team2))
            target[team1].Add(team2);
    }

    /// <summary>
    /// Returns true if team has been defined
    /// </summary>
    /// <param name="team">The team that may be defined</param>
    /// <returns></returns>
    public bool Contains(string team)
    {
        return TeamList.Contains(team);
    }

    /// <summary>
    /// Makes team1 friendly to team2. Does not affect team2 defined relations
    /// </summary>
    public void OneWayFriendly(string team1, string team2)
    {
        SetRelation(Friendly, Neutral, Hostile, team1, team2);
    }

    /// <summary>
    /// Makes team1 and team2 mutually friendly.
    /// </summary>
    public void MutualFriendly(string team1, string team2)
    {
        OneWayFriendly(team1, team2);
        OneWayFriendly(team2, team1);
    }

    /// <summary>
    /// Makes team1 neutral to team2. Does not affect team2 defined relations
    /// </summary>
    public void OneWayNeutral(string team1, string team2)
    {
        SetRelation(Neutral, Friendly, Hostile, team1, team2);
    }

    /// <summary>
    /// Makes team1 and team2 mutually neutral.
    /// </summary>
    public void MutualNeutral(string team1, string team2)
    {
        OneWayNeutral(team1, team2);
        OneWayNeutral(team2, team1);
    }

    /// <summary>
    /// Makes team1 hostile to team2. Does not affect team2 defined relations
    /// </summary>
    public void OneWayHostile(string team1, string team2)
    {
        SetRelation(Hostile, Friendly, Neutral, team1, team2);
    }

    /// <summary>
    /// Makes team1 and team2 mutually hostile.
    /// </summary>
    public void MutualHostile(string team1, string team2)
    {
        OneWayHostile(team1, team2);
        OneWayHostile(team2, team1);
    }

    /// <summary>
    /// Returns true if team1 is friendly to team2
    /// </summary>
    public bool IsFriendly(string team1, string team2)
    {
        return Friendly[team1].Contains(team2);
    }

    /// <summary>
    /// Returns true if team1 is neutral to team2
    /// </summary>
    public bool IsNeutral(string team1, string team2)
    {
        return Neutral[team1].Contains(team2);
    }

    /// <summary>
    /// Returns true if team1 is hostile to team2
    /// </summary>
    public bool IsHostile(string team1, string team2)
    {
        return Hostile[team1].Contains(team2);
    }

    /// <summary>
    /// Returns list friendly teams 
    /// </summary>
    public List<string> FriendlyTo(string team)
    {
        return Friendly[team];
    }
    public List<string> FriendlyTo(GameObject g)
    {
        return FriendlyTo(g.GetComponent<Team>().Name);
    }

    /// <summary>
    /// Returns list neutral teams 
    /// </summary>
    public List<string> NeutralTo(string team)
    {
        return Neutral[team];
    }
    public List<string> NeutralTo(GameObject g)
    {
        return NeutralTo(g.GetComponent<Team>().Name);
    }

    /// <summary>
    /// Returns list hostile teams 
    /// </summary>
    public List<string> HostileTo(string team)
    {
        return Hostile[team];
    }
    public List<string> HostileTo(GameObject g)
    {
        return FriendlyTo(g.GetComponent<Team>().Name);
    }
}