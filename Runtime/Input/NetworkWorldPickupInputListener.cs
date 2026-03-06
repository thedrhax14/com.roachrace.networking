using FishNet.Object;
using RoachRace.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Owner-only input listener which triggers a world pickup attempt using an aim ray.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkWorldPickupInputListener : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField] private NetworkWorldPickupInputListenerConfig config;

        [Header("Dependencies")]
        [Tooltip("If not assigned, will search on the same GameObject at runtime.")]
        [SerializeField] private NetworkWorldPickupInteractor pickupInteractor;

        [Tooltip("Optional. Used for the aim ray; falls back to Camera.main or this transform.")]
        [SerializeField] private Transform aimTransform;

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

            pickupInteractor ??= GetComponent<NetworkWorldPickupInteractor>();
            if (pickupInteractor == null)
            {
                Debug.LogError($"[{nameof(NetworkWorldPickupInputListener)}] Missing {nameof(NetworkWorldPickupInteractor)} on '{gameObject.name}'.", gameObject);
                return;
            }

            pickupInteractor.ConfigureRaycast(config.MaxDistance, config.PickupMask);

            _boundAction = config.InputActionReference.action;
            if (_boundAction == null)
                return;

            _boundAction.performed += OnAction;
            _boundAction.canceled += OnAction;
            _boundAction.Enable();

            if (config.LogBinding)
                Debug.Log($"[{nameof(NetworkWorldPickupInputListener)}] Bound '{_boundAction.name}' on '{gameObject.name}'.", gameObject);
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
                // Common cases: button (0/1) or trigger/axis.
                try { value = ctx.ReadValue<float>(); } catch { value = ctx.ReadValueAsButton() ? 1f : 0f; }
            }

            if (config.LogInputEvents)
                Debug.Log($"[{nameof(NetworkWorldPickupInputListener)}] Action '{ctx.action.name}' phase={ctx.phase} value={value}", gameObject);

            if (ctx.phase != InputActionPhase.Performed) return;
            if (value < config.PressedThreshold) return;

            if (!TryGetAimRay(out Vector3 origin, out Vector3 direction))
                return;

            pickupInteractor.TryPickup(origin, direction);
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
    }
}
