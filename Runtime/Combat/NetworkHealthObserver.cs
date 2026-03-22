using System;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.Interaction;
using RoachRace.Networking.Extensions;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Bridges inventory “health-asset” deltas into <see cref="NetworkHealth"/> combat semantics on the server.<br/>
    ///<br/>
    /// Typical usage:<br/>
    /// - Attach this to the same player GameObject that has <see cref="NetworkPlayerInventory"/> and <see cref="NetworkHealth"/>.<br/>
    /// - Assign <see cref="healthAsset"/> to the ItemDefinition that represents health units.<br/>
    /// - Any server-authoritative inventory deltas for that item id will be translated into damage/heal calls on <see cref="NetworkHealth"/> (preserving death flow and attribution for damage via <see cref="DamageInfo"/>).<br/>
    ///<br/>
    /// Design intent:<br/>
    /// - Status effects (and other gameplay systems) operate on inventory transactions, not bespoke health-meter code.<br/>
    /// - This component localizes the “health is special” mapping to one place, rather than spreading it across effect implementations.
    /// </summary>
    public sealed class NetworkHealthObserver : NetworkBehaviour, INetworkPlayerInventoryDeltaObserver
    {
        [Header("Health Asset")]
        [SerializeField]
        [Tooltip("ItemDefinition id used as the key for health units in inventory.")]
        private ItemDefinition healthAsset;

        NetworkPlayerInventory inventory;
        NetworkHealth networkHealth;

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (!TryGetComponent(out inventory))
            {
                Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': inventory.", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': inventory.");
            }

            if (!TryGetComponent(out networkHealth))
            {
                networkHealth = GetComponentInChildren<NetworkHealth>();
                if (networkHealth == null)
                {
                    Debug.LogError($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': networkHealth.", gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkHealthObserver)}] Missing required reference on '{gameObject.name}': networkHealth.");
                }
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

        public void OnServerInventoryItemDeltaApplied(NetworkPlayerInventory inventory, ushort itemId, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            if (healthAsset == null)
                return;

            if (itemId != healthAsset.id)
                return;

            if (appliedDelta == 0)
                return;

            if (appliedDelta > 0)
            {
                networkHealth.Heal(appliedDelta);
                return;
            }

            var damageAmount = -appliedDelta;

            DamageSource source = new DamageSource
            {
                AttackerName = string.Empty,
                AttackerAvatarUrl = string.Empty,
                SourcePosition = transform.position,
                WeaponIconKey = weaponIconKey
            };

            DamageInfo damageInfo = NetworkExtensions.CreateDamageInfo(
                instigatorConnectionId,
                damageAmount,
                transform.position,
                Vector3.up,
                source);

            networkHealth.TryConsume(damageInfo);
        }
    }
}
