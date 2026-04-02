using UnityEngine;

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
        /// Delta sign convention: negative = consumed, positive = added.<br/>
        /// Typical usage: inspect <paramref name="delta"/> to decide whether the observer should react to the item change, then use its item id and attribution fields to drive server-side gameplay or UI bridges.
        /// </summary>
        /// <param name="delta">Packed inventory delta context for the applied transaction.</param>
        void OnServerInventoryItemDeltaApplied(in NetworkPlayerInventoryDeltaContext delta);
    }
}
