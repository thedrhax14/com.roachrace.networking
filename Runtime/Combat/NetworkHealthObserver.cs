using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observes authoritative inventory-backed health and starts death handling when health reaches zero.<br/>
    /// Typical usage: attach this beneath a controller/player object that owns a <see cref="NetworkPlayerInventory"/> and a health item definition so server-side systems can observe damage and death without C# events.<br/>
    /// Configuration/context: this runs on the server, derives health from the configured <see cref="healthAsset"/>, and forwards damage attribution provided by inventory deltas.
    /// </summary>
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
        readonly HashSet<INetworkHealthServerDamageObserver> serverDamageObservers = new();
        public int currentHealth;

        /// <summary>
        /// Subscribes to the authoritative inventory so health can be recalculated from server-side item totals.<br/>
        /// Typical usage: FishNet invokes this when the object starts on the server; external callers should not call it directly.<br/>
        /// Configuration/context: requires a parent <see cref="NetworkPlayerInventory"/> and a configured <see cref="healthAsset"/>.
        /// </summary>
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
            currentHealth = inventory.GetTotalCountByItemId(healthAsset.id);
        }

        /// <summary>
        /// Unsubscribes from the authoritative inventory when the server stops this object.<br/>
        /// Typical usage: FishNet invokes this during teardown to avoid dangling observer references.<br/>
        /// Server/client constraints: server-only lifecycle callback.
        /// </summary>
        public override void OnStopServer()
        {
            if (inventory != null)
                inventory.RemoveServerDeltaObserver(this);

            base.OnStopServer();
        }

        /// <summary>
        /// Recalculates health after an inventory delta and notifies server-side observers when health actually decreases.<br/>
        /// Typical usage: <see cref="NetworkPlayerInventory"/> invokes this after an authoritative item delta; this method converts inventory-wide attribution into health-specific damage information.<br/>
        /// Configuration/context: only health loss produces a <see cref="NetworkHealthDamageInfo"/> notification; non-health inventory changes are ignored because they do not change the resolved health total.
        /// </summary>
        /// <param name="inventory">The authoritative inventory that applied the delta.</param>
        /// <param name="appliedDelta">The raw inventory delta that triggered recalculation.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon/effect key supplied by the authoritative source.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection, or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object, or -1 for environment/unknown.</param>
        public void OnServerInventoryItemDeltaApplied(NetworkPlayerInventory inventory, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            if (healthAsset == null)
                return;

            if (appliedDelta == 0)
                return;

            int previousHealth = currentHealth;
            currentHealth = inventory.GetTotalCountByItemId(healthAsset.id);
            int damageAmount = Mathf.Max(0, previousHealth - currentHealth);
            if (damageAmount > 0)
            {
                var damageInfo = new NetworkHealthDamageInfo(previousHealth, currentHealth, damageAmount, weaponIconKey, instigatorConnectionId, instigatorObjectId);
                NotifyServerDamageObservers(damageInfo);
            }

            if (currentHealth <= 0)
            {
                foreach (var observer in serverDeathObservers)
                {
                    observer.OnNetworkHealthServerDied();
                }
                StartHandleDeath();
            }
        }

        /// <summary>
        /// Starts authoritative death handling for this object after health reaches zero.<br/>
        /// Typical usage: called internally after recalculating health and notifying death observers.<br/>
        /// Configuration/context: ownership is cleared before despawn to avoid transport-specific issues; optional death spawn FX are spawned on the server.
        /// </summary>
        [Server]
        void StartHandleDeath()
        {
            if(objectToDespawn == null)
            {
                objectToDespawn = GetComponentInParent<NetworkObject>();
                if (objectToDespawn == null)
                {
                    Debug.LogError($"[{nameof(NetworkHealthObserver)}] No NetworkObject found to despawn on death for '{gameObject.name}'. Please assign one explicitly or ensure a parent has a NetworkObject component.", gameObject);
                    return;
                }
            }
            bool waitForOwnershipTransfer = objectToDespawn.OwnerId != -1;
            objectToDespawn.RemoveOwnership();
            if (objectToSpawn) Spawn(Instantiate(objectToSpawn, transform.position, transform.rotation));
            if (waitForOwnershipTransfer) StartCoroutine(HandleDeathCoroutine());
            else objectToDespawn.Despawn();
        }

        /// <summary>
        /// Waits briefly for ownership removal to propagate before despawning the target object.<br/>
        /// Typical usage: internal coroutine used by <see cref="StartHandleDeath"/> for transports that can race ownership transfer against despawn.<br/>
        /// Configuration/context: times out after a short fixed delay to avoid hanging server cleanup.
        /// </summary>
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

        /// <summary>
        /// Adds a server-side death observer for this health component.<br/>
        /// Typical usage: controller lifecycle/respawn systems register after spawning the controller they want to monitor.<br/>
        /// Server/client constraints: server-only observer registration.
        /// </summary>
        /// <param name="networkPlayerControllerLifecycle">The server-side lifecycle observer to register.</param>
        internal void AddServerDeathObserver(NetworkPlayerControllerLifecycle networkPlayerControllerLifecycle)
        {
            serverDeathObservers.Add(networkPlayerControllerLifecycle);
        }

        /// <summary>
        /// Removes a server-side death observer from this health component.<br/>
        /// Typical usage: lifecycle systems unregister during despawn or controller swaps to avoid stale references.<br/>
        /// Server/client constraints: server-only observer registration.
        /// </summary>
        /// <param name="networkPlayerControllerLifecycle">The server-side lifecycle observer to remove.</param>
        internal void RemoveServerDeathObserver(NetworkPlayerControllerLifecycle networkPlayerControllerLifecycle)
        {
            serverDeathObservers.Remove(networkPlayerControllerLifecycle);
        }

        /// <summary>
        /// Adds a server-side damage observer for this health component.<br/>
        /// Typical usage: combat presentation bridge systems register here before converting authoritative damage into owner-only UI feedback or stats updates.<br/>
        /// Server/client constraints: server-only observer registration.
        /// </summary>
        /// <param name="observer">The observer to register.</param>
        public void AddServerDamageObserver(INetworkHealthServerDamageObserver observer)
        {
            if (observer == null)
                return;

            serverDamageObservers.Add(observer);
        }

        /// <summary>
        /// Removes a server-side damage observer from this health component.<br/>
        /// Typical usage: bridge systems unregister during teardown to avoid stale server references.<br/>
        /// Server/client constraints: server-only observer registration.
        /// </summary>
        /// <param name="observer">The observer to remove.</param>
        public void RemoveServerDamageObserver(INetworkHealthServerDamageObserver observer)
        {
            if (observer == null)
                return;

            serverDamageObservers.Remove(observer);
        }

        /// <summary>
        /// Notifies registered server-side damage observers about an authoritative health loss.<br/>
        /// Typical usage: called internally immediately after health is recalculated and confirmed to have decreased.<br/>
        /// Configuration/context: uses a snapshot iteration so observers can safely add or remove registrations during callback.
        /// </summary>
        /// <param name="damageInfo">Resolved damage context for the applied health loss.</param>
        private void NotifyServerDamageObservers(NetworkHealthDamageInfo damageInfo)
        {
            if (serverDamageObservers.Count == 0)
                return;

            foreach (var observer in serverDamageObservers)
            {
                if (observer == null)
                    continue;

                observer.OnNetworkHealthServerDamaged(this, damageInfo);
            }
        }
    }
}
