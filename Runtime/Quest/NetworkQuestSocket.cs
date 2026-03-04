using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking.Quest
{
    [DisallowMultipleComponent]
    public sealed class NetworkQuestSocket : NetworkBehaviour
    {
        [Header("Requirements")]
        [Tooltip("Item id required to be deposited into this socket.")]
        [SerializeField] private ushort requiredItemId;

        [Tooltip("How many deposits are required before completion.")]
        [SerializeField, Min(1)] private byte requiredCount = 1;

        [Tooltip("How long a deposit takes once started (server-timed, replicated).")]
        [SerializeField, Min(0.05f)] private float useDurationSeconds = 2.0f;

        [Header("Door")]
        [Tooltip("Optional. Door to open when the socket completes.")]
        [SerializeField] private NetworkQuestDoor door;

        private readonly SyncVar<byte> _depositedCount = new(0);
        private readonly SyncVar<bool> _busy = new(false);

        private readonly SyncTimer _useTimer = new();

        public ushort RequiredItemId => requiredItemId;
        public byte RequiredCount => requiredCount;
        public byte DepositedCount => _depositedCount.Value;
        public bool IsBusy => _busy.Value;

        private void Awake()
        {
            _useTimer.OnChange += OnUseTimerChanged;
        }

        private void OnDestroy()
        {
            _useTimer.OnChange -= OnUseTimerChanged;
        }

        private void Update()
        {
            if (!IsServerInitialized && !IsClientInitialized)
                return;

            if (_useTimer.Remaining > 0f)
                _useTimer.Update();
        }

        private void OnUseTimerChanged(SyncTimerOperation op, float prev, float next, bool asServer)
        {
            if (op != SyncTimerOperation.Finished)
                return;

            if (!asServer)
                return;

            if (!_busy.Value)
                return;

            ServerCompleteDeposit();
        }

        public bool ServerCanBeginDeposit(ushort itemId, out ItemUseFailReason reason)
        {
            if (!IsServerInitialized)
            {
                reason = ItemUseFailReason.ServerRejected;
                return false;
            }

            if (requiredItemId == 0)
            {
                reason = ItemUseFailReason.InvalidItemId;
                return false;
            }

            if (itemId != requiredItemId)
            {
                reason = ItemUseFailReason.InvalidTarget;
                return false;
            }

            if (_busy.Value)
            {
                reason = ItemUseFailReason.TargetBusy;
                return false;
            }

            if (_depositedCount.Value >= Mathf.Max(1, requiredCount))
            {
                reason = ItemUseFailReason.NotUsableNow;
                return false;
            }

            reason = ItemUseFailReason.None;
            return true;
        }

        [Server]
        public bool ServerBeginDeposit(NetworkPlayerInventory inventory, ushort itemId, out ItemUseFailReason reason)
        {
            if (!ServerCanBeginDeposit(itemId, out reason))
                return false;

            if (inventory == null)
            {
                reason = ItemUseFailReason.ServerRejected;
                return false;
            }

            // Non-interruptible: consume immediately so completion is guaranteed even if the player disconnects.
            int consumed = inventory.ConsumeByItemId(itemId, amount: 1);
            if (consumed <= 0)
            {
                reason = ItemUseFailReason.NotInInventory;
                return false;
            }

            _busy.Value = true;
            _useTimer.StartTimer(useDurationSeconds);

            reason = ItemUseFailReason.None;
            return true;
        }

        [Server]
        private void ServerCompleteDeposit()
        {
            if (!_busy.Value)
                return;

            // Stop any active timer so it cannot finish again.
            if (_useTimer.Remaining > 0f)
                _useTimer.StopTimer(sendRemaining: false);

            _busy.Value = false;

            byte required = (byte)Mathf.Max(1, requiredCount);
            if (_depositedCount.Value < required)
                _depositedCount.Value++;

            if (_depositedCount.Value >= required)
            {
                if (door != null)
                    door.ServerOpen();
            }
        }
    }
}
