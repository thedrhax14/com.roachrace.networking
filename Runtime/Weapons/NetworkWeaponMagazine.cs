using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Weapons
{
    /// <summary>
    /// Server-authoritative weapon magazine state.
    ///
    /// Model:
    /// - Inventory holds reserve ammo (stacked items).
    /// - This component holds magazine ammo and reload state.
    /// - Reload transfers ammo from inventory reserve -> magazine (server only).
    ///
    /// Intended placement:
    /// - Put this on the same GameObject as the weapon item (eg, WeaponPropItemAdapter) so it can
    ///   read the weapon's ItemInstance id and stop use when the magazine runs dry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWeaponMagazine : NetworkBehaviour
    {
        [Header("Config")]
        [Tooltip("Inventory item id that represents reserve ammo for this weapon.")]
        [SerializeField] private ushort ammoItemId;

        [Tooltip("Maximum rounds this weapon can hold in the magazine.")]
        [SerializeField, Min(1)] private int magazineSize = 30;

        [Tooltip("If true, magazine starts full on server spawn.")]
        [SerializeField] private bool startFull = true;

        private readonly SyncVar<int> _ammoInMag = new(0);
        private readonly SyncVar<bool> _isReloading = new(false);

        private NetworkPlayerInventory _inventory;
        private ItemInstance _itemInstance;

        [Header("UI (Optional)")]
        [Tooltip("Optional. If assigned (or provided via InventoryGlobals), owner client will push weapon HUD state into this model.")]
        [SerializeField] private WeaponHudModel weaponHudModel;

        [Tooltip("Optional. Used to resolve the weapon icon from the ItemDefinition.")]
        [SerializeField] private ItemDatabase itemDatabase;

        public ushort AmmoItemId => ammoItemId;
        public int MagazineSize => magazineSize;
        public int AmmoInMag => _ammoInMag.Value;
        public bool IsReloading => _isReloading.Value;

        /// <summary>
        /// Animation-event entry point.
        /// Call this from a reload-end animation event.
        /// Safe to call on clients; only the server will commit the reload.
        /// </summary>
        public void NotifyReloadAnimationEnded()
        {
            if (!IsServerInitialized)
                return;

            ServerFinishReload();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            _inventory = GetComponentInParent<NetworkPlayerInventory>();
            _itemInstance = GetComponent<ItemInstance>();

            if (_inventory == null)
            {
                Debug.LogError($"[{nameof(NetworkWeaponMagazine)}] NetworkPlayerInventory not found in parents of '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkWeaponMagazine)}] _inventory is null on '{gameObject.name}'.");
            }

            if (_itemInstance == null || _itemInstance.ItemId == 0)
            {
                Debug.LogError($"[{nameof(NetworkWeaponMagazine)}] ItemInstance missing or has invalid ItemId on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkWeaponMagazine)}] ItemInstance is missing/invalid on '{gameObject.name}'.");
            }

            if (ammoItemId == 0)
            {
                Debug.LogError($"[{nameof(NetworkWeaponMagazine)}] ammoItemId is 0 on '{gameObject.name}'. Assign the reserve ammo item id.", gameObject);
                throw new System.ArgumentException($"[{nameof(NetworkWeaponMagazine)}] ammoItemId must be non-zero on '{gameObject.name}'.");
            }

            _ammoInMag.Value = startFull ? Mathf.Max(1, magazineSize) : 0;
            _isReloading.Value = false;
        }

        private void Awake()
        {
            // Non-throwing cache for clients as well.
            if (_inventory == null)
                _inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (_itemInstance == null)
                _itemInstance = GetComponent<ItemInstance>();

            // Auto-wire shared references.
            if (InventoryGlobals.TryGet(out var globals) && globals != null)
            {
                if (weaponHudModel == null)
                    weaponHudModel = globals.weaponHudModel;
                if (itemDatabase == null)
                    itemDatabase = globals.itemDatabase;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!IsOwner)
                return;

            if (weaponHudModel == null)
                return;

            // Set static presentation fields (icon + ammo item id).
            Sprite icon = null;
            if (itemDatabase != null && _itemInstance != null && _itemInstance.ItemId != 0 && itemDatabase.TryGet(_itemInstance.ItemId, out var def) && def != null)
                icon = def.icon;

            weaponHudModel.SetWeapon(icon, ammoItemId, magazineSize);
            weaponHudModel.SetAmmoInMag(_ammoInMag.Value);
            weaponHudModel.SetReloading(_isReloading.Value);

            _ammoInMag.OnChange += OnAmmoInMagChanged;
            _isReloading.OnChange += OnReloadingChanged;

            weaponHudModel.NotifyAll();
        }

        public override void OnStopClient()
        {
            _ammoInMag.OnChange -= OnAmmoInMagChanged;
            _isReloading.OnChange -= OnReloadingChanged;
            base.OnStopClient();
        }

        private void OnAmmoInMagChanged(int prev, int next, bool asServer)
        {
            if (!IsOwner || weaponHudModel == null)
                return;

            weaponHudModel.SetAmmoInMag(next);
        }

        private void OnReloadingChanged(bool prev, bool next, bool asServer)
        {
            if (!IsOwner || weaponHudModel == null)
                return;

            weaponHudModel.SetReloading(next);
        }

        /// <summary>
        /// Server-only: attempt to consume one round for a fired shot.
        /// Returns true when a round was consumed.
        /// </summary>
        [Server]
        public bool ServerTryConsumeShot()
        {
            if (_isReloading.Value)
                return false;

            if (_ammoInMag.Value <= 0)
            {
                // Ensure the firing loop is stopped across observers.
                _inventory.ForceStopUsingItem(_itemInstance.ItemId);
                return false;
            }

            _ammoInMag.Value = Mathf.Max(0, _ammoInMag.Value - 1);

            if (_ammoInMag.Value <= 0)
            {
                // Stop firing immediately when we hit 0.
                _inventory.ForceStopUsingItem(_itemInstance.ItemId);
            }

            return true;
        }

        /// <summary>
        /// Server-only: gate used by inventory before starting item use.
        /// </summary>
        [Server]
        public bool ServerCanStartUse(out ItemUseFailReason reason)
        {
            if (_isReloading.Value)
            {
                reason = ItemUseFailReason.Reloading;
                return false;
            }

            if (_ammoInMag.Value <= 0)
            {
                reason = ItemUseFailReason.NoAmmoInMagazine;
                return false;
            }

            reason = ItemUseFailReason.None;
            return true;
        }

        /// <summary>
        /// Owner entry point for reload.
        /// </summary>
        public bool RequestReload()
        {
            if (IsServerInitialized)
                return ServerTryReload(out _);

            if (!IsOwner || !IsClientInitialized)
                return false;

            ReloadServerRpc();
            return true;
        }

        [ServerRpc(RequireOwnership = true)]
        private void ReloadServerRpc(NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            ServerTryReload(out _);
        }

        [Server]
        public bool ServerTryReload(out ItemUseFailReason reason)
        {
            if (_isReloading.Value)
            {
                reason = ItemUseFailReason.Reloading;
                return false;
            }

            int size = Mathf.Max(1, magazineSize);
            if (_ammoInMag.Value >= size)
            {
                reason = ItemUseFailReason.MagazineFull;
                return false;
            }

            // No reserve ammo? Still allow the reload animation to be denied quickly.
            int needed = size - _ammoInMag.Value;
            if (_inventory.GetTotalCountByItemId(ammoItemId) <= 0 || needed <= 0)
            {
                reason = ItemUseFailReason.NotInInventory;
                return false;
            }

            // Lock weapon use while reloading.
            _isReloading.Value = true;

            // Stop firing immediately when reload begins.
            if (_itemInstance != null)
                _inventory.ForceStopUsingItem(_itemInstance.ItemId);

            // Presentation: play reload visuals on server (host) and observers.
            PlayReloadVisualsServer();
            PlayReloadVisualsObserversRpc();

            reason = ItemUseFailReason.None;
            return true;
        }

        [Server]
        private void PlayReloadVisualsServer()
        {
            var reloadable = GetComponent<IInventoryReloadableItem>();
            reloadable?.PlayReloadVisuals();
        }

        [ObserversRpc(ExcludeServer = true)]
        private void PlayReloadVisualsObserversRpc()
        {
            var reloadable = GetComponent<IInventoryReloadableItem>();
            reloadable?.PlayReloadVisuals();
        }

        [Server]
        private void ServerFinishReload()
        {
            if (!_isReloading.Value)
                return;

            int size = Mathf.Max(1, magazineSize);
            int needed = Mathf.Max(0, size - _ammoInMag.Value);
            if (needed > 0)
            {
                int consumed = _inventory.ConsumeByItemId(ammoItemId, needed);
                _ammoInMag.Value = Mathf.Clamp(_ammoInMag.Value + consumed, 0, size);
            }

            _isReloading.Value = false;
        }
    }
}
