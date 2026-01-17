using FishNet.Object;
using RoachRace.Data;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Runtime-spawnable pickup.
    /// 
    /// Prefab setup:
    /// - Must have a NetworkObject.
    /// - Must have a Trigger collider (IsTrigger = true).
    /// - This component should be on the same GameObject as the collider (or any child collider; it uses OnTriggerEnter).
    /// - itemId must correspond to an ItemDefinition id present in your ItemDatabase.
    /// 
    /// Runtime behavior:
    /// - Server grants the item when a player enters the trigger.
    /// - By default only Survivors can pick it up.
    /// - Pickup despawns server-side after successful grant.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkItemPickup : NetworkBehaviour
    {
        [Header("Pickup")]
        [Tooltip("Optional. Assign an ItemDefinition asset to avoid manually typing ids. When set, itemId will be kept in sync.")]
        [SerializeField] private ItemDefinition itemDefinition;

        [Tooltip("ItemDefinition id to grant. 0 is reserved for empty. This is the value actually used at runtime/network.")]
        [SerializeField] private ushort itemId = 1;
        [Tooltip("How many to grant. For non-stackable items this will effectively grant 1 per empty slot.")]
        [SerializeField] private byte amount = 1;
        [Tooltip("If true, only players on Team.Survivor can pick this up.")]
        [SerializeField] private bool survivorsOnly = true;

        private void Awake()
        {
            SyncItemIdFromDefinition();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SyncItemIdFromDefinition();
        }
#endif

        private void SyncItemIdFromDefinition()
        {
            if (itemDefinition == null) return;
            if (itemDefinition.id == 0) return;
            itemId = itemDefinition.id;
            gameObject.name = $"ItemPickup_{itemDefinition.displayName}_(id{itemId})";
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerInitialized) return;

            var inv = other.GetComponentInParent<NetworkPlayerInventory>();
            if (inv == null) return;

            if (survivorsOnly)
            {
                var player = inv.GetComponent<NetworkPlayer>();
                if (player != null && player.Team != Team.Survivor) return;
            }

            bool added = inv.TryAddItem(itemId, amount);
            if (!added) return;

            // Despawn pickup across the network.
            if (NetworkObject != null)
                ServerManager.Despawn(NetworkObject);
        }
    }
}
