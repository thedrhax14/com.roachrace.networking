using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    [CreateAssetMenu(
        fileName = "NetworkInventoryDropInputListenerConfig",
        menuName = "RoachRace/Networking/Input/Network Inventory Drop Input Listener Config")]
    public sealed class NetworkInventoryDropInputListenerConfig : ScriptableObject
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

        public bool LogBinding => logBinding;
        public bool LogInputEvents => logInputEvents;

        public InputActionReference InputActionReference => inputActionReference;
        public float PressedThreshold => pressedThreshold;

#if UNITY_EDITOR
        private void OnValidate()
        {
            pressedThreshold = Mathf.Clamp01(pressedThreshold);
        }
#endif
    }
}
