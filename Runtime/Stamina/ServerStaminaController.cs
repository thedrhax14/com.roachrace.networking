using System;
using FishNet.Object;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Server-side stamina drain and run-intent controller.<br/>
    /// Typical usage: attach beside <see cref="NetworkPlayerInventory"/> and <see cref="NetworkStaminaObserver"/> so the server can accept owner run intent, spend stamina each physics tick, and shut off running when stamina is exhausted.<br/>
    /// Configuration/context: this component does not move the player directly; it only consumes the configured stamina item and exposes whether stamina is currently available for running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServerStaminaController : NetworkBehaviour
    {
        [Header("Drain")]
        [SerializeField, Min(0f)]
        [Tooltip("Stamina units consumed per second while run intent is active and stamina remains available.")]
        private float staminaDrainPerSecond = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("Stamina units recovered per second while run intent is inactive and stamina is below the observed maximum.")]
        private float staminaRecoveryPerSecond = 5f;

        private NetworkPlayerInventory inventory;
        private NetworkStaminaObserver staminaObserver;
        private bool _runRequested;
        private float _staminaDebt;
        private float _staminaRecoveryDebt;

        /// <summary>
        /// Gets whether the server currently considers running available.<br/>
        /// Typical usage: local movement code can read this to decide whether sprint blend is allowed.<br/>
        /// Configuration/context: returns true while the observed stamina snapshot is above zero, or while the observer has not finished initializing.
        /// </summary>
        public bool CanRun => staminaObserver == null || !staminaObserver.IsInitialized || staminaObserver.HasStamina;

        /// <summary>
        /// Validates required stamina dependencies before the controller begins processing input and drain ticks.<br/>
        /// Typical usage: Unity invokes this during object initialization so the component can fail fast if the stamina plumbing is incomplete.<br/>
        /// Configuration/context: both the inventory and the stamina observer are required because this controller spends inventory-backed stamina.
        /// </summary>
        private void Awake()
        {
            inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (inventory == null)
            {
                Debug.LogError($"[{nameof(ServerStaminaController)}] Missing required reference on '{gameObject.name}': inventory.", gameObject);
                throw new InvalidOperationException($"[{nameof(ServerStaminaController)}] Missing required reference on '{gameObject.name}': inventory.");
            }
            staminaObserver = transform.root.GetComponentInChildren<NetworkStaminaObserver>();
            if (staminaObserver == null)
            {
                Debug.LogError($"[{nameof(ServerStaminaController)}] Missing required reference on '{gameObject.name}': staminaObserver.", gameObject);
                throw new InvalidOperationException($"[{nameof(ServerStaminaController)}] Missing required reference on '{gameObject.name}': staminaObserver.");
            }
        }

        /// <summary>
        /// Resets run intent and any leftover drain debt when the controller starts on the server.<br/>
        /// Typical usage: FishNet invokes this when the NetworkObject becomes active on the server.<br/>
        /// Server/client constraints: server-only lifecycle hook.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            _runRequested = false;
            _staminaDebt = 0f;
            _staminaRecoveryDebt = 0f;
        }

        /// <summary>
        /// Clears run intent and drain debt when the server stops this object.<br/>
        /// Typical usage: FishNet invokes this during teardown to prevent stale state from leaking into a respawned controller.<br/>
        /// Server/client constraints: server-only lifecycle hook.
        /// </summary>
        public override void OnStopServer()
        {
            _runRequested = false;
            _staminaDebt = 0f;
            _staminaRecoveryDebt = 0f;

            base.OnStopServer();
        }

        /// <summary>
        /// Receives owner run intent on the server.<br/>
        /// Typical usage: called by the owning client's movement input when the run key is pressed or released.<br/>
        /// Server/client constraints: server RPC; the owning client may invoke this to mirror local run intent to the server.
        /// </summary>
        /// <param name="isRunning">True when the player is attempting to run, false when the run input is released.</param>
        [ServerRpc(RequireOwnership = true)]
        public void SetRunningRequestedServerRpc(bool isRunning)
        {
            _runRequested = isRunning;

            if (!isRunning)
                _staminaDebt = 0f;
            else
                _staminaRecoveryDebt = 0f;
        }

        /// <summary>
        /// Applies stamina drain while run intent is active and stamina recovery while run intent is inactive.<br/>
        /// Typical usage: runs once per physics tick on the server so stamina spending and recovery stay aligned with movement simulation.<br/>
        /// Configuration/context: drain and recovery are both based on run intent, not raw measured speed, so the controller stays stable if movement is later affected by external forces.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsServerInitialized || inventory == null || staminaObserver == null)
                return;

            if (!_runRequested)
            {
                _staminaDebt = 0f;
                RecoverStamina();
                return;
            }

            _staminaRecoveryDebt = 0f;

            if (!staminaObserver.IsInitialized || !staminaObserver.HasStamina)
                return;

            _staminaDebt += staminaDrainPerSecond * Time.fixedDeltaTime;
            int staminaToConsume = Mathf.FloorToInt(_staminaDebt);
            if (staminaToConsume <= 0)
                return;

            int consumed = inventory.ConsumeByItemId(staminaObserver.StaminaItemId, staminaToConsume);
            if (consumed <= 0)
                return;

            _staminaDebt -= consumed;
            if (_staminaDebt < 0f)
                _staminaDebt = 0f;
        }

        /// <summary>
        /// Restores stamina back toward the observed maximum while the player is not running.<br/>
        /// Typical usage: called from <see cref="FixedUpdate"/> when run intent is inactive so the controller can gradually refill the inventory-backed stamina resource.
        /// </summary>
        private void RecoverStamina()
        {
            if (!staminaObserver.IsInitialized)
                return;

            int maxStamina = staminaObserver.MaxStamina;
            int currentStamina = staminaObserver.CurrentStamina;
            if (maxStamina <= 0 || currentStamina >= maxStamina)
            {
                _staminaRecoveryDebt = 0f;
                return;
            }

            _staminaRecoveryDebt += staminaRecoveryPerSecond * Time.fixedDeltaTime;
            int staminaToRecover = Mathf.FloorToInt(_staminaRecoveryDebt);
            if (staminaToRecover <= 0)
                return;

            int recoverable = Mathf.Min(staminaToRecover, maxStamina - currentStamina);
            int added = inventory.AddItemUpTo(staminaObserver.StaminaItemId, recoverable);
            if (added <= 0)
                return;

            _staminaRecoveryDebt -= added;
            if (_staminaRecoveryDebt < 0f)
                _staminaRecoveryDebt = 0f;
        }
    }
}