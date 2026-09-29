using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.Creature.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using Game.Rules.Unity.Composition;
using Game.Rules.Unity.Vfx;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>Selects Rage visuals from committed typed effect state.</summary>
    public static class RagePersistentVfxSelector
    {
        /// <summary>Returns the normal or Quick-Tempered production cue.</summary>
        public static PersistentVfxSelection? Select(ActiveEffectInstance effect)
        {
            if (effect.State is not RageEffectState rage)
                return null;
            return new PersistentVfxSelection(
                effect.SourceCreature,
                new VfxCueId(
                    rage.StartedByQuickTempered ? "auxiliary/rage-quick-tempered" : "auxiliary/rage"
                )
            );
        }
    }

    /// <summary>Owns Rage's dispatcher and combatant-state composition.</summary>
    internal sealed class UnityRageModule
        : IUnityEncounterDispatcherModule,
            IUnityEncounterRuntimeModule,
            IUnityCombatantEnrollmentModule
    {
        private readonly RageActionDefinition definition;
        private readonly IReadOnlyDictionary<CreatureId, CreatureComponent> creatures;
        private readonly UnityVfxPlayback vfx;

        internal UnityRageModule(
            RageActionDefinition definition,
            IReadOnlyDictionary<CreatureId, CreatureComponent> creatures,
            UnityVfxPlayback vfx
        )
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.creatures = creatures ?? throw new ArgumentNullException(nameof(creatures));
            this.vfx = vfx ?? throw new ArgumentNullException(nameof(vfx));
        }

        /// <inheritdoc/>
        public void ConfigureDispatcher(RuleDispatcherBuilder builder) =>
            builder.UseRageRules(definition);

        /// <inheritdoc/>
        public void RegisterRuntime(RuleDispatcher dispatcher, CompositeLifetime lifetime)
        {
            UnityPersistentVfxObserver persistent = new(
                vfx,
                creatures,
                RagePersistentVfxSelector.Select
            );
            lifetime.Add(persistent);
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectCreatedFact>(persistent));
            lifetime.Add(dispatcher.RegisterFactObserver<ActiveEffectRemovedFact>(persistent));
            lifetime.Add(
                dispatcher.RegisterFactObserver<EncounterOutcomeCommittedFact>(persistent)
            );
        }

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
            string armorCategory = creature.equippedArmor?.category ?? string.Empty;
            return new RageActorState(
                prepared.HasOwnedItem("rage"),
                prepared.HasOwnedItem("quick-tempered"),
                string.Equals(armorCategory, "heavy", StringComparison.OrdinalIgnoreCase),
                prepared.RollOptions.Contains("feat:invulnerable-rager"),
                Math.Max(0, creature.level),
                creature.conMod
            );
        }
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
