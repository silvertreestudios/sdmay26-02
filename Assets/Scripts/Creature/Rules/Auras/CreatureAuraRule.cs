using Game.Creature;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>Describes one Unity-facing aura visualization definition.</summary>
    public interface ICreatureAuraRule
    {
        /// <summary>Gets the stable aura slug.</summary>
        string Slug { get; }

        /// <summary>Determines whether the configured aura contributes scene visualization.</summary>
        bool HasVisual(CreatureAura aura);
    }

    /// <summary>Connects one live aura configuration to its Unity visualization definition.</summary>
    public sealed class CreatureAuraInstance
    {
        /// <summary>Creates one live visual aura instance.</summary>
        public CreatureAuraInstance(
            ActionController sourceController,
            CreatureComponent sourceCreature,
            CreatureAura aura,
            ICreatureAuraRule rule
        )
        {
            SourceController = sourceController;
            SourceCreature = sourceCreature;
            Aura = aura;
            Rule = rule;
        }

        /// <summary>Gets the source controller.</summary>
        public ActionController SourceController { get; }

        /// <summary>Gets the source creature data.</summary>
        public CreatureComponent SourceCreature { get; }

        /// <summary>Gets the configured aura.</summary>
        public CreatureAura Aura { get; }

        /// <summary>Gets the visualization definition.</summary>
        public ICreatureAuraRule Rule { get; }

        /// <summary>Gets the live source object when a controller is available.</summary>
        public GameObject SourceObject =>
            SourceController == null ? null : SourceController.gameObject;
    }
}
