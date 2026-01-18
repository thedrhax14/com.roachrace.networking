using RoachRace.Interaction;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking.Input
{
    [CreateAssetMenu(
        fileName = "NetworkItemUseInputListenerConfig",
        menuName = "RoachRace/Networking/Input/Network Item Use Input Listener Config")]
    public sealed class NetworkItemUseInputListenerConfig : ScriptableObject
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

        public bool LogBinding => logBinding;
        public bool LogInputEvents => logInputEvents;

        public ItemDefinition ItemDefinition => itemDefinition;
        public ushort ItemId => itemId;

        public InputActionReference InputActionReference => inputActionReference;
        public float PressedThreshold => pressedThreshold;
        public float HoldDurationSeconds => holdDurationSeconds;
        public bool AutoRepeat => autoRepeat;
        public float RepeatIntervalSeconds => repeatIntervalSeconds;

        public bool UseAimRay => useAimRay;

        public ActionPromptModel PromptModel => promptModel;
        public Sprite PromptKeyIcon => promptKeyIcon;
        public string PromptKeyText => promptKeyText;
        public string PromptActionName => promptActionName;
        public bool ShowUsesLeft => showUsesLeft;
        public float UsesLeftRefreshSeconds => usesLeftRefreshSeconds;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (itemDefinition != null && itemDefinition.id != 0)
                itemId = itemDefinition.id;

            pressedThreshold = Mathf.Clamp01(pressedThreshold);
            holdDurationSeconds = Mathf.Max(0f, holdDurationSeconds);
            repeatIntervalSeconds = Mathf.Max(0.01f, repeatIntervalSeconds);
            usesLeftRefreshSeconds = Mathf.Max(0.05f, usesLeftRefreshSeconds);
        }
#endif
    }
}
