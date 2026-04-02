using System;
using System.Collections;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Observes authoritative stamina inventory totals and mirrors them into the owning client's player stats model.<br/>
    /// Typical usage: attach to the player object beside <see cref="NetworkPlayerInventory"/> and <see cref="ServerStaminaController"/> so stamina changes flow from server inventory to owner-local UI state without a separate client bridge.<br/>
    /// Configuration/context: the server derives stamina from the configured <see cref="staminaAsset"/>, while only the owning client writes to <see cref="playerStatsModel"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkStaminaObserver : NetworkBehaviour, INetworkPlayerInventoryDeltaObserver
    {
        [Header("Stamina Asset")]
        [SerializeField]
        [Tooltip("ItemDefinition id used as the stamina resource key in inventory.")]
        private ItemDefinition staminaAsset;

        [Header("UI")]
        [SerializeField]
        [Tooltip("Owner-local PlayerStatsModel to update when stamina changes.")]
        private PlayerStatsModel playerStatsModel;

        private NetworkPlayerInventory inventory;
        private readonly SyncVar<int> _currentStamina = new(0);
        private readonly SyncVar<int> _maxStamina = new(0);
        private bool _serverSubscribed;
        private bool _initialized;

        /// <summary>
        /// Gets the configured inventory item id used as the stamina resource key.<br/>
        /// Typical usage: shared by server-side stamina drain logic so both the observer and controller target the same inventory item.<br/>
        /// Configuration/context: returns 0 when <see cref="staminaAsset"/> is not assigned.
        /// </summary>
        public ushort StaminaItemId => staminaAsset != null ? staminaAsset.id : (ushort)0;

        /// <summary>
        /// Gets the current authoritative stamina snapshot mirrored from inventory.<br/>
        /// Typical usage: read by presentation or movement code that needs to know whether the player still has stamina remaining.<br/>
        /// Configuration/context: this value is synchronized from the server and is not a local prediction buffer.
        /// </summary>
        public int CurrentStamina => _currentStamina.Value;

        /// <summary>
        /// Gets the current stamina cap mirrored to the owning client.<br/>
        /// Typical usage: feed the maximum value into <see cref="PlayerStatsModel.SetStamina(float, float)"/> so the UI can render a current/max pair.<br/>
        /// Configuration/context: this value is treated as the baseline cap for recovery and only grows if the inventory gains more stamina later.
        /// </summary>
        public int MaxStamina => _maxStamina.Value;

        /// <summary>
        /// Gets whether the authoritative stamina snapshot is still above zero.<br/>
        /// Typical usage: movement code can use this as a lightweight gate for sprint or other stamina-locked actions.<br/>
        /// Configuration/context: returns true when current stamina is greater than zero.
        /// </summary>
        public bool HasStamina => CurrentStamina > 0;

        /// <summary>
        /// Gets whether the observer has completed its server-side inventory bootstrap.<br/>
        /// Typical usage: dependent server components can delay stamina consumption until the initial inventory snapshot has been established.<br/>
        /// Configuration/context: becomes true after the server has subscribed to inventory deltas and seeded current/max stamina.
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Validates required dependencies before the stamina observer starts network lifecycle processing.<br/>
        /// Typical usage: Unity invokes this during object initialization; the component fails fast if the inventory dependency is missing.<br/>
        /// Configuration/context: the owner-local <see cref="playerStatsModel"/> is validated later because only the owning client needs it.
        /// </summary>
        private void Awake()
        {
            if (staminaAsset == null)
            {
                Debug.LogError($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': staminaAsset.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': staminaAsset.");
            }
            inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (inventory == null)
            {
                Debug.LogError($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': inventory.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': inventory.");
            }
        }

        /// <summary>
        /// Starts the server-side bootstrap that seeds the stamina snapshot from inventory and subscribes to delta notifications.<br/>
        /// Typical usage: FishNet invokes this when the object is spawned on the server; the observer waits for inventory readiness before subscribing.<br/>
        /// Server/client constraints: server-only lifecycle hook.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            _serverSubscribed = false;
            _initialized = false;
            StartCoroutine(InitializeServerStamina());
        }

        /// <summary>
        /// Unsubscribes from server inventory deltas when the object stops on the server.<br/>
        /// Typical usage: FishNet invokes this during teardown to avoid dangling server observers.<br/>
        /// Server/client constraints: server-only lifecycle hook.
        /// </summary>
        public override void OnStopServer()
        {
            if (_serverSubscribed && inventory != null)
                inventory.RemoveServerDeltaObserver(this);

            _serverSubscribed = false;
            _initialized = false;

            base.OnStopServer();
        }

        /// <summary>
        /// Subscribes the owning client to stamina snapshot changes and pushes the initial current/max values into the player stats model.<br/>
        /// Typical usage: FishNet invokes this on all clients; only the owner binds UI state because the player stats model is owner-local.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            _currentStamina.OnChange += CurrentStamina_OnChange;
            _maxStamina.OnChange += MaxStamina_OnChange;

            if (IsOwner)
            {
                if (playerStatsModel == null)
                {
                    Debug.LogError($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': playerStatsModel.", gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': playerStatsModel.");
                }

                PushStaminaToPlayerStatsModel();
            }
        }

        /// <summary>
        /// Unsubscribes the client-side SyncVar listeners when the object stops on a client.<br/>
        /// Typical usage: FishNet invokes this during teardown or ownership changes to avoid stale callbacks.<br/>
        /// Server/client constraints: client-side lifecycle hook.
        /// </summary>
        public override void OnStopClient()
        {
            _currentStamina.OnChange -= CurrentStamina_OnChange;
            _maxStamina.OnChange -= MaxStamina_OnChange;

            base.OnStopClient();
        }

        /// <summary>
        /// Recalculates stamina after any authoritative inventory delta and republishes the current snapshot to clients when needed.<br/>
        /// Typical usage: the inventory invokes this after any item transaction; the observer re-reads the configured stamina item total and keeps UI state in sync.
        /// </summary>
        /// <param name="delta">Packed inventory delta context for the applied transaction.</param>
        public void OnServerInventoryItemDeltaApplied(in NetworkPlayerInventoryDeltaContext delta)
        {
            if (!_serverSubscribed || delta.Inventory == null || staminaAsset == null)
                return;

            if (delta.ItemId != staminaAsset.id)
                return;

            RefreshServerSnapshot();
        }

        /// <summary>
        /// Seeds the stamina snapshot from inventory on the server and subscribes to later authoritative deltas.<br/>
        /// Typical usage: internal bootstrap called from <see cref="OnStartServer"/> after FishNet spawns the object.<br/>
        /// Configuration/context: waits until the inventory reports ready so the initial loadout is reflected before stamina is observed.
        /// </summary>
        private IEnumerator InitializeServerStamina()
        {
            while (inventory != null && !inventory.InventoryReady)
                yield return null;

            if (!IsServerInitialized)
                yield break;

            if (inventory == null)
            {
                Debug.LogError($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': inventory.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': inventory.");
            }

            if (staminaAsset == null)
            {
                Debug.LogError($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': staminaAsset.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkStaminaObserver)}] Missing required reference on '{gameObject.name}': staminaAsset.");
            }

            if (!_serverSubscribed)
            {
                inventory.AddServerDeltaObserver(this);
                _serverSubscribed = true;
            }

            RefreshServerSnapshot();
        }

        /// <summary>
        /// Rebuilds the authoritative stamina snapshot from inventory and publishes it into the synchronized values.<br/>
        /// Typical usage: called after server bootstrap and after every inventory delta to keep the current/max snapshot current.
        /// </summary>
        private void RefreshServerSnapshot()
        {
            if (!IsServerInitialized || inventory == null || staminaAsset == null)
                return;

            int current = inventory.GetTotalCountByItemId(staminaAsset.id);
            _currentStamina.Value = current;
            if(_maxStamina.Value <= 0)
                _maxStamina.Value = current;
            _initialized = true;
        }

        /// <summary>
        /// Pushes the synchronized stamina snapshot into the owner-local player stats model.<br/>
        /// Typical usage: called when the owner client receives stamina updates so the HUD can refresh immediately.<br/>
        /// Configuration/context: ignored on non-owners because their player stats model should not be touched by remote players.
        /// </summary>
        private void PushStaminaToPlayerStatsModel()
        {
            if (!IsOwner || playerStatsModel == null)
                return;

            playerStatsModel.SetStamina(_currentStamina.Value, _maxStamina.Value);
        }

        /// <summary>
        /// Forwards current stamina changes to the owner-local player stats model.<br/>
        /// Typical usage: wired to the synchronized stamina value so the HUD stays in sync when the server consumes or restores stamina.
        /// </summary>
        /// <param name="previous">Previous synchronized stamina value.</param>
        /// <param name="next">New synchronized stamina value.</param>
        /// <param name="asServer">True when invoked on the server side of the sync callback.</param>
        private void CurrentStamina_OnChange(int previous, int next, bool asServer)
        {
            PushStaminaToPlayerStatsModel();
        }

        /// <summary>
        /// Forwards current max stamina changes to the owner-local player stats model.<br/>
        /// Typical usage: wired to the synchronized stamina cap so the HUD reflects capacity increases as well as consumption.
        /// </summary>
        /// <param name="previous">Previous synchronized max stamina value.</param>
        /// <param name="next">New synchronized max stamina value.</param>
        /// <param name="asServer">True when invoked on the server side of the sync callback.</param>
        private void MaxStamina_OnChange(int previous, int next, bool asServer)
        {
            PushStaminaToPlayerStatsModel();
        }
    }
}