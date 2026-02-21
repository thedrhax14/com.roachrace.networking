using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Controls;
using RoachRace.Data;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Core component for anything that can take damage.
    /// Handles server-side health state and client-side effect broadcasting.
    /// </summary>
    public class NetworkHealth : PlayerResource, IDamageable
    {
        [Header("Health Settings")]
        [FormerlySerializedAs("maxHealth")]
        [SerializeField] private int initialMaxHealth = 100;

        // SyncVars ensure all clients know the current/max health.
        private readonly SyncVar<int> _maxHealth = new(100);
        private readonly SyncVar<int> _currentHealth = new(100);
        [Header("Death/Despawn Settings")]
        NetworkObject objectToDespawn;
        [Tooltip("Optional NetworkObject to spawn on death (e.g., explosion effect).")]
        public NetworkObject objectToSpawn;
        [Header("Events")]
        // Events for UI, Effects, and Logic
        public UnityEvent<DamageInfo> OnDamaged; // Called on Server and Client (via RPC)
        public UnityEvent<int, int> OnHealthChanged; // current, max
        
        [Header("Dependencies")]
        [SerializeField] private DamageEventModel damageEventModel;
        
        private string _victimName;

        public int CurrentHealth => _currentHealth.Value;
        public int MaxHealth => _maxHealth.Value;

        public override float Current => _currentHealth.Value;
        public override float Max => _maxHealth.Value;
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

        public override void OnStartClient()
        {
            base.OnStartClient();
            _currentHealth.OnChange += OnHealthSyncChanged;
            _maxHealth.OnChange += OnMaxHealthSyncChanged;
            // Initial update for UI or listeners
            OnHealthChanged?.Invoke(_currentHealth.Value, _maxHealth.Value);
            NotifyChanged();
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
            NotifyChanged();
        }

        private void OnMaxHealthSyncChanged(int prev, int next, bool asServer)
        {
            OnHealthChanged?.Invoke(_currentHealth.Value, next);
            NotifyChanged();
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
                
                // 1. Publish damage event for UI visualization (damage popups, etc)
                PublishDamageEvent(damageInfo, newHealth <= 0, newHealth);
                
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
        private void PublishDamageEvent(DamageInfo damageInfo, bool isFatal, int remainingHealth)
        {
            if (damageEventModel == null)
            {
                Debug.LogWarning($"[{nameof(NetworkHealth)}] DamageEventModel is not assigned on '{gameObject.name}'. Damage events will not be published.");
                return;
            }

            var damageEvent = new DamageEventData
            {
                DamageInfo = damageInfo,
                VictimName = _victimName,
                DamagePosition = transform.position,
                IsFatal = isFatal,
                VictimRemainingHealth = remainingHealth,
                EventTime = Time.time
            };

            damageEventModel.PublishDamageEvent(damageEvent);
        }

        [Server]
        void StartHandleDeath(DamageInfo damageInfo)
        {
            DeathLogBroadcaster.Instance?.ServerPublishDeath(this, damageInfo);

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

        [Server]
        public override bool TryConsume(float amount)
        {
            // Health is not typically consumed as a cost.
            return false;
        }

        [Server]
        public override void Add(float amount)
        {
            Heal(Mathf.CeilToInt(amount));
        }

        [ObserversRpc(ExcludeServer = true)]
        private void RpcOnHit(DamageInfo info)
        {
            OnDamaged?.Invoke(info);
        }

#if UNITY_EDITOR
        private int GetLocalPlayerObjectId()
        {
            var networkManager = InstanceFinder.NetworkManager;
            if (networkManager != null && networkManager.ClientManager != null && networkManager.ClientManager.Connection != null)
            {
                HashSet<NetworkObject> ownedObjects = networkManager.ClientManager.Connection.Objects;
                if (ownedObjects != null && ownedObjects.Count > 0)
                {
                    // Return the first owned object's ID (typically the player)
                    return ownedObjects.First().ObjectId;
                }
            }

            // Fallback: return -1 if no owned objects found
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
            int localPlayerId = GetLocalPlayerObjectId();
            
            var info = new DamageInfo
            {
                Amount = amount,
                Type = DamageType.Environment,
                Point = transform.position,
                Normal = Vector3.up,
                InstigatorId = localPlayerId,
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
