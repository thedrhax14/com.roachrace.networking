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

        [Tooltip("Reload duration used for the networked reload timer.")]
        [SerializeField, Min(0.05f)] private float reloadDurationSeconds = 1.6f;

        private readonly SyncVar<int> _ammoInMag = new(0);
        private readonly SyncVar<bool> _isReloading = new(false);

        // SyncTimer is used to deterministically complete reloads even when animation events are unreliable on server.
        private readonly SyncTimer _reloadTimer = new();

        private NetworkPlayerInventory _inventory;
        private ItemInstance _itemInstance;

        // Owner-only client prediction for presentation.
        private int _predictedAmmoInMag;
        private bool _predictionInitialized;
        private bool _hudActive;

        [Header("UI (Optional)")]
        [Tooltip("Optional. If assigned (or provided via InventoryGlobals), owner client will push weapon HUD state into this model.")]
        [SerializeField] private WeaponHudModel weaponHudModel;

        [Tooltip("Optional. Used to resolve the weapon icon from the ItemDefinition.")]
        [SerializeField] private ItemDatabase itemDatabase;

        private Sprite _resolvedIcon;
        private bool _iconResolved;

        public ushort AmmoItemId => ammoItemId;
        public int MagazineSize => magazineSize;
        public int AmmoInMag => _ammoInMag.Value;
        public bool IsReloading => _isReloading.Value;

        /// <summary>
        /// Owner-only presentation value. Never exceeds server ammo.
        /// </summary>
        public int PredictedAmmoInMag => _predictionInitialized ? _predictedAmmoInMag : _ammoInMag.Value;

        /// <summary>
        /// Called by the equipped item to allow this magazine to control the shared WeaponHudModel.
        /// Only meaningful on the owning client.
        /// </summary>
        public void SetHudActive(bool active)
        {
            _hudActive = active;

            if (!IsOwner || weaponHudModel == null)
                return;

            if (_hudActive)
            {
                PushHudState();
            }
            else
            {
                // Clear to avoid stale HUD when nothing is equipped.
                weaponHudModel.SetWeapon(null, 0, 0);
                weaponHudModel.SetAmmoInMag(0);
                weaponHudModel.SetReloading(false);
                weaponHudModel.NotifyAll();
            }
        }

        /// <summary>
        /// Owner-client only. Decrements predicted ammo for local presentation.
        /// Returns true if a shot should play VFX/SFX.
        /// </summary>
        public bool TryPredictLocalShotForPresentation()
        {
            if (!IsOwner || !IsClientInitialized)
                return true;

            InitializePredictionIfNeeded();

            if (_isReloading.Value)
                return false;

            // Never allow prediction to exceed the latest server value.
            _predictedAmmoInMag = Mathf.Min(_predictedAmmoInMag, _ammoInMag.Value);

            if (_predictedAmmoInMag <= 0)
                return false;

            _predictedAmmoInMag = Mathf.Max(0, _predictedAmmoInMag - 1);

            if (_hudActive && weaponHudModel != null)
                weaponHudModel.SetAmmoInMag(_predictedAmmoInMag);

            return true;
        }

        /// <summary>
        /// Animation-event entry point.
        /// Call this from a reload-end animation event.
        /// Safe to call on clients; only the server will commit the reload.
        /// </summary>
        public void NotifyReloadAnimationEnded()
        {
            // Intentionally no-op for gameplay.
            // Reload completion is handled by the synced reload timer to avoid relying on animation events.
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

            _reloadTimer.OnChange += OnReloadTimerChanged;
        }

        private void OnDestroy()
        {
            _reloadTimer.OnChange -= OnReloadTimerChanged;
        }

        private void Update()
        {
            // Keep the SyncTimer progressing on both server and clients.
            if (!IsServerInitialized && !IsClientInitialized)
                return;

            if (_reloadTimer.Remaining > 0f)
                _reloadTimer.Update();
        }

        private void OnReloadTimerChanged(SyncTimerOperation op, float prev, float next, bool asServer)
        {
            if (op != SyncTimerOperation.Finished)
                return;

            if (!asServer)
                return;

            // Only commit if still reloading (prevents double-completion).
            if (!_isReloading.Value)
                return;

            ServerFinishReload();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!IsOwner)
                return;

            if (weaponHudModel == null)
                return;

            InitializePredictionIfNeeded();

            _ammoInMag.OnChange += OnAmmoInMagChanged;
            _isReloading.OnChange += OnReloadingChanged;

            // Do not push HUD here; only the equipped weapon should drive the shared model.
            if (_hudActive)
                PushHudState();
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

            InitializePredictionIfNeeded();

            // Never let predicted exceed server.
            _predictedAmmoInMag = Mathf.Min(_predictedAmmoInMag, next);

            if (_hudActive)
                weaponHudModel.SetAmmoInMag(_predictedAmmoInMag);
        }

        private void OnReloadingChanged(bool prev, bool next, bool asServer)
        {
            if (!IsOwner || weaponHudModel == null)
                return;

            InitializePredictionIfNeeded();

            // Reload finished: snap predicted up to server (allowed jump-up).
            if (prev && !next)
                _predictedAmmoInMag = _ammoInMag.Value;

            if (_hudActive)
            {
                weaponHudModel.SetReloading(next);
                weaponHudModel.SetAmmoInMag(_predictedAmmoInMag);
            }
        }

        private void InitializePredictionIfNeeded()
        {
            if (_predictionInitialized)
                return;

            _predictedAmmoInMag = _ammoInMag.Value;
            _predictionInitialized = true;
        }

        private void PushHudState()
        {
            if (!IsOwner || weaponHudModel == null)
                return;

            InitializePredictionIfNeeded();
            ResolveIconIfNeeded();

            weaponHudModel.SetWeapon(_resolvedIcon, ammoItemId, magazineSize);
            weaponHudModel.SetAmmoInMag(_predictedAmmoInMag);
            weaponHudModel.SetReloading(_isReloading.Value);
            weaponHudModel.NotifyAll();
        }

        private void ResolveIconIfNeeded()
        {
            if (_iconResolved)
                return;

            _iconResolved = true;
            _resolvedIcon = null;

            if (itemDatabase != null && _itemInstance != null && _itemInstance.ItemId != 0 && itemDatabase.TryGet(_itemInstance.ItemId, out var def) && def != null)
                _resolvedIcon = def.icon;
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

            // Deterministic, synced reload completion.
            _reloadTimer.StartTimer(reloadDurationSeconds);

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

            // Stop any active timer so it cannot finish again later.
            if (_reloadTimer.Remaining > 0f)
                _reloadTimer.StopTimer(sendRemaining: false);

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
