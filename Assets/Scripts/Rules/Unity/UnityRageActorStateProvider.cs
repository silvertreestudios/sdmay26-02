using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Captures Unity creature data as immutable inputs consumed only by the Rage rules module.
    /// </summary>
    internal sealed class UnityRageActorStateProvider : IRageActorStateProvider
    {
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;

        internal UnityRageActorStateProvider(
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures
        ) => this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));

        /// <inheritdoc/>
        public RageActorState Get(CreatureId actor)
        {
            if (!creatures.TryGetValue(actor, out CreatureComponent creature))
                throw new InvalidOperationException(
                    "Rage actor facts require a registered Unity creature."
                );
            return CreateState(creature);
        }

        internal static RageActorState CreateState(CreatureComponent creature)
        {
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            PreparedCharacter prepared = Pf2eCharacterPreparer.EnsurePrepared(creature);
            PreparedCreatureInputs inputs = prepared.Rules.Inputs;
            Conditions conditions = creature.GetComponent<Conditions>();
            string armorCategory = creature.equippedArmor?.category ?? string.Empty;
            return new RageActorState(
                prepared.HasOwnedItem("rage"),
                prepared.HasOwnedItem("quick-tempered"),
                HasCondition(conditions, "Fatigued"),
                HasCondition(conditions, "Encumbered"),
                string.Equals(armorCategory, "heavy", StringComparison.OrdinalIgnoreCase),
                prepared.HasOwnedItem("invulnerable-rager"),
                inputs.Level,
                inputs.Abilities.Constitution
            );
        }

        private static bool HasCondition(Conditions conditions, string expected) =>
            conditions != null
            && conditions.ActiveConditionNames.Any(condition =>
                string.Equals(condition, expected, StringComparison.OrdinalIgnoreCase)
            );
    }
}
