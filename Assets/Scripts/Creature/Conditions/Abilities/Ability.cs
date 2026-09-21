using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Rules.Runtime;
using UnityEngine;
using UnityEngine.Events;

public class Ability : ConditionSource
{
    public string Name { get; protected set; }
    public List<string> Traits = new List<string>();
    UnityAction<GameObject> ApplyCallback;

    /// <summary>Creates an imported passive with stable replay provenance.</summary>
    /// <param name="name">The nonblank passive name used to derive its persistent source.</param>
    /// <param name="apply">The Unity adapter that installs the passive's current behavior.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="apply"/> is null.</exception>
    public Ability(string name, UnityAction<GameObject> apply)
        : base(CreateReplaySource(name))
    {
        this.Name = name;
        ApplyCallback = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    private static RuleSource CreateReplaySource(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A passive name is required.", nameof(name));
        return RuleSource.FromSlug($"passive:{name.Trim().ToLower(CultureInfo.InvariantCulture)}");
    }

    public void Apply(GameObject g)
    {
        ApplyCallback(g);
        Debug.Log("Applied ability " + Name + " to " + g.name);
    }
}
