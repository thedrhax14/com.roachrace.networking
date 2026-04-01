using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observes authoritative inventory-backed health, mirrors health into the owner-local player stats model, and starts death handling when health reaches zero.<br/>
    /// Typical usage: attach this beneath a controller/player object that owns a <see cref="NetworkPlayerInventory"/> and a health item definition so server-side systems can observe damage and death without C# events while the owning client renders health through <see cref="PlayerStatsModel"/>.<br/>
    /// Configuration/context: this runs on the server, derives health from the configured <see cref="healthAsset"/>, forwards damage attribution provided by inventory deltas, and updates the owning client's UI model through synchronized values.
    /// </summary>
    public sealed class NetworkHealthObserver : NetworkBehaviour, INetworkPlayerInventoryDeltaObserver
    {
        [Header("Health Asset")]
        [SerializeField]
        [Tooltip("ItemDefinition id used as the key for health units in inventory.")]
        private ItemDefinition healthAsset;

        [Header("UI")]
        [SerializeField]
        [Tooltip("Owner-local PlayerStatsModel to update when health changes.")]
        private PlayerStatsModel playerStatsModel;

        [Header("Health Capacity")]
        [SerializeField, Min(0)]
        [Tooltip("Optional starting max health. If 0, the first inventory snapshot becomes the baseline max.")]
        private int startingMaxHealth = 100;

        [Header("Death/Despawn Settings")]
        [Tooltip("NetworkObject to despawn on death. If null, will attempt to find a NetworkObject in parent hierarchy.")]
        public NetworkObject objectToDespawn;
        [Tooltip("Optional NetworkObject to spawn on death (e.g., explosion effect).")]
        public NetworkObject objectToSpawn;
        NetworkPlayerInventory inventory;
        private readonly SyncVar<int> _currentHealth = new(0);
        private readonly SyncVar<int> _maxHealth = new(0);
        private bool _clientSubscribed;
        readonly HashSet<INetworkHealthServerDeathObserver> serverDeathObservers = new();
        readonly HashSet<INetworkHealthServerDamageObserver> serverDamageObservers = new();
        public int currentHealth;

        /// <summary>
        /// Gets the synchronized current health snapshot.<br/>
        /// Typical usage: read by owner-local presentation or gameplay code that needs to know whether the player still has health remaining.<br/>
        /// Configuration/context: the value is maintained by the server from inventory-backed health totals.
        /// </summary>
        public int CurrentHealth => _currentHealth.Value;

        /// <summary>
        /// Gets the synchronized maximum health snapshot.<br/>
        /// Typical usage: feed the maximum value into <see cref="PlayerStatsModel.SetHealth(int, int)"/> so the HUD can render a current/max pair.<br/>
        /// Configuration/context: if the inventory total grows beyond the initial baseline, this value expands to match the observed maximum.
        /// </summary>
        public int MaxHealth => _maxHealth.Value;

        /// <summary>
        /// Validates required dependencies before the health observer starts network lifecycle processing.<br/>
        /// Typical usage: Unity invokes this during object initialization so the component can fail fast if the health inventory dependency is missing.<br/>
        /// Configuration/context: the owner-local <see cref="playerStatsModel"/> is validated later because only the owning client needs it.
        /// </summary>
        private void Awake()
        {
            if (healthAsset == null)
            {
                Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': healthAsset.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': healthAsset.");
            }
        }

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
            RefreshServerSnapshot();
        }

        /// <summary>
        /// Subscribes the owning client to synchronized health changes and pushes the current snapshot into the player stats model.<br/>
        /// Typical usage: FishNet invokes this on all clients; only the owner binds UI state because the player stats model is owner-local.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            _currentHealth.OnChange += CurrentHealth_OnChange;
            _maxHealth.OnChange += MaxHealth_OnChange;

            if (IsOwner)
            {
                if (playerStatsModel == null)
                {
                    Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': playerStatsModel.", gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': playerStatsModel.");
                }

                PushHealthToPlayerStatsModel();
            }
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
        /// Unsubscribes the client-side health listeners when the object stops on a client.<br/>
        /// Typical usage: FishNet invokes this during teardown or ownership changes to avoid stale callbacks.<br/>
        /// Server/client constraints: client-side lifecycle hook.
        /// </summary>
        public override void OnStopClient()
        {
            _currentHealth.OnChange -= CurrentHealth_OnChange;
            _maxHealth.OnChange -= MaxHealth_OnChange;

            base.OnStopClient();
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
        /// <param name="hasSourceWorldPosition">Whether a world-space source position was supplied for this hit.</param>
        /// <param name="sourceWorldPosition">World-space source position when <paramref name="hasSourceWorldPosition"/> is true.</param>
        /// <param name="hasTargetWorldPosition">Whether a world-space target hit point was supplied for this hit.</param>
        /// <param name="targetWorldPosition">World-space hit point on the damaged target when <paramref name="hasTargetWorldPosition"/> is true.</param>
        public void OnServerInventoryItemDeltaApplied(NetworkPlayerInventory inventory, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId, bool hasSourceWorldPosition, Vector3 sourceWorldPosition, bool hasTargetWorldPosition, Vector3 targetWorldPosition)
        {
            if (healthAsset == null)
                return;

            if (appliedDelta == 0)
                return;

            int previousHealth = currentHealth;
            RefreshServerSnapshot();
            int nextHealth = currentHealth;
            int damageAmount = Mathf.Max(0, previousHealth - nextHealth);
            if (damageAmount > 0)
            {
                Vector3 resolvedTargetWorldPosition = hasTargetWorldPosition ? targetWorldPosition : transform.position;
                var damageInfo = new NetworkHealthDamageInfo(previousHealth, nextHealth, damageAmount, weaponIconKey, instigatorConnectionId, instigatorObjectId, resolvedTargetWorldPosition, hasSourceWorldPosition, sourceWorldPosition);
                NotifyServerDamageObservers(damageInfo);
            }

            if (nextHealth <= 0)
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

        /// <summary>
        /// Rebuilds the authoritative health snapshot from inventory and publishes it into the synchronized values.<br/>
        /// Typical usage: called after server bootstrap and after every inventory delta to keep the current/max snapshot current.
        /// </summary>
        private void RefreshServerSnapshot()
        {
            if (!IsServerInitialized || inventory == null || healthAsset == null)
                return;

            int current = inventory.GetTotalCountByItemId(healthAsset.id);
            int max = startingMaxHealth > 0 ? startingMaxHealth : current;
            if (current > max)
                max = current;

            currentHealth = current;
            _currentHealth.Value = current;
            _maxHealth.Value = max;
        }

        /// <summary>
        /// Pushes the synchronized health snapshot into the owner-local player stats model.<br/>
        /// Typical usage: called when the owner client receives health updates so the HUD can refresh immediately.<br/>
        /// Configuration/context: ignored on non-owners because their player stats model should not be touched by remote players.
        /// </summary>
        private void PushHealthToPlayerStatsModel()
        {
            if (!IsOwner || playerStatsModel == null)
                return;

            playerStatsModel.SetHealth(_currentHealth.Value, _maxHealth.Value);
        }

        /// <summary>
        /// Forwards current health changes to the owner-local player stats model.<br/>
        /// Typical usage: wired to the synchronized health value so the HUD stays in sync when the server consumes or restores health.
        /// </summary>
        /// <param name="previous">Previous synchronized health value.</param>
        /// <param name="next">New synchronized health value.</param>
        /// <param name="asServer">True when invoked on the server side of the sync callback.</param>
        private void CurrentHealth_OnChange(int previous, int next, bool asServer)
        {
            PushHealthToPlayerStatsModel();
        }

        /// <summary>
        /// Forwards current max health changes to the owner-local player stats model.<br/>
        /// Typical usage: wired to the synchronized health cap so the HUD reflects capacity increases as well as consumption.
        /// </summary>
        /// <param name="previous">Previous synchronized max health value.</param>
        /// <param name="next">New synchronized max health value.</param>
        /// <param name="asServer">True when invoked on the server side of the sync callback.</param>
        private void MaxHealth_OnChange(int previous, int next, bool asServer)
        {
            PushHealthToPlayerStatsModel();
        }
    }
}
