using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Utility.Template;
using UnityEngine;

namespace RoachRace.Networking.Quest
{
    /// <summary>
    /// Base class for networked doors which provides shared Animator presentation controls.
    /// Derived classes decide how "opening progress" is replicated (ticks, SyncTimer, etc).
    /// </summary>
    public abstract class NetworkDoorAnimatorBase : TickNetworkBehaviour
    {
        protected enum DoorState : byte
        {
            Locked = 0,
            Opening = 1,
            Open = 2,
        }

        [Header("Presentation (Optional)")]
        [Tooltip("Optional Animator which drives door open/close visuals.")]
        [SerializeField] private Animator animator;

        [Tooltip("Optional. Animator bool parameter set to true when the door is open (or opening).")]
        [SerializeField] private string openBoolParameter = "IsOpen";

        [Tooltip("Optional. Animator state name used to play/seek the door opening animation.")]
        [SerializeField] private string openStateName = "Open";

        [Tooltip("Animator layer index used for Play/seek.")]
        [SerializeField, Min(0)] private int animatorLayer = 0;

        [Header("Timing")]
        [Tooltip("Duration (seconds) of the door opening animation.")]
        [SerializeField, Min(0.05f)] private float openDurationSeconds = 1.0f;

        protected float OpenDurationSeconds => openDurationSeconds;

        protected uint GetOpenDurationTicks()
        {
            if (TimeManager == null)
                return 0u;

            uint ticks = TimeManager.TimeToTicks(openDurationSeconds, TickRounding.RoundUp);
            return ticks == 0u ? 1u : ticks;
        }

        protected void ApplyDoorPresentation(DoorState state, float openingNormalized)
        {
            if (animator == null)
                return;

            bool shouldBeOpenBool = state == DoorState.Open;
            if (!string.IsNullOrWhiteSpace(openBoolParameter)) {
                animator.SetBool(openBoolParameter, shouldBeOpenBool);
                // Debug.Log($"[{nameof(NetworkDoorAnimatorBase)}] Set Animator bool '{openBoolParameter}' to {shouldBeOpenBool} on '{gameObject.name}'", gameObject);
            }

            float normalized = state == DoorState.Open ? 1f : Mathf.Clamp01(openingNormalized);
            if (!string.IsNullOrWhiteSpace(openStateName))
            {
                animator.Play(openStateName, animatorLayer, normalized);
                animator.Update(0f);
                // Debug.Log($"[{nameof(NetworkDoorAnimatorBase)}] Playing Animator state '{openStateName}' at normalized time {normalized:F2} on '{gameObject.name}'", gameObject);
            }
        }
    }
}
