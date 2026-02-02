using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using RoachRace.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking
{
    /// <summary>
    /// Predicted Rigidbody-based "human" motor using FishNet prediction.
    /// Movement is driven by AddForce and yaw-only rotation is driven by MoveRotation.
    /// </summary>
    public class PredictedHumanMotor : TickNetworkBehaviour
    {
        #region Types
        public struct ReplicateData : IReplicateData
        {
            public Vector2 Move;
            public float Yaw;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public PredictionRigidbody Root;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;

        [Header("Tuning")]
        [SerializeField] private float speed = 3f;
        [SerializeField] private float turnSpeed = 720f; // deg/sec

        [Header("Constraints")]
        [SerializeField] private bool lockPitchAndRoll = true;
        
        private HumanCameraController view;
        private float _bodyYaw;

        private Rigidbody rb;

        private readonly PredictionRigidbody _root = new();
        private float _dt;

        private void Awake()
        {
            if (rb == null && !TryGetComponent(out rb))
            {
                Debug.LogError($"[{nameof(PredictedHumanMotor)}] Rigidbody is not assigned and was not found on GameObject '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }

            if (moveAction == null || moveAction.action == null)
            {
                Debug.LogError($"[{nameof(PredictedHumanMotor)}] Move InputActionReference is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] moveAction is null on '{gameObject.name}'.");
            }

            _root.Initialize(rb);

            if (lockPitchAndRoll)
                rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            // Rigidbodies need Tick + PostTick.
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
            _dt = (float)TimeManager.TickDelta;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (!IsClientInitialized)
                return;

            if (IsOwner)
                EnableInput();
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
            CreateReconcile();
        }

        private void EnableInput()
        {
            moveAction.action.Enable();
            view = FindAnyObjectByType<HumanCameraController>();
        }

        private ReplicateData BuildMoveData()
        {
            if (!IsOwner)
                return default;

            Vector2 move = moveAction.action.ReadValue<Vector2>();
            float targetYaw = view.Yaw;

            _bodyYaw = Mathf.MoveTowardsAngle(
                _bodyYaw,
                targetYaw,
                turnSpeed * _dt
            );

            return new ReplicateData
            {
                Move = move,
                Yaw = _bodyYaw
            };
        }

        public override void CreateReconcile()
        {
            ReconcileData rd = new() { Root = _root };

            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            _dt = (float)TimeManager.TickDelta;

            _root.MoveRotation(Quaternion.Euler(0f, rd.Yaw, 0f));

            _root.Simulate();
            
            Vector2 moveInput = Vector2.ClampMagnitude(rd.Move, 1f);
            float inputMag = Mathf.Clamp01(moveInput.magnitude);
            if (inputMag <= 0.0001f) return;

            float yaw = rb.rotation.eulerAngles.y;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 moveWorld = yawRot * new Vector3(moveInput.x, 0f, moveInput.y);

            // ForceMode.Acceleration makes this independent of mass.
            _root.Velocity(moveWorld * (speed * inputMag));

            _root.Simulate();
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _root.Reconcile(rd.Root);
        }
    }
}
