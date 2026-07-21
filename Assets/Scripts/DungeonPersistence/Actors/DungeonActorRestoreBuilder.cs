using System;
using System.Collections.Generic;
using System.Linq;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.DungeonPersistence.Actors
{
    public static partial class DungeonActorStateAdapter
    {
        private static ActorRestorePlan BuildRestorePlan(
            DungeonActorRestoreTarget target,
            IReadOnlyDictionary<string, ActionController> controllersById,
            IDictionary<string, ConditionSource> conditionSources
        )
        {
            ActionController controller = target.Controller;
            DungeonCreatureSaveState state = target.State;
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            if (creature == null)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has no CreatureComponent."
                );
            if (!creature.CanRestoreHealthBeforeEncounter)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' is not fresh enough for health restoration."
                );

            DungeonPartyMemberIdentity identity =
                controller.GetComponent<DungeonPartyMemberIdentity>();
            if (
                identity != null
                && identity.IsConfigured
                && (
                    identity.ActorInstanceId != state.InstanceId
                    || identity.CreatureContentId != state.CreatureContentId
                )
            )
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' does not match its configured stable identity."
                );

            HealthState health = RestoreHealth(state.Health);
            Conditions conditions = controller.GetComponent<Conditions>();
            if (conditions != null && !conditions.CanRestorePersistentState)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' already has live conditions."
                );
            ConditionPersistenceApplication[] restoredConditions = state
                .Conditions.Select(condition => new ConditionPersistenceApplication(
                    condition.ConditionId,
                    condition.Value,
                    ResolveConditionSource(condition.SourceInstanceId, conditionSources),
                    condition.ApplicationId
                ))
                .ToArray();

            PreparedRestoreState prepared = BuildPreparedRestoreState(creature, state);
            EquipmentRestoreState equipment = BuildEquipmentRestoreState(creature, state);
            ValidateUniqueTimedEffects(state.TimedEffects, state.InstanceId);
            TimedEffectRestoreState[] timedEffects = state
                .TimedEffects.OrderBy(effect => effect.BindingCreationOrder)
                .ThenBy(effect => effect.InstanceId, StringComparer.Ordinal)
                .Select(effect => RestoreTimedEffect(effect, state.InstanceId, controllersById))
                .ToArray();

            return new ActorRestorePlan(
                controller,
                creature,
                state,
                health,
                conditions,
                restoredConditions,
                prepared,
                equipment,
                timedEffects
            );
        }

        private static HealthState RestoreHealth(DungeonHealthSaveState health)
        {
            RuleSource temporarySource =
                health.TemporaryHitPointSourceId.Length == 0
                    ? default
                    : RuleSource.FromSlug(health.TemporaryHitPointSourceId);
            return new HealthState(
                health.CurrentHitPoints,
                health.MaximumHitPoints,
                health.TemporaryHitPoints,
                temporarySource,
                health.TemporaryHitPointImmunitySourceIds.Select(RuleSource.FromSlug)
            );
        }

        private static ConditionSource ResolveConditionSource(
            string sourceId,
            IDictionary<string, ConditionSource> sources
        )
        {
            if (sourceId.Length == 0)
                return null;
            if (!sources.TryGetValue(sourceId, out ConditionSource source))
            {
                source = new ConditionSource();
                source.RestorePersistenceIdentity(sourceId);
                sources.Add(sourceId, source);
            }
            return source;
        }

        private static PreparedRestoreState BuildPreparedRestoreState(
            CreatureComponent creature,
            DungeonCreatureSaveState state
        )
        {
            DungeonPreparedRuleSaveState saved = state.PreparedRules;
            PreparedCharacter prepared = creature.Prepared;
            bool hasSavedPreparedState =
                saved.RollOptions.Count > 0
                || saved.ActiveEffects.Count > 0
                || saved.SpellPools.Count > 0;
            if (prepared == null)
            {
                if (hasSavedPreparedState)
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' has saved prepared rules but no prepared character."
                    );
                return PreparedRestoreState.Empty;
            }

            ActivePf2eEffect[] effects = saved
                .ActiveEffects.Select(effect => new ActivePf2eEffect(
                    effect.Name,
                    effect.Slug,
                    effect.SourceSlug,
                    effect.EffectId
                ))
                .ToArray();
            SpellcastingState spellcasting = prepared.Spellcasting;
            if (spellcasting == null)
            {
                if (saved.SpellPools.Count > 0)
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' has saved spell pools but no spellcasting state."
                    );
                return new PreparedRestoreState(
                    prepared,
                    saved.RollOptions.ToArray(),
                    effects,
                    Array.Empty<SpellPoolRestoreState>()
                );
            }

            if (spellcasting.Pools.Count != saved.SpellPools.Count)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' spell-pool definitions do not match saved content."
                );
            List<SpellPoolRestoreState> pools = new();
            foreach (DungeonSpellPoolSaveState savedPool in saved.SpellPools)
            {
                if (!spellcasting.Pools.TryGetValue(savedPool.PoolId, out SpellSlotPool livePool))
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' is missing spell pool '{savedPool.PoolId}'."
                    );
                if (livePool.MaxUses != savedPool.MaximumUses)
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' spell pool '{savedPool.PoolId}' has changed maximum uses."
                    );
                pools.Add(new SpellPoolRestoreState(livePool, savedPool.RemainingUses));
            }
            return new PreparedRestoreState(
                prepared,
                saved.RollOptions.ToArray(),
                effects,
                pools.ToArray()
            );
        }

        private static TimedEffectRestoreState RestoreTimedEffect(
            DungeonTimedEffectSaveState saved,
            string actorId,
            IReadOnlyDictionary<string, ActionController> controllersById
        )
        {
            if (saved.OwnerCreatureId != actorId || saved.TargetCreatureId != actorId)
                throw new InvalidOperationException(
                    $"Timed effect '{saved.InstanceId}' is bound to the wrong actor."
                );
            if (saved.StateDiscriminator != LegacyEffectStateDiscriminator)
                throw new InvalidOperationException(
                    $"Timed effect '{saved.InstanceId}' uses unsupported state discriminator '{saved.StateDiscriminator}'."
                );
            if (saved.StateJson.Length > 0 && saved.StateJson != EmptyEffectStateJson)
                throw new InvalidOperationException(
                    $"Timed effect '{saved.InstanceId}' has unsupported kind state."
                );

            GameObject source = null;
            if (saved.SourceCreatureId.Length > 0)
            {
                if (
                    controllersById.TryGetValue(
                        saved.SourceCreatureId,
                        out ActionController sourceController
                    )
                )
                    source = sourceController.gameObject;
            }

            Func<ActiveSpellEffect> factory = saved.Kind switch
            {
                "shield" => () => new ShieldSpellEffect(source),
                "guidance" => () => new GuidanceSpellEffect(source),
                "guidance-immunity" => () => new GuidanceImmunitySpellEffect(source),
                "bless" => () => new BlessSpellEffect(source),
                "infuse-vitality" => () => new InfuseVitalitySpellEffect(source),
                _ => throw new InvalidOperationException(
                    $"Timed effect '{saved.InstanceId}' uses unsupported kind '{saved.Kind}'."
                ),
            };
            return new TimedEffectRestoreState(() =>
            {
                ActiveSpellEffect effect = factory();
                effect.RestorePersistenceState(
                    saved.RemainingTargetTurnStarts,
                    consumed: false,
                    saved.SourceCreatureId,
                    saved.InstanceId,
                    saved.BindingCreationOrder
                );
                return effect;
            });
        }

        private static void ValidateUniqueTimedEffects(
            IReadOnlyList<DungeonTimedEffectSaveState> effects,
            string actorId
        )
        {
            for (int left = 0; left < effects.Count; left++)
            {
                for (int right = left + 1; right < effects.Count; right++)
                {
                    if (
                        effects[left].Kind == effects[right].Kind
                        && effects[left].SourceCreatureId == effects[right].SourceCreatureId
                    )
                        throw new InvalidOperationException(
                            $"Actor '{actorId}' has duplicate timed effect kind/source bindings."
                        );
                }
            }
        }
    }
}
