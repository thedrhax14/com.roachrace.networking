using FishNet.Object;
using RoachRace.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Owner-only input listener which drops the currently selected inventory stack.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkInventoryDropInputListener : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField] private NetworkInventoryDropInputListenerConfig config;

        [Header("Dependencies")]
        [Tooltip("If not assigned, will search on the same GameObject at runtime.")]
        [SerializeField] private NetworkInventoryDropper dropper;

        private InputAction _boundAction;

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshBinding();
        }

        public override void OnStopClient()
        {
            Unbind();
            base.OnStopClient();
        }

        private void OnEnable()
        {
            if (IsClientInitialized)
                RefreshBinding();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void RefreshBinding()
        {
            Unbind();

            if (!IsOwner) return;
            if (config == null || config.InputActionReference == null)
                return;

            dropper ??= GetComponent<NetworkInventoryDropper>();
            if (dropper == null)
            {
                Debug.LogError($"[{nameof(NetworkInventoryDropInputListener)}] Missing {nameof(NetworkInventoryDropper)} on '{gameObject.name}'.", gameObject);
                return;
            }

            _boundAction = config.InputActionReference.action;
            if (_boundAction == null)
                return;

            _boundAction.performed += OnAction;
            _boundAction.canceled += OnAction;
            _boundAction.Enable();

            if (config.LogBinding)
                Debug.Log($"[{nameof(NetworkInventoryDropInputListener)}] Bound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);
        }

        private void Unbind()
        {
            if (_boundAction == null)
                return;

            _boundAction.performed -= OnAction;
            _boundAction.canceled -= OnAction;
            _boundAction.Disable();
            _boundAction = null;
        }

        private void OnAction(InputAction.CallbackContext ctx)
        {
            if (!IsOwner) return;
            if (config == null) return;

            float value = 0f;
            if (ctx.control != null)
            {
                try { value = ctx.ReadValue<float>(); } catch { value = ctx.ReadValueAsButton() ? 1f : 0f; }
            }

            if (config.LogInputEvents)
                Debug.Log($"[{nameof(NetworkInventoryDropInputListener)}] Action '{ctx.action.name}' phase={ctx.phase} value={value}", gameObject);

            if (ctx.phase != InputActionPhase.Performed) return;
            if (value < config.PressedThreshold) return;

            dropper.RequestDropSelectedStack();
        }
    }
}
