using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Utility.Template;
using UnityEngine;

namespace RoachRace.Networking.Quest
{
    [DisallowMultipleComponent]
    public sealed class NetworkQuestDoor : NetworkDoorAnimatorBase
    {
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
            DoorState state = _state.Value;
            float openingNormalized = state == DoorState.Open ? 1f : GetOpeningNormalizedTime();
            ApplyDoorPresentation(state, openingNormalized);
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
