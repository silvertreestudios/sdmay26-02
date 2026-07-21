using System;
using System.Collections.Generic;
using System.Linq;
using Game.DungeonPersistence.Repository;

namespace Game.DungeonPersistence.Actors
{
    internal static partial class DungeonActorStateAdapter
    {
        private static readonly HashSet<string> SupportedTimedEffectKinds = new(
            new[] { "shield", "guidance", "guidance-immunity", "bless", "infuse-vitality" },
            StringComparer.Ordinal
        );

        /// <summary>
        /// Validates adapter-specific saved actor semantics without reading or mutating Unity
        /// objects. Run this before map population, then use <see cref="PreflightRestore(IEnumerable{DungeonActorRestoreTarget})"/>
        /// after actors are materialized to validate content-definition compatibility.
        /// </summary>
        /// <param name="states">Every actor state that will participate in the load.</param>
        public static void ValidateForRestore(IEnumerable<DungeonCreatureSaveState> states)
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            DungeonCreatureSaveState[] copied = states.ToArray();
            if (copied.Any(state => state == null))
                throw new ArgumentException("Actor states cannot contain null.", nameof(states));
            if (
                copied.Select(state => state.InstanceId).Distinct(StringComparer.Ordinal).Count()
                != copied.Length
            )
                throw new ArgumentException("Actor state IDs must be unique.", nameof(states));

            foreach (DungeonCreatureSaveState state in copied)
                ValidateActorForRestore(state);
        }

        private static void ValidateActorForRestore(DungeonCreatureSaveState state)
        {
            ValidateTimedEffectsForRestore(state);
            ValidatePreparedRulesForRestore(state);
            ValidateEquipmentForRestore(state);
        }

        private static void ValidateTimedEffectsForRestore(DungeonCreatureSaveState state)
        {
            foreach (DungeonTimedEffectSaveState effect in state.TimedEffects)
            {
                if (effect.OwnerCreatureId != state.InstanceId)
                    throw new InvalidOperationException(
                        $"Timed effect '{effect.InstanceId}' has the wrong binding owner."
                    );
                if (effect.TargetCreatureId != state.InstanceId)
                    throw new InvalidOperationException(
                        $"Timed effect '{effect.InstanceId}' has the wrong target actor."
                    );
                if (!SupportedTimedEffectKinds.Contains(effect.Kind))
                    throw new InvalidOperationException(
                        $"Timed effect '{effect.InstanceId}' uses unsupported kind '{effect.Kind}'."
                    );
                if (effect.StateDiscriminator != LegacyEffectStateDiscriminator)
                    throw new InvalidOperationException(
                        $"Timed effect '{effect.InstanceId}' uses unsupported state discriminator '{effect.StateDiscriminator}'."
                    );
                if (effect.StateJson.Length > 0 && effect.StateJson != EmptyEffectStateJson)
                    throw new InvalidOperationException(
                        $"Timed effect '{effect.InstanceId}' has unsupported kind state."
                    );
            }
            ValidateUniqueTimedEffects(state.TimedEffects, state.InstanceId);
        }

        private static void ValidatePreparedRulesForRestore(DungeonCreatureSaveState state)
        {
            if (
                state.PreparedRules.RollOptions.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != state.PreparedRules.RollOptions.Count
            )
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has case-insensitive duplicate roll options."
                );
            if (
                state
                    .PreparedRules.ActiveEffects.Select(effect =>
                        effect.Slug + "\n" + effect.SourceSlug
                    )
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != state.PreparedRules.ActiveEffects.Count
            )
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has duplicate prepared-effect identities."
                );
        }

        private static void ValidateEquipmentForRestore(DungeonCreatureSaveState state)
        {
            int leftHandCount = 0;
            int rightHandCount = 0;
            int equippedArmorCount = 0;
            foreach (DungeonInventoryItemSaveState item in state.Equipment.Items)
            {
                if (item.Quantity != 1)
                    throw new InvalidOperationException(
                        $"Actor '{state.InstanceId}' inventory entry '{item.EntryId}' has an unsupported stacked quantity."
                    );
                switch (item.Slot)
                {
                    case DungeonEquipmentSlot.LeftHand:
                        leftHandCount++;
                        break;
                    case DungeonEquipmentSlot.RightHand:
                        rightHandCount++;
                        break;
                    case DungeonEquipmentSlot.Armor:
                        equippedArmorCount++;
                        if (!item.IsLoaded)
                            throw new InvalidOperationException(
                                $"Actor '{state.InstanceId}' marks equipped armor '{item.EntryId}' unloaded."
                            );
                        break;
                }
            }
            if (leftHandCount > 1)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has more than one left-hand item."
                );
            if (rightHandCount > 1)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has more than one right-hand item."
                );
            if (equippedArmorCount > 1)
                throw new InvalidOperationException(
                    $"Actor '{state.InstanceId}' has more than one equipped armor item."
                );
        }
    }
}
