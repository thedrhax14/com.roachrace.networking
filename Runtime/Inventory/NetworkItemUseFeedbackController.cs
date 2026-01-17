using System;
using FishNet.Connection;
using FishNet.Object;

namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Owner-facing feedback channel for item use requests.<br/>
    ///<br/>
    /// Intended placement:<br/>
    /// - Add this on (or under) the same GameObject hierarchy as <see cref="NetworkPlayerInventory"/>, on the same NetworkObject.<br/>
    ///<br/>
    /// Responsibilities:<br/>
    /// - Server: sends owner-only failure notifications via TargetRpc.<br/>
    /// - Client (owner): raises events so UI can display feedback (toast, slot highlight, action prompt status).<br/>
    ///<br/>
    /// Notes:<br/>
    /// - This is intentionally minimal; throttling, localization mapping, and UI-model routing can be added later.<br/>
    /// </summary>
    public sealed class NetworkItemUseFeedbackController : NetworkBehaviour
    {
        /// <summary>
        /// Raised on the owning client when the server rejects an item use.<br/>
        ///<br/>
        /// Typical usage:<br/>
        /// - Inventory UI listens and highlights <see cref="ItemUseFailure.SlotIndex"/>.<br/>
        /// - Action prompt UI listens and shows a reason-specific message.<br/>
        /// </summary>
        public event Action<ItemUseFailure> OnItemUseFailed;

        /// <summary>
        /// Server-only entry point to report a use failure to the owner.<br/>
        ///<br/>
        /// Typical behavior (planned/current):<br/>
        /// - Sends a TargetRpc to the current owner connection.<br/>
        ///<br/>
        /// TODO (future):<br/>
        /// - Add per-slot/per-reason throttling to prevent UI spam during hold-to-use.<br/>
        /// - Optionally emit a generic ObserversRpc for non-sensitive failure FX.<br/>
        /// </summary>
        [Server]
        public void ReportUseFailed(ushort itemId, int slotIndex, ItemUseFailReason reason)
        {
            // Keep this safe on server builds.
            if (Owner == null) return;

            ItemUseFailedTargetRpc(Owner, itemId, slotIndex, reason);
            ItemUseFailedObserversRpc(Owner.ClientId, itemId, slotIndex);
        }

        /// <summary>
        /// Owner-only RPC which delivers failure details.<br/>
        ///<br/>
        /// Current behavior:<br/>
        /// - Raises <see cref="OnItemUseFailed"/> for local UI.<br/>
        ///<br/>
        /// TODO (future):<br/>
        /// - Route into a ScriptableObject UI model for your action prompt slot(s).<br/>
        /// </summary>
        [TargetRpc]
        private void ItemUseFailedTargetRpc(NetworkConnection owner, ushort itemId, int slotIndex, ItemUseFailReason reason)
        {
            OnItemUseFailed?.Invoke(new ItemUseFailure(itemId, slotIndex, reason));
        }

        /// <summary>
        /// Optional observers-visible failure feedback.<br/>
        ///<br/>
        /// TODO (future):<br/>
        /// - If you want everyone to see a generic "use failed" animation/sfx, invoke this on server failure.<br/>
        /// - Do NOT send sensitive reason codes here.<br/>
        /// </summary>
        [ObserversRpc(ExcludeServer = true)]
        private void ItemUseFailedObserversRpc(int ownerId, ushort itemId, int slotIndex)
        {
            // Intentionally not implemented yet.
        }
    }
}
