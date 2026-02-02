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

        [Header("References")]
        [SerializeField] private SurvivorRemoteAnimator survivorRemoteAnimator;
        [SerializeField] private HumanCameraController view;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;

        [Header("Tuning")]
        [SerializeField] private float walkSpeed = 1.3f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float turnSpeed = 720f; // deg/sec

        [Header("Step Climb")]

        [Tooltip("Maximum step height to climb.")]
        [SerializeField, Min(0f)] private float stepHeight = 0.35f;

        [Tooltip("How far ahead to check for a step.")]
        [SerializeField, Min(0f)] private float stepCheckDistance = 0.35f;

        [Tooltip("Upward speed applied when stepping up.")]
        [SerializeField, Min(0f)] private float stepUpSpeed = 4f;

        [Tooltip("Which layers count as step obstacles.")]
        [SerializeField] private LayerMask stepMask = ~0;

        [Tooltip("How far down to check for ground before allowing stepping.")]
        [SerializeField, Min(0f)] private float groundCheckDistance = 0.25f;

        [Tooltip("If 0, an automatic radius based on the collider will be used.")]
        [SerializeField, Min(0f)] private float stepCheckRadiusOverride = 0f;

        [Header("Constraints")]
        [SerializeField] private bool lockPitchAndRoll = true;
        
        private float _bodyYaw;

        private Rigidbody rb;
        private Collider _bodyCollider;
        private float _stepCheckRadius;

        private readonly PredictionRigidbody _root = new();
        private float _dt;

        private void Awake()
        {
            if (rb == null && !TryGetComponent(out rb))
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }

            if (!TryGetComponent(out _bodyCollider) || _bodyCollider == null)
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] Collider is null on GameObject '{gameObject.name}'. A Collider is required for stepping.");
            }

            if (moveAction == null || moveAction.action == null)
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] moveAction is null on '{gameObject.name}'.");
            }

            if(!TryGetComponent(out survivorRemoteAnimator))
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] SurvivorRemoteAnimator is null on '{gameObject.name}'.");
            }

            _root.Initialize(rb);

            // Default step check radius from the collider when possible.
            if (stepCheckRadiusOverride > 0f)
            {
                _stepCheckRadius = stepCheckRadiusOverride;
            }
            else if (_bodyCollider is CapsuleCollider capsule)
            {
                _stepCheckRadius = Mathf.Max(0.02f, capsule.radius * 0.9f);
            }
            else if (_bodyCollider is SphereCollider sphere)
            {
                _stepCheckRadius = Mathf.Max(0.02f, sphere.radius * 0.9f);
            }
            else
            {
                // Conservative fallback.
                _stepCheckRadius = 0.15f;
            }

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
            view.enabled = IsOwner;
            if (IsOwner)
                EnableInput();
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
            if(IsOwner)
            {
                survivorRemoteAnimator.SetPitch(view.Pitch);
                survivorRemoteAnimator.SetYaw(view.Yaw);
            }
            CreateReconcile();
        }

        private void EnableInput()
        {
            moveAction.action.Enable();
        }

        private ReplicateData BuildMoveData()
        {
            if (!IsOwner)
                return default;

            Vector2 move = moveAction.action.ReadValue<Vector2>();

            _bodyYaw = Mathf.MoveTowardsAngle(
                _bodyYaw,
                view.Yaw,
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

            // Game design: run only when there is forward input.
            // Backwards or direct left/right (no forward component) forces walk speed.
            const float axisDeadZone = 0.01f;
            bool hasForwardInput = moveInput.y > axisDeadZone;
            float moveSpeed = hasForwardInput ? runSpeed : walkSpeed;

            float yaw = rb.rotation.eulerAngles.y;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 moveWorld = yawRot * new Vector3(moveInput.x, 0f, moveInput.y);

            // Preserve vertical velocity from physics (gravity/jumps/steps).
            Vector3 currentVel = rb.linearVelocity;
            Vector3 desiredVel = moveWorld * (moveSpeed * inputMag);
            desiredVel.y = currentVel.y;

            TryApplyStepUp(ref desiredVel);  

            // ForceMode.Acceleration makes this independent of mass.
            _root.Velocity(desiredVel);

            _root.Simulate();
        }

        private void TryApplyStepUp(ref Vector3 desiredVelocity)
        {
            if (stepHeight <= 0f || stepCheckDistance <= 0f || stepUpSpeed <= 0f)
                return;

            Vector3 planarVel = Vector3.ProjectOnPlane(desiredVelocity, Vector3.up);
            if (planarVel.sqrMagnitude <= 0.0001f)
                return;

            // Only attempt stepping when grounded-ish.
            // IMPORTANT: rb.position is usually the collider center, not the feet.
            Bounds bounds = _bodyCollider.bounds;
            float footY = bounds.min.y;
            Vector3 basePos = new Vector3(bounds.center.x, footY + _stepCheckRadius + 0.02f, bounds.center.z);

            bool grounded = Physics.Raycast(
                basePos,
                Vector3.down,
                groundCheckDistance,
                stepMask,
                QueryTriggerInteraction.Ignore
            );
            if (!grounded)
                return;

            Vector3 dir = planarVel.normalized;

            // Lower cast hits the obstacle; upper cast must be clear.
            float lowerHeight = 0.05f;
            float upperHeight = stepHeight + 0.05f;

            Vector3 lowerOrigin = basePos + Vector3.up * lowerHeight;
            Vector3 upperOrigin = basePos + Vector3.up * upperHeight;

            bool lowerHit = Physics.SphereCast(
                lowerOrigin,
                _stepCheckRadius,
                dir,
                out _,
                stepCheckDistance,
                stepMask,
                QueryTriggerInteraction.Ignore
            );

            if (!lowerHit)
                return;

            bool upperHit = Physics.SphereCast(
                upperOrigin,
                _stepCheckRadius,
                dir,
                out _,
                stepCheckDistance,
                stepMask,
                QueryTriggerInteraction.Ignore
            );

            if (upperHit)
                return;

            // Clear above but blocked below => step up.
            desiredVelocity.y = Mathf.Max(desiredVelocity.y, stepUpSpeed);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _root.Reconcile(rd.Root);
        }
    }
}
