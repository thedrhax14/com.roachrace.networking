using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using RoachRace.Controls;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking
{
    [RequireComponent(typeof(CapsuleCollider))]
    /// <summary>
    /// Predicted Rigidbody-based "human" motor using FishNet prediction.
    /// Movement is driven by AddForce and yaw-only rotation is driven by MoveRotation.
    /// Currently there is a strange problem. At 150-180 ms it was observed that the motor
    /// would teleport to a position as if it didn't jump while jump is being predicted.
    /// Interesting enough, this only happened when there was some latency, not with
    /// minimum possible latency (50ms). Another issue if user strafes fast left and right
    /// then player jitters visible. Needs further investigation. Current script for smoother
    /// can't smooth it properly. Additionally, because of smoothing delay the controls don't
    /// feel instant enough. Needs further investigation and tuning. Potential idea is to run
    /// prediction faster then normal ticks but that may have other side effects.
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
            public int CoyoteTicks;
            public int JumpBufferTicks;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        [Header("Math")]
        [SerializeField] private HumanMotorMathProfile motor;

        [Header("References")]
        [SerializeField] private SurvivorRemoteAnimator survivorRemoteAnimator;
        [SerializeField] private HumanCameraController view;
        protected CapsuleCollider capsuleCollider;
        [SerializeField] private CinemachineCamera virtualCamera;

        [Header("Debug")]
        [Tooltip("Optional. If assigned, receives debug data such as replicate state history for editor visualization.")]
        [SerializeField] private PredictedHumanMotorDebug debug;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;

        [Header("Debug")]
        [SerializeField] private bool drawGroundCheckGizmos = true;
        [SerializeField] private Color groundedGizmoColor = new(0.2f, 1f, 0.2f, 0.6f);
        [SerializeField] private Color notGroundedGizmoColor = new(1f, 0.2f, 0.2f, 0.6f);

        private float _bodyYaw;
        private Rigidbody rb;
        private readonly PredictionRigidbody _root = new();
        private float _dt;

        private bool _jumpQueued;

        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private int _coyoteTicksMax;
        private int _jumpBufferTicksMax;

        private readonly HashSet<int> _groundColliderIds = new();
    #if UNITY_EDITOR
        private Vector3 _lastGroundNormal;
        private Vector3 _lastGroundPoint;
        private bool _hasLastGround;
    #endif


        private void Awake()
        {
            if (motor == null)
            {
                Debug.LogError($"[{nameof(PredictedHumanMotor)}] motor is not assigned! Please assign it in the Inspector.", gameObject);
                throw new System.NullReferenceException($"[{nameof(PredictedHumanMotor)}] motor is null on GameObject '{gameObject.name}'.");
            }

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

            if (motor.LockPitchAndRoll)
                rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            // Rigidbodies need Tick + PostTick.
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
            _dt = (float)TimeManager.TickDelta;

            // Cache tick-based windows for deterministic simulation.
            motor.ComputeTickWindows(_dt, out _coyoteTicksMax, out _jumpBufferTicksMax);
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (!IsClientInitialized) return;
            view.enabled = IsOwner;
            virtualCamera.enabled = IsOwner;
            if (IsOwner) EnableInput();
        }

        protected override void TimeManager_OnTick()
        {
            PerformReplicate(BuildMoveData());
            if (IsOwner)
            {
                survivorRemoteAnimator.SetPitchAndYaw(view.Pitch, view.Yaw);
            }
            CreateReconcile();
        }

        private void EnableInput()
        {
            moveAction.action.Enable();

            jumpAction.action.performed -= JumpAction_Performed;
            jumpAction.action.performed += JumpAction_Performed;
            jumpAction.action.Enable();
            
            virtualCamera.transform.parent = null;
            virtualCamera.Prioritize();
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

            _bodyYaw = motor.StepBodyYaw(_bodyYaw, view.Yaw, _dt);

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
            return _groundColliderIds.Count > 0;
        }

        private void OnCollisionEnter(Collision collision)
        {
            // OnCollisionStay(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (collision == null)
                return;

#if UNITY_EDITOR
            debug?.RecordCollisionStayTick(gameObject.name);
#endif

            Collider other = collision.collider;
            if (other == null)
                return;

            int id = other.GetInstanceID();
            if (motor.TryGetGroundContact(rb, capsuleCollider, collision, out ContactPoint groundContact))
            {
                _groundColliderIds.Add(id);
                #if UNITY_EDITOR
                _lastGroundPoint = groundContact.point;
                _lastGroundNormal = groundContact.normal;
                _hasLastGround = true;
                #endif
            }
            else
                _groundColliderIds.Remove(id);
        }

        private void OnCollisionExit(Collision collision)
        {
            Collider other = collision?.collider;
            if (other == null)
                return;

            _groundColliderIds.Remove(other.GetInstanceID());
        }

        private void OnDisable()
        {
            _groundColliderIds.Clear();
        }
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGroundCheckGizmos)
                return;

            Rigidbody rbGizmo = rb;
            if (rbGizmo == null)
                TryGetComponent(out rbGizmo);
            if (rbGizmo == null)
                return;

            // Contact-based ground visualization.
            if (Application.isPlaying)
            {
                bool grounded = IsGrounded();
                Gizmos.color = grounded ? groundedGizmoColor : notGroundedGizmoColor;

                Vector3 center = rbGizmo.position + capsuleCollider.center;
                Gizmos.DrawWireSphere(center, 0.05f);

                if (_hasLastGround)
                {
                    Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
                    Gizmos.DrawWireSphere(_lastGroundPoint, 0.05f);
                    Gizmos.DrawLine(_lastGroundPoint, _lastGroundPoint + _lastGroundNormal * 0.35f);
                }

                return;
            }

            // Edit-mode visualization: approximate "feet" point of the capsule.
            // (Collision contacts only exist during play mode.)
            Gizmos.color = notGroundedGizmoColor;
            Vector3 editCenter = rbGizmo.position + capsuleCollider.center;
            Gizmos.DrawWireSphere(editCenter, 0.05f);

            float radius = capsuleCollider.radius;
            float halfHeight = Mathf.Max(radius, capsuleCollider.height * 0.5f);
            Vector3 bottom = editCenter + Vector3.down * (halfHeight - radius);
            Gizmos.DrawWireSphere(bottom, radius);
            Gizmos.DrawLine(bottom, bottom + Vector3.up * 0.25f);
        }
#endif

        public override void CreateReconcile()
        {
            ReconcileData rd = new()
            {
                Root = _root,
                CoyoteTicks = _coyoteTicksRemaining,
                JumpBufferTicks = _jumpBufferTicksRemaining
            };

            PerformReconcile(rd);
        }

        [Replicate]
        private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            _dt = (float)TimeManager.TickDelta;

            _root.MoveRotation(Quaternion.Euler(0f, rd.Yaw, 0f));
            bool isGrounded = IsGrounded();
            bool groundedProbe = isGrounded;

            motor.UpdateJumpWindows(isGrounded, rd.Jump, ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, _coyoteTicksMax, _jumpBufferTicksMax);

            bool appliedJump = false;
            if (motor.TryConsumeJump(ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, out Vector3 jumpVelChange))
            {
                _root.AddForce(jumpVelChange, ForceMode.VelocityChange);
                isGrounded = false;
                appliedJump = true;
            }

#if UNITY_EDITOR
            debug?.RecordReplicateState(state, groundedProbe, rd.Jump, appliedJump, rd.GetTick());
#endif

            float yaw = rb.rotation.eulerAngles.y;
            Vector3 currentVel = rb.linearVelocity;
            Vector3 planarAccel = motor.ComputePlanarAcceleration(rd.Move, yaw, currentVel, isGrounded, _dt);
            _root.AddForce(planarAccel, ForceMode.Acceleration);

            _root.Simulate();
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _root.Reconcile(rd.Root);

            // Keep prediction helper state in sync.
            _coyoteTicksRemaining = rd.CoyoteTicks;
            _jumpBufferTicksRemaining = rd.JumpBufferTicks;
        }
    }
}
