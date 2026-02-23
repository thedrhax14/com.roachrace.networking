using CAS_Demo.Scripts.FPS;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
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
        [SerializeField] private CharacterCamera _characterCamera;
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private ProceduralAnimationFPSData proceduralAnimationFPSData;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference lookAction;

        private Rigidbody rb;
        private CapsuleCollider capsuleCollider;

        private float _dt;
        private float _bodyYaw;
        private bool _jumpQueued;

        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private int _coyoteTicksMax;
        private int _jumpBufferTicksMax;
        private Vector3 _lookInput;
        private readonly SyncVar<Vector3> _syncLookInput = new (Vector3.zero);
        private Quaternion _aimRotation = Quaternion.identity;

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

            if (IsOwner) EnableInput();
            else _syncLookInput.OnChange += SyncLookInput_OnChange;

            if (_characterCamera != null) _characterCamera.enabled = IsOwner;
            if (virtualCamera != null) virtualCamera.enabled = IsOwner;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (!IsOwner)
                _syncLookInput.OnChange -= SyncLookInput_OnChange;
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

            lookAction.action.performed -= LookAction_Performed;
            lookAction.action.performed += LookAction_Performed;
            lookAction.action.Enable();

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

        private void LookAction_Performed(InputAction.CallbackContext ctx)
        {
            if (!IsOwner || _characterCamera == null)
                return;

            Vector2 input = ctx.ReadValue<Vector2>();
            _lookInput.x += input.x;
            _lookInput.y = Mathf.Clamp(_lookInput.y - input.y, -90f, 90f);
            _lookInput.z = KMath.FloatInterp(_lookInput.z, 0, 8f, Time.deltaTime);
            
            _aimRotation *= Quaternion.Euler(0f, input.x, 0f);
            _aimRotation.Normalize();
            
            _characterCamera.pitchInput = _lookInput.y;
            _characterCamera.yawInput = _aimRotation.eulerAngles.y;
            proceduralAnimationFPSData.lookInput = _lookInput;
            proceduralAnimationFPSData.deltaLookInput = input;
            SetSyncLookInputRPC(_lookInput);
        }

        [ServerRpc]
        private void SetSyncLookInputRPC(Vector3 value)
        {
            _syncLookInput.Value = value;
        }

        private void SyncLookInput_OnChange(Vector3 prev, Vector3 next, bool asServer)
        {
            if (IsOwner) return;
            _characterCamera.pitchInput = next.y;
            _characterCamera.yawInput = next.x;
            proceduralAnimationFPSData.lookInput = next;
            proceduralAnimationFPSData.deltaLookInput = next - prev;
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

            _bodyYaw = motor.StepBodyYaw(_bodyYaw, _characterCamera != null ? _characterCamera.yawInput : _bodyYaw, _dt);
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

            survivorRemoteAnimator.SetPitchAndYaw(_lookInput.x, _lookInput.y);
            proceduralAnimationFPSData.lookInput = _lookInput;
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

        private void OnDestroy()
        {
            if (IsOwner)
            {
                moveAction.action.Disable();

                jumpAction.action.performed -= JumpAction_Performed;
                jumpAction.action.Disable();

                lookAction.action.performed -= LookAction_Performed;
                lookAction.action.Disable();
            }
        }
    }
}
