using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>Owns Rage's dispatcher and combatant-state composition.</summary>
    internal sealed class UnityRageModule
        : IUnityEncounterDispatcherModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly RageActionDefinition definition;

        internal UnityRageModule(RageActionDefinition definition) =>
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseRageRules(definition);

        /// <inheritdoc/>
        public void PrepareCombatant(UnityCombatantEnrollmentBuilder builder)
        {
            RageActorState state = UnityRageActorStateProvider.CreateState(builder.Creature);
            builder.AddRuleBindings(RageRules.CreateInitialBindings(builder.CreatureId, state));
        }
    }

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
            Conditions conditions = creature.GetComponent<Conditions>();
            string armorCategory = creature.equippedArmor?.category ?? string.Empty;
            return new RageActorState(
                prepared.HasOwnedItem("rage"),
                prepared.HasOwnedItem("quick-tempered"),
                HasCondition(conditions, "Fatigued"),
                HasCondition(conditions, "Encumbered"),
                string.Equals(armorCategory, "heavy", StringComparison.OrdinalIgnoreCase),
                prepared.RollOptions.Contains("feat:invulnerable-rager"),
                Math.Max(0, creature.level),
                creature.conMod
            );
        }

        private static bool HasCondition(Conditions conditions, string expected) =>
            conditions != null
            && conditions.ActiveConditionNames.Any(condition =>
                string.Equals(condition, expected, StringComparison.OrdinalIgnoreCase)
            );
    }
}

/// <summary>
/// Presents Rage in Unity while delegating eligibility, costs, effects, and health changes to the
/// rules runtime.
/// </summary>
public sealed class RulesRageAction : EntityAction
{
    /// <summary>Creates the one-action Rage action-bar entry.</summary>
    public RulesRageAction()
        : base(1) { }

    /// <inheritdoc/>
    public override string ActionName => "Rage";

    /// <inheritdoc/>
    public override bool IsAvailable(ActionController controller)
    {
        if (
            controller == null
            || !controller.TryGetCombatRules(
                out UnityCombatRulesBridge bridge,
                out CreatureId creature
            )
        )
        {
            return false;
        }

        CreatureComponent actor = controller.GetComponent<CreatureComponent>();
        return actor != null
            && RageRules.GetAvailability(
                bridge.Snapshot,
                creature,
                UnityRageActorStateProvider.CreateState(actor)
            ) is AvailableActionAvailability;
    }

    /// <inheritdoc/>
    public override void Invoke(GameObject target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        ActionController controller = target.GetComponent<ActionController>();
        try
        {
            if (
                controller == null
                || !controller.TryGetCombatRules(
                    out UnityCombatRulesBridge bridge,
                    out CreatureId creature
                )
            )
            {
                Debug.LogWarning("Rage requires active combat rules authority.", target);
                return;
            }

            OpResult<RageStartOutcome> result = bridge.Dispatch(new RageActionOp(creature));
            if (result is ResolvedOpResult<RageStartOutcome>)
            {
                CombatLog.GetInstance().Log("- " + target.name + " used Rage");
            }
            else if (result is InvalidOpResult<RageStartOutcome> invalid)
            {
                Debug.LogWarning($"Rage was rejected: {invalid.Reason}", target);
            }
            else
            {
                Debug.LogWarning("Rage did not complete.", target);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, target);
        }
        finally
        {
            if (controller != null)
                controller.IsTakingAction = false;
            OnActionComplete.Invoke();
            CombatManager.GetInstance().CheckForEndOfGame();
            OnGameplayStateCommitted.Invoke();
        }
    }
}
