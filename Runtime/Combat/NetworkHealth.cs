using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Data;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Core networked health + damage component.<br/>
    /// Handles server-authoritative health state and broadcasts damage/death to clients for VFX/UI.<br/>
    /// Inventory-driven damage should be applied via server systems (e.g., inventory delta observers) calling <see cref="TryConsume(DamageInfo)" />.
    /// </summary>
    public class NetworkHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [FormerlySerializedAs("maxHealth")]
        [SerializeField] private int initialMaxHealth = 100;

        // SyncVars ensure all clients know the current/max health.
        private readonly SyncVar<int> _maxHealth = new(100);
        private readonly SyncVar<int> _currentHealth = new(100);
        [Header("Death/Despawn Settings")]
        [Tooltip("NetworkObject to despawn on death. If null, will attempt to find a NetworkObject in parent hierarchy.")]
        public NetworkObject objectToDespawn;
        [Tooltip("Optional NetworkObject to spawn on death (e.g., explosion effect).")]
        public NetworkObject objectToSpawn;
        [Header("Events")]
        // Events for UI, Effects, and Logic
        public UnityEvent<DamageInfo> OnDamaged; // Called on Server and Client (via RPC)
        public UnityEvent<int, int> OnHealthChanged; // current, max

        private readonly HashSet<INetworkHealthServerDeathObserver> _serverDeathObservers = new();
        
        [Header("Dependencies")]
        [SerializeField] private DamageEventModel damageEventModel;
        
        private string _victimName;

        public int CurrentHealth => _currentHealth.Value;
        public int MaxHealth => _maxHealth.Value;

        public float Current => _currentHealth.Value;
        public float Max => _maxHealth.Value;
        public bool IsAlive => _currentHealth.Value > 0;

        public void Heal(int amount)
        {
            if (!IsServerInitialized) return;
            if (!IsAlive) return;
            if (amount <= 0) return;

            int oldHealth = _currentHealth.Value;
            int max = _maxHealth.Value;
            int newHealth = Mathf.Min(max, oldHealth + amount);
            if (oldHealth == newHealth) return;

            _currentHealth.Value = newHealth;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            int max = Mathf.Max(1, initialMaxHealth);
            
            // Cache victim name for damage events
            _victimName = gameObject.name;
            _maxHealth.Value = max;
            _currentHealth.Value = max;

            if (objectToDespawn == null) objectToDespawn = GetComponentInParent<NetworkObject>();
            if (objectToDespawn == null) throw new System.NullReferenceException($"[{nameof(NetworkHealth)}] 'objectToDespawn' is not assigned on '{gameObject.name}'. It is required to despawn the object on death.");
        }

        /// <summary>
        /// Subscribes an observer to the server-only death notification.<br>
        /// Typical usage: server-side controller/respawn systems call this after spawning a controller to be notified when death handling begins.<br>
        /// Server/client constraints: this notification is server-only and will never be invoked on clients.
        /// </summary>
        /// <param name="observer">Observer to register; duplicates are ignored.</param>
        [Server]
        public void AddServerDeathObserver(INetworkHealthServerDeathObserver observer)
        {
            if (observer == null) return;
            _serverDeathObservers.Add(observer);
        }

        /// <summary>
        /// Unsubscribes an observer from the server-only death notification.<br>
        /// Typical usage: called when swapping controllers or tearing down server-side systems to avoid leaks.<br>
        /// Server/client constraints: server-only.
        /// </summary>
        /// <param name="observer">Observer to unregister.</param>
        [Server]
        public void RemoveServerDeathObserver(INetworkHealthServerDeathObserver observer)
        {
            if (observer == null) return;
            _serverDeathObservers.Remove(observer);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _currentHealth.OnChange += OnHealthSyncChanged;
            _maxHealth.OnChange += OnMaxHealthSyncChanged;
            // Initial update for UI or listeners
            OnHealthChanged?.Invoke(_currentHealth.Value, _maxHealth.Value);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _currentHealth.OnChange -= OnHealthSyncChanged;
            _maxHealth.OnChange -= OnMaxHealthSyncChanged;
        }

        private void OnHealthSyncChanged(int prev, int next, bool asServer)
        {
            OnHealthChanged?.Invoke(next, _maxHealth.Value);
        }

        private void OnMaxHealthSyncChanged(int prev, int next, bool asServer)
        {
            OnHealthChanged?.Invoke(_currentHealth.Value, next);
        }

        // Server-only entry point
        public void TryConsume(DamageInfo damageInfo)
        {
            if (!IsServerInitialized) return; // Authority check
            if (!IsAlive) return;

            int damage = damageInfo.Amount;
            int oldHealth = _currentHealth.Value;
            int newHealth = Mathf.Max(0, oldHealth - damage);

            if (oldHealth != newHealth)
            {
                _currentHealth.Value = newHealth;
                
                // 1. Publish damage event for UI visualization (damage popups, etc).
                // Host can publish locally in-process; remote clients receive the mirrored RPC below.
                if (IsClientInitialized)
                    PublishDamageEventLocal(damageInfo, newHealth <= 0, newHealth);
                PublishDamageEventObserversRpc(damageInfo, newHealth <= 0, newHealth, Time.time);
                
                // 2. Notify clients about the hit (for particles/sounds)
                RpcOnHit(damageInfo);

                if (newHealth <= 0)
                {
                    StartHandleDeath(damageInfo);
                }
                else
                {
                    OnDamaged?.Invoke(damageInfo);
                }
            }
        }

        [Server]
        private void PublishDamageEventLocal(DamageInfo damageInfo, bool isFatal, int remainingHealth, float eventTime = -1f)
        {
            // TODO currently null damageEventModel is silently ignored, but we should consider adding a fallback (eg direct event) to ensure damage events are always published for UI and effects. For now we log a warning to catch missing references.
            if (damageEventModel == null)
            {
                // Debug.LogWarning($"[{nameof(NetworkHealth)}] DamageEventModel is not assigned on '{gameObject.name}'. Damage events will not be published.", gameObject);
                // Debug.Log($"[{nameof(NetworkHealth)}] DamageInfo: Amount={damageInfo.Amount}, Type={damageInfo.Type}, InstigatorId={damageInfo.InstigatorId}", gameObject);
                return;
            }

            var damageEvent = new DamageEventData
            {
                DamageInfo = damageInfo,
                VictimName = _victimName,
                DamagePosition = transform.position,
                IsFatal = isFatal,
                VictimRemainingHealth = remainingHealth,
                EventTime = eventTime >= 0f ? eventTime : Time.time
            };

            damageEventModel.PublishDamageEvent(damageEvent);
        }

        [ObserversRpc(ExcludeServer = true)]
        private void PublishDamageEventObserversRpc(DamageInfo damageInfo, bool isFatal, int remainingHealth, float eventTime)
        {
            PublishDamageEventLocal(damageInfo, isFatal, remainingHealth, eventTime);
        }

        [Server]
        void StartHandleDeath(DamageInfo damageInfo)
        {
            var observersSnapshot = new List<INetworkHealthServerDeathObserver>(_serverDeathObservers);
            foreach (var observer in observersSnapshot)
            {
                Debug.Log($"[{nameof(NetworkHealth)}] Notifying server death observer {observer.GetType().Name} of death on '{gameObject.name}'", gameObject);
                observer.OnNetworkHealthServerDied(this, damageInfo);
            }
            DeathLogBroadcaster.Instance.ServerPublishDeath(this, damageInfo);

            bool waitForOwnershipTransfer = objectToDespawn.OwnerId != -1;
            objectToDespawn.GiveOwnership(null);
            if (objectToSpawn) Spawn(Instantiate(objectToSpawn, transform.position, transform.rotation));
            if (waitForOwnershipTransfer) StartCoroutine(HandleDeathCoroutine());
            else objectToDespawn.Despawn();
        }

        private IEnumerator HandleDeathCoroutine()
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

        [ObserversRpc(ExcludeServer = true)]
        private void RpcOnHit(DamageInfo info)
        {
            OnDamaged?.Invoke(info);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only helper to resolve the local connection's ClientId ("real user") when running as host/client in-editor.<br/>
        /// Returns -1 when no local client connection is available.
        /// </summary>
        private int GetLocalClientId()
        {
            var networkManager = InstanceFinder.NetworkManager;
            if (networkManager != null && networkManager.ClientManager != null && networkManager.ClientManager.Connection != null)
            {
                return networkManager.ClientManager.Connection.ClientId;
            }

            // Fallback: return -1 if no local client connection is available.
            return -1;
        }

        public void EditorTriggerDeath()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[{nameof(NetworkHealth)}] EditorTriggerDeath() only works in Play Mode.", gameObject);
                return;
            }

            if (!IsServerInitialized)
            {
                Debug.LogWarning($"[{nameof(NetworkHealth)}] EditorTriggerDeath() requires a server instance.", gameObject);
                return;
            }

            if (!IsAlive)
                return;

            int amount = Mathf.Max(1, _currentHealth.Value);
            int localClientId = GetLocalClientId();
            
            var info = new DamageInfo
            {
                Amount = amount,
                Type = DamageType.Environment,
                Point = transform.position,
                Normal = Vector3.up,
                InstigatorId = localClientId,
                Source = new DamageSource
                {
                    AttackerName = "Editor",
                    AttackerAvatarUrl = string.Empty,
                    SourcePosition = transform.position,
                    WeaponIconKey = string.Empty,
                }
            };

            TryConsume(info);
        }
#endif
    }
}
