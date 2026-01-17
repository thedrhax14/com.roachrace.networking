using FishNet.Connection;
using FishNet.Object;
using RoachRace.Interaction;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Forwards a chosen input (Aim or UseItem) to use a specific inventory item id.
    ///
    /// Setup:
    /// - Add this component to a player object with <see cref="NetworkPlayerInventory" /> or under it as child.
    /// - Assign <see cref="itemDefinition" /> (recommended) or set <see cref="itemId" /> manually.
    /// - Assign <see cref="inputActionReference" />.
    ///
    /// Behavior:
    /// - When the configured input is pressed, this calls <see cref="NetworkPlayerInventory.TryUseByItemId" /> using <see cref="itemId" />.
    /// - Optional hold: require the input to be held for <see cref="holdDurationSeconds" /> before triggering.
    /// - Optional auto-repeat: if enabled, re-triggers every <see cref="repeatIntervalSeconds" /> while held.
    /// </summary>
    public sealed class OwnedUseSpecificItemInputForwarder : NetworkBehaviour
    {
        private const float DefaultPressedThreshold = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logBinding = false;
        [SerializeField] private bool logInputEvents = false;

        [Header("Item")]
        [Tooltip("Optional. Assign an ItemDefinition to keep itemId in sync.")]
        [SerializeField] private ItemDefinition itemDefinition;

        [Tooltip("ItemDefinition id to use. 0 is reserved for empty.")]
        [SerializeField] private ushort itemId;

        [Header("Input")]
        [Tooltip("Input Action Reference to use (required).")]
        [SerializeField] private InputActionReference inputActionReference;

        private InputAction _boundAction;

        [Tooltip("Analog press threshold for UseItem.")]
        [SerializeField, Range(0f, 1f)] private float pressedThreshold = DefaultPressedThreshold;

        [Tooltip("Seconds the input must be held before first trigger. 0 = immediate.")]
        [SerializeField, Min(0f)] private float holdDurationSeconds = 0f;

        [Tooltip("If true, re-triggers while held after the first trigger.")]
        [SerializeField] private bool autoRepeat = false;

        [Tooltip("Seconds between repeats while held (only used when Auto Repeat is enabled).")]
        [SerializeField, Min(0.01f)] private float repeatIntervalSeconds = 0.2f;

        [Header("Aim (for aim-required items)")]
        [Tooltip("If true, uses an aim ray (origin/direction) when invoking the inventory use call.")]
        [SerializeField] private bool useAimRay = true;

        [Tooltip("Optional override for aim ray source. If null, uses Camera.main when available, otherwise this transform.")]
        [SerializeField] private Transform aimTransform;

        [Header("Action Prompt (UI)")]
        [Tooltip("Optional. If assigned, this forwarder will update the model for an on-screen action prompt widget.")]
        [SerializeField] private ActionPromptModel promptModel;

        [Tooltip("Optional key icon (eg gamepad button or keyboard key image).")]
        [SerializeField] private Sprite promptKeyIcon;

        [Tooltip("Optional key text (eg 'E', 'LMB', 'RT').")]
        [SerializeField] private string promptKeyText;

        [Tooltip("Optional override for the displayed action name. If empty, uses ItemDefinition.displayName when available.")]
        [SerializeField] private string promptActionName;

        [Tooltip("If true, shows UsesLeft based on inventory counts for the configured item id.")]
        [SerializeField] private bool showUsesLeft = true;

        [Tooltip("How often to refresh uses-left while owned (seconds).")]
        [SerializeField, Min(0.05f)] private float usesLeftRefreshSeconds = 0.25f;

        [Header("Dependencies")]
        [Tooltip("Optional. If not assigned, will auto-resolve on Awake.")]
        [SerializeField] private NetworkPlayerInventory inventory;

        private bool _isHeld;
        private bool _hasTriggeredThisHold;
        private float _holdStartTime;
        private float _nextRepeatTime;

        private bool _promptInitialized;
        private float _nextUsesLeftRefreshTime;
        private int _lastUsesLeft = int.MinValue;
        private float _lastHoldProgress01 = -1f;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (itemDefinition == null) return;
            if (itemDefinition.id == 0) return;
            itemId = itemDefinition.id;
            gameObject.name = $"UseItem-{itemDefinition.displayName}";
        }
#endif

        private void Awake()
        {
            inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (inventory == null)
            {
                Debug.LogError($"[{nameof(OwnedUseSpecificItemInputForwarder)}] NetworkPlayerInventory is not assigned and was not found on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(OwnedUseSpecificItemInputForwarder)}] NetworkPlayerInventory is null on GameObject '{gameObject.name}'.");
            }
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
            UnbindInputAction();
            base.OnStopClient();
        }

        private void RefreshBinding()
        {
            if (!IsClientInitialized)
                return;

            if (IsOwner)
                BindInputAction();
            else
                UnbindInputAction();

            if (logBinding)
            {
                string actionName = inputActionReference != null && inputActionReference.action != null
                    ? inputActionReference.action.name
                    : "<null>";
                Debug.Log($"[{nameof(OwnedUseSpecificItemInputForwarder)}] RefreshBinding on '{gameObject.name}': IsOwner={IsOwner} OwnerId={OwnerId} action={actionName} bound={_boundAction != null}", gameObject);
            }

            ResetHoldState();
            ResetPromptState();
        }

        private void BindInputAction()
        {
            if (_boundAction != null)
                return;

            if (inputActionReference == null)
            {
                Debug.LogWarning($"[{nameof(OwnedUseSpecificItemInputForwarder)}] No InputActionReference assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            InputAction action = inputActionReference.action;
            if (action == null)
            {
                Debug.LogWarning($"[{nameof(OwnedUseSpecificItemInputForwarder)}] inputActionReference has no action assigned.", gameObject);
                return;
            }

            _boundAction = action;

            // Important: InputActionReferences point at the project's InputActionAsset instance.
            // If only RoachRaceInputActionsHost is enabled, it may be enabling a different generated asset,
            // so we ensure this action is enabled as well (we never Disable() here to avoid shared-action conflicts).
            if (!_boundAction.enabled)
            {
                _boundAction.Enable();
                if (logBinding)
                    Debug.Log($"[{nameof(OwnedUseSpecificItemInputForwarder)}] Enabled '{_boundAction.name}' (assetInstanceId={_boundAction.actionMap?.asset?.GetInstanceID()}).", gameObject);
            }

            _boundAction.performed += OnBoundActionChanged;
            _boundAction.canceled += OnBoundActionChanged;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedUseSpecificItemInputForwarder)}] Bound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);
        }

        private void UnbindInputAction()
        {
            if (_boundAction == null)
                return;

            _boundAction.performed -= OnBoundActionChanged;
            _boundAction.canceled -= OnBoundActionChanged;

            if (logBinding)
                Debug.Log($"[{nameof(OwnedUseSpecificItemInputForwarder)}] Unbound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);

            _boundAction = null;
        }

        private void OnBoundActionChanged(InputAction.CallbackContext ctx)
        {
            if (logInputEvents)
                Debug.Log($"[{nameof(OwnedUseSpecificItemInputForwarder)}] Action '{ctx.action.name}' phase={ctx.phase} enabled={ctx.action.enabled} IsOwner={IsOwner} OwnerId={OwnerId}", gameObject);

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

            HandlePressed(value > pressedThreshold);
        }

        private void HandlePressed(bool pressed)
        {
            if (pressed && !_isHeld)
            {
                _isHeld = true;
                _hasTriggeredThisHold = false;
                _holdStartTime = Time.unscaledTime;
                _nextRepeatTime = float.PositiveInfinity;

                // If no hold required, trigger immediately.
                if (holdDurationSeconds <= 0f)
                    TriggerOnceOrScheduleRepeat();
            }
            else if (!pressed && _isHeld)
            {
                _isHeld = false;
                _hasTriggeredThisHold = false;
                _nextRepeatTime = float.PositiveInfinity;
            }
        }

        private void Update()
        {
            if (!IsOwner || !IsClientInitialized)
                return;

            if (inputActionReference == null)
                return;

            UpdatePromptModel();

            if (!_isHeld)
                return;

            float now = Time.unscaledTime;

            // Wait until the hold duration elapses before first trigger.
            if (!_hasTriggeredThisHold)
            {
                if (now - _holdStartTime >= holdDurationSeconds)
                    TriggerOnceOrScheduleRepeat();

                return;
            }

            // After first trigger, handle repeat if enabled.
            if (autoRepeat && now >= _nextRepeatTime)
            {
                TriggerInternal();
                _nextRepeatTime = now + Mathf.Max(0.01f, repeatIntervalSeconds);
            }
        }

        private void UpdatePromptModel()
        {
            if (promptModel == null)
                return;

            // Initialize static fields once per ownership/subscription to avoid UI churn.
            if (!_promptInitialized)
            {
                _promptInitialized = true;
                promptModel.SetVisible(true);
                promptModel.SetKey(promptKeyIcon, promptKeyText);

                string actionName = !string.IsNullOrWhiteSpace(promptActionName)
                    ? promptActionName
                    : (itemDefinition != null ? itemDefinition.displayName : string.Empty);
                promptModel.SetActionName(actionName);

                _lastHoldProgress01 = -1f;
                _lastUsesLeft = int.MinValue;
                _nextUsesLeftRefreshTime = 0f;
            }

            // Uses-left (throttled)
            if (showUsesLeft)
            {
                float now = Time.unscaledTime;
                if (now >= _nextUsesLeftRefreshTime)
                {
                    _nextUsesLeftRefreshTime = now + Mathf.Max(0.05f, usesLeftRefreshSeconds);
                    int usesLeft = GetUsesLeft();
                    if (usesLeft != _lastUsesLeft)
                    {
                        _lastUsesLeft = usesLeft;
                        promptModel.SetUsesLeft(usesLeft);
                    }
                }
            }
            else if (_lastUsesLeft != -1)
            {
                _lastUsesLeft = -1;
                promptModel.SetUsesLeft(-1);
            }

            // Hold/repeat progress (only update when the computed value changes)
            float progress01 = 0f;
            if (_isHeld)
            {
                float now = Time.unscaledTime;

                if (!_hasTriggeredThisHold)
                {
                    if (holdDurationSeconds > 0f)
                        progress01 = Mathf.Clamp01((now - _holdStartTime) / Mathf.Max(0.0001f, holdDurationSeconds));
                }
                else if (autoRepeat)
                {
                    float interval = Mathf.Max(0.01f, repeatIntervalSeconds);
                    progress01 = 1f - Mathf.Clamp01((_nextRepeatTime - now) / interval);
                }
            }

            if (Mathf.Abs(progress01 - _lastHoldProgress01) > 0.0001f)
            {
                _lastHoldProgress01 = progress01;
                promptModel.SetHoldProgress(progress01);
            }
        }

        private int GetUsesLeft()
        {
            if (inventory == null) return -1;
            if (itemId == 0) return -1;

            // If we have a definition and it doesn't consume inventory on use, "uses left" might be meaningless.
            // Still allow showing if explicitly enabled (showUsesLeft).
            int total = 0;
            var slots = inventory.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != itemId) continue;
                total += s.Count;
            }

            return total;
        }

        private void TriggerOnceOrScheduleRepeat()
        {
            TriggerInternal();
            _hasTriggeredThisHold = true;

            if (autoRepeat)
                _nextRepeatTime = Time.unscaledTime + Mathf.Max(0.01f, repeatIntervalSeconds);
        }

        private void TriggerInternal()
        {
            if (itemId == 0)
            {
                Debug.LogWarning($"[{nameof(OwnedUseSpecificItemInputForwarder)}] itemId is 0 (reserved). Assign an ItemDefinition or a non-zero id.", gameObject);
                return;
            }

            // Server-authoritative behavior is handled by NetworkPlayerInventory via RPCs when not server.
            if (useAimRay && TryGetAimRay(out Vector3 origin, out Vector3 direction))
                inventory.TryUseByItemId(itemId, origin, direction);
            else
                inventory.TryUseByItemId(itemId);
        }

        private bool TryGetAimRay(out Vector3 origin, out Vector3 direction)
        {
            Transform t = aimTransform;
            if (t == null && Camera.main != null)
                t = Camera.main.transform;
            if (t == null)
                t = transform;

            origin = t.position;
            direction = t.forward;
            return true;
        }

        private void ResetHoldState()
        {
            _isHeld = false;
            _hasTriggeredThisHold = false;
            _nextRepeatTime = float.PositiveInfinity;
        }

        private void ResetPromptState()
        {
            _promptInitialized = false;
            _nextUsesLeftRefreshTime = 0f;
            _lastUsesLeft = int.MinValue;
            _lastHoldProgress01 = -1f;

            if (!IsOwner || inputActionReference == null)
            {
                promptModel?.Clear();
                return;
            }

            if (promptModel != null)
            {
                promptModel.SetVisible(true);
                promptModel.SetKey(promptKeyIcon, promptKeyText);
                promptModel.SetHoldProgress(0f);
            }
        }

        private void OnDisable()
        {
            // If this component is toggled off while owned, make sure we unbind and clear UI.
            if (IsClientInitialized)
                UnbindInputAction();

            ResetHoldState();
            promptModel?.Clear();
        }
    }
}
