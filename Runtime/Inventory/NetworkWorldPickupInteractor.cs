using FishNet.Connection;
using FishNet.Object;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Player-owned helper to pick up world items (NetworkItemPickup) by looking at them and pressing a pickup action.
    /// Server-authoritative: the server raycasts and performs the inventory grant.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWorldPickupInteractor : NetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkPlayerInventory inventory;

        [Header("Raycast")]
        [SerializeField, Min(0.1f)] private float maxDistance = 3f;
        [SerializeField] private LayerMask pickupMask = ~0;

        public void ConfigureRaycast(float newMaxDistance, LayerMask newPickupMask)
        {
            maxDistance = Mathf.Max(0.1f, newMaxDistance);
            pickupMask = newPickupMask;
        }

        public void TryPickup(Vector3 origin, Vector3 direction)
        {
            if (IsServerInitialized)
            {
                ServerTryPickup(origin, direction);
                return;
            }

            if (!IsOwner) return;
            TryPickupServerRpc(origin, direction);
        }

        [ServerRpc]
        private void TryPickupServerRpc(Vector3 origin, Vector3 direction, NetworkConnection conn = null)
        {
            // Only the owning client should be able to request pickup from this component.
            if (conn != null && !conn.IsActive) return;
            ServerTryPickup(origin, direction);
        }

        [Server]
        private void ServerTryPickup(Vector3 origin, Vector3 direction)
        {
            inventory ??= GetComponent<NetworkPlayerInventory>();
            if (inventory == null) return;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, pickupMask, QueryTriggerInteraction.Collide))
                return;

            var pickup = hit.collider != null ? hit.collider.GetComponentInParent<NetworkItemPickup>() : null;
            if (pickup == null) return;

            // Basic sanity: ensure the pickup isn't wildly far from the player.
            float distToPlayer = Vector3.Distance(transform.position, pickup.transform.position);
            if (distToPlayer > maxDistance + 1f) return;

            pickup.ServerTryPickup(inventory);
        }
    }
}
