using UnityEngine;

namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Packed server-side inventory delta context used by <see cref="INetworkPlayerInventoryDeltaObserver"/> callbacks.<br/>
    /// Typical usage: pass this by <c>in</c> from <see cref="NetworkPlayerInventory"/> to observers so item id, delta magnitude, attribution, and hit positions stay together in one value object.<br/>
    /// Configuration/context: delta sign convention is negative = consumed, positive = added.
    /// </summary>
    public readonly struct NetworkPlayerInventoryDeltaContext
    {
        /// <summary>
        /// Creates a packed inventory delta context for a single server-side item transaction.<br/>
        /// Typical usage: constructed by <see cref="NetworkPlayerInventory"/> immediately before notifying observers.<br/>
        /// Configuration/context: <paramref name="itemId"/> identifies the item affected by the transaction and <paramref name="appliedDelta"/> stores the signed amount actually applied.
        /// </summary>
        /// <param name="inventory">The inventory which applied the delta.</param>
        /// <param name="itemId">ItemDefinition id affected by the transaction.</param>
        /// <param name="appliedDelta">The delta actually applied (may be smaller magnitude than requested).</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon key to attribute the delta (eg killfeed icon key). Empty when not applicable.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection (real user), or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object (combat attribution), or -1 for environment/unknown.</param>
        /// <param name="hasSourceWorldPosition">Whether a world-space source position was supplied for this delta.</param>
        /// <param name="sourceWorldPosition">World-space origin of the effect or damage source when <paramref name="hasSourceWorldPosition"/> is true.</param>
        /// <param name="hasTargetWorldPosition">Whether a world-space target hit point was supplied for this delta.</param>
        /// <param name="targetWorldPosition">World-space hit point on the damaged target when <paramref name="hasTargetWorldPosition"/> is true.</param>
        public NetworkPlayerInventoryDeltaContext(NetworkPlayerInventory inventory, ushort itemId, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId, bool hasSourceWorldPosition, Vector3 sourceWorldPosition, bool hasTargetWorldPosition, Vector3 targetWorldPosition)
        {
            Inventory = inventory;
            ItemId = itemId;
            AppliedDelta = appliedDelta;
            WeaponIconKey = weaponIconKey;
            InstigatorConnectionId = instigatorConnectionId;
            InstigatorObjectId = instigatorObjectId;
            HasSourceWorldPosition = hasSourceWorldPosition;
            SourceWorldPosition = sourceWorldPosition;
            HasTargetWorldPosition = hasTargetWorldPosition;
            TargetWorldPosition = targetWorldPosition;
        }

        /// <summary>Gets the inventory which applied the delta.</summary>
        public NetworkPlayerInventory Inventory { get; }

        /// <summary>Gets the item id affected by the transaction.</summary>
        public ushort ItemId { get; }

        /// <summary>Gets the signed delta actually applied to the item.</summary>
        public int AppliedDelta { get; }

        /// <summary>Gets the optional UI-facing weapon/effect key used for attribution.</summary>
        public string WeaponIconKey { get; }

        /// <summary>Gets the client id of the instigator connection, or -1 for environment/unknown.</summary>
        public int InstigatorConnectionId { get; }

        /// <summary>Gets the network object id of the instigator object, or -1 for environment/unknown.</summary>
        public int InstigatorObjectId { get; }

        /// <summary>Gets whether a world-space source position was supplied for this delta.</summary>
        public bool HasSourceWorldPosition { get; }

        /// <summary>Gets the world-space origin when <see cref="HasSourceWorldPosition"/> is true.</summary>
        public Vector3 SourceWorldPosition { get; }

        /// <summary>Gets whether a world-space target hit point was supplied for this delta.</summary>
        public bool HasTargetWorldPosition { get; }

        /// <summary>Gets the world-space hit point when <see cref="HasTargetWorldPosition"/> is true.</summary>
        public Vector3 TargetWorldPosition { get; }
    }
}