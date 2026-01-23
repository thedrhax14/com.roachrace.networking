using RoachRace.Interaction;
using UnityEngine;

namespace RoachRace.Networking.Inventory
{
    [CreateAssetMenu(
        menuName = "RoachRace/Inventory/Inventory Loadout",
        fileName = "InventoryLoadout")]
    public sealed class InventoryLoadout : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Optional. Assign an ItemDefinition asset to avoid manually typing ids. When set, itemId will be kept in sync in the editor.")]
            public ItemDefinition itemDefinition;

            [Tooltip("ItemDefinition id to grant. 0 is reserved for empty.")]
            public ushort itemId;

            [Tooltip("How many to grant. For non-stackable items this will effectively grant 1 per empty slot.")]
            public byte amount;
        }

        [Tooltip("Items granted by the server when this player spawns. Useful for default loadouts.")]
        [SerializeField] private Entry[] entries;

        public Entry[] Entries => entries;

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
