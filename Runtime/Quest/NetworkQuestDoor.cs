using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Utility.Template;
using UnityEngine;

namespace RoachRace.Networking.Quest
{
    [DisallowMultipleComponent]
    public sealed class NetworkQuestDoor : TickNetworkBehaviour
    {
        private enum DoorState : byte
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
        [Tooltip("Duration (seconds) of the door opening animation. Used for client catch-up/fast-forward.")]
        [SerializeField, Min(0.05f)] private float openDurationSeconds = 1.0f;

        private readonly SyncVar<DoorState> _state = new(DoorState.Locked);
        private readonly SyncVar<uint> _openStartTick = new(0u);

        public bool IsOpen => _state.Value == DoorState.Open;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick);
        }

        protected override void TimeManager_OnTick()
        {
            // Server: advance state to Open when the timer elapses.
            if (IsServerInitialized && _state.Value == DoorState.Opening)
            {
                uint startTick = _openStartTick.Value;
                uint durationTicks = GetOpenDurationTicks();
                uint nowTick = TimeManager != null ? TimeManager.Tick : 0u;

                if (durationTicks > 0u && nowTick >= startTick + durationTicks)
                {
                    _state.Value = DoorState.Open;
                    ApplyPresentationForCurrentState();
                }
            }

            // Client/host presentation: continuously seek so late packets and drift don't matter.
            if ((IsClientInitialized || IsServerInitialized) && _state.Value != DoorState.Locked)
                ApplyPresentationForCurrentState();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _state.OnChange += OnStateChanged;
            _openStartTick.OnChange += OnOpenStartTickChanged;
            ApplyPresentationForCurrentState();
        }

        public override void OnStopClient()
        {
            _state.OnChange -= OnStateChanged;
            _openStartTick.OnChange -= OnOpenStartTickChanged;
            base.OnStopClient();
        }

        private void OnStateChanged(DoorState prev, DoorState next, bool asServer)
        {
            if (asServer) return;
            ApplyPresentationForCurrentState();
        }

        private void OnOpenStartTickChanged(uint prev, uint next, bool asServer)
        {
            if (asServer) return;
            ApplyPresentationForCurrentState();
        }

        private uint GetOpenDurationTicks()
        {
            if (TimeManager == null)
                return 0u;

            uint ticks = TimeManager.TimeToTicks(openDurationSeconds, TickRounding.RoundUp);
            return ticks == 0u ? 1u : ticks;
        }

        private float GetOpeningNormalizedTime()
        {
            if (TimeManager == null)
                return 1f;

            uint startTick = _openStartTick.Value;
            uint nowTick = TimeManager.Tick;
            uint durationTicks = GetOpenDurationTicks();

            if (durationTicks == 0u)
                return 1f;

            // Handle wrap-around safely via unchecked subtraction.
            uint elapsedTicks = unchecked(nowTick - startTick);
            float t = elapsedTicks / (float)durationTicks;
            return Mathf.Clamp01(t);
        }

        private void ApplyPresentationForCurrentState()
        {
            if (animator == null)
                return;

            DoorState state = _state.Value;
            bool shouldBeOpenBool = state != DoorState.Locked;

            if (!string.IsNullOrWhiteSpace(openBoolParameter))
                animator.SetBool(openBoolParameter, shouldBeOpenBool);

            if (state == DoorState.Locked)
            {
                // Leave the Animator to whatever its controller does for "closed".
                animator.speed = 1f;
                return;
            }

            // Deterministic pose: pause and seek to the authoritative progress.
            animator.speed = 0f;

            float normalized = state == DoorState.Open ? 1f : GetOpeningNormalizedTime();
            if (!string.IsNullOrWhiteSpace(openStateName))
            {
                animator.Play(openStateName, animatorLayer, normalized);
                animator.Update(0f);
            }
        }

        [Server]
        public void ServerOpen()
        {
            if (!IsServerInitialized)
                return;

            if (_state.Value == DoorState.Open || _state.Value == DoorState.Opening)
                return;

            _openStartTick.Value = TimeManager != null ? TimeManager.Tick : 0u;
            _state.Value = DoorState.Opening;

            // Host needs immediate local presentation.
            ApplyPresentationForCurrentState();
        }
    }
}
