using System.Collections.Generic;
using UnityEngine;

public class Team : MonoBehaviour
{
    [field: SerializeField]
    public string Name { get; set; }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return;

        TeamRules tr = TeamRules.GetInstance();
        if(!tr.Contains(Name)) 
        {
            tr.AddHostileTeam(Name);
            tr.OneWayFriendly(Name, Name);
        }
    }
}
