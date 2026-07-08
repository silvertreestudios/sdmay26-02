using Game.Creature;
using System;
using UnityEngine;

namespace Game.Creature.Rules
{
    /// <summary>
    /// Extracts Unity component state into Unity-free rule state; it should not apply rule results or contain action logic.
    /// </summary>
    public static class UnityCreatureRulesAdapter
    {
        /// <summary>
        /// Builds a rules snapshot from an actor GameObject for use by pure PF2e rule classes.
        /// </summary>
        /// <param name="actor">The Unity actor whose creature, conditions, and prepared data should be read.</param>
        /// <returns>A Unity-free creature rules state, or an empty state when the actor is missing required components.</returns>
        public static CreatureRulesState From(GameObject actor)
        {
            if (actor == null)
                return new CreatureRulesState();

            CreatureComponent creature = actor.GetComponent<CreatureComponent>();
            if (creature == null)
                return new CreatureRulesState();

            Conditions conditions = actor.GetComponent<Conditions>();
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);

            return new CreatureRulesState
            {
                Level = creature.level,
                ConstitutionModifier = creature.conMod,
                ArmorCategory = creature.equippedArmor?.category,
                Prepared = prepared,
                Conditions = conditions?.GetConditionNames() ?? Array.Empty<string>(),
                TempHpImmunitySources = creature.GetTempHpImmunitySources()
            };
        }
    }
}
