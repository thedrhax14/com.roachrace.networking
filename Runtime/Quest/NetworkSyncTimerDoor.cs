using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RoachRace.Networking.Quest
{
    /// <summary>
    /// Door implementation which replicates opening progress using a SyncTimer.
    /// Useful when you prefer timer-based replication over tick-stamped start times.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSyncTimerDoor : NetworkDoorAnimatorBase
    {
        public float RemainingOpenTime;
        private readonly SyncVar<DoorState> _state = new(DoorState.Locked);
        private readonly SyncTimer _openTimer = new();

        private void Awake()
        {
            _openTimer.OnChange += OnOpenTimerChanged;
        }

        private void OnDestroy()
        {
            _openTimer.OnChange -= OnOpenTimerChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerOpen();
        }

        private void Update()
        {
            if (!IsServerInitialized && !IsClientInitialized)
                return;

            // Keep the SyncTimer progressing on both server and clients.
            if (_openTimer.Remaining > 0f)
                _openTimer.Update();
            RemainingOpenTime = _openTimer.Remaining;

            if (_state.Value != DoorState.Locked)
                ApplyPresentationForCurrentState();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _state.OnChange += OnStateChanged;
            ApplyPresentationForCurrentState();
        }

        public override void OnStopClient()
        {
            _state.OnChange -= OnStateChanged;
            base.OnStopClient();
        }

        private void OnStateChanged(DoorState prev, DoorState next, bool asServer)
        {
            if (asServer)
                return;

            ApplyPresentationForCurrentState();
        }

        private void OnOpenTimerChanged(SyncTimerOperation op, float prev, float next, bool asServer)
        {
            if (op != SyncTimerOperation.Finished)
                return;

            if (!asServer)
                return;

            if (_state.Value != DoorState.Opening)
                return;

            _state.Value = DoorState.Open;
            ApplyPresentationForCurrentState();
        }

        private float GetOpeningNormalizedTime()
        {
            if (_state.Value == DoorState.Open)
                return 1f;

            float duration = Mathf.Max(0.05f, OpenDurationSeconds);
            float remaining = Mathf.Max(0f, _openTimer.Remaining);
            float normalized = 1f - (remaining / duration);
            return Mathf.Clamp01(normalized);
        }

        private void ApplyPresentationForCurrentState()
        {
            DoorState state = _state.Value;
            float openingNormalized = state == DoorState.Locked ? 0f : GetOpeningNormalizedTime();
            ApplyDoorPresentation(state, openingNormalized);
        }

        [Server]
        public void ServerOpen()
        {
            if (!IsServerInitialized)
                return;

            if (_state.Value == DoorState.Open || _state.Value == DoorState.Opening)
                return;

            _state.Value = DoorState.Opening;
            _openTimer.StartTimer(OpenDurationSeconds);

            // Host needs immediate local presentation.
            ApplyPresentationForCurrentState();
        }
    }
}
