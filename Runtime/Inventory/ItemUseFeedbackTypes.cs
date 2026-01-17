namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Coarse, network-friendly reason codes for why an item use was rejected by the server.<br/>
    ///<br/>
    /// Notes:<br/>
    /// - Keep this stable over time; UI can map reasons to localized strings.<br/>
    /// - Avoid leaking sensitive info to non-owners; owner-only feedback should use TargetRpc.<br/>
    /// </summary>
    public enum ItemUseFailReason : byte
    {
        None = 0,

        /// <summary>Selected slot index was invalid/out of range.</summary>
        InvalidSlotIndex,

        /// <summary>Client is not the owner, or ownership requirements failed.</summary>
        NotOwner,

        /// <summary>Item id was 0 or otherwise invalid.</summary>
        InvalidItemId,

        /// <summary>Selected slot was invalid or empty.</summary>
        EmptySlot,

        /// <summary>Item is not present in inventory slots (or has 0 count).</summary>
        NotInInventory,

        /// <summary>ItemRegistry was missing or the expected RoachRaceItemComponent was not found.</summary>
        MissingItemComponent,

        /// <summary>Item cannot be used right now due to generic gating (stunned/dead/locked/etc.).</summary>
        NotUsableNow,

        /// <summary>Item requires aim data but none was supplied.</summary>
        RequiresAimData,

        /// <summary>Item needs a valid target but target acquisition failed.</summary>
        InvalidTarget,

        /// <summary>Server rejected use for unspecified reasons (fallback).</summary>
        ServerRejected,
    }

    /// <summary>
    /// Payload sent from server to the owning client to explain a failed item use.<br/>
    ///<br/>
    /// Typical usage:<br/>
    /// - Sent via TargetRpc (owner only) so UI can show a toast/prompt on the correct slot.<br/>
    /// - <see cref="SlotIndex"/> is included so HUD can highlight the action prompt/inventory slot.<br/>
    /// </summary>
    public readonly struct ItemUseFailure
    {
        /// <summary>
        /// The item definition id that was attempted.
        /// </summary>
        public readonly ushort ItemId;

        /// <summary>
        /// The inventory slot index associated with the attempted use.
        /// - For "use selected" this should be the selected slot.<br/>
        /// - For "use by item id" this should be the slot that contained the item.<br/>
        /// - Use -1 when no slot can be determined.<br/>
        /// </summary>
        public readonly int SlotIndex;

        /// <summary>
        /// The reason the use failed.
        /// </summary>
        public readonly ItemUseFailReason Reason;

        public ItemUseFailure(ushort itemId, int slotIndex, ItemUseFailReason reason)
        {
            ItemId = itemId;
            SlotIndex = slotIndex;
            Reason = reason;
        }
    }
}
