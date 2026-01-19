using FishNet.Connection;
using FishNet.Object;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Listens to an InputAction and triggers <see cref="NetworkPlayerInventory.TryUseByItemId" /> for a configured item id.
    ///
    /// Setup:
    /// - Add this component to a player object with <see cref="NetworkPlayerInventory" /> (or under it as a child).
    /// - Assign <see cref="config" /> (holds item id, input action, thresholds, prompt settings, etc).
    ///
    /// Notes:
    /// - This only binds input for the owning client (FishNet ownership).
    /// - Optional hold and auto-repeat behavior are configured in the ScriptableObject.
    /// </summary>
    public sealed class NetworkItemUseInputListener : NetworkBehaviour
    {
        [Header("Config")]
        [Tooltip("Non-scene settings for this input forwarder (required).")]
        [SerializeField] private NetworkItemUseInputListenerConfig config;

        private InputAction _boundAction;

        [Header("Aim (for aim-required items)")]
        [Tooltip("Optional override for aim ray source. If null, uses Camera.main when available, otherwise this transform.")]
        [SerializeField] private Transform aimTransform;

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

            // Help catch missing config early. Avoid log spam by warning once per Editor session per instance.
            string warnKey = $"{nameof(NetworkItemUseInputListener)}.{GetInstanceID()}.MissingConfigWarned";
            if (config == null)
            {
                if (!SessionState.GetBool(warnKey, false))
                {
                    SessionState.SetBool(warnKey, true);
                    Debug.LogWarning($"[{nameof(NetworkItemUseInputListener)}] Config is not assigned on '{gameObject.name}'.", gameObject);
                }
                return;
            }

            // If it becomes valid again, allow future warnings if it is later cleared.
            SessionState.SetBool(warnKey, false);

            if (config == null) return;
            if (config.ItemDefinition == null) return;
            if (config.ItemDefinition.id == 0) return;
            gameObject.name = $"UseItem-{config.ItemDefinition.displayName}";
        }
#endif

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[{nameof(NetworkItemUseInputListener)}] Config is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkItemUseInputListener)}] Config is null on GameObject '{gameObject.name}'.");
            }

            if (config.InputActionReference == null)
            {
                Debug.LogError($"[{nameof(NetworkItemUseInputListener)}] Config.InputActionReference is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkItemUseInputListener)}] Config.InputActionReference is null on GameObject '{gameObject.name}'.");
            }

            if (inventory == null)
                inventory = GetComponentInParent<NetworkPlayerInventory>();
            if (inventory == null)
            {
                Debug.LogError($"[{nameof(NetworkItemUseInputListener)}] NetworkPlayerInventory is not assigned and was not found on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkItemUseInputListener)}] NetworkPlayerInventory is null on GameObject '{gameObject.name}'.");
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

            if (config.LogBinding)
            {
                string actionName = config.InputActionReference != null && config.InputActionReference.action != null
                    ? config.InputActionReference.action.name
                    : "<null>";
                Debug.Log($"[{nameof(NetworkItemUseInputListener)}] RefreshBinding on '{gameObject.name}': IsOwner={IsOwner} OwnerId={OwnerId} action={actionName} bound={_boundAction != null}", gameObject);
            }

            ResetHoldState();
            ResetPromptState();
        }

        private void BindInputAction()
        {
            if (_boundAction != null)
                return;

            if (config == null || config.InputActionReference == null)
            {
                Debug.LogWarning($"[{nameof(NetworkItemUseInputListener)}] No Config/InputActionReference assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            InputAction action = config.InputActionReference.action;
            if (action == null)
            {
                Debug.LogWarning($"[{nameof(NetworkItemUseInputListener)}] inputActionReference has no action assigned.", gameObject);
                return;
            }

            _boundAction = action;

            // Important: InputActionReferences point at the project's InputActionAsset instance.
            // If only RoachRaceInputActionsHost is enabled, it may be enabling a different generated asset,
            // so we ensure this action is enabled as well (we never Disable() here to avoid shared-action conflicts).
            if (!_boundAction.enabled)
            {
                _boundAction.Enable();
                if (config.LogBinding)
                    Debug.Log($"[{nameof(NetworkItemUseInputListener)}] Enabled '{_boundAction.name}' (assetInstanceId={_boundAction.actionMap?.asset?.GetInstanceID()}).", gameObject);
            }

            _boundAction.performed += OnBoundActionChanged;
            _boundAction.canceled += OnBoundActionChanged;

            if (config.LogBinding)
                Debug.Log($"[{nameof(NetworkItemUseInputListener)}] Bound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);
        }

        private void UnbindInputAction()
        {
            if (_boundAction == null)
                return;

            _boundAction.performed -= OnBoundActionChanged;
            _boundAction.canceled -= OnBoundActionChanged;

            if (config != null && config.LogBinding)
                Debug.Log($"[{nameof(NetworkItemUseInputListener)}] Unbound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);

            _boundAction = null;
        }

        private void OnBoundActionChanged(InputAction.CallbackContext ctx)
        {
            if (config != null && config.LogInputEvents)
                Debug.Log($"[{nameof(NetworkItemUseInputListener)}] Action '{ctx.action.name}' phase={ctx.phase} enabled={ctx.action.enabled} IsOwner={IsOwner} OwnerId={OwnerId}", gameObject);

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

            float threshold = config != null ? config.PressedThreshold : 0.1f;
            HandlePressed(value > threshold);
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
                float holdSeconds = config != null ? config.HoldDurationSeconds : 0f;
                if (holdSeconds <= 0f)
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

            if (config == null || config.InputActionReference == null)
                return;

            UpdatePromptModel();

            if (!_isHeld)
                return;

            float now = Time.unscaledTime;

            // Wait until the hold duration elapses before first trigger.
            if (!_hasTriggeredThisHold)
            {
                if (now - _holdStartTime >= config.HoldDurationSeconds)
                    TriggerOnceOrScheduleRepeat();

                return;
            }

            // After first trigger, handle repeat if enabled.
            if (config.AutoRepeat && now >= _nextRepeatTime)
            {
                TriggerInternal();
                _nextRepeatTime = now + Mathf.Max(0.01f, config.RepeatIntervalSeconds);
            }
        }

        private void UpdatePromptModel()
        {
            if (config == null)
                return;

            ActionPromptModel promptModel = config.PromptModel;
            if (promptModel == null)
                return;

            // Initialize static fields once per ownership/subscription to avoid UI churn.
            if (!_promptInitialized)
            {
                _promptInitialized = true;
                promptModel.SetVisible(true);
                promptModel.SetKey(config.PromptKeyIcon, config.PromptKeyText);

                string actionName = !string.IsNullOrWhiteSpace(config.PromptActionName)
                    ? config.PromptActionName
                    : (config.ItemDefinition != null ? config.ItemDefinition.displayName : string.Empty);
                promptModel.SetActionName(actionName);

                _lastHoldProgress01 = -1f;
                _lastUsesLeft = int.MinValue;
                _nextUsesLeftRefreshTime = 0f;
            }

            // Uses-left (throttled)
            if (config.ShowUsesLeft)
            {
                float now = Time.unscaledTime;
                if (now >= _nextUsesLeftRefreshTime)
                {
                    _nextUsesLeftRefreshTime = now + Mathf.Max(0.05f, config.UsesLeftRefreshSeconds);
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
                    if (config.HoldDurationSeconds > 0f)
                        progress01 = Mathf.Clamp01((now - _holdStartTime) / Mathf.Max(0.0001f, config.HoldDurationSeconds));
                }
                else if (config.AutoRepeat)
                {
                    float interval = Mathf.Max(0.01f, config.RepeatIntervalSeconds);
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
            if (config == null) return -1;
            if (config.ItemId == 0) return -1;

            // If we have a definition and it doesn't consume inventory on use, "uses left" might be meaningless.
            // Still allow showing if explicitly enabled (showUsesLeft).
            int total = 0;
            var slots = inventory.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.IsEmpty) continue;
                if (s.ItemId != config.ItemId) continue;
                total += s.Count;
            }

            return total;
        }

        private void TriggerOnceOrScheduleRepeat()
        {
            TriggerInternal();
            _hasTriggeredThisHold = true;

            if (config != null && config.AutoRepeat)
                _nextRepeatTime = Time.unscaledTime + Mathf.Max(0.01f, config.RepeatIntervalSeconds);
        }

        private void TriggerInternal()
        {
            if (config == null)
                return;

            if (config.ItemId == 0)
            {
                Debug.LogWarning($"[{nameof(NetworkItemUseInputListener)}] itemId is 0 (reserved). Assign an ItemDefinition or a non-zero id.", gameObject);
                return;
            }

            // Server-authoritative behavior is handled by NetworkPlayerInventory via RPCs when not server.
            if (config.UseAimRay && TryGetAimRay(out Vector3 origin, out Vector3 direction))
                inventory.TryUseByItemId(config.ItemId, origin, direction);
            else
                inventory.TryUseByItemId(config.ItemId);
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

            ActionPromptModel promptModel = config.PromptModel;

            if (!IsOwner || config == null || config.InputActionReference == null)
            {
                promptModel?.Clear();
                return;
            }

            if (promptModel != null)
            {
                promptModel.SetVisible(true);
                promptModel.SetKey(config.PromptKeyIcon, config.PromptKeyText);
                promptModel.SetHoldProgress(0f);
            }
        }

        private void OnDisable()
        {
            // If this component is toggled off while owned, make sure we unbind and clear UI.
            if (IsClientInitialized)
                UnbindInputAction();

            ResetHoldState();
            ActionPromptModel promptModel = config.PromptModel;
            promptModel?.Clear();
        }
    }
}
