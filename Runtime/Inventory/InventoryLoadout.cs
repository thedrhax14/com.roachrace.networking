using RoachRace.Interaction;
using UnityEngine;

namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// ScriptableObject describing server-granted inventory entries for a spawned player.<br/>
    /// Typical usage: assign this asset on <see cref="NetworkPlayerInventory"/> to define default items granted on spawn.<br/>
    /// Configuration/context: each entry only declares item id and amount; storage placement is resolved from the granted item's inventory rules.
    /// </summary>
    [CreateAssetMenu(
        menuName = "RoachRace/Inventory/Inventory Loadout",
        fileName = "InventoryLoadout")]
    public sealed class InventoryLoadout : ScriptableObject
    {
        /// <summary>
        /// Single inventory grant entry within a loadout.<br/>
        /// Typical usage: point at an <see cref="ItemDefinition"/> when possible so the editor keeps ids in sync, then configure amount.<br/>
        /// Configuration/context: the granted item's inventory rules decide whether it goes into the visible/selectable prefix or the hidden suffix.
        /// </summary>
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Optional. Assign an ItemDefinition asset to avoid manually typing ids. When set, itemId will be kept in sync in the editor.")]
            public ItemDefinition itemDefinition;

            [Tooltip("ItemDefinition id to grant. 0 is reserved for empty.")]
            public ushort itemId;

            [Tooltip("How many to grant. For non-stackable items this will effectively grant 1 per empty slot.")]
            public int amount;
        }

        [Tooltip("Items granted by the server when this player spawns. Useful for default loadouts.")]
        [SerializeField] private Entry[] entries;

        /// <summary>
        /// Gets the authored loadout entries.<br/>
        /// Typical usage: <see cref="NetworkPlayerInventory"/> reads this during server spawn to grant configured items.
        /// </summary>
        public Entry[] Entries => entries;

        /// <summary>
        /// Unity editor validation hook.<br/>
        /// Typical usage: keeps item ids in sync with assigned definitions and normalizes zero amounts to one for non-empty entries.<br/>
        /// Configuration/context: runs only in the editor while authoring the loadout asset.
        /// </summary>
        private void OnValidate()
        {
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.itemDefinition != null && entry.itemDefinition.id != 0)
                    entry.itemId = entry.itemDefinition.id;

                if (entry.itemId != 0 && entry.amount == 0)
                    entry.amount = 1;

                entries[i] = entry;
            }
        }
    }
}
