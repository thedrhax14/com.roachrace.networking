namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Server-side observer for inventory item delta application ("transaction") events.<br/>
    ///<br/>
    /// Typical usage:<br/>
    /// - Gameplay systems (health/death, status effects, quests) subscribe to a player's <c>NetworkPlayerInventory</c> to react to authoritative item balance changes without relying on C# events/delegates.<br/>
    /// - Intended to carry instigator context (eg attacker NetworkObjectId) so downstream systems can attribute effects.<br/>
    /// </summary>
    public interface INetworkPlayerInventoryDeltaObserver
    {
        /// <summary>
        /// Called on the server after the inventory has applied a delta for typically one observed item.<br/>
        /// Delta sign convention: negative = consumed, positive = added.
        /// </summary>
        /// <param name="inventory">The inventory which applied the delta.</param>
        /// <param name="appliedDelta">The delta actually applied (may be smaller magnitude than requested).</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon key to attribute the delta (eg killfeed icon key). Empty when not applicable.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection (real user), or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object (combat attribution), or -1 for environment/unknown.</param>
        void OnServerInventoryItemDeltaApplied(NetworkPlayerInventory inventory, int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId);
    }
}
