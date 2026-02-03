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
    [RequireComponent(typeof(CapsuleCollider))]
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
            public bool Jump;

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
        protected CapsuleCollider capsuleCollider;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;

        [Header("Tuning")]
        [SerializeField] private float walkSpeed = 1.3f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float turnSpeed = 720f; // deg/sec

        [Header("Movement Forces")]
        [Tooltip("Maximum planar acceleration (m/s^2) applied while there is movement input.")]
        [SerializeField, Min(0f)] private float maxPlanarAcceleration = 25f;

        [Tooltip("Maximum planar deceleration (m/s^2) applied when there is no movement input.")]
        [SerializeField, Min(0f)] private float maxPlanarDeceleration = 35f;

        [Header("Jump")]
        [Tooltip("Upward velocity change applied when jumping.")]
        [SerializeField, Min(0f)] private float jumpVelocityChange = 5.5f;

        [Tooltip("Layer(s) considered ground for jump checks.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Sphere radius used for ground check.")]
        [SerializeField, Min(0.01f)] private float groundCheckRadius = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool drawGroundCheckGizmos = true;
        [SerializeField] private Color groundedGizmoColor = new(0.2f, 1f, 0.2f, 0.6f);
        [SerializeField] private Color notGroundedGizmoColor = new(1f, 0.2f, 0.2f, 0.6f);

        [Header("Constraints")]
        [SerializeField] private bool lockPitchAndRoll = true;

        private float _bodyYaw;
        private Rigidbody rb;
        private readonly PredictionRigidbody _root = new();
        private float _dt;

        private bool _jumpQueued;


        private void Awake()
        {
            if (rb == null && !TryGetComponent(out rb))
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }

            if (moveAction == null || moveAction.action == null)
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] moveAction is null on '{gameObject.name}'.");
            }

            if (jumpAction == null || jumpAction.action == null)
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] jumpAction is null on '{gameObject.name}'.");
            }

            if (!TryGetComponent(out survivorRemoteAnimator))
            {
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] SurvivorRemoteAnimator is null on '{gameObject.name}'.");
            }
            capsuleCollider = GetComponent<CapsuleCollider>();

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

            if (!IsClientInitialized) return;
            view.enabled = IsOwner;
            if (IsOwner) EnableInput();
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
            if (IsOwner)
            {
                survivorRemoteAnimator.SetPitch(view.Pitch);
                survivorRemoteAnimator.SetYaw(view.Yaw);
            }
            CreateReconcile();
        }

        private void EnableInput()
        {
            moveAction.action.Enable();

            jumpAction.action.performed -= JumpAction_Performed;
            jumpAction.action.performed += JumpAction_Performed;
            jumpAction.action.Enable();
        }

        private void JumpAction_Performed(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _jumpQueued = true;
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

            bool jump = _jumpQueued;
            _jumpQueued = false;

            return new ReplicateData
            {
                Move = move,
                Yaw = _bodyYaw,
                Jump = jump
            };
        }

        private bool IsGrounded()
        {
            // Small, deterministic ground probe.
            // Cast from just above the rigidbody position to reduce false negatives on slopes/steps.
            Vector3 origin = rb.position + capsuleCollider.center;
            float castDistance = capsuleCollider.height * 0.5f + 0.05f;
            return Physics.SphereCast(
                origin,
                groundCheckRadius,
                Vector3.down,
                out _,
                castDistance,
                groundMask,
                QueryTriggerInteraction.Ignore
            );
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGroundCheckGizmos)
                return;

            Rigidbody rbGizmo = rb;
            if (rbGizmo == null)
                TryGetComponent(out rbGizmo);
            if (rbGizmo == null)
                return;

            Vector3 origin = rbGizmo.position + capsuleCollider.center;
            float castDistance = capsuleCollider.height * 0.5f + 0.05f;

            bool hit = Physics.SphereCast(
                origin,
                groundCheckRadius,
                Vector3.down,
                out RaycastHit hitInfo,
                castDistance,
                groundMask,
                QueryTriggerInteraction.Ignore
            );

            Gizmos.color = hit ? groundedGizmoColor : notGroundedGizmoColor;

            // Origin sphere.
            Gizmos.DrawWireSphere(origin, groundCheckRadius);

            // Cast line.
            Vector3 end = origin + Vector3.down * castDistance;
            Gizmos.DrawLine(origin, end);

            // Hit point sphere.
            if (hit)
            {
                Gizmos.DrawWireSphere(hitInfo.point, groundCheckRadius * 0.6f);
                Gizmos.DrawLine(hitInfo.point, hitInfo.point + hitInfo.normal * 0.25f);
            }
            else
            {
                Gizmos.DrawWireSphere(end, groundCheckRadius * 0.6f);
            }
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
            bool isGrounded = IsGrounded();
            if(isGrounded) {
                // Jump is edge-triggered (queued from InputAction.performed and consumed once).
                if (rd.Jump && jumpVelocityChange > 0f)
                    _root.AddForce(Vector3.up * jumpVelocityChange, ForceMode.VelocityChange);
                Vector2 moveInput = Vector2.ClampMagnitude(rd.Move, 1f);
                float inputMag = Mathf.Clamp01(moveInput.magnitude);
                bool hasInput = inputMag > 0.0001f;

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
                Vector3 desiredVel = hasInput ? (moveWorld * (moveSpeed * inputMag)) : Vector3.zero;
                desiredVel.y = currentVel.y;

                // Acceleration-based planar movement toward the desired velocity.
                Vector3 currentPlanarVel = new(currentVel.x, 0f, currentVel.z);
                Vector3 desiredPlanarVel = new(desiredVel.x, 0f, desiredVel.z);

                float dt = Mathf.Max(0.000001f, _dt);
                Vector3 desiredPlanarAccel = (desiredPlanarVel - currentPlanarVel) / dt;
                float accelLimit = hasInput ? maxPlanarAcceleration : maxPlanarDeceleration;
                if (accelLimit > 0f)
                    desiredPlanarAccel = Vector3.ClampMagnitude(desiredPlanarAccel, accelLimit);
                else
                    desiredPlanarAccel = Vector3.zero;
                
                _root.AddForce(desiredPlanarAccel, ForceMode.Acceleration);
            }

            _root.Simulate();
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _root.Reconcile(rd.Root);
        }
    }
}
