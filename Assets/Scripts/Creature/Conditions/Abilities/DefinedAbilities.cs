using System.Collections.Generic;
using UnityEngine;

public static class DefinedAbilities
{
    private static Dictionary<string, Ability> Abilities = new()
    {
        {"Slow", Slow },
    };

    private static Ability Slow = new("Slow", (GameObject g) =>
    {
        Condition slow;
        if ((slow = DefinedConditions.TryGet("Slow 1")) != null)
        {
            slow.Apply(Slow, g);
        }
        g.GetComponent<ActionController>().GetReactionsEvent.AddListener(
            (List<EntityAction> reactions) => reactions.Clear()
        );
    });
}
