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
    public class ClientAuthoritativeHumanMotor : NetworkBehaviour
    {
        [Header("Math")]
        [SerializeField] private HumanMotorMathProfile motor;

        [Header("References")]
        [SerializeField] private SurvivorRemoteAnimator survivorRemoteAnimator;
        [SerializeField] private HumanCameraController view;
        [SerializeField] private CinemachineCamera virtualCamera;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;

        private Rigidbody rb;
        private CapsuleCollider capsuleCollider;

        private float _dt;
        private float _bodyYaw;
        private bool _jumpQueued;

        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private int _coyoteTicksMax;
        private int _jumpBufferTicksMax;

        private readonly HashSet<int> _groundColliderIds = new();

        private void Awake()
        {
            if (motor == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] motor is null on GameObject '{gameObject.name}'.");

            if (!TryGetComponent(out rb) || rb == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");

            capsuleCollider = GetComponent<CapsuleCollider>();
            if (capsuleCollider == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] CapsuleCollider is null on GameObject '{gameObject.name}'.");

            if (moveAction == null || moveAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] moveAction is null on '{gameObject.name}'.");

            if (jumpAction == null || jumpAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] jumpAction is null on '{gameObject.name}'.");

            if (!TryGetComponent(out survivorRemoteAnimator) || survivorRemoteAnimator == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] SurvivorRemoteAnimator is null on '{gameObject.name}'.");

            if (motor.LockPitchAndRoll)
                rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Non-owners should not simulate physics locally.
            if (!IsOwner && rb != null)
                rb.isKinematic = true;
            else if (IsOwner && rb != null)
                rb.isKinematic = false;

            if (IsOwner)
                EnableInput();

            if (view != null) view.enabled = IsOwner;
            if (virtualCamera != null) virtualCamera.enabled = IsOwner;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Client authoritative: server does not simulate this body.
            if (rb != null)
                rb.isKinematic = true;
        }

        private void EnableInput()
        {
            moveAction.action.Enable();

            jumpAction.action.performed -= JumpAction_Performed;
            jumpAction.action.performed += JumpAction_Performed;
            jumpAction.action.Enable();

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

        private bool IsGrounded()
        {
            return _groundColliderIds.Count > 0;
        }

        private void FixedUpdate()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            _dt = Time.fixedDeltaTime;
            if (_coyoteTicksMax == 0 && _jumpBufferTicksMax == 0)
                motor.ComputeTickWindows(_dt, out _coyoteTicksMax, out _jumpBufferTicksMax);

            Vector2 move = moveAction.action.ReadValue<Vector2>();

            _bodyYaw = motor.StepBodyYaw(_bodyYaw, view != null ? view.Yaw : _bodyYaw, _dt);
            rb.MoveRotation(Quaternion.Euler(0f, _bodyYaw, 0f));

            bool isGrounded = IsGrounded();

            bool jumpPressed = _jumpQueued;
            _jumpQueued = false;

            motor.UpdateJumpWindows(isGrounded, jumpPressed, ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, _coyoteTicksMax, _jumpBufferTicksMax);

            if (motor.TryConsumeJump(ref _coyoteTicksRemaining, ref _jumpBufferTicksRemaining, out Vector3 jumpVelChange))
            {
                rb.AddForce(jumpVelChange, ForceMode.VelocityChange);
                isGrounded = false;
            }

            float yaw = rb.rotation.eulerAngles.y;
            Vector3 currentVel = rb.linearVelocity;
            Vector3 planarAccel = motor.ComputePlanarAcceleration(move, yaw, currentVel, isGrounded, _dt);
            rb.AddForce(planarAccel, ForceMode.Acceleration);

            survivorRemoteAnimator.SetPitchAndYaw(view.Pitch, view.Yaw);
        }

        private void OnCollisionStay(Collision collision)
        {
            // Grounding is only needed on the owner (local sim).
            if (!IsOwner || collision == null)
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
            if (!IsOwner)
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
