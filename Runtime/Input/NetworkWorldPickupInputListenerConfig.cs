using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    [CreateAssetMenu(
        fileName = "NetworkWorldPickupInputListenerConfig",
        menuName = "RoachRace/Networking/Input/Network World Pickup Input Listener Config")]
    public sealed class NetworkWorldPickupInputListenerConfig : ScriptableObject
    {
        private const float DefaultPressedThreshold = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logBinding = false;
        [SerializeField] private bool logInputEvents = false;

        [Header("Input")]
        [Tooltip("Input Action Reference to use (required).")]
        [SerializeField] private InputActionReference inputActionReference;

        [Tooltip("Analog press threshold.")]
        [SerializeField, Range(0f, 1f)] private float pressedThreshold = DefaultPressedThreshold;

        [Header("Raycast")]
        [SerializeField, Min(0.1f)] private float maxDistance = 3f;
        [SerializeField] private LayerMask pickupMask = ~0;

        public bool LogBinding => logBinding;
        public bool LogInputEvents => logInputEvents;

        public InputActionReference InputActionReference => inputActionReference;
        public float PressedThreshold => pressedThreshold;

        public float MaxDistance => maxDistance;
        public LayerMask PickupMask => pickupMask;

#if UNITY_EDITOR
        private void OnValidate()
        {
            pressedThreshold = Mathf.Clamp01(pressedThreshold);
            maxDistance = Mathf.Max(0.1f, maxDistance);
        }
#endif
    }
}
