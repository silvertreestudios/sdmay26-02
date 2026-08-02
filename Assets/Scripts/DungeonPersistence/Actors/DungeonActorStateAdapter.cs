using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Repository;
using Game.Rules;
using Game.Rules.Runtime;
using Game.Rules.Unity;
using UnityEngine;

[assembly: InternalsVisibleTo("EditModeAssembly")]

namespace Game.DungeonPersistence.Actors
{
    internal static class DungeonActorStateAdapter
    {
        internal static DungeonActorSaveState Capture(
            ActionController controller,
            Func<GameObject, string> identifyActor
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (identifyActor == null)
                throw new ArgumentNullException(nameof(identifyActor));
            CreatureComponent creature = RequireCreature(controller);
            HealthState health = creature.Health;

            IReadOnlyList<DungeonConditionSaveState> conditions = CaptureConditions(controller);
            IReadOnlyList<DungeonTimedEffectSaveState> timedEffects = CaptureTimedEffects(
                controller,
                identifyActor
            );
            IReadOnlyList<DungeonPreparedEffectSaveState> preparedEffects =
                creature.Prepared == null
                    ? Array.Empty<DungeonPreparedEffectSaveState>()
                    : creature
                        .Prepared.ActiveEffects.Select(effect => new DungeonPreparedEffectSaveState
                        {
                            Name = effect.Name,
                            Slug = effect.Slug,
                            SourceSlug = effect.SourceSlug,
                        })
                        .ToArray();

            return new DungeonActorSaveState
            {
                TemporaryHitPoints = health.Temporary,
                TemporaryHitPointSource = health.TemporarySource.Slug ?? string.Empty,
                TemporaryHitPointImmunities = health
                    .TemporaryHitPointImmunities.Select(source => source.Slug)
                    .ToArray(),
                RageWasActive =
                    controller.TryGetCombatRules(
                        out UnityCombatRulesBridge bridge,
                        out CreatureId creatureId
                    ) && RageRules.IsRaging(bridge.Snapshot, creatureId),
                Conditions = conditions.ToArray(),
                TimedEffects = timedEffects.ToArray(),
                PreparedEffects = preparedEffects.ToArray(),
                Equipment = CaptureEquipment(creature),
            };
        }

        internal static Action PrepareRestore(
            ActionController controller,
            DungeonActorSaveState saved,
            int currentHitPoints,
            bool isDefeated,
            Func<string, GameObject> resolveActor
        )
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            if (resolveActor == null)
                throw new ArgumentNullException(nameof(resolveActor));

            CreatureComponent creature = RequireCreature(controller);
            if (
                currentHitPoints < 0
                || currentHitPoints > creature.maxHp
                || isDefeated != (currentHitPoints == 0)
            )
                throw new InvalidOperationException(
                    $"Saved health is invalid for actor '{controller.name}'."
                );

            RuleSource temporarySource =
                saved.TemporaryHitPointSource.Length == 0
                    ? default
                    : RuleSource.FromSlug(saved.TemporaryHitPointSource);
            RuleSource[] immunities = saved
                .TemporaryHitPointImmunities.Select(RuleSource.FromSlug)
                .ToArray();
            HealthState health = RageRules.NormalizeRestoredHealth(
                new HealthState(
                    currentHitPoints,
                    creature.maxHp,
                    saved.TemporaryHitPoints,
                    temporarySource,
                    immunities
                ),
                saved.RageWasActive
            );

            ConditionApplicationSnapshot[] conditions = PrepareConditions(saved.Conditions);
            ActiveSpellEffect[] timedEffects = saved
                .TimedEffects.Select(effect => RestoreTimedEffect(effect, resolveActor))
                .ToArray();
            ActivePf2eEffect[] preparedEffects = saved
                .PreparedEffects.Select(effect => new ActivePf2eEffect(
                    effect.Name,
                    effect.Slug,
                    effect.SourceSlug
                ))
                .ToArray();
            if (preparedEffects.Length > 0 && creature.Prepared == null && creature.Build != null)
                creature.Prepared = Pf2eCharacterPreparer.Prepare(creature, creature.Build);
            if (preparedEffects.Length > 0 && creature.Prepared == null)
                throw new InvalidOperationException(
                    $"Actor '{controller.name}' cannot restore prepared effects without prepared rules."
                );

            EquipmentWeapon leftHand = ResolveWeapon(creature.weapons, saved.Equipment.LeftHandId);
            EquipmentWeapon rightHand = ResolveWeapon(
                creature.weapons,
                saved.Equipment.RightHandId
            );
            EquipmentArmor armor = ResolveArmor(creature.armor, saved.Equipment.ArmorId);
            AmmoCount[] ammunition = PrepareAmmunition(creature, saved.Equipment.Ammunition);
            string[] unloaded = saved.Equipment.UnloadedWeaponIds.ToArray();
            foreach (string definitionId in unloaded)
            {
                if (
                    !creature.weapons.Any(weapon =>
                        weapon != null
                        && string.Equals(
                            NormalizeEquipmentId(weapon.name),
                            definitionId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                    throw new InvalidOperationException(
                        $"Saved unloaded weapon '{definitionId}' is not in actor '{controller.name}' inventory."
                    );
            }

            return () =>
            {
                creature.InitializeHealthBeforeEncounter(health);
                Conditions conditionController =
                    controller.GetComponent<Conditions>()
                    ?? controller.gameObject.AddComponent<Conditions>();
                conditionController.RestoreApplications(conditions);

                SpellEffectController spellEffects =
                    controller.GetComponent<SpellEffectController>();
                if (spellEffects != null || timedEffects.Length > 0)
                {
                    (
                        spellEffects ?? SpellEffectController.GetOrAdd(controller.gameObject)
                    ).RestoreEffects(timedEffects);
                }

                creature.Prepared?.RestoreActiveEffects(preparedEffects);
                creature.equippedLeftHand = leftHand;
                creature.equippedRightHand = rightHand;
                creature.equippedArmor = armor;
                creature.ammunition = ammunition.ToList();
                creature.unloadedWeapons = unloaded.ToList();
                creature.CalculateAC();
                if (isDefeated)
                    creature.RestoreDefeatBeforeEncounter();
            };
        }

        private static IReadOnlyList<DungeonConditionSaveState> CaptureConditions(
            ActionController controller
        )
        {
            Conditions conditions = controller.GetComponent<Conditions>();
            if (conditions == null)
                return Array.Empty<DungeonConditionSaveState>();

            List<DungeonConditionSaveState> captured = new();
            foreach (ConditionApplicationSnapshot application in conditions.CaptureApplications())
            {
                string sourceActorId = application.SourceActorId;
                if (!DurableActorSourceIdentity.IsCanonical(sourceActorId))
                    throw new InvalidOperationException(
                        $"Condition {application.EffectId.Value} has no canonical durable source actor provenance."
                    );
                DungeonConditionStateKind stateKind = GetConditionStateKind(application.State);
                captured.Add(
                    new DungeonConditionSaveState
                    {
                        EffectId = application.EffectId.Value,
                        BindingId = application.BindingId.Value,
                        DefinitionId = application.DefinitionId.Value,
                        SourceActorId = sourceActorId,
                        RuleSource = application.Source.Slug,
                        DurationKind = application.Duration.Kind,
                        DurationAmount = application.Duration.Amount,
                        Version = application.Version.Value,
                        Status = application.Status,
                        CreationOrder = application.CreationOrder,
                        BindingEnabled = application.BindingEnabled,
                        StateKind = stateKind,
                        Value = GetConditionValue(application.State),
                        AllowedActionIds = application.State is QuickenedConditionState quickened
                            ? quickened.AllowedActions.Select(action => action.Value).ToArray()
                            : Array.Empty<string>(),
                        HasTiming = application.Timing != null,
                        RemainingBoundaries = application.Timing?.RemainingBoundaries ?? 0,
                        ExpiresWithEncounter = application.Timing?.ExpiresWithEncounter ?? false,
                    }
                );
            }
            return captured;
        }

        private static ConditionApplicationSnapshot[] PrepareConditions(
            IReadOnlyList<DungeonConditionSaveState> saved
        )
        {
            return saved
                .Select(application =>
                {
                    return new ConditionApplicationSnapshot(
                        new ActiveEffectId(application.EffectId),
                        new BindingId(application.BindingId),
                        new RuleDefinitionId(application.DefinitionId),
                        application.SourceActorId,
                        RuleSource.FromSlug(application.RuleSource),
                        RestoreDuration(application),
                        new EffectStateVersion(application.Version),
                        RestoreConditionState(application),
                        application.Status,
                        application.CreationOrder,
                        application.BindingEnabled,
                        application.HasTiming
                            ? new ConditionTimingSnapshot(
                                application.RemainingBoundaries,
                                application.ExpiresWithEncounter
                            )
                            : null
                    );
                })
                .ToArray();
        }

        private static DungeonConditionStateKind GetConditionStateKind(IEffectState state) =>
            state switch
            {
                ConditionMarkerState => DungeonConditionStateKind.Marker,
                SlowedConditionState => DungeonConditionStateKind.Slowed,
                ValuedStunnedConditionState => DungeonConditionStateKind.StunnedValued,
                DurationOnlyStunnedConditionState => DungeonConditionStateKind.StunnedDurationOnly,
                QuickenedConditionState quickened when quickened.IsRestricted =>
                    DungeonConditionStateKind.QuickenedRestricted,
                QuickenedConditionState => DungeonConditionStateKind.QuickenedUnrestricted,
                _ => throw new InvalidOperationException(
                    $"Unsupported persisted condition state {state.GetType().Name}."
                ),
            };

        private static int GetConditionValue(IEffectState state) =>
            state switch
            {
                SlowedConditionState slowed => slowed.Value,
                ValuedStunnedConditionState stunned => stunned.Value,
                _ => 0,
            };

        private static EffectDuration RestoreDuration(DungeonConditionSaveState saved) =>
            saved.DurationKind switch
            {
                EffectDurationKind.Indefinite => EffectDuration.Indefinite,
                EffectDurationKind.Encounter => EffectDuration.Encounter,
                EffectDurationKind.Rounds => EffectDuration.Rounds(saved.DurationAmount),
                EffectDurationKind.Minutes => EffectDuration.Minutes(saved.DurationAmount),
                _ => throw new InvalidOperationException("Unsupported condition duration kind."),
            };

        private static IEffectState RestoreConditionState(DungeonConditionSaveState saved) =>
            saved.StateKind switch
            {
                DungeonConditionStateKind.Marker => ConditionMarkerState.Instance,
                DungeonConditionStateKind.Slowed => new SlowedConditionState(saved.Value),
                DungeonConditionStateKind.StunnedValued => new ValuedStunnedConditionState(
                    saved.Value
                ),
                DungeonConditionStateKind.StunnedDurationOnly =>
                    DurationOnlyStunnedConditionState.Instance,
                DungeonConditionStateKind.QuickenedRestricted => new QuickenedConditionState(
                    saved.AllowedActionIds.Select(action => new ActionDefinitionId(action))
                ),
                DungeonConditionStateKind.QuickenedUnrestricted =>
                    QuickenedConditionState.Unrestricted,
                _ => throw new InvalidOperationException("Unsupported condition state kind."),
            };

        private static IReadOnlyList<DungeonTimedEffectSaveState> CaptureTimedEffects(
            ActionController controller,
            Func<GameObject, string> identifyActor
        )
        {
            SpellEffectController effects = controller.GetComponent<SpellEffectController>();
            if (effects == null)
                return Array.Empty<DungeonTimedEffectSaveState>();

            return effects
                .Effects.Where(effect => !effect.Consumed)
                .Select(effect =>
                {
                    string sourceActorId =
                        effect.Source != null
                            ? identifyActor(effect.Source)
                            : effect.PersistentSourceActorId;
                    if (string.IsNullOrWhiteSpace(sourceActorId))
                    {
                        throw new InvalidOperationException(
                            $"Timed effect '{effect.SourceLabel}' has no source actor."
                        );
                    }
                    return new DungeonTimedEffectSaveState
                    {
                        Kind = GetEffectKind(effect),
                        SourceActorId = sourceActorId,
                        RemainingTurnStarts = effect.RemainingTargetTurnStarts,
                    };
                })
                .ToArray();
        }

        private static string GetEffectKind(ActiveSpellEffect effect)
        {
            return effect switch
            {
                ShieldSpellEffect => "shield",
                GuidanceSpellEffect => "guidance",
                GuidanceImmunitySpellEffect => "guidance-immunity",
                BlessSpellEffect => "bless",
                InfuseVitalitySpellEffect => "infuse-vitality",
                _ => throw new InvalidOperationException(
                    $"Timed effect type '{effect.GetType().Name}' is not persistable."
                ),
            };
        }

        private static ActiveSpellEffect RestoreTimedEffect(
            DungeonTimedEffectSaveState saved,
            Func<string, GameObject> resolveActor
        )
        {
            GameObject source = resolveActor(saved.SourceActorId);
            ActiveSpellEffect effect = saved.Kind switch
            {
                "shield" => new ShieldSpellEffect(source),
                "guidance" => new GuidanceSpellEffect(source),
                "guidance-immunity" => new GuidanceImmunitySpellEffect(source),
                "bless" => new BlessSpellEffect(source),
                "infuse-vitality" => new InfuseVitalitySpellEffect(source),
                _ => throw new InvalidOperationException(
                    $"Timed effect kind '{saved.Kind}' is not supported."
                ),
            };
            effect.RestorePersistentSource(saved.SourceActorId, source);
            effect.RemainingTargetTurnStarts = saved.RemainingTurnStarts;
            return effect;
        }

        private static DungeonEquipmentSaveState CaptureEquipment(CreatureComponent creature)
        {
            return new DungeonEquipmentSaveState
            {
                LeftHandId = FindWeaponId(creature.weapons, creature.equippedLeftHand),
                RightHandId = FindWeaponId(creature.weapons, creature.equippedRightHand),
                ArmorId = FindArmorId(creature.armor, creature.equippedArmor),
                Ammunition = creature.ammunition.ToArray(),
                UnloadedWeaponIds = creature
                    .unloadedWeapons.Select(NormalizeEquipmentId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        }

        private static string FindWeaponId(
            IReadOnlyList<EquipmentWeapon> inventory,
            EquipmentWeapon selected
        )
        {
            if (selected == null || string.IsNullOrWhiteSpace(selected.name))
                return string.Empty;
            EquipmentWeapon authored = inventory.FirstOrDefault(item =>
                item != null
                && string.Equals(item.name, selected.name, StringComparison.OrdinalIgnoreCase)
            );
            return authored != null
                ? authored.name
                : throw new InvalidOperationException(
                    "An equipped weapon is not in actor inventory."
                );
        }

        private static string FindArmorId(
            IReadOnlyList<EquipmentArmor> inventory,
            EquipmentArmor selected
        )
        {
            if (selected == null || string.IsNullOrWhiteSpace(selected.name))
                return string.Empty;
            EquipmentArmor authored = inventory.FirstOrDefault(item =>
                item != null
                && string.Equals(item.name, selected.name, StringComparison.OrdinalIgnoreCase)
            );
            return authored != null
                ? authored.name
                : throw new InvalidOperationException("Equipped armor is not in actor inventory.");
        }

        private static EquipmentWeapon ResolveWeapon(
            IReadOnlyList<EquipmentWeapon> inventory,
            string definitionId
        )
        {
            if (string.IsNullOrEmpty(definitionId))
                return null;
            return inventory.FirstOrDefault(item =>
                    item != null
                    && string.Equals(item.name, definitionId, StringComparison.OrdinalIgnoreCase)
                )
                ?? throw new InvalidOperationException($"Weapon '{definitionId}' is unavailable.");
        }

        private static EquipmentArmor ResolveArmor(
            IReadOnlyList<EquipmentArmor> inventory,
            string definitionId
        )
        {
            if (string.IsNullOrEmpty(definitionId))
                return null;
            return inventory.FirstOrDefault(item =>
                    item != null
                    && string.Equals(item.name, definitionId, StringComparison.OrdinalIgnoreCase)
                ) ?? throw new InvalidOperationException($"Armor '{definitionId}' is unavailable.");
        }

        private static AmmoCount[] PrepareAmmunition(
            CreatureComponent creature,
            IReadOnlyList<AmmoCount> saved
        )
        {
            HashSet<string> authored = new(
                creature.ammunition.Select(pool => pool.ammoName),
                StringComparer.OrdinalIgnoreCase
            );
            HashSet<string> restored = new(
                saved.Select(pool => pool.ammoName),
                StringComparer.OrdinalIgnoreCase
            );
            if (!authored.SetEquals(restored))
                throw new InvalidOperationException(
                    $"Saved ammunition does not match actor '{creature.name}' inventory."
                );
            return saved.ToArray();
        }

        private static string NormalizeEquipmentId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private static CreatureComponent RequireCreature(ActionController controller)
        {
            CreatureComponent creature = controller.GetComponent<CreatureComponent>();
            return creature != null
                ? creature
                : throw new InvalidOperationException(
                    $"Actor '{controller.name}' has no CreatureComponent."
                );
        }
    }
}
