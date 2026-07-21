using System;
using System.Collections.Generic;
using System.Linq;
using Game.Creature;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Actors
{
    public static partial class DungeonActorStateAdapter
    {
        private static EquipmentRestoreState BuildEquipmentRestoreState(
            CreatureComponent creature,
            DungeonCreatureSaveState state
        )
        {
            EquipmentWeapon[] availableWeapons = creature.weapons.ToArray();
            EquipmentArmor[] availableArmor = creature.armor.ToArray();
            ValidateAvailableEquipment(state.InstanceId, availableWeapons, availableArmor);

            Dictionary<string, Queue<EquipmentWeapon>> weaponsById = BuildQueues(
                availableWeapons,
                weapon => NormalizeDefinitionId(weapon.name, "weapon")
            );
            Dictionary<string, Queue<EquipmentArmor>> armorById = BuildQueues(
                availableArmor,
                armor => NormalizeDefinitionId(armor.name, "armor")
            );
            List<EquipmentWeapon> restoredWeapons = new();
            List<EquipmentArmor> restoredArmor = new();
            List<EquipmentWeapon> unloadedWeapons = new();
            List<EquipmentWeaponIdentityRestore> weaponIdentities = new();
            List<EquipmentArmorIdentityRestore> armorIdentities = new();
            EquipmentWeapon leftHand = null;
            EquipmentWeapon rightHand = null;
            EquipmentArmor equippedArmor = null;

            foreach (DungeonInventoryItemSaveState item in state.Equipment.Items)
            {
                if (item.Quantity != 1)
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' inventory entry '{item.EntryId}' has an unsupported stacked quantity."
                    );

                bool weaponAvailable = QueueHasItems(weaponsById, item.ItemDefinitionId);
                bool armorAvailable = QueueHasItems(armorById, item.ItemDefinitionId);
                bool restoreAsWeapon = ResolveItemKind(
                    state.InstanceId,
                    item,
                    weaponAvailable,
                    armorAvailable
                );
                if (restoreAsWeapon)
                {
                    EquipmentWeapon weapon = Dequeue(
                        weaponsById,
                        item.ItemDefinitionId,
                        state.InstanceId,
                        item.EntryId,
                        "weapon"
                    );
                    restoredWeapons.Add(weapon);
                    weaponIdentities.Add(new EquipmentWeaponIdentityRestore(weapon, item.EntryId));
                    AssignWeaponSlot(state.InstanceId, item, weapon, ref leftHand, ref rightHand);
                    if (!item.IsLoaded)
                    {
                        if (creature.GetReloadCost(weapon) <= 0)
                            throw new InvalidOperationException(
                                $"Actor '{state.InstanceId}' marks non-reload weapon '{item.EntryId}' unloaded."
                            );
                        unloadedWeapons.Add(weapon);
                    }
                }
                else
                {
                    EquipmentArmor armor = Dequeue(
                        armorById,
                        item.ItemDefinitionId,
                        state.InstanceId,
                        item.EntryId,
                        "armor"
                    );
                    if (!item.IsLoaded)
                        throw new InvalidOperationException(
                            $"Actor '{state.InstanceId}' marks armor '{item.EntryId}' unloaded."
                        );
                    restoredArmor.Add(armor);
                    armorIdentities.Add(new EquipmentArmorIdentityRestore(armor, item.EntryId));
                    if (item.Slot == DungeonEquipmentSlot.Armor)
                    {
                        if (equippedArmor != null)
                            throw new InvalidOperationException(
                                $"Actor '{state.InstanceId}' has more than one equipped armor item."
                            );
                        equippedArmor = armor;
                    }
                }
            }

            ValidateAllEquipmentResolved(state.InstanceId, weaponsById, armorById);
            if (
                leftHand != null
                && rightHand != null
                && (leftHand.hands == 2 || rightHand.hands == 2)
            )
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has incompatible equipped hand slots."
                );

            AmmoCount[] ammunition = state
                .Equipment.Ammunition.Select(pool => new AmmoCount
                {
                    ammoName = pool.AmmunitionId,
                    quantity = pool.Quantity,
                })
                .ToArray();
            return new EquipmentRestoreState(
                restoredWeapons.ToArray(),
                restoredArmor.ToArray(),
                ammunition,
                leftHand,
                rightHand,
                equippedArmor,
                unloadedWeapons.ToArray(),
                weaponIdentities.ToArray(),
                armorIdentities.ToArray()
            );
        }

        private static void ValidateAvailableEquipment(
            string actorId,
            IReadOnlyList<EquipmentWeapon> weapons,
            IReadOnlyList<EquipmentArmor> armor
        )
        {
            if (weapons.Any(weapon => weapon == null))
                throw new InvalidOperationException($"Actor '{actorId}' has a null weapon.");
            if (armor.Any(item => item == null))
                throw new InvalidOperationException($"Actor '{actorId}' has a null armor item.");
            if (
                weapons.Distinct(ReferenceEqualityComparer<EquipmentWeapon>.Instance).Count()
                != weapons.Count
            )
                throw new InvalidOperationException(
                    $"Actor '{actorId}' has duplicate weapon object references."
                );
            if (
                armor.Distinct(ReferenceEqualityComparer<EquipmentArmor>.Instance).Count()
                != armor.Count
            )
                throw new InvalidOperationException(
                    $"Actor '{actorId}' has duplicate armor object references."
                );
        }

        private static bool ResolveItemKind(
            string actorId,
            DungeonInventoryItemSaveState item,
            bool weaponAvailable,
            bool armorAvailable
        ) =>
            item.Slot switch
            {
                DungeonEquipmentSlot.LeftHand => true,
                DungeonEquipmentSlot.RightHand => true,
                DungeonEquipmentSlot.Armor => false,
                DungeonEquipmentSlot.Carried when weaponAvailable && !armorAvailable => true,
                DungeonEquipmentSlot.Carried when armorAvailable && !weaponAvailable => false,
                DungeonEquipmentSlot.Carried when weaponAvailable && armorAvailable =>
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' carried item '{item.ItemDefinitionId}' is ambiguous between weapon and armor content."
                    ),
                _ => throw new InvalidOperationException(
                    $"Actor '{actorId}' cannot resolve inventory entry '{item.EntryId}'."
                ),
            };

        private static void AssignWeaponSlot(
            string actorId,
            DungeonInventoryItemSaveState item,
            EquipmentWeapon weapon,
            ref EquipmentWeapon leftHand,
            ref EquipmentWeapon rightHand
        )
        {
            if (item.Slot == DungeonEquipmentSlot.LeftHand)
            {
                if (leftHand != null)
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has more than one left-hand item."
                    );
                leftHand = weapon;
            }
            else if (item.Slot == DungeonEquipmentSlot.RightHand)
            {
                if (rightHand != null)
                    throw new InvalidOperationException(
                        $"Actor '{actorId}' has more than one right-hand item."
                    );
                rightHand = weapon;
            }
        }

        private static Dictionary<string, Queue<T>> BuildQueues<T>(
            IEnumerable<T> source,
            Func<T, string> keySelector
        )
        {
            Dictionary<string, Queue<T>> result = new(StringComparer.Ordinal);
            foreach (T item in source)
            {
                string key = keySelector(item);
                if (!result.TryGetValue(key, out Queue<T> queue))
                {
                    queue = new Queue<T>();
                    result.Add(key, queue);
                }
                queue.Enqueue(item);
            }
            return result;
        }

        private static bool QueueHasItems<T>(
            IReadOnlyDictionary<string, Queue<T>> queues,
            string key
        ) => queues.TryGetValue(key, out Queue<T> queue) && queue.Count > 0;

        private static T Dequeue<T>(
            IReadOnlyDictionary<string, Queue<T>> queues,
            string definitionId,
            string actorId,
            string entryId,
            string kind
        )
        {
            if (!queues.TryGetValue(definitionId, out Queue<T> queue) || queue.Count == 0)
                throw new InvalidOperationException(
                    $"Actor '{actorId}' cannot resolve saved {kind} '{entryId}' from content '{definitionId}'."
                );
            return queue.Dequeue();
        }

        private static void ValidateAllEquipmentResolved(
            string actorId,
            IReadOnlyDictionary<string, Queue<EquipmentWeapon>> weapons,
            IReadOnlyDictionary<string, Queue<EquipmentArmor>> armor
        )
        {
            if (weapons.Values.Any(queue => queue.Count > 0))
                throw new InvalidOperationException(
                    $"Actor '{actorId}' has weapon definitions absent from its save."
                );
            if (armor.Values.Any(queue => queue.Count > 0))
                throw new InvalidOperationException(
                    $"Actor '{actorId}' has armor definitions absent from its save."
                );
        }
    }
}
