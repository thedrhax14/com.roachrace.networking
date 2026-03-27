using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    public sealed class NetworkHealthObserver : NetworkBehaviour, INetworkPlayerInventoryDeltaObserver
    {
        [Header("Health Asset")]
        [SerializeField]
        [Tooltip("ItemDefinition id used as the key for health units in inventory.")]
        private ItemDefinition healthAsset;
        [Header("Death/Despawn Settings")]
        [Tooltip("NetworkObject to despawn on death. If null, will attempt to find a NetworkObject in parent hierarchy.")]
        public NetworkObject objectToDespawn;
        [Tooltip("Optional NetworkObject to spawn on death (e.g., explosion effect).")]
        public NetworkObject objectToSpawn;
        NetworkPlayerInventory inventory;
        readonly HashSet<INetworkHealthServerDeathObserver> serverDeathObservers = new();
        public int currentHealth;

        public override void OnStartServer()
        {
            base.OnStartServer();
            inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (inventory == null)
            {
                Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': inventory.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': inventory.");
            }

            if (healthAsset == null)
            {
                Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': healthAsset.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': healthAsset.");
            }

            inventory.AddServerDeltaObserver(this);
        }

        public override void OnStopServer()
        {
            if (inventory != null)
                inventory.RemoveServerDeltaObserver(this);

            base.OnStopServer();
        }

        public void OnServerInventoryItemDeltaApplied(NetworkPlayerInventory inventory, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            if (healthAsset == null)
                return;

            if (appliedDelta == 0)
                return;
            currentHealth = inventory.GetTotalCountByItemId(healthAsset.id);
            if (currentHealth <= 0)
            {
                foreach (var observer in serverDeathObservers)
                {
                    observer.OnNetworkHealthServerDied();
                }
                StartHandleDeath();
            }
        }

        [Server]
        void StartHandleDeath()
        {
            bool waitForOwnershipTransfer = objectToDespawn.OwnerId != -1;
            objectToDespawn.RemoveOwnership();
            if (objectToSpawn) Spawn(Instantiate(objectToSpawn, transform.position, transform.rotation));
            if (waitForOwnershipTransfer) StartCoroutine(HandleDeathCoroutine());
            else objectToDespawn.Despawn();
        }

        IEnumerator HandleDeathCoroutine()
        {
            // Wait for ownership to clear to avoid despawn issues on some transports.
            const float timeoutSeconds = 2f;
            float elapsed = 0f;
            while (objectToDespawn != null && objectToDespawn.OwnerId != -1 && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            objectToDespawn.Despawn();
        }

        internal void AddServerDeathObserver(NetworkPlayerControllerLifecycle networkPlayerControllerLifecycle)
        {
            serverDeathObservers.Add(networkPlayerControllerLifecycle);
        }

        internal void RemoveServerDeathObserver(NetworkPlayerControllerLifecycle networkPlayerControllerLifecycle)
        {
            serverDeathObservers.Remove(networkPlayerControllerLifecycle);
        }
    }
}
