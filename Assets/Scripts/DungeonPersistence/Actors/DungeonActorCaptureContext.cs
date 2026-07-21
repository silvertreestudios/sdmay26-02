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
        private sealed class CaptureContext
        {
            private readonly IReadOnlyDictionary<GameObject, string> actorIds;
            private readonly Dictionary<ConditionSource, string> conditionSourceIds = new(
                ReferenceEqualityComparer<ConditionSource>.Instance
            );
            private readonly Dictionary<string, ConditionSource> conditionSourcesById = new(
                StringComparer.Ordinal
            );

            internal CaptureContext(IReadOnlyDictionary<GameObject, string> actorIds)
            {
                this.actorIds = actorIds;
            }

            internal DungeonCreatureSaveState Capture(DungeonActorCaptureTarget target)
            {
                CreatureComponent creature = target.Controller.GetComponent<CreatureComponent>();
                if (creature == null)
                    throw new InvalidOperationException(
                        $"Actor '{target.InstanceId}' has no CreatureComponent."
                    );
                HealthState health = creature.Health;
                DungeonHealthSaveState savedHealth = new(
                    health.Current,
                    health.Maximum,
                    health.Temporary,
                    health.TemporarySource.IsEmpty ? string.Empty : health.TemporarySource.Slug,
                    health.TemporaryHitPointImmunities.Select(source => source.Slug)
                );
                Conditions conditions = target.Controller.GetComponent<Conditions>();
                DungeonConditionSaveState[] savedConditions = CaptureConditions(
                    target.InstanceId,
                    conditions
                );
                DungeonTimedEffectSaveState[] timedEffects = CaptureTimedEffects(target);
                DungeonPreparedRuleSaveState preparedRules = CapturePreparedRules(
                    target.InstanceId,
                    creature.Prepared
                );
                DungeonEquipmentSaveState equipment = CaptureEquipment(target.InstanceId, creature);
                Vector3 position = target.Controller.transform.position;
                return new DungeonCreatureSaveState(
                    target.InstanceId,
                    target.CreatureContentId,
                    new DungeonSaveCell(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z)),
                    savedHealth,
                    creature.IsDefeated,
                    savedConditions,
                    timedEffects,
                    preparedRules,
                    equipment
                );
            }

            private DungeonConditionSaveState[] CaptureConditions(
                string actorId,
                Conditions conditions
            )
            {
                if (conditions == null)
                    return Array.Empty<DungeonConditionSaveState>();
                IReadOnlyList<ConditionPersistenceApplication> applications =
                    conditions.CapturePersistentState();
                HashSet<string> applicationIds = new(StringComparer.Ordinal);
                foreach (ConditionPersistenceApplication application in applications)
                {
                    if (
                        application.ApplicationId.Length > 0
                        && !applicationIds.Add(application.ApplicationId)
                    )
                        throw new InvalidOperationException(
                            $"Actor '{actorId}' has duplicate condition application identity '{application.ApplicationId}'."
                        );
                }
                DungeonConditionSaveState[] result = new DungeonConditionSaveState[
                    applications.Count
                ];
                int nextApplicationSequence = 0;
                for (int index = 0; index < applications.Count; index++)
                {
                    ConditionPersistenceApplication application = applications[index];
                    if (application.ApplicationId.Length == 0)
                    {
                        string generatedId;
                        do
                        {
                            generatedId = $"{actorId}/condition/{nextApplicationSequence:D4}";
                            nextApplicationSequence++;
                        } while (!applicationIds.Add(generatedId));
                        application.EnsurePersistenceIdentity(generatedId);
                    }
                    result[index] = new DungeonConditionSaveState(
                        application.ApplicationId,
                        application.ConditionId,
                        ResolveConditionSourceId(application.Source),
                        application.Value
                    );
                }
                return result;
            }

            private string ResolveConditionSourceId(ConditionSource source)
            {
                if (source == null)
                    return string.Empty;
                if (source.PersistentInstanceId.Length > 0)
                {
                    if (
                        conditionSourcesById.TryGetValue(
                            source.PersistentInstanceId,
                            out ConditionSource existing
                        ) && !ReferenceEquals(existing, source)
                    )
                        throw new InvalidOperationException(
                            $"Distinct live condition sources share persistence ID '{source.PersistentInstanceId}'."
                        );
                    conditionSourcesById[source.PersistentInstanceId] = source;
                    conditionSourceIds[source] = source.PersistentInstanceId;
                    return source.PersistentInstanceId;
                }
                if (!conditionSourceIds.TryGetValue(source, out string id))
                {
                    int sequence = conditionSourceIds.Count;
                    do
                    {
                        id = $"condition-source/{sequence:D4}";
                        sequence++;
                    } while (conditionSourcesById.ContainsKey(id));
                    conditionSourceIds.Add(source, id);
                    conditionSourcesById.Add(id, source);
                    source.RestorePersistenceIdentity(id);
                }
                return id;
            }

            private DungeonTimedEffectSaveState[] CaptureTimedEffects(
                DungeonActorCaptureTarget target
            )
            {
                SpellEffectController controller =
                    target.Controller.GetComponent<SpellEffectController>();
                if (controller == null)
                    return Array.Empty<DungeonTimedEffectSaveState>();
                List<DungeonTimedEffectSaveState> result = new();
                ActiveSpellEffect[] effects = controller
                    .Effects.Where(effect => effect != null && !effect.Consumed)
                    .OrderBy(effect => effect.BindingCreationOrder)
                    .ToArray();
                if (controller.Effects.Any(effect => effect == null))
                    throw new InvalidOperationException(
                        $"Actor '{target.InstanceId}' has a null timed effect."
                    );
                if (
                    effects.Any(effect => effect.BindingCreationOrder < 0)
                    || effects.Select(effect => effect.BindingCreationOrder).Distinct().Count()
                        != effects.Length
                )
                    throw new InvalidOperationException(
                        $"Actor '{target.InstanceId}' has invalid timed-effect binding order metadata."
                    );
                HashSet<string> instanceIds = new(StringComparer.Ordinal);
                foreach (ActiveSpellEffect effect in effects)
                {
                    if (
                        effect.PersistentInstanceId.Length > 0
                        && !instanceIds.Add(effect.PersistentInstanceId)
                    )
                        throw new InvalidOperationException(
                            $"Actor '{target.InstanceId}' has duplicate timed-effect identity '{effect.PersistentInstanceId}'."
                        );
                }
                long nextIdentitySequence = 0;
                foreach (ActiveSpellEffect effect in effects)
                {
                    if (effect.PersistentInstanceId.Length == 0)
                    {
                        string generatedId;
                        do
                        {
                            generatedId =
                                $"{target.InstanceId}/timed-effect/{nextIdentitySequence:D4}";
                            nextIdentitySequence++;
                        } while (!instanceIds.Add(generatedId));
                        effect.EnsurePersistenceIdentity(generatedId, effect.BindingCreationOrder);
                    }
                    string sourceId = ResolveTimedEffectSourceId(target.InstanceId, effect);
                    string kind = GetEffectKind(effect);
                    result.Add(
                        new DungeonTimedEffectSaveState(
                            effect.PersistentInstanceId,
                            kind,
                            LegacyEffectStateDiscriminator,
                            sourceId,
                            target.InstanceId,
                            target.InstanceId,
                            effect.BindingCreationOrder,
                            effect.RemainingTargetTurnStarts,
                            EmptyEffectStateJson
                        )
                    );
                }
                return result.ToArray();
            }

            private string ResolveTimedEffectSourceId(string ownerActorId, ActiveSpellEffect effect)
            {
                if (effect.PersistentSourceActorId.Length > 0)
                {
                    if (
                        effect.Source != null
                        && actorIds.TryGetValue(effect.Source, out string liveSourceId)
                        && liveSourceId != effect.PersistentSourceActorId
                    )
                        throw new InvalidOperationException(
                            $"Actor '{ownerActorId}' has a timed effect with conflicting live and persistent source identities."
                        );
                    return effect.PersistentSourceActorId;
                }
                if (effect.Source == null)
                    return string.Empty;
                if (actorIds.TryGetValue(effect.Source, out string sourceId))
                    return sourceId;
                throw new InvalidOperationException(
                    $"Actor '{ownerActorId}' has a timed effect whose source is outside the capture set and has no stable identity."
                );
            }

            private static string GetEffectKind(ActiveSpellEffect effect) =>
                effect switch
                {
                    ShieldSpellEffect => "shield",
                    GuidanceSpellEffect => "guidance",
                    GuidanceImmunitySpellEffect => "guidance-immunity",
                    BlessSpellEffect => "bless",
                    InfuseVitalitySpellEffect => "infuse-vitality",
                    _ => throw new InvalidOperationException(
                        $"Timed effect type '{effect.GetType().Name}' is not registered for dungeon persistence."
                    ),
                };

            private static DungeonPreparedRuleSaveState CapturePreparedRules(
                string actorId,
                PreparedCharacter prepared
            )
            {
                if (prepared == null)
                    return new DungeonPreparedRuleSaveState(
                        Array.Empty<string>(),
                        Array.Empty<DungeonPreparedEffectSaveState>(),
                        Array.Empty<DungeonSpellPoolSaveState>()
                    );
                ActivePf2eEffect[] liveEffects = prepared.ActiveEffects.ToArray();
                if (liveEffects.Any(effect => effect == null))
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has a null prepared effect."
                    );
                HashSet<string> effectIds = new(StringComparer.Ordinal);
                foreach (ActivePf2eEffect effect in liveEffects)
                {
                    if (
                        effect.PersistentInstanceId.Length > 0
                        && !effectIds.Add(effect.PersistentInstanceId)
                    )
                        throw new InvalidOperationException(
                            $"Actor '{actorId}' has duplicate prepared-effect identity '{effect.PersistentInstanceId}'."
                        );
                }
                int nextEffectSequence = 0;
                foreach (ActivePf2eEffect effect in liveEffects)
                {
                    if (effect.PersistentInstanceId.Length > 0)
                        continue;
                    string generatedId;
                    do
                    {
                        generatedId = $"{actorId}/prepared-effect/{nextEffectSequence:D4}";
                        nextEffectSequence++;
                    } while (!effectIds.Add(generatedId));
                    effect.EnsurePersistenceIdentity(generatedId);
                }
                DungeonPreparedEffectSaveState[] effects = liveEffects
                    .Select(effect => new DungeonPreparedEffectSaveState(
                        effect.PersistentInstanceId,
                        effect.Name,
                        effect.Slug,
                        effect.SourceSlug
                    ))
                    .ToArray();
                IEnumerable<DungeonSpellPoolSaveState> pools =
                    prepared.Spellcasting == null
                        ? Array.Empty<DungeonSpellPoolSaveState>()
                        : prepared.Spellcasting.Pools.Values.Select(
                            pool => new DungeonSpellPoolSaveState(
                                pool.Id,
                                pool.UsesRemaining,
                                pool.MaxUses
                            )
                        );
                return new DungeonPreparedRuleSaveState(prepared.RollOptions, effects, pools);
            }

            private static DungeonEquipmentSaveState CaptureEquipment(
                string actorId,
                CreatureComponent creature
            )
            {
                EquipmentWeapon[] weapons = creature.weapons.ToArray();
                EquipmentArmor[] armor = creature.armor.ToArray();
                if (weapons.Any(weapon => weapon == null))
                    throw new InvalidOperationException($"Actor '{actorId}' has a null weapon.");
                if (armor.Any(item => item == null))
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has a null armor item."
                    );
                if (
                    weapons.Distinct(ReferenceEqualityComparer<EquipmentWeapon>.Instance).Count()
                    != weapons.Length
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has duplicate weapon object references."
                    );
                if (
                    armor.Distinct(ReferenceEqualityComparer<EquipmentArmor>.Instance).Count()
                    != armor.Length
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has duplicate armor object references."
                    );
                if (
                    creature.equippedLeftHand != null
                    && creature.equippedRightHand != null
                    && ReferenceEquals(creature.equippedLeftHand, creature.equippedRightHand)
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' uses one weapon instance in both hand slots, which the save schema cannot represent."
                    );

                EnsureEquipmentPersistenceIdentities(actorId, weapons, armor);

                List<DungeonInventoryItemSaveState> items = new();
                foreach (EquipmentWeapon weapon in weapons)
                {
                    DungeonEquipmentSlot slot =
                        ReferenceEquals(creature.equippedLeftHand, weapon)
                            ? DungeonEquipmentSlot.LeftHand
                        : ReferenceEquals(creature.equippedRightHand, weapon)
                            ? DungeonEquipmentSlot.RightHand
                        : DungeonEquipmentSlot.Carried;
                    items.Add(
                        new DungeonInventoryItemSaveState(
                            weapon.DungeonPersistenceInstanceId,
                            NormalizeDefinitionId(weapon.name, "weapon"),
                            1,
                            slot,
                            creature.IsWeaponLoaded(weapon)
                        )
                    );
                }
                foreach (EquipmentArmor item in armor)
                {
                    items.Add(
                        new DungeonInventoryItemSaveState(
                            item.DungeonPersistenceInstanceId,
                            NormalizeDefinitionId(item.name, "armor"),
                            1,
                            ReferenceEquals(creature.equippedArmor, item)
                                ? DungeonEquipmentSlot.Armor
                                : DungeonEquipmentSlot.Carried,
                            isLoaded: true
                        )
                    );
                }
                ValidateEquippedInventory(actorId, creature, weapons, armor);

                DungeonAmmunitionSaveState[] ammunition = creature
                    .ammunition.Select(pool => new DungeonAmmunitionSaveState(
                        NormalizeDefinitionId(pool.ammoName, "ammunition pool"),
                        pool.quantity
                    ))
                    .ToArray();
                return new DungeonEquipmentSaveState(items, ammunition);
            }

            private static void EnsureEquipmentPersistenceIdentities(
                string actorId,
                IReadOnlyList<EquipmentWeapon> weapons,
                IReadOnlyList<EquipmentArmor> armor
            )
            {
                HashSet<string> ids = new(StringComparer.Ordinal);
                foreach (EquipmentWeapon weapon in weapons)
                {
                    if (
                        weapon.DungeonPersistenceInstanceId.Length > 0
                        && !ids.Add(weapon.DungeonPersistenceInstanceId)
                    )
                        throw new InvalidOperationException(
                            $"Actor '{actorId}' has duplicate inventory identity '{weapon.DungeonPersistenceInstanceId}'."
                        );
                }
                foreach (EquipmentArmor item in armor)
                {
                    if (
                        item.DungeonPersistenceInstanceId.Length > 0
                        && !ids.Add(item.DungeonPersistenceInstanceId)
                    )
                        throw new InvalidOperationException(
                            $"Actor '{actorId}' has duplicate inventory identity '{item.DungeonPersistenceInstanceId}'."
                        );
                }

                int nextSequence = 0;
                foreach (EquipmentWeapon weapon in weapons)
                {
                    if (weapon.DungeonPersistenceInstanceId.Length > 0)
                        continue;
                    weapon.EnsureDungeonPersistenceIdentity(
                        NextInventoryId(actorId, ids, ref nextSequence)
                    );
                }
                foreach (EquipmentArmor item in armor)
                {
                    if (item.DungeonPersistenceInstanceId.Length > 0)
                        continue;
                    item.EnsureDungeonPersistenceIdentity(
                        NextInventoryId(actorId, ids, ref nextSequence)
                    );
                }
            }

            private static string NextInventoryId(
                string actorId,
                ISet<string> existingIds,
                ref int nextSequence
            )
            {
                string generatedId;
                do
                {
                    generatedId = $"{actorId}/inventory/{nextSequence:D4}";
                    nextSequence++;
                } while (!existingIds.Add(generatedId));
                return generatedId;
            }

            private static void ValidateEquippedInventory(
                string actorId,
                CreatureComponent creature,
                IReadOnlyList<EquipmentWeapon> weapons,
                IReadOnlyList<EquipmentArmor> armor
            )
            {
                if (
                    creature.equippedLeftHand != null
                    && !weapons.Any(weapon => ReferenceEquals(weapon, creature.equippedLeftHand))
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has a left-hand weapon outside inventory."
                    );
                if (
                    creature.equippedRightHand != null
                    && !weapons.Any(weapon => ReferenceEquals(weapon, creature.equippedRightHand))
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has a right-hand weapon outside inventory."
                    );
                if (
                    creature.equippedArmor != null
                    && !armor.Any(item => ReferenceEquals(item, creature.equippedArmor))
                )
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has equipped armor outside inventory."
                    );
            }
        }
    }
}
