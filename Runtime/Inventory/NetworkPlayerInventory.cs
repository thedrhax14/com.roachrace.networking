using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using RoachRace.Controls;
using RoachRace.Interaction;
using RoachRace.Networking.Input;
using RoachRace.UI.Models;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

using InventorySlotState = RoachRace.Data.InventorySlotState;

namespace RoachRace.Networking.Inventory
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerLookState), typeof(PlayerItemRegistry))]
    /// <summary>
    /// Network-authoritative inventory for a player.<br/>
    /// <br/>
    /// Scene/Prefab setup:<br/>
    /// - Add this to the same GameObject as the player's NetworkObject (and typically FishnetPlayerControllerOverride).<br/>
    /// - Ensure a PlayerItemRegistry exists on the player hierarchy (items are local child GameObjects).<br/>
    /// - Each child item should have an ItemInstance + IRoachRaceItem (e.g. HealItem/KeycardItem/CasPropItemAdapter/WeaponPropItemAdapter).<br/>
    /// - Assign an ItemDatabase (ScriptableObject) so UI can resolve itemId -> icon/flags.<br/>
    /// - Assign an InventoryModel (ScriptableObject) for local HUD rendering (required).<br/>
    /// <br/>
    /// Runtime behavior:<br/>
    /// - Server owns slot contents and selection.<br/>
    /// - Client requests selection/use via RPCs.<br/>
    /// - Selection toggles child item visibility via PlayerItemRegistry.<br/>
    /// </summary>
    public sealed class NetworkPlayerInventory : NetworkBehaviour, IPlayerInventory
    {
        [Header("Config")]
        [Tooltip("Number of inventory slots supported for this character. Survivors typically use 8, ghosts 6, monsters can use 8 (4 left + 4 right UI).")]
        [SerializeField, Range(1, 9)] private int slotCount = 9;

        [Header("Initial Items")]
        [Tooltip("Optional. Items granted by the server when this player spawns. Use this for reusable default loadouts. Note: item child objects are hidden unless their itemId exists in inventory slots and that slot is selected.")]
        [SerializeField] private InventoryLoadout initialLoadout;
        [SerializeField] private CharacterAnimationComponent _characterAnimationComponent;

        [Tooltip("If false (recommended), ItemDatabase and InventoryModel will be auto-assigned from InventoryGlobals. Enable only when you need per-prefab overrides.")]
        [SerializeField, HideInInspector] private bool overrideGlobalInventoryReferences;

        [Tooltip("Item database ScriptableObject used to resolve itemIds to definitions (icons, stack size, useOnSelect).")]
        [SerializeField] private ItemDatabase itemDatabase;
        [Tooltip("Registry mapping itemId -> child item component under this player. Typically placed on the player root or an items container.")]
        [SerializeField] private PlayerItemRegistry itemRegistry;
        [Tooltip("Required. The local owner client will push slot/selection updates into this UI model for HUD rendering.")]
        [SerializeField] private InventoryModel inventoryModel;
        [Tooltip("Optional. If assigned, server will notify owning client about item use failures (reason + slot index) so UI can show feedback.")]
        [SerializeField] private NetworkItemUseFeedbackController itemUseFeedback;
        [Tooltip("Required. Replicated look state used by aim-driven items so use RPCs do not need per-call aim payloads.")]
        [SerializeField] private NetworkPlayerLookState lookState;

        [Tooltip("Server-authoritative slot list. Do not modify on clients.")]
        public readonly SyncList<InventorySlotState> Slots = new();
        private readonly SyncVar<int> _selectedSlotIndex = new(0);
        private readonly SyncVar<bool> _inventoryReady = new(false);

        public int SlotCount => slotCount;
        public int SelectedSlotIndex => _selectedSlotIndex.Value;
        public bool InventoryReady => _inventoryReady.Value;

        private bool _uiModelDirty;
        private bool _characterAnimationInitialized = false;
        private bool _ownerFeedbackSubscribed;
        private bool _ownerSlotsSubscribed;

        private readonly HashSet<INetworkPlayerInventoryDeltaObserver> _serverDeltaObservers = new();

        /// <summary>
        /// Server-only: subscribes an observer to inventory item delta (transaction) notifications.<br/>
        /// Intended for gameplay systems which need attribution (instigator) and deterministic server-side execution.
        /// </summary>
        /// <param name="observer">Observer to register; duplicates are ignored.</param>
        [Server]
        public void AddServerDeltaObserver(INetworkPlayerInventoryDeltaObserver observer)
        {
            if (observer == null) return;
            _serverDeltaObservers.Add(observer);
        }

        /// <summary>
        /// Server-only: unsubscribes an observer from inventory item delta (transaction) notifications.
        /// </summary>
        /// <param name="observer">Observer to unregister.</param>
        [Server]
        public void RemoveServerDeltaObserver(INetworkPlayerInventoryDeltaObserver observer)
        {
            if (observer == null) return;
            _serverDeltaObservers.Remove(observer);
        }

        /// <summary>
        /// Server-only helper to report an item use failure to the owning client.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Delegates to <see cref="NetworkItemUseFeedbackController"/> when present.<br/>
        /// - Logs an error and returns when no feedback controller is available.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Used on the server when an item use request fails validation or execution.<br/>
        /// </summary>
        [Server]
        private void ReportUseFailed(ushort itemId, int slotIndex, ItemUseFailReason reason)
        {
            // Failure feedback is best-effort; log when the feedback controller is missing.
            if (itemUseFeedback == null) {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] Cannot report item use failure because itemUseFeedback is not assigned on '{gameObject.name}'.", gameObject);
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] Failed to use itemId {itemId} in slot {slotIndex} for reason '{reason}'.", gameObject);
                return;
            }
            itemUseFeedback.ReportUseFailed(itemId, slotIndex, reason);
        }

        /// <summary>
        /// Unity lifecycle hook.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Ensures <see cref="itemRegistry"/> is assigned by looking on the same GameObject.<br/>
        /// - Ensures <see cref="lookState"/> is assigned by looking on the same GameObject.<br/>
        /// - Attempts to locate <see cref="itemUseFeedback"/> in this GameObject's children when it is not explicitly assigned.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Runs on all peers (server and clients) because it's a Unity <c>MonoBehaviour</c> lifecycle method.<br/>
        /// </summary>
        private void Awake()
        {
            // PlayerItemRegistry is expected to live on the same GameObject as this component.
            if (itemRegistry == null)
                TryGetComponent(out itemRegistry);

            if (lookState == null)
                TryGetComponent(out lookState);

            // Auto-wire shared assets from InventoryGlobals when possible.
            if (InventoryGlobals.TryGet(out var globals) && globals != null)
            {
                if (!overrideGlobalInventoryReferences)
                {
                    if (globals.itemDatabase != null)
                        itemDatabase = globals.itemDatabase;
                    if (globals.inventoryModel != null)
                        inventoryModel = globals.inventoryModel;
                }
                else
                {
                    // Even when overriding, attempt to heal missing references from globals.
                    if (itemDatabase == null)
                        itemDatabase = globals.itemDatabase;
                    if (inventoryModel == null)
                        inventoryModel = globals.inventoryModel;
                }
            }

            if (itemRegistry == null)
            {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] PlayerItemRegistry is not assigned and was not found on '{gameObject.name}'. This component requires a PlayerItemRegistry on the same GameObject.", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(NetworkPlayerInventory)}] itemRegistry is null on GameObject '{gameObject.name}'. Add PlayerItemRegistry to the same GameObject.");
            }

            if (lookState == null)
            {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] NetworkPlayerLookState is not assigned and was not found on '{gameObject.name}'. This component requires a NetworkPlayerLookState on the same GameObject.", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(NetworkPlayerInventory)}] lookState is null on GameObject '{gameObject.name}'. Add NetworkPlayerLookState to the same GameObject.");
            }

            if (itemDatabase == null)
            {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] ItemDatabase is not assigned on '{gameObject.name}'. Assign it or configure a Resources-based {nameof(InventoryGlobals)} asset.", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(NetworkPlayerInventory)}] itemDatabase is null on GameObject '{gameObject.name}'. Assign an ItemDatabase or set up InventoryGlobals in Resources.");
            }

            if (inventoryModel == null)
            {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] InventoryModel is not assigned on '{gameObject.name}'. This component requires an InventoryModel for HUD rendering.", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(NetworkPlayerInventory)}] inventoryModel is null on GameObject '{gameObject.name}'. Assign an InventoryModel in the Inspector.");
            }

            // Best-effort auto-wire: feedback controller may live under this object.
            if (itemUseFeedback == null)
                itemUseFeedback = GetComponentInChildren<NetworkItemUseFeedbackController>(true);
        }

#if UNITY_EDITOR
        /// <summary>
    /// Unity editor validation hook.<br/>
    /// <br/>
    /// Typical behavior:<br/>
    /// - InventoryLoadout assets validate themselves (id syncing + minimum amounts).<br/>
    /// <br/>
    /// Expected context:<br/>
    /// - Editor-only; called when values change in the Inspector.<br/>
        /// </summary>
        protected override void OnValidate()
        {
            base.OnValidate();

            // Auto-wire references to reduce per-prefab setup and prevent lost references.
            if (itemRegistry == null)
                TryGetComponent(out itemRegistry);

            if (lookState == null)
                TryGetComponent(out lookState);

            // Prefer InventoryGlobals when present.
            if (InventoryGlobals.TryGet(out var globals) && globals != null)
            {
                if (!overrideGlobalInventoryReferences)
                {
                    if (globals.itemDatabase != null)
                        itemDatabase = globals.itemDatabase;
                    if (globals.inventoryModel != null)
                        inventoryModel = globals.inventoryModel;
                }
                else
                {
                    // Even when overriding, attempt to heal missing references from globals.
                    if (itemDatabase == null)
                        itemDatabase = globals.itemDatabase;
                    if (inventoryModel == null)
                        inventoryModel = globals.inventoryModel;
                }
            }

            // Editor fallback: if there is exactly one asset of the type in the project, pick it.
            // This helps repair references when prefabs lose serialized fields.
            if (itemDatabase == null)
                itemDatabase = TryFindSingleAsset<ItemDatabase>();
            if (inventoryModel == null)
                inventoryModel = TryFindSingleAsset<InventoryModel>();
        }

        private static T TryFindSingleAsset<T>() where T : Object
        {
            // Note: this is Editor-only and intentionally not used at runtime.
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids == null || guids.Length != 1) return null;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
#endif

        /// <summary>
        /// FishNet lifecycle hook: called when the object starts on the server.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Initializes server-authoritative slot list to <see cref="slotCount"/> empty entries.<br/>
        /// - Resets selection to slot 0.<br/>
        /// - Grants configured <see cref="initialLoadout"/>.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Server only.<br/>
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            Slots.Clear();
            for (int i = 0; i < slotCount; i++)
                Slots.Add(default);

            _selectedSlotIndex.Value = 0;

            ApplyInitialItemsServer();

            // Mark readiness only after slots are initialized and the server has applied the initial loadout.
            _inventoryReady.Value = true;
        }

        /// <summary>
        /// Server-only helper to grant starting items defined in <see cref="initialLoadout"/>.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Attempts to add each configured item to slots (stacking where possible).<br/>
        /// - Ensures the selected slot points at a non-empty slot if any were granted.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Server only.<br/>
        /// </summary>
        [Server]
        private void ApplyInitialItemsServer()
        {
            if (initialLoadout == null || initialLoadout.Entries == null || initialLoadout.Entries.Length == 0) return;

            var entries = initialLoadout.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                ushort id = entry.itemDefinition != null ? entry.itemDefinition.id : entry.itemId;
                if (id == 0) continue;

                int amt = entry.amount;
                if (amt == 0) amt = 1;

                TryAddItem(id, amt);
            }

            // If nothing was auto-selected, keep selection on a non-empty slot if possible.
            int current = _selectedSlotIndex.Value;
            if (current < 0 || current >= Slots.Count || Slots[current].IsEmpty)
            {
                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i].IsEmpty) continue;
                    _selectedSlotIndex.Value = i;
                    break;
                }
            }
        }

        /// <summary>
        /// FishNet lifecycle hook: called when the object starts on a client.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Subscribes to selection changes so clients can update item visibility when selection replicates.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Clients (including host client).<br/>
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();
            _selectedSlotIndex.OnChange += OnSelectedSlotChanged;

            _inventoryReady.OnChange += OnInventoryReadyChanged;
            RefreshOwnerClientSubscriptions();
            TryInitializeCharacterAnimationIfReady();
        }

        /// <summary>
        /// FishNet lifecycle hook: called when ownership changes on a client.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - When this client becomes the owner and <see cref="inventoryModel"/> is assigned, begins listening for slot changes so the local HUD can be updated.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Clients.<br/>
        /// </summary>
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            RefreshOwnerClientSubscriptions();
        }

        /// <summary>
        /// Unity lifecycle hook.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Coalesces multiple <see cref="Slots"/> change events into a single HUD push per frame.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Runs on all peers, but exits early unless this client is the local owner and <see cref="inventoryModel"/> is assigned.<br/>
        /// </summary>
        private void LateUpdate()
        {
            // SyncList initial state replication can fire OnChange once per element. Coalesce into one UI push per frame.
            if (!IsOwner) return;
            if (!_uiModelDirty) return;

            _uiModelDirty = false;
            PushToModel();
        }

        /// <summary>
        /// FishNet lifecycle hook: called when the object stops on a client.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Unsubscribes event handlers to avoid leaks / duplicate callbacks.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Clients.<br/>
        /// </summary>
        public override void OnStopClient()
        {
            base.OnStopClient();
            _selectedSlotIndex.OnChange -= OnSelectedSlotChanged;

            _inventoryReady.OnChange -= OnInventoryReadyChanged;
            RemoveOwnerClientSubscriptions();
        }

        /// <summary>
        /// Refreshes owner-only client subscriptions used for HUD updates and predicted-use cancellation.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Subscribes when this client is the local owner.<br/>
        /// - Unsubscribes when ownership is lost.<br/>
        /// </summary>
        private void RefreshOwnerClientSubscriptions()
        {
            RemoveOwnerClientSubscriptions();

            if (!IsOwner)
                return;

            Slots.OnChange += OnSlotsChanged;
            _ownerSlotsSubscribed = true;

            if (itemUseFeedback != null)
            {
                itemUseFeedback.OnItemUseFailed += OnLocalItemUseFailed;
                _ownerFeedbackSubscribed = true;
            }

            _uiModelDirty = true;
        }

        /// <summary>
        /// Removes owner-only client subscriptions to avoid duplicate callbacks and leaks.<br/>
        /// </summary>
        private void RemoveOwnerClientSubscriptions()
        {
            if (_ownerSlotsSubscribed)
            {
                Slots.OnChange -= OnSlotsChanged;
                _ownerSlotsSubscribed = false;
            }

            if (_ownerFeedbackSubscribed && itemUseFeedback != null)
            {
                itemUseFeedback.OnItemUseFailed -= OnLocalItemUseFailed;
                _ownerFeedbackSubscribed = false;
            }
        }

        /// <summary>
        /// Owner-client failure callback used to recover local state when the server rejects the request.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Matches the currently selected item against the reported failure.<br/>
        /// - Resets local ammo/UI state without sending another RPC.<br/>
        /// </summary>
        /// <param name="failure">Failure information reported by the server.</param>
        private void OnLocalItemUseFailed(ItemUseFailure failure)
        {
            if (!IsOwner)
                return;

            if (!TryGetSelectedItem(out var slotState, out var item) || item == null)
                return;

            bool matchesSelectedSlot = failure.SlotIndex >= 0 && failure.SlotIndex == _selectedSlotIndex.Value;
            bool matchesSelectedItem = failure.ItemId != 0 && failure.ItemId == slotState.ItemId;
            if (!matchesSelectedSlot && !matchesSelectedItem)
                return;

            if (item is MonoBehaviour itemBehaviour && itemBehaviour.TryGetComponent<Weapons.NetworkWeaponMagazine>(out var magazine))
                magazine.ResetPredictedAmmoToServer();

        }

        private void OnInventoryReadyChanged(bool prev, bool next, bool asServer)
        {
            if (asServer) return;
            if (!next) return;
            TryInitializeCharacterAnimationIfReady();
        }

        private void TryInitializeCharacterAnimationIfReady()
        {
            if (!IsClientInitialized) return;
            if (!_inventoryReady.Value) return;
            if (_characterAnimationInitialized) return;

            // Ensure selection visuals (and equip callbacks) run once when initial replicated state arrives.
            // This also causes PlayerItemRegistry to apply CAS animation settings for the selected item.
            ApplySelectionVisuals(_selectedSlotIndex.Value);
            _characterAnimationInitialized = true;
        }

        /// <summary>
        /// SyncVar callback invoked when <see cref="_selectedSlotIndex"/> changes.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Updates local item GameObject visibility (equipping/unequipping visuals) to match selected slot.<br/>
        /// - Marks the local HUD dirty for the owning client.<br/>
        /// <br/>
        /// Notes:<br/>
        /// - <paramref name="asServer"/> indicates whether the callback is executing on the server instance.<br/>
        /// </summary>
        private void OnSelectedSlotChanged(int prev, int next, bool asServer)
        {
            ApplySelectionVisuals(next);

            if (IsOwner) _uiModelDirty = true;
        }

        /// <summary>
        /// SyncList callback invoked when <see cref="Slots"/> changes.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - For the owning client, marks the HUD dirty so the inventory UI can be refreshed.<br/>
        /// <br/>
        /// Notes:<br/>
        /// - <paramref name="asServer"/> indicates whether the callback is executing on the server instance.<br/>
        /// </summary>
        private void OnSlotsChanged(SyncListOperation op, int index, InventorySlotState oldItem, InventorySlotState newItem, bool asServer)
        {
            if (!IsOwner) return;
                _uiModelDirty = true;   
        }

        /// <summary>
        /// Pushes the current replicated slot/selection state into the local <see cref="inventoryModel"/>.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Copies <see cref="Slots"/> into an array (UI-friendly) and calls <c>inventoryModel.SetInventory(...)</c>.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Owning client only.<br/>
        /// </summary>
        private void PushToModel()
        {
            // Copy to array for UI.
            var arr = new InventorySlotState[Slots.Count];
            for (int i = 0; i < Slots.Count; i++)
                arr[i] = Slots[i];

            inventoryModel.SetInventory(arr, _selectedSlotIndex.Value);
        }

        /// <summary>
        /// Returns the current state of a slot by index.<br/>
        /// <br/>
        /// Expected context:<br/>
        /// - Any peer (server or client). Reads replicated state.<br/>
        /// </summary>
        /// <returns>
        /// A valid <see cref="InventorySlotState"/> when <paramref name="slotIndex"/> is in range, otherwise <c>default</c> (empty).<br/>
        /// </returns>
        public InventorySlotState GetSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count) return default;
            return Slots[slotIndex];
        }

        /// <summary>
        /// Attempts to resolve the item component for the currently selected slot.
        /// </summary>
        /// <remarks>
        /// This is a convenience helper for gameplay code which wants the currently "equipped" item implementation.
        /// Returns <c>false</c> when the selected slot is empty, out of range, or the item has no registered implementation.
        /// </remarks>
        public bool TryGetSelectedItem(out IRoachRaceItem item)
        {
            return TryGetSelectedItem(out _, out item);
        }

        /// <summary>
        /// Attempts to resolve the item component for the currently selected slot and also returns the slot state.
        /// </summary>
        public bool TryGetSelectedItem(out InventorySlotState slotState, out IRoachRaceItem item)
        {
            item = null;
            slotState = GetSlot(_selectedSlotIndex.Value);
            if (slotState.IsEmpty) return false;
            if (itemRegistry == null) return false;
            return itemRegistry.TryGetItem(slotState.ItemId, out item);
        }

        /// <summary>
        /// Attempts to resolve the <see cref="ItemInstance"/> for the currently selected slot and also returns the slot state.
        /// </summary>
        public bool TryGetSelectedItemInstance(out InventorySlotState slotState, out ItemInstance instance)
        {
            instance = null;

            if (!TryGetSelectedItem(out slotState, out var item) || item == null) {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] Failed to get selected item for slot index {_selectedSlotIndex.Value} on '{gameObject.name}'. Slot state: {slotState}.", gameObject);
                return false;
            }

            // ItemInstance is expected to live on the same GameObject as the item component.
            instance = item.GetItemInstance();

            if(instance == null) {
                Debug.LogError($"[{nameof(NetworkPlayerInventory)}] Selected item component '{item.GetType().Name}' on '{gameObject.name}' returned null ItemInstance.", gameObject);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Requests selecting an inventory slot.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - If running on the server, applies selection immediately.<br/>
        /// - Otherwise, only the local owner can request selection via RPC.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> request was accepted/queued (or applied immediately on server).<br/>
        /// <c>false</c> invalid index or caller is not permitted (non-owner client).<br/>
        /// </returns>
        public bool TrySelectSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotCount) return false;

            if (IsServerInitialized)
            {
                ServerSelectSlot(slotIndex, useOnSelect: false);
                return true;
            }

            if (!IsOwner) return false;
            SelectSlotServerRpc(slotIndex);
            return true;
        }

        // Invoked on client side first
        public bool TryUseSelected()
        {
            if (!IsOwner) return false;
            UseSelected(); // needed so that client can show visually usage of item
            if(!IsServerInitialized) UseSelectedServerRpc(); // needed so that server plays usage animation and apply item use logic (e.g. ammo decrement, cooldown start, etc) and notify other clients about it
            return true;
        }

        // Invoked on client side first
        public bool TryStopUseSelected()
        {
            if (!IsOwner) return false;
            ServerStopUseSelected(); // needed so that server can stop usage of the item
            if(!IsServerInitialized) StopUseSelectedServerRpc(); // needed so that clients are notified about the stop usage
            return true;
        }

        public bool TryUseByItemId(ushort itemId)
        {
            if (itemId == 0) return false;

            if (IsServerInitialized)
            {
                return ServerUseByItemId(itemId);
            }

            if (!IsOwner) return false;
            UseByItemIdServerRpc(itemId);
            return true;
        }

        /// <summary>
        /// Requests using the first available stack/slot matching <paramref name="itemId"/>.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - If the item implements <see cref="IRoachRaceAimItem"/>, aim data is read from <see cref="lookState"/> and applied before use.<br/>
        /// - If running on the server, use happens immediately.<br/>
        /// - Otherwise, only the local owner can request use via RPC.<br/>
        /// <br/>
        /// Notes:<br/>
        /// - The server performs validation via <see cref="CanUseByItemId"/> and will return <c>false</c> if unusable.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> request was accepted/queued (or applied immediately on server).<br/>
        /// <c>false</c> invalid <paramref name="itemId"/> (0), caller is not permitted (non-owner client), or the server-side use failed.<br/>
        /// </returns>
        private bool TryGetLookAim(bool preferLocal, out Vector3 origin, out Vector3 direction)
        {
            return lookState.TryGetLook(out origin, out direction, preferLocal);
        }

        private bool TryApplyAimFromLookState(IRoachRaceItem item, bool preferLocal, int slotIndex, ushort itemId)
        {
            if (item is not IRoachRaceAimItem aimItem)
                return true;

            if (!TryGetLookAim(preferLocal, out var origin, out var direction))
            {
                ReportUseFailed(itemId, slotIndex, ItemUseFailReason.RequiresAimData);
                return false;
            }

            aimItem.SetAim(origin, direction);
            return true;
        }

        /// <summary>
        /// Server-only check for whether <paramref name="itemId"/> can currently be used.<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Verifies server is initialized, item exists in <see cref="itemRegistry"/>, and there is at least one non-empty stack with a positive count in <see cref="Slots"/>.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> item exists and at least one unit is available in slots.<br/>
        /// <c>false</c> called on a client, item missing, or no units available.<br/>
        /// </returns>
        public bool CanUseByItemId(ushort itemId)
        {
            if (!IsServerInitialized) return false;
            if (itemId == 0) return false;
            if (itemRegistry == null) return false;
            if (!itemRegistry.TryGetItem(itemId, out _)) return false;

            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != itemId) continue;
                if (s.Count <= 0) continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Owner-to-server RPC to request selecting <paramref name="slotIndex"/>.<br/>
        /// <br/>
        /// Expected behavior:<br/>
        /// - Server applies selection and may auto-use depending on item rules.<br/>
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void SelectSlotServerRpc(int slotIndex)
        {
            ServerSelectSlot(slotIndex, useOnSelect: true);
        }

        /// <summary>
        /// Owner-to-server RPC to request using the currently selected item (no aim data).<br/>
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void UseSelectedServerRpc()
        {
            UseSelected();
        }

        /// <summary>
        /// Owner-to-server RPC to request stopping the currently selected item's use.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void StopUseSelectedServerRpc()
        {
            ServerStopUseSelected();
        }

        private bool ServerStopUseSelected()
        {
            int idx = _selectedSlotIndex.Value;
            if (idx < 0 || idx >= Slots.Count)
                return false;

            var slot = Slots[idx];
            if (slot.IsEmpty)
                return false;

            if (itemRegistry == null)
                return false;

            if (!itemRegistry.TryGetItem(slot.ItemId, out IRoachRaceItem item) || item == null)
                return false;

            item.UseStop();
            StopItemObserversRpc(slot.ItemId);
            return true;
        }

        /// <summary>
        /// Server-to-observers RPC that stops item use on observing clients.
        /// </summary>
        [ObserversRpc(ExcludeServer = true)]
        private void StopItemObserversRpc(ushort itemId)
        {
            if (itemRegistry == null) return;
            if (!itemRegistry.TryGetItem(itemId, out var item) || item == null) return;
            item.UseStop();
        }

        /// <summary>
        /// Owner-to-server RPC to request using an item by its definition id (no aim data).<br/>
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void UseByItemIdServerRpc(ushort itemId)
        {
            ServerUseByItemId(itemId);
        }

        /// <summary>
        /// Server-authoritative use by item definition id.<br/>
        /// Currently the code that reports use failures is somewhat duplicated between this and <see cref="UseSelected"/>;
        /// Could be unified if desired since they share a lot of validation steps, but for now it's separate for clarity and 
        /// because they may diverge in the future (e.g., if we want to allow using by id from non-selected slots). I am
        /// considering to move this to items code. Maybe we will move all item related code to networking package<br/>
        /// <br/>
        /// Typical behavior:<br/>
        /// - Validates the item is usable and available.<br/>
        /// - Finds the first matching slot stack.<br/>
        /// - Initializes the item use context and calls <c>UseStart()</c> on the item component.<br/>
        /// - Optionally applies aim data when the item implements <see cref="IRoachRaceAimItem"/>.<br/>
        /// - Optionally decrements inventory count when the definition says it consumes on use.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> item use executed and (if applicable) inventory state mutated.<br/>
        /// <c>false</c> invalid id, unusable, missing registry/component, or no available stack.<br/>
        /// </returns>
        [Server]
        private bool ServerUseByItemId(ushort itemId)
        {
            if (itemId == 0)
            {
                ReportUseFailed(itemId, slotIndex: -1, ItemUseFailReason.InvalidItemId);
                return false;
            }

            // Quick gate to avoid running any item logic when the item isn't usable.
            if (!CanUseByItemId(itemId))
            {
                // CanUseByItemId includes both "not present" and "registry missing" style failures.
                if (itemRegistry == null || !itemRegistry.TryGetItem(itemId, out _))
                    ReportUseFailed(itemId, slotIndex: -1, ItemUseFailReason.MissingItemComponent);
                else
                    ReportUseFailed(itemId, slotIndex: -1, ItemUseFailReason.NotInInventory);

                return false;
            }

            int slotIndex = -1;
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != itemId) continue;
                if (s.Count <= 0) continue;
                slotIndex = i;
                break;
            }

            if (slotIndex < 0)
            {
                ReportUseFailed(itemId, slotIndex: -1, ItemUseFailReason.NotInInventory);
                return false;
            }

            if (itemRegistry == null)
            {
                ReportUseFailed(itemId, slotIndex, ItemUseFailReason.MissingItemComponent);
                return false;
            }
            if (!itemRegistry.TryGetItem(itemId, out IRoachRaceItem item))
            {
                ReportUseFailed(itemId, slotIndex, ItemUseFailReason.MissingItemComponent);
                return false;
            }

            // Some items rely on client-derived aim data (e.g., raycast from camera).
            if (!TryApplyAimFromLookState(item, preferLocal: false, slotIndex, itemId))
                return false;

            int seed = UnityEngine.Random.Range(0, int.MaxValue);
            item.InitializeUseContext(seed, OwnerId, true, gameObject);

            // Aim may have already been set above for gating; set again here to ensure the current use context
            // sees the latest values even if the item overwrote aim during gating.
            if (!TryApplyAimFromLookState(item, preferLocal: false, slotIndex, itemId))
                return false;

            item.UseStart();

            // Consume one stack entry only when the definition says so.
            if (TryGetDefinition(itemId, out var def) && def != null && def.ConsumesInventoryOnUse)
            {
                var slot = Slots[slotIndex];
                bool slotBecameEmpty = false;

                if (slot.Count <= 1)
                {
                    Slots[slotIndex] = default;
                    slotBecameEmpty = true;
                }
                else
                {
                    slot.Count--;
                    Slots[slotIndex] = slot;
                }

                // If consuming emptied the currently selected slot, update item visibility on all clients.
                if (slotBecameEmpty && slotIndex == _selectedSlotIndex.Value)
                {
                    ApplySelectionVisuals(slotIndex);
                    SetSelectionVisualsObserversRpc(slotIndex);
                }
            }

            return true;
        }

        /// <summary>
        /// Server-authoritative add to inventory.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - If the item is stackable, fills existing stacks up to max stack.<br/>
        /// - Puts any remaining amount into empty slots.<br/>
        /// - May auto-select newly filled slot if current selection is empty and <see cref="autoSelectOnPickup"/> is enabled.<br/>
        /// <br/>
        /// Notes:<br/>
        /// - If inventory is full, some or all of <paramref name="amount"/> may not fit; this method currently still returns <c>true</c><br/>
        ///   if it was able to add any portion.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> at least one unit was added (even if inventory becomes full later).<br/>
        /// <c>false</c> invalid inputs (id 0 or amount 0).<br/>
        /// </returns>
        [Server]
        public bool TryAddItem(ushort itemId, int amount)
        {
            if (itemId == 0) return false;
            if (amount == 0) return false;

            // If stackable, try to stack first.
            if (TryGetDefinition(itemId, out var def) && def != null && def.IsStackable)
            {
                for (int i = 0; i < Slots.Count; i++)
                {
                    var s = Slots[i];
                    if (s.ItemId != itemId) continue;

                    int maxStack = def.MaxStack;
                    int canAdd = maxStack - s.Count;
                    if (canAdd <= 0) continue;

                    int toAdd = Mathf.Min(canAdd, amount);
                    s.Count += toAdd;
                    Slots[i] = s;
                    amount -= toAdd;
                    if (amount == 0) return true;
                }
            }

            // Put remaining into empty slots.
            for (int i = 0; i < Slots.Count && amount > 0; i++)
            {
                var s = Slots[i];
                if (!s.IsEmpty) continue;

                int put;
                if (TryGetDefinition(itemId, out var def2) && def2 != null && def2.IsStackable)
                {
                    int maxStack = def2.MaxStack;
                    put = Mathf.Min(maxStack, amount);
                }
                else
                {
                    put = 1;
                }

                Slots[i] = new InventorySlotState { ItemId = itemId, Count = put };
                amount -= put;
            }

            return true;
        }

        /// <summary>
        /// Server-authoritative add to inventory, returning exactly how many units were actually added.
        /// Useful when granting from a world pickup which must retain leftover units if the inventory is full.
        /// </summary>
        /// <returns>
        /// Units successfully added ($0..amount$).
        /// </returns>
        [Server]
        public int AddItemUpTo(ushort itemId, int amount)
        {
            if (itemId == 0) return 0;
            if (amount <= 0) return 0;

            int remaining = amount;
            int added = 0;

            // If stackable, fill existing stacks first.
            if (TryGetDefinition(itemId, out var def) && def != null && def.IsStackable)
            {
                for (int i = 0; i < Slots.Count && remaining > 0; i++)
                {
                    var s = Slots[i];
                    if (s.IsEmpty) continue;
                    if (s.ItemId != itemId) continue;

                    int maxStack = def.MaxStack;
                    int canAdd = maxStack - s.Count;
                    if (canAdd <= 0) continue;

                    int toAdd = Mathf.Min(canAdd, remaining);
                    s.Count += toAdd;
                    Slots[i] = s;
                    remaining -= toAdd;
                    added += toAdd;
                }
            }

            // Put remaining into empty slots.
            for (int i = 0; i < Slots.Count && remaining > 0; i++)
            {
                var s = Slots[i];
                if (!s.IsEmpty) continue;

                int put;
                if (TryGetDefinition(itemId, out var def2) && def2 != null && def2.IsStackable)
                {
                    int maxStack = def2.MaxStack;
                    put = Mathf.Min(maxStack, remaining);
                }
                else
                {
                    put = 1;
                }

                Slots[i] = new InventorySlotState { ItemId = itemId, Count = put };
                remaining -= put;
                added += put;
            }

            return added;
        }

        /// <summary>
        /// Server-authoritative removal of the entire selected stack.
        /// Intended for "drop selected" behavior.
        /// </summary>
        [Server]
        public bool TryRemoveSelectedStack(out ushort itemId, out int count)
        {
            itemId = 0;
            count = 0;

            int idx = _selectedSlotIndex.Value;
            if (idx < 0 || idx >= Slots.Count) return false;

            var s = Slots[idx];
            if (s.IsEmpty) return false;

            itemId = s.ItemId;
            count = s.Count;
            Slots[idx] = default;

            // Slot became empty; ensure equipped visuals are updated for everyone.
            ApplySelectionVisuals(idx);
            SetSelectionVisualsObserversRpc(idx);

            return true;
        }

        /// <summary>
        /// Resolves an ItemDefinition via the configured ItemDatabase.
        /// This is useful for other server-authoritative systems (eg drop gating) which need definition flags.
        /// </summary>
        public bool TryResolveDefinition(ushort id, out ItemDefinition def) => TryGetDefinition(id, out def);

        /// <summary>
        /// Server-authoritative removal of one unit from the currently selected slot.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Decrements stack count or clears the slot when it reaches 0.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> one unit removed.<br/>
        /// <c>false</c> selected index invalid or selected slot empty.<br/>
        /// </returns>
        [Server]
        public bool TryRemoveOneFromSelected()
        {
            int idx = _selectedSlotIndex.Value;
            if (idx < 0 || idx >= Slots.Count) return false;

            var s = Slots[idx];
            if (s.IsEmpty) return false;

            if (s.Count <= 1)
                Slots[idx] = default;
            else
            {
                s.Count--;
                Slots[idx] = s;
            }

            return true;
        }

        /// <summary>
        /// Server-only: consumes up to <paramref name="amount"/> units from any stacks matching <paramref name="itemId"/>, starting from the lowest slot index.<br/>
        ///<br/>
        /// Typical usage:<br/>
        /// - Ammo-style items where the weapon (not the inventory "use" action) decides how much to consume on reload.<br/>
        /// - Status effects or gameplay systems consuming a discrete balance, while carrying instigator context for attribution.<br/>
        /// </summary>
        /// <param name="itemId">ItemDefinition id to consume.</param>
        /// <param name="amount">Units requested to consume ($>0$).</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection (real user), or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object (combat attribution), or -1 for environment/unknown.</param>
        /// <returns>The amount actually consumed ($0..amount$).</returns>
        [Server]
        public int ConsumeByItemId(ushort itemId, int amount, int instigatorConnectionId = -1, int instigatorObjectId = -1)
        {
            return ConsumeByItemIdInternal(itemId, amount, weaponIconKey: string.Empty, instigatorConnectionId, instigatorObjectId);
        }

        [Server]
        private int ConsumeByItemIdInternal(ushort itemId, int amount, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            if (itemId == 0) return 0;
            if (amount <= 0) return 0;

            int remaining = amount;
            int selectedIdx = _selectedSlotIndex.Value;
            bool selectedBecameEmpty = false;

            for (int i = 0; i < Slots.Count && remaining > 0; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != itemId) continue;

                int available = s.Count;
                if (available <= 0) continue;

                int take = Mathf.Min(available, remaining);
                int newCount = available - take;

                if (newCount <= 0)
                {
                    Slots[i] = default;
                    if (i == selectedIdx)
                        selectedBecameEmpty = true;
                }
                else
                {
                    s.Count = newCount;
                    Slots[i] = s;
                }

                remaining -= take;
            }

            // If consuming emptied the selected slot, update item visibility on all clients.
            if (selectedBecameEmpty)
            {
                ApplySelectionVisuals(selectedIdx);
                SetSelectionVisualsObserversRpc(selectedIdx);
            }

            int consumed = amount - remaining;
            if (consumed != 0)
                NotifyServerDeltaObservers(-consumed, weaponIconKey, instigatorConnectionId, instigatorObjectId);

            return consumed;
        }

        /// <summary>
        /// Server-only: applies a signed delta to stacks of <paramref name="itemId"/>.<br/>
        /// Delta convention: negative = consume, positive = add.<br/>
        /// Includes attribution context for observers:<br/>
        /// - <paramref name="weaponIconKey"/> for UI-facing source labeling (eg killfeed icon key).<br/>
        /// - Instigator connection (real user) and object (combat attribution).
        /// </summary>
        /// <param name="itemId">ItemDefinition id.</param>
        /// <param name="delta">Signed delta to apply.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon key to attribute the delta (eg killfeed icon key). Empty when not applicable.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection, or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object, or -1 for environment/unknown.</param>
        /// <returns>The delta actually applied (may be 0 or reduced magnitude).</returns>
        [Server]
        public int ApplyDeltaByItemId(ushort itemId, int delta, string weaponIconKey, int instigatorConnectionId = -1, int instigatorObjectId = -1)
        {
            if (itemId == 0) return 0;
            if (delta == 0) return 0;

            if (delta < 0)
            {
                int consumed = ConsumeByItemIdInternal(itemId, -delta, weaponIconKey, instigatorConnectionId, instigatorObjectId);
                return -consumed;
            }

            int added = AddItemUpTo(itemId, delta);
            if (added != 0)
                NotifyServerDeltaObservers(added, weaponIconKey, instigatorConnectionId, instigatorObjectId);
            return added;
        }

        [Server]
        private void NotifyServerDeltaObservers(int appliedDelta, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            if (_serverDeltaObservers.Count == 0) return;

            // Iterate a snapshot to avoid issues if observers add/remove during callback.
            foreach (var observer in _serverDeltaObservers.ToArray())
            {
                if (observer == null) continue;
                observer.OnServerInventoryItemDeltaApplied(this, appliedDelta, weaponIconKey, instigatorConnectionId, instigatorObjectId);
            }
        }

        /// <summary>
        /// Server-only: returns the total units available for a given <paramref name="itemId"/> across all stacks.<br/>
        /// Useful for weapon logic checks (can we fully reload?) before consuming.<br/>
        /// </summary>
        /// <returns>
        /// Total count across all slots (0 if <paramref name="itemId"/> is 0 or not present).<br/>
        /// </returns>
        [Server]
        public int GetTotalCountByItemId(ushort itemId)
        {
            if (itemId == 0) return 0;

            int total = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != itemId) continue;
                total += s.Count;
            }

            return total;
        }

        /// <summary>
        /// Server-authoritative slot selection implementation.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Updates <see cref="_selectedSlotIndex"/> (replicated).<br/>
        /// - Updates selection visuals locally and via observers RPC.<br/>
        /// - When <paramref name="useOnSelect"/> is true, may auto-use the selected item depending on team/item rules.<br/>
        ///<br/>
        /// Expected context:<br/>
        /// - Server only.<br/>
        /// </summary>
        [Server]
        private void ServerSelectSlot(int slotIndex, bool useOnSelect)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count) return;

            _selectedSlotIndex.Value = slotIndex;

            ApplySelectionVisuals(slotIndex);
            SetSelectionVisualsObserversRpc(slotIndex);

            if (!useOnSelect) return;

            // Monsters have no inventory; Ghost vs Survivor behavior differs.
            var slot = Slots[slotIndex];
            if (slot.IsEmpty) return;
        }


        // Should be invocked on both client and server. On the server, it will run the full logic. On the client, it will run validation logic and VFX related code paths, but skip actual item use and inventory mutation.
        private bool UseSelected()
        {
            int idx = _selectedSlotIndex.Value;
            if (idx < 0 || idx >= Slots.Count)
            {
                ReportUseFailed(itemId: 0, slotIndex: idx, ItemUseFailReason.InvalidSlotIndex);
                return false;
            }

            var slot = Slots[idx];
            if (slot.IsEmpty)
            {
                ReportUseFailed(itemId: 0, slotIndex: idx, ItemUseFailReason.EmptySlot);
                return false;
            }

            if (itemRegistry == null)
            {
                ReportUseFailed(slot.ItemId, idx, ItemUseFailReason.MissingItemComponent);
                return false;
            }
            if (!itemRegistry.TryGetItem(slot.ItemId, out IRoachRaceItem item))
            {
                ReportUseFailed(slot.ItemId, idx, ItemUseFailReason.MissingItemComponent);
                return false;
            }

            // Some items rely on client-derived aim data (e.g., raycast from camera).
            if (!TryApplyAimFromLookState(item, preferLocal: false, idx, slot.ItemId))
                return false;

            int seed = UnityEngine.Random.Range(0, int.MaxValue);
            item.InitializeUseContext(seed, OwnerId, true, gameObject);

            // Aim may have already been set above for gating; set again here to ensure the current use context
            // sees the latest values even if the item overwrote aim during gating.
            if (!TryApplyAimFromLookState(item, preferLocal: false, idx, slot.ItemId))
                return false;

            item.UseStart();
            
            if(IsServerInitialized) {
                // Consume one stack entry only when the definition says so.
                if (TryGetDefinition(slot.ItemId, out var def) && def != null && def.ConsumesInventoryOnUse)
                {
                    TryRemoveOneFromSelected();

                    // Slot contents may have changed; update visuals for currently selected slot.
                    ApplySelectionVisuals(idx);
                    SetSelectionVisualsObserversRpc(idx);
                }

                UseItemObserversRpc(slot.ItemId, seed);
            }

            // If the item is consumable, it can call back into inventory removal itself later.
            // For now, keycard/heal items already manage their own charges; inventory count represents possession.
            return true;
        }

        /// <summary>
        /// Server-only: forces stopping use of an item by id and broadcasts stop to observers.
        /// Intended for weapon logic (eg, magazine reached 0 during automatic fire).
        /// </summary>
        [Server]
        public void ForceStopUsingItem(ushort itemId)
        {
            if (!IsServerInitialized) return;
            if (itemId == 0) return;
            if (itemRegistry == null) return;

            if (!itemRegistry.TryGetItem(itemId, out IRoachRaceItem item) || item == null)
                return;

            item.UseStop();
            StopItemObserversRpc(itemId);
        }

        /// <summary>
        /// Attempts to resolve an <see cref="ItemDefinition"/> for a given id via <see cref="itemDatabase"/>.<br/>
        /// </summary>
        /// <returns>
        /// <c>true</c> definition found; <paramref name="def"/> is non-null.<br/>
        /// <c>false</c> database missing or id not found; <paramref name="def"/> is null.<br/>
        /// </returns>
        private bool TryGetDefinition(ushort id, out ItemDefinition def)
        {
            def = null;
            if (itemDatabase == null) return false;
            return itemDatabase.TryGet(id, out def);
        }

        /// <summary>
        /// Applies local item visibility (equip/unequip) to match the specified slot index.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - If slot is invalid or empty, hides all registered item GameObjects.<br/>
        /// - Otherwise, enables only the item matching the selected slot's itemId.<br/>
        ///<br/>
        /// Expected context:<br/>
        /// - Runs on server and clients; used both locally and via observers RPC.<br/>
        /// </summary>
        private void ApplySelectionVisuals(int slotIndex)
        {
            if (itemRegistry == null) return;

            if (slotIndex < 0 || slotIndex >= Slots.Count)
            {
                itemRegistry.HideAll();
                return;
            }

            var slot = Slots[slotIndex];
            if (slot.IsEmpty)
                itemRegistry.HideAll();
            else
                itemRegistry.SetOnlyActive(slot.ItemId);
        }

        /// <summary>
        /// Server-to-observers RPC that applies selection visuals on observing clients.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Mirrors server selection visuals on non-server clients (server excluded).<br/>
        /// </summary>
        [ObserversRpc(ExcludeServer = true)]
        private void SetSelectionVisualsObserversRpc(int slotIndex)
        {
            ApplySelectionVisuals(slotIndex);
        }

        /// <summary>
        /// Server-to-observers RPC that plays item use on observing clients without aim data.<br/>
        ///<br/>
        /// Typical behavior:<br/>
        /// - Re-initializes use context for deterministic playback and calls <c>UseStart()</c>.<br/>
        ///<br/>
        /// Expected context:<br/>
        /// - Runs on clients which are observing this NetworkObject (server excluded).<br/>
        /// </summary>
        [ObserversRpc(ExcludeServer = true)]
        private void UseItemObserversRpc(ushort itemId, int seed)
        {
            if (itemRegistry == null) return;
            if (!itemRegistry.TryGetItem(itemId, out var item)) return;

            item.InitializeUseContext(seed, OwnerId, false, gameObject);
            if (item is IRoachRaceAimItem)
                TryApplyAimFromLookState(item, preferLocal: false, slotIndex: -1, itemId);
            item.UseStart();
        }
    }
}
