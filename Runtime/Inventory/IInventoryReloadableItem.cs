namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Optional presentation hook for items which support a reload action.
    ///
    /// Notes:
    /// - This is a visual/presentation call only (animations/SFX). Gameplay state is handled server-side
    ///   by systems like <see cref="RoachRace.Networking.Weapons.NetworkWeaponMagazine"/>.
    /// - Must be safe to run on both server and clients.
    /// </summary>
    public interface IInventoryReloadableItem
    {
        void PlayReloadVisuals();
    }
}
