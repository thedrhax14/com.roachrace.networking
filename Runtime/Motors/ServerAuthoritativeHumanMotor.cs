using FishNet.Connection;
using FishNet.Object;
using RoachRace.Controls;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoachRace.Networking
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class ServerAuthoritativeHumanMotor : NetworkBehaviour
    {
        private struct InputData
        {
            public Vector2 Move;
            public float Yaw;
            public bool Jump;
            public bool Run;
        }

        [Header("Math")]
        [SerializeField] private HumanMotorMathProfile motor;

        [Header("References")]
        [SerializeField] private SurvivorRemoteAnimator survivorRemoteAnimator;
        [SerializeField] private HumanCameraController view;
        [SerializeField] private CinemachineCamera virtualCamera;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference runAction;

        [Header("Running")]
        [Tooltip("Speed (blend units per second) used to ramp from walk to run while Shift is held.")]
        [SerializeField, Min(0f)] private float runBlendIncreasePerSecond = 3f;

        [Tooltip("Speed (blend units per second) used to return from run to walk after Shift is released. Higher = faster drop.")]
        [SerializeField, Min(0f)] private float runBlendDecreasePerSecond = 9f;

        private Rigidbody rb;
        private CapsuleCollider capsuleCollider;

        private float _dt;
        private float _bodyYaw;
        private bool _jumpQueued;

        private bool _runHeld;
        private float _runBlend;

        private InputData _latestInput;

        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private int _coyoteTicksMax;
        private int _jumpBufferTicksMax;

        private readonly HashSet<int> _groundColliderIds = new();

        private void Awake()
        {
            if (motor == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] motor is null on GameObject '{gameObject.name}'.");

            if (!TryGetComponent(out rb) || rb == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");

            capsuleCollider = GetComponent<CapsuleCollider>();
            if (capsuleCollider == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] CapsuleCollider is null on GameObject '{gameObject.name}'.");

            if (moveAction == null || moveAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] moveAction is null on '{gameObject.name}'.");

            if (jumpAction == null || jumpAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] jumpAction is null on '{gameObject.name}'.");

            if (runAction == null || runAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] runAction is null on '{gameObject.name}'.");

            if (!TryGetComponent(out survivorRemoteAnimator) || survivorRemoteAnimator == null)
                throw new System.NullReferenceException($"[{nameof(ServerAuthoritativeHumanMotor)}] SurvivorRemoteAnimator is null on '{gameObject.name}'.");

            if (motor.LockPitchAndRoll)
                rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (!IsClientInitialized)
                return;

            bool owner = IsOwner;
            if (view != null) view.enabled = owner;
            if (virtualCamera != null) virtualCamera.enabled = owner;

            if (owner)
                EnableInput();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Clients do not simulate physics; server does.
            if (!IsServerInitialized && rb != null)
                rb.isKinematic = true;

            if (view != null) view.enabled = IsOwner;
            if (virtualCamera != null) virtualCamera.enabled = IsOwner;
            if (IsOwner)
                EnableInput();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (rb != null)
                rb.isKinematic = false;

            _dt = Time.fixedDeltaTime;
            motor.ComputeTickWindows(_dt, out _coyoteTicksMax, out _jumpBufferTicksMax);
        }

        private void EnableInput()
        {
            moveAction.action.Enable();

            jumpAction.action.performed -= JumpAction_Performed;
            jumpAction.action.performed += JumpAction_Performed;
            jumpAction.action.Enable();

            runAction.action.started -= RunAction_Began;
            runAction.action.started += RunAction_Began;
            runAction.action.canceled -= RunAction_Ended;
            runAction.action.canceled += RunAction_Ended;
            runAction.action.Enable();

            if (virtualCamera != null)
            {
                virtualCamera.transform.parent = null;
                virtualCamera.Prioritize();
            }
        }

        private void JumpAction_Performed(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _jumpQueued = true;
        }

        /// <summary>
        /// Handles the start of the local run input for the owning client.<br/>
        /// Typical usage: bound to the Input System <c>started</c> callback for hold-to-run behavior (e.g., Shift).<br/>
        /// Context: only affects input that is sent to the server; server is authoritative for physics.
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void RunAction_Began(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _runHeld = true;
        }

        /// <summary>
        /// Handles the end of the local run input for the owning client.<br/>
        /// Typical usage: bound to the Input System <c>canceled</c> callback for hold-to-run behavior (e.g., Shift).<br/>
        /// Context: only affects input that is sent to the server; server is authoritative for physics.
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void RunAction_Ended(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _runHeld = false;
        }

        private void FixedUpdate()
        {
            _dt = Time.fixedDeltaTime;

            if (IsClientInitialized && IsOwner)
            {
                Vector2 move = moveAction.action.ReadValue<Vector2>();
                _bodyYaw = motor.StepBodyYaw(_bodyYaw, view != null ? view.Yaw : _bodyYaw, _dt);

                bool jump = _jumpQueued;
                _jumpQueued = false;

                bool run = _runHeld;

                // Send input to server each tick.
                SendInputServerRpc(move, _bodyYaw, jump, run);

                survivorRemoteAnimator.SetPitchAndYaw(view.Pitch, view.Yaw);
            }

            if (!IsServerInitialized)
                return;

            if (_coyoteTicksMax == 0 && _jumpBufferTicksMax == 0)
                motor.ComputeTickWindows(_dt, out _coyoteTicksMax, out _jumpBufferTicksMax);

            SimulateOnServer(_latestInput);
        }

        [ServerRpc(RequireOwnership = true)]
        private void SendInputServerRpc(Vector2 move, float yaw, bool jump, bool run)
        {
            _latestInput = new InputData
            {
                Move = move,
                Yaw = yaw,
                Jump = jump,
                Run = run
            };
        }

        private bool IsGrounded()
        {
            return _groundColliderIds.Count > 0;
        }

        private void SimulateOnServer(InputData input)
        {
            rb.MoveRotation(Quaternion.Euler(0f, input.Yaw, 0f));

            float targetRunBlend = input.Run ? 1f : 0f;
            float runBlendSpeed = input.Run ? runBlendIncreasePerSecond : runBlendDecreasePerSecond;
            _runBlend = Mathf.MoveTowards(_runBlend, targetRunBlend, runBlendSpeed * _dt);

            bool isGrounded = IsGrounded();
            motor.UpdateJumpWindows(isGrounded, input.Jump, ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, _coyoteTicksMax, _jumpBufferTicksMax);

            if (motor.TryConsumeJump(ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, out Vector3 jumpVelChange))
            {
                rb.AddForce(jumpVelChange, ForceMode.VelocityChange);
                isGrounded = false;
            }

            // Use current yaw (post-rotation) for movement.
            float yaw = rb.rotation.eulerAngles.y;

            Vector3 currentVel = rb.linearVelocity;
            Vector3 planarAccel = motor.ComputePlanarAcceleration(input.Move, yaw, currentVel, isGrounded, _dt, _runBlend);

            rb.AddForce(planarAccel, ForceMode.Acceleration);
        }

        private void OnDestroy()
        {
            if (!IsOwner)
                return;

            moveAction.action.Disable();

            jumpAction.action.performed -= JumpAction_Performed;
            jumpAction.action.Disable();

            runAction.action.started -= RunAction_Began;
            runAction.action.canceled -= RunAction_Ended;
            runAction.action.Disable();
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsServerInitialized || collision == null)
                return;

            Collider other = collision.collider;
            if (other == null)
                return;

            int id = other.GetInstanceID();
            if (motor.TryGetGroundContact(rb, capsuleCollider, collision, out _))
                _groundColliderIds.Add(id);
            else
                _groundColliderIds.Remove(id);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            Collider other = collision?.collider;
            if (other == null)
                return;

            _groundColliderIds.Remove(other.GetInstanceID());
        }

        private void OnDisable()
        {
            _groundColliderIds.Clear();
        }
    }
}
