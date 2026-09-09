using System;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Retains Slowed's pre-attachment seed and receives projections from committed rules effects.
    /// </summary>
    internal sealed class SlowedSeed : MonoBehaviour
    {
        [SerializeField, Range(0, 3)]
        private int value;

        internal int Value => value;

        internal void ApplyBeforeAttachment(int appliedValue) =>
            value = Math.Max(value, appliedValue);

        internal void Project(int committedValue) => value = committedValue;
    }
}
