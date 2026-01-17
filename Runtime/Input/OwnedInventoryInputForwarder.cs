using FishNet.Connection;
using FishNet.Object;
using RoachRace.Networking.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Forwards digit selection + use-item input to a player inventory (server-authoritative via RPCs).
    ///
    /// Setup:
    /// - Add this component to the same GameObject as an <see cref="RoachRace.Controls.IPlayerInventory" /> implementation
    ///   (typically <c>NetworkPlayerInventory</c>), or assign it via <see cref="inventoryBehaviour" />.
    /// - Assign <see cref="useItemAction"/> and (optionally) <see cref="digitSelectedAction"/>.
    ///
    /// Behavior:
    /// - Digits 1..9 map to slot indices 0..8.
    /// - UseItem triggers <see cref="IPlayerInventory.TryUseSelected" /> on rising edge (press), not every frame while held.
    /// </summary>
    public sealed class OwnedInventoryInputForwarder : NetworkBehaviour
    {
        private const float UseItemPressedThreshold = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logBinding = false;
        [SerializeField] private bool logInputEvents = false;

        [Header("Input")]
        [Tooltip("Action used to trigger using the currently selected item.")]
        [SerializeField] private InputActionReference useItemAction;

        [Tooltip("Optional. Action which provides 1..9 as a float value to select slots.")]
        [SerializeField] private InputActionReference digitSelectedAction;

        [Header("Dependencies")]
        [Tooltip("Component implementing IPlayerInventory (eg NetworkPlayerInventory). Optional; will be auto-resolved if null.")]
        [SerializeField] private NetworkPlayerInventory _inventory;

        private InputAction _useAction;
        private InputAction _digitAction;

        private bool _useWasPressed;

        private void Awake()
        {
            if (_inventory == null)
            {
                _inventory = GetComponentInParent<NetworkPlayerInventory>();
            }

            if (_inventory == null)
            {
                Debug.LogError($"[{nameof(OwnedInventoryInputForwarder)}] No NetworkPlayerInventory found on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(OwnedInventoryInputForwarder)}] NetworkPlayerInventory is null on GameObject '{gameObject.name}'.");
            }
        }

        private void OnDisable()
        {
            UnbindActions();
            _useWasPressed = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshBinding();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            RefreshBinding();
        }

        public override void OnStopClient()
        {
            UnbindActions();
            base.OnStopClient();
        }

        private void RefreshBinding()
        {
            if (!IsClientInitialized)
                return;

            if (IsOwner)
                BindActions();
            else
                UnbindActions();

            if (logBinding)
            {
                string useName = useItemAction != null && useItemAction.action != null ? useItemAction.action.name : "<null>";
                string digitName = digitSelectedAction != null && digitSelectedAction.action != null ? digitSelectedAction.action.name : "<null>";
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] RefreshBinding on '{gameObject.name}': IsOwner={IsOwner} OwnerId={OwnerId} Use={useName} boundUse={_useAction != null} Digit={digitName} boundDigit={_digitAction != null}", gameObject);
            }

            _useWasPressed = false;
        }

        private void BindActions()
        {
            BindUseAction();
            BindDigitAction();
        }

        private void UnbindActions()
        {
            UnbindUseAction();
            UnbindDigitAction();
        }

        private void BindUseAction()
        {
            if (_useAction != null)
                return;

            if (useItemAction == null)
            {
                Debug.LogWarning($"[{nameof(OwnedInventoryInputForwarder)}] No UseItem InputActionReference assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            var action = useItemAction.action;
            if (action == null)
            {
                Debug.LogWarning($"[{nameof(OwnedInventoryInputForwarder)}] useItemAction has no action assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            _useAction = action;

            // Note: The project may also have a separate RoachRaceInputActionsHost instance which enables
            // its own generated InputActionAsset. InputActionReference actions come from a different asset
            // and will not fire unless they are enabled too.
            if (!_useAction.enabled)
            {
                _useAction.Enable();
                if (logBinding)
                    Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Enabled Use '{_useAction.name}' (assetInstanceId={_useAction.actionMap?.asset?.GetInstanceID()}).", gameObject);
            }

            _useAction.performed += OnUseActionChanged;
            _useAction.canceled += OnUseActionChanged;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Bound Use '{_useAction.name}' on '{gameObject.name}'.", gameObject);
        }

        private void UnbindUseAction()
        {
            if (_useAction == null)
                return;

            _useAction.performed -= OnUseActionChanged;
            _useAction.canceled -= OnUseActionChanged;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Unbound Use '{_useAction.name}' on '{gameObject.name}'.", gameObject);
            _useAction = null;
        }

        private void BindDigitAction()
        {
            if (_digitAction != null)
                return;

            if (digitSelectedAction == null)
                return;

            var action = digitSelectedAction.action;
            if (action == null)
                return;

            _digitAction = action;

            if (!_digitAction.enabled)
            {
                _digitAction.Enable();
                if (logBinding)
                    Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Enabled DigitSelected '{_digitAction.name}' (assetInstanceId={_digitAction.actionMap?.asset?.GetInstanceID()}).", gameObject);
            }

            _digitAction.performed += OnDigitActionPerformed;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Bound DigitSelected '{_digitAction.name}' on '{gameObject.name}'.", gameObject);
        }

        private void UnbindDigitAction()
        {
            if (_digitAction == null)
                return;

            _digitAction.performed -= OnDigitActionPerformed;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Unbound DigitSelected '{_digitAction.name}' on '{gameObject.name}'.", gameObject);
            _digitAction = null;
        }

        private void OnUseActionChanged(InputAction.CallbackContext ctx)
        {
            if (logInputEvents)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Action '{ctx.action.name}' phase={ctx.phase} enabled={ctx.action.enabled} IsOwner={IsOwner} OwnerId={OwnerId}", gameObject);

            if (!IsOwner || !IsClientInitialized)
                return;

            float value;
            try
            {
                value = ctx.ReadValue<float>();
            }
            catch
            {
                value = ctx.ReadValueAsButton() ? 1f : 0f;
            }

            bool pressed = value > UseItemPressedThreshold;

            // Only trigger on rising edge.
            if (pressed && !_useWasPressed)
            {
                var cam = Camera.main;
                if (cam != null)
                    _inventory.TryUseSelected(cam.transform.position, cam.transform.forward);
                else
                    _inventory.TryUseSelected(transform.position, transform.forward);
            }

            _useWasPressed = pressed;
        }

        private void OnDigitActionPerformed(InputAction.CallbackContext ctx)
        {
            if (logInputEvents)
                Debug.Log($"[{nameof(OwnedInventoryInputForwarder)}] Action '{ctx.action.name}' phase={ctx.phase} enabled={ctx.action.enabled} IsOwner={IsOwner} OwnerId={OwnerId}", gameObject);

            if (!IsOwner || !IsClientInitialized)
                return;

            int digit;
            try
            {
                digit = Mathf.RoundToInt(ctx.ReadValue<float>());
            }
            catch
            {
                return;
            }

            if (digit <= 0)
                return;

            int slotIndex = digit - 1;
            _inventory.TrySelectSlot(slotIndex);
        }
    }
}
