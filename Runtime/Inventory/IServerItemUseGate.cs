using RoachRace.Interaction;

namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Optional server-only gating hook for items used through <see cref="NetworkPlayerInventory"/>.
    ///
    /// Purpose:
    /// - Prevent the server from calling <see cref="IRoachRaceItem.UseStart"/> and broadcasting it to observers
    ///   when the action should be rejected (eg, weapon has no ammo, item is locked, reloading).
    ///
    /// Notes:
    /// - Implement on the item component itself (eg, WeaponPropItemAdapter) so inventory can query it
    ///   without searching other components.
    /// - This is server-authoritative: it is only evaluated on the server.
    /// </summary>
    public interface IServerItemUseGate
    {
        /// <summary>
        /// Called on the server before <see cref="IRoachRaceItem.UseStart"/>.
        /// Return false to reject the use request.
        /// </summary>
        bool CanStartUse(NetworkPlayerInventory inventory, int slotIndex, out ItemUseFailReason failReason);
    }
}
