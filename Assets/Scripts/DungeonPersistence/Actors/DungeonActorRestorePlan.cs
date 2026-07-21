using System;
using System.Collections.Generic;
using Game.Combat.Spells;
using Game.Creature;
using Game.Creature.Rules;
using Game.DungeonPersistence.Repository;
using Game.Rules.Runtime;

namespace Game.DungeonPersistence.Actors
{
    /// <summary>
    /// Applies a fully prevalidated group restore. All actors must be materialized before this plan
    /// is created so cross-actor spell sources can resolve by stable identity.
    /// </summary>
    internal sealed class DungeonActorRestorePlan
    {
        private readonly IReadOnlyList<DungeonActorStateAdapter.ActorRestorePlan> actors;
        private readonly DungeonActorGridRestorePlan gridPlan;
        private bool applied;

        internal DungeonActorRestorePlan(
            IReadOnlyList<DungeonActorStateAdapter.ActorRestorePlan> actors,
            DungeonActorGridRestorePlan gridPlan
        )
        {
            this.actors = actors;
            this.gridPlan = gridPlan;
        }

        /// <summary>
        /// Restores grid-coherent positions, base actor state, cross-actor timed effects, then
        /// defeat state. A plan can only be applied once.
        /// </summary>
        public void Apply()
        {
            if (applied)
                throw new InvalidOperationException(
                    "An actor restore plan can only be applied once."
                );

            applied = true;
            gridPlan.Apply();
            foreach (DungeonActorStateAdapter.ActorRestorePlan actor in actors)
                actor.ApplyBaseState();
            foreach (DungeonActorStateAdapter.ActorRestorePlan actor in actors)
                actor.ApplyTimedEffects();
            foreach (DungeonActorStateAdapter.ActorRestorePlan actor in actors)
                actor.ApplyDefeatState();
        }
    }

    internal static partial class DungeonActorStateAdapter
    {
        internal sealed class ActorRestorePlan
        {
            private readonly ActionController controller;
            private readonly CreatureComponent creature;
            private readonly DungeonCreatureSaveState state;
            private readonly HealthState health;
            private readonly Conditions conditions;
            private readonly ConditionPersistenceApplication[] restoredConditions;
            private readonly PreparedRestoreState prepared;
            private readonly EquipmentRestoreState equipment;
            private readonly TimedEffectRestoreState[] timedEffects;

            internal ActorRestorePlan(
                ActionController controller,
                CreatureComponent creature,
                DungeonCreatureSaveState state,
                HealthState health,
                Conditions conditions,
                ConditionPersistenceApplication[] restoredConditions,
                PreparedRestoreState prepared,
                EquipmentRestoreState equipment,
                TimedEffectRestoreState[] timedEffects
            )
            {
                this.controller = controller;
                this.creature = creature;
                this.state = state;
                this.health = health;
                this.conditions = conditions;
                this.restoredConditions = restoredConditions;
                this.prepared = prepared;
                this.equipment = equipment;
                this.timedEffects = timedEffects;
            }

            internal void ApplyBaseState()
            {
                creature.InitializeHealthBeforeEncounter(health);
                equipment.ApplyPersistenceIdentities();
                creature.RestoreEquipmentBeforeEncounter(
                    equipment.Weapons,
                    equipment.Armor,
                    equipment.Ammunition,
                    equipment.LeftHand,
                    equipment.RightHand,
                    equipment.EquippedArmor,
                    equipment.UnloadedWeapons
                );
                Conditions targetConditions = conditions;
                if (targetConditions == null && restoredConditions.Length > 0)
                    targetConditions = controller.gameObject.AddComponent<Conditions>();
                targetConditions?.RestorePersistentState(restoredConditions);
                prepared.Apply();
            }

            internal void ApplyTimedEffects()
            {
                SpellEffectController effectController =
                    controller.GetComponent<SpellEffectController>();
                if (effectController == null && timedEffects.Length > 0)
                    effectController = SpellEffectController.GetOrAdd(controller.gameObject);
                effectController?.RestorePersistentEffects(
                    Array.ConvertAll(timedEffects, effect => effect.Create())
                );
            }

            internal void ApplyDefeatState()
            {
                if (state.IsDefeated)
                    creature.RestoreDefeatBeforeEncounter();
            }
        }

        internal sealed class PreparedRestoreState
        {
            internal static readonly PreparedRestoreState Empty = new(
                null,
                Array.Empty<string>(),
                Array.Empty<ActivePf2eEffect>(),
                Array.Empty<SpellPoolRestoreState>()
            );

            private readonly PreparedCharacter prepared;
            private readonly string[] rollOptions;
            private readonly ActivePf2eEffect[] effects;
            private readonly SpellPoolRestoreState[] pools;

            internal PreparedRestoreState(
                PreparedCharacter prepared,
                string[] rollOptions,
                ActivePf2eEffect[] effects,
                SpellPoolRestoreState[] pools
            )
            {
                this.prepared = prepared;
                this.rollOptions = rollOptions;
                this.effects = effects;
                this.pools = pools;
            }

            internal void Apply()
            {
                if (prepared == null)
                    return;
                prepared.RestorePersistentRuleState(rollOptions, effects);
                foreach (SpellPoolRestoreState pool in pools)
                    pool.Pool.RestoreUsesRemaining(pool.RemainingUses);
            }
        }

        internal readonly struct SpellPoolRestoreState
        {
            internal SpellPoolRestoreState(SpellSlotPool pool, int remainingUses)
            {
                Pool = pool;
                RemainingUses = remainingUses;
            }

            internal SpellSlotPool Pool { get; }
            internal int RemainingUses { get; }
        }

        internal sealed class EquipmentRestoreState
        {
            internal EquipmentRestoreState(
                EquipmentWeapon[] weapons,
                EquipmentArmor[] armor,
                AmmoCount[] ammunition,
                EquipmentWeapon leftHand,
                EquipmentWeapon rightHand,
                EquipmentArmor equippedArmor,
                EquipmentWeapon[] unloadedWeapons,
                EquipmentWeaponIdentityRestore[] weaponIdentities,
                EquipmentArmorIdentityRestore[] armorIdentities
            )
            {
                Weapons = weapons;
                Armor = armor;
                Ammunition = ammunition;
                LeftHand = leftHand;
                RightHand = rightHand;
                EquippedArmor = equippedArmor;
                UnloadedWeapons = unloadedWeapons;
                WeaponIdentities = weaponIdentities;
                ArmorIdentities = armorIdentities;
            }

            internal EquipmentWeapon[] Weapons { get; }
            internal EquipmentArmor[] Armor { get; }
            internal AmmoCount[] Ammunition { get; }
            internal EquipmentWeapon LeftHand { get; }
            internal EquipmentWeapon RightHand { get; }
            internal EquipmentArmor EquippedArmor { get; }
            internal EquipmentWeapon[] UnloadedWeapons { get; }
            internal EquipmentWeaponIdentityRestore[] WeaponIdentities { get; }
            internal EquipmentArmorIdentityRestore[] ArmorIdentities { get; }

            internal void ApplyPersistenceIdentities()
            {
                foreach (EquipmentWeaponIdentityRestore identity in WeaponIdentities)
                    identity.Apply();
                foreach (EquipmentArmorIdentityRestore identity in ArmorIdentities)
                    identity.Apply();
            }
        }

        internal readonly struct EquipmentWeaponIdentityRestore
        {
            private readonly EquipmentWeapon weapon;
            private readonly string instanceId;

            internal EquipmentWeaponIdentityRestore(EquipmentWeapon weapon, string instanceId)
            {
                this.weapon = weapon;
                this.instanceId = instanceId;
            }

            internal void Apply() => weapon.EnsureDungeonPersistenceIdentity(instanceId);
        }

        internal readonly struct EquipmentArmorIdentityRestore
        {
            private readonly EquipmentArmor armor;
            private readonly string instanceId;

            internal EquipmentArmorIdentityRestore(EquipmentArmor armor, string instanceId)
            {
                this.armor = armor;
                this.instanceId = instanceId;
            }

            internal void Apply() => armor.EnsureDungeonPersistenceIdentity(instanceId);
        }

        internal readonly struct TimedEffectRestoreState
        {
            private readonly Func<ActiveSpellEffect> create;

            internal TimedEffectRestoreState(Func<ActiveSpellEffect> create)
            {
                this.create = create;
            }

            internal ActiveSpellEffect Create() => create();
        }
    }
}
