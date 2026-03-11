using FishNet.Object;
using FishNet.Object.Synchronizing;
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
    /// - Must have a Collider so server raycasts can hit it.
    /// - itemId must correspond to an ItemDefinition id present in your ItemDatabase.
    /// 
    /// Runtime behavior:
    /// - Manual pickup only (look + press): server validates and transfers into inventory.
    /// - Survivor-only pickup (enforced server-side).
    /// - Pickup despawns server-side after fully transferring all remaining units.
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
        [Tooltip("Initial amount available in this pickup. For non-stackable items this will effectively grant 1 per empty slot.")]
        [SerializeField, Min(1)] private int amount = 1;

        private readonly SyncVar<int> _remainingAmount = new(0);

        public ushort ItemId => itemId;
        public int RemainingAmount => _remainingAmount.Value;

        private void Awake()
        {
            SyncItemIdFromDefinition();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // If not initialized by a spawner, default to inspector amount.
            if (_remainingAmount.Value == 0)
                _remainingAmount.Value = amount;
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

        /// <summary>
        /// Server-only: initialize this pickup's payload.
        /// Intended for drops (inventory -> world) and runtime spawns.
        /// </summary>
        [Server]
        public void ServerSetPayload(ushort newItemId, int newAmount)
        {
            if (newItemId == 0) return;
            if (newAmount == 0) return;

            itemId = newItemId;
            amount = newAmount;
            _remainingAmount.Value = newAmount;
        }

        /// <summary>
        /// Server-only: attempt to transfer from this pickup into the player's inventory.
        /// Will retain leftover units in the pickup if the inventory cannot fit everything.
        /// </summary>
        [Server]
        public bool ServerTryPickup(NetworkPlayerInventory inv)
        {
            if (inv == null) return false;
            if (_remainingAmount.Value == 0) return false;

            // Survivors-only (hard rule).
            var player = inv.GetComponent<NetworkPlayer>();
            if (player != null && player.Team != Team.Survivor) return false;

            int requested = _remainingAmount.Value;
            int added = inv.AddItemUpTo(itemId, requested);
            if (added <= 0) return false;

            int remaining = requested - added;
            _remainingAmount.Value = Mathf.Max(remaining, 0);

            if (_remainingAmount.Value == 0 && NetworkObject != null)
                ServerManager.Despawn(NetworkObject);

            return true;
        }
    }
}
