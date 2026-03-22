using FishNet.Connection;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Player-owned helper to drop the currently selected inventory stack into the world as a NetworkItemPickup.
    /// Server-authoritative: the server validates team + droppable rules, removes the stack, then spawns a pickup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkInventoryDropper : NetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkPlayerInventory inventory;

        [Header("Drop Prefab")]
        [Tooltip("Fallback prefab to spawn when dropping items if the ItemDefinition has no world pickup rules prefab set. Must have NetworkObject + NetworkItemPickup.")]
        [SerializeField] private NetworkObject pickupPrefab;

        [Header("Drop Placement")]
        [Tooltip("Optional. If not assigned, uses this transform.")]
        [SerializeField] private Transform dropOrigin;

        [SerializeField, Min(0f)] private float forwardOffset = 0.75f;
        [SerializeField, Min(0f)] private float upOffset = 0.25f;
        [SerializeField, Min(0.1f)] private float groundSnapDistance = 3f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Rules")]
        [Tooltip("If true, only survivors can drop items.")]
        [SerializeField] private bool survivorsOnly = true;

        public void RequestDropSelectedStack()
        {
            if (IsServerInitialized)
            {
                ServerDropSelectedStack();
                return;
            }

            if (!IsOwner) return;
            DropSelectedStackServerRpc();
        }

        [ServerRpc]
        private void DropSelectedStackServerRpc(NetworkConnection conn = null)
        {
            if (conn != null && !conn.IsActive) return;
            ServerDropSelectedStack();
        }

        [Server]
        private void ServerDropSelectedStack()
        {
            inventory ??= GetComponent<NetworkPlayerInventory>();
            if (inventory == null) return;

            if (survivorsOnly)
            {
                var player = inventory.GetComponent<NetworkPlayer>();
                if (player != null && player.Team != Team.Survivor) return;
            }

            // Peek selected stack first for droppable gating.
            var slot = inventory.GetSlot(inventory.SelectedSlotIndex);
            if (slot.IsEmpty) return;

            // Resolve definition for drop gating + prefab selection.
            NetworkObject prefabToSpawn = null;
            if (inventory.TryResolveDefinition(slot.ItemId, out var def) && def != null)
            {
                if (!def.CanDropFromInventory)
                    return;

                var worldPrefab = def.WorldPickupPrefab;
                if (worldPrefab != null)
                {
                    prefabToSpawn = worldPrefab.GetComponent<NetworkObject>();
                    if (prefabToSpawn == null)
                    {
                        Debug.LogError($"[{nameof(NetworkInventoryDropper)}] Invalid world pickup prefab for '{gameObject.name}': ItemDefinition '{def.name}' has prefab '{worldPrefab.name}' but it has no NetworkObject.", gameObject);
                        return;
                    }
                }
            }

            if (!inventory.TryRemoveSelectedStack(out ushort itemId, out int count))
                return;

            prefabToSpawn ??= pickupPrefab;
            if (prefabToSpawn == null)
            {
                Debug.LogError($"[{nameof(NetworkInventoryDropper)}] No drop prefab is available for '{gameObject.name}'. Assign a fallback pickupPrefab or set an ItemDefinition world pickup rules prefab.", gameObject);
                return;
            }

            Transform origin = dropOrigin != null ? dropOrigin : transform;
            Vector3 spawnPos = origin.position + origin.forward * forwardOffset + Vector3.up * upOffset;
            Quaternion spawnRot = Quaternion.identity;

            // Snap to ground if possible.
            if (Physics.Raycast(spawnPos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, groundSnapDistance + 0.5f, groundMask, QueryTriggerInteraction.Ignore))
                spawnPos = hit.point + Vector3.up * 0.02f;

            NetworkObject nob = Instantiate(prefabToSpawn, spawnPos, spawnRot);
            var pickup = nob != null ? nob.GetComponent<NetworkItemPickup>() : null;
            if (pickup == null)
            {
                Debug.LogError($"[{nameof(NetworkInventoryDropper)}] Drop prefab '{prefabToSpawn.name}' is missing {nameof(NetworkItemPickup)}.", gameObject);
                if (nob != null)
                    Destroy(nob.gameObject);
                return;
            }

            pickup.ServerSetPayload(itemId, count);
            ServerManager.Spawn(nob);
        }
    }
}
