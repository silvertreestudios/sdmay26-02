using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Game.DungeonPersistence.Repository
{
    /// <summary>Identifies where one inventory entry is equipped.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum DungeonEquipmentSlot
    {
        /// <summary>The item is carried but not equipped.</summary>
        Carried,

        /// <summary>The item is equipped as armor.</summary>
        Armor,

        /// <summary>The item is equipped in the left hand.</summary>
        LeftHand,

        /// <summary>The item is equipped in the right hand.</summary>
        RightHand,
    }

    /// <summary>Records one inventory entry without duplicating catalog-derived item definitions.</summary>
    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonInventoryItemSaveState
    {
        /// <summary>Creates a stable inventory-entry record.</summary>
        /// <param name="entryId">The stable per-actor item instance identifier.</param>
        /// <param name="itemDefinitionId">The stable item catalog identifier.</param>
        /// <param name="quantity">The positive item quantity.</param>
        /// <param name="slot">Where the entry is currently equipped.</param>
        /// <param name="isLoaded">Whether a reload-capable weapon entry is currently loaded.</param>
        public DungeonInventoryItemSaveState(
            string entryId,
            string itemDefinitionId,
            int quantity,
            DungeonEquipmentSlot slot,
            bool isLoaded
        )
        {
            EntryId = DungeonSaveContractGuard.RequiredId(entryId, nameof(entryId));
            ItemDefinitionId = DungeonSaveContractGuard.RequiredId(
                itemDefinitionId,
                nameof(itemDefinitionId)
            );
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "Item quantity must be positive."
                );
            if (!Enum.IsDefined(typeof(DungeonEquipmentSlot), slot))
                throw new ArgumentOutOfRangeException(nameof(slot), "Equipment slot is undefined.");
            Quantity = quantity;
            Slot = slot;
            IsLoaded = isLoaded;
        }

        /// <summary>Gets the stable per-actor item instance identifier.</summary>
        public string EntryId { get; }

        /// <summary>Gets the stable item catalog identifier.</summary>
        public string ItemDefinitionId { get; }

        /// <summary>Gets the positive item quantity.</summary>
        public int Quantity { get; }

        /// <summary>Gets where the entry is currently equipped.</summary>
        public DungeonEquipmentSlot Slot { get; }

        /// <summary>Gets whether a reload-capable weapon entry is loaded.</summary>
        public bool IsLoaded { get; }
    }

    /// <summary>Records one ammunition pool independently from catalog-derived definitions.</summary>
    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonAmmunitionSaveState
    {
        /// <summary>Creates an ammunition pool.</summary>
        /// <param name="ammunitionId">The stable ammunition catalog identifier.</param>
        /// <param name="quantity">The remaining nonnegative quantity.</param>
        public DungeonAmmunitionSaveState(string ammunitionId, int quantity)
        {
            AmmunitionId = DungeonSaveContractGuard.RequiredId(ammunitionId, nameof(ammunitionId));
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "Ammunition cannot be negative."
                );
            Quantity = quantity;
        }

        /// <summary>Gets the stable ammunition catalog identifier.</summary>
        public string AmmunitionId { get; }

        /// <summary>Gets the remaining nonnegative quantity.</summary>
        public int Quantity { get; }
    }

    /// <summary>Records meaningful inventory, equipment, ammunition, and loading state.</summary>
    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class DungeonEquipmentSaveState
    {
        /// <summary>Creates an immutable, deterministically ordered equipment snapshot.</summary>
        /// <param name="items">Inventory entries with unique stable instance IDs.</param>
        /// <param name="ammunition">Ammunition pools with unique stable catalog IDs.</param>
        public DungeonEquipmentSaveState(
            IEnumerable<DungeonInventoryItemSaveState> items,
            IEnumerable<DungeonAmmunitionSaveState> ammunition
        )
        {
            Items = DungeonSaveContractGuard.UniqueSorted(
                items,
                item => item.EntryId,
                nameof(items)
            );
            Ammunition = DungeonSaveContractGuard.UniqueSorted(
                ammunition,
                item => item.AmmunitionId,
                nameof(ammunition)
            );
        }

        /// <summary>Gets inventory entries ordered by stable instance ID.</summary>
        public IReadOnlyList<DungeonInventoryItemSaveState> Items { get; }

        /// <summary>Gets ammunition pools ordered by stable catalog ID.</summary>
        public IReadOnlyList<DungeonAmmunitionSaveState> Ammunition { get; }
    }
}
