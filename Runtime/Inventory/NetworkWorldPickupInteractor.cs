using FishNet.Connection;
using FishNet.Object;
using RoachRace.Networking.Input;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Player-owned helper to pick up world items (NetworkItemPickup) by looking at them and pressing a pickup action.
    /// Server-authoritative: the server raycasts and performs the inventory grant.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerLookState))]
    public sealed class NetworkWorldPickupInteractor : NetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkPlayerInventory inventory;
        [SerializeField] private NetworkPlayerLookState lookState;

        [Header("Raycast")]
        [SerializeField, Min(0.1f)] private float maxDistance = 3f;
        [SerializeField] private LayerMask pickupMask = ~0;

        public void ConfigureRaycast(float newMaxDistance, LayerMask newPickupMask)
        {
            maxDistance = Mathf.Max(0.1f, newMaxDistance);
            pickupMask = newPickupMask;
        }

        private void Awake()
        {
            inventory ??= GetComponent<NetworkPlayerInventory>();
            lookState ??= GetComponent<NetworkPlayerLookState>();

            if (lookState == null)
            {
                Debug.LogError($"[{nameof(NetworkWorldPickupInteractor)}] {nameof(NetworkPlayerLookState)} is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkWorldPickupInteractor)}] lookState is null on GameObject '{gameObject.name}'. Add {nameof(NetworkPlayerLookState)} to the same GameObject.");
            }
        }

        public void TryPickup()
        {
            if (IsServerInitialized)
            {
                ServerTryPickup();
                return;
            }

            if (!IsOwner) return;
            TryPickupServerRpc();
        }

        [ServerRpc]
        private void TryPickupServerRpc(NetworkConnection conn = null)
        {
            // Only the owning client should be able to request pickup from this component.
            if (conn != null && !conn.IsActive) return;
            ServerTryPickup();
        }

        [Server]
        private void ServerTryPickup()
        {
            inventory ??= GetComponent<NetworkPlayerInventory>();
            if (inventory == null) return;

            if (!lookState.TryGetLook(out Vector3 origin, out Vector3 direction, preferLocal: false))
            {
                Debug.LogError($"[{nameof(NetworkWorldPickupInteractor)}] Failed to resolve aim data from {nameof(NetworkPlayerLookState)} on '{gameObject.name}'.", gameObject);
                return;
            }

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
