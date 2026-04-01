using CAS_Demo.Scripts.FPS;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Camera;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using System.Collections.Generic;
using RoachRace.Networking.Inventory;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;

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
        [SerializeField] private RecoilAnimation recoilAnimation;

        [Header("Input")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference aimAction;
        [SerializeField] private InputActionReference runAction;
        [SerializeField] private float lookSensitivity = 15f;

        [Header("Running")]
        [Tooltip("Speed (blend units per second) used to ramp from walk to run while Shift is held.")]
        [SerializeField, Min(0f)] private float runBlendIncreasePerSecond = 3f;

        [Tooltip("Speed (blend units per second) used to return from run to walk after Shift is released. Higher = faster drop.")]
        [SerializeField, Min(0f)] private float runBlendDecreasePerSecond = 9f;

        private Rigidbody rb;
        private CapsuleCollider capsuleCollider;
        private NetworkPlayerInventory inventory;
        private NetworkStaminaObserver staminaObserver;
        private ServerStaminaController staminaController;

        private float _dt;
        private float _bodyYaw;
        private bool _jumpQueued;
        private bool _isAiming;

        private int _coyoteTicksRemaining;
        private int _jumpBufferTicksRemaining;
        private int _coyoteTicksMax;
        private int _jumpBufferTicksMax;
        private Vector2 lookInput;
        private bool updateLookInput;
        private Vector3 _lookRotation;
        private readonly SyncVar<Vector3> _syncLookInput = new (Vector3.zero);
        private Quaternion _aimRotation = Quaternion.identity;
        private Quaternion _syncedAimRotation = Quaternion.identity;

        private bool _runHeld;
        private float _runBlend;

        private readonly HashSet<int> _groundColliderIds = new();

        public Quaternion AimRotation => _syncedAimRotation;

        /// <summary>
        /// Unity lifecycle hook.<br/>
        /// Typical usage: validates required serialized references and cached components so the motor fails fast instead of erroring mid-simulation.<br/>
        /// Context: this motor is client authoritative; only the owning client reads input and simulates physics.
        /// </summary>
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

            if (aimAction == null || aimAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] aimAction is null on '{gameObject.name}'.");

            if (runAction == null || runAction.action == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] runAction is null on '{gameObject.name}'.");

            if (!TryGetComponent(out inventory) || inventory == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] NetworkPlayerInventory is null on '{gameObject.name}'.");

            staminaObserver = GetComponentInChildren<NetworkStaminaObserver>();
            staminaController = GetComponentInChildren<ServerStaminaController>();

             if (staminaObserver == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] NetworkStaminaObserver is null on '{gameObject.name}'.");

             if (staminaController == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] ServerStaminaController is null on '{gameObject.name}'.");

            if (!TryGetComponent(out survivorRemoteAnimator) || survivorRemoteAnimator == null)
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeHumanMotor)}] SurvivorRemoteAnimator is null on '{gameObject.name}'.");

            if (motor.LockPitchAndRoll)
                rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        /// <summary>
        /// FishNet client lifecycle hook.<br/>
        /// Typical usage: configures owner-only input/camera and ensures the motor's yaw state is initialized from the spawned pose so respawns do not snap to global forward on the first simulation tick.<br/>
        /// Server/client constraints: runs on all clients; only the owner enables input and simulates physics.
        /// </summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            // Non-owners should not simulate physics locally.
            if (!IsOwner && rb != null)
                rb.isKinematic = true;
            else if (IsOwner && rb != null)
                rb.isKinematic = false;

            if (IsOwner)
            {
                float initialYawDegrees = rb != null ? rb.rotation.eulerAngles.y : transform.rotation.eulerAngles.y;
                _bodyYaw = initialYawDegrees;

                _lookRotation.x = initialYawDegrees;
                _aimRotation = Quaternion.Euler(0f, _lookRotation.x, 0f);
                _syncedAimRotation = Quaternion.Euler(_lookRotation.y, _lookRotation.x, 0f);
                _aimRotation.Normalize();

                if (_characterCamera != null)
                {
                    _characterCamera.pitchInput = _lookRotation.y;
                    _characterCamera.yawInput = _aimRotation.eulerAngles.y;
                }

                if (proceduralAnimationFPSData != null)
                    proceduralAnimationFPSData.lookInput = _lookRotation;
            }

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

            if(lookAction != null) {
                lookAction.action.performed -= LookAction_Performed;
                lookAction.action.performed += LookAction_Performed;
                lookAction.action.canceled -= LookAction_Canceled;
                lookAction.action.canceled += LookAction_Canceled;
                lookAction.action.Enable();
            }

            aimAction.action.started -= AimAction_Began;
            aimAction.action.started += AimAction_Began;
            aimAction.action.canceled -= AimAction_Ended;
            aimAction.action.canceled += AimAction_Ended;
            aimAction.action.Enable();

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
        /// Context: ignored for non-owners so remote players do not drive local simulation state.
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void RunAction_Began(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _runHeld = true;

            if (staminaController != null)
                staminaController.SetRunningRequestedServerRpc(true);
        }

        /// <summary>
        /// Handles the end of the local run input for the owning client.<br/>
        /// Typical usage: bound to the Input System <c>canceled</c> callback for hold-to-run behavior (e.g., Shift).<br/>
        /// Context: ignored for non-owners so remote players do not drive local simulation state.
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void RunAction_Ended(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            _runHeld = false;

            if (staminaController != null)
                staminaController.SetRunningRequestedServerRpc(false);
        }

        private void LookAction_Performed(InputAction.CallbackContext ctx)
        {
            if (!IsOwner) return;
            updateLookInput = true;
        }

        private void LookAction_Canceled(InputAction.CallbackContext ctx)
        {
            if (!IsOwner) return;
            updateLookInput = true;
        }

        /// <summary>
        /// Marks the local owner as aiming and mirrors that state into the camera, procedural animation data, and active weapon item.<br/>
        /// Typical usage: called from owner-only input callbacks so local ADS behavior stays in sync with animation and recoil systems.<br/>
        /// Context: this method intentionally does not replicate aim to remote clients because aim is a local-only effect in this setup.<br/>
        /// </summary>
        /// <param name="isAiming">True when aim is engaged, false when aim is released.</param>
        private void SetAimState(bool isAiming)
        {
            _isAiming = isAiming;

            if (proceduralAnimationFPSData != null)
                proceduralAnimationFPSData.isAiming = isAiming;

            if (_characterCamera != null)
                _characterCamera.isAiming = isAiming;

            WeaponProp weapon = GetActiveWeaponProp();
            if (weapon != null)
                weapon.OnAim(isAiming);
        }

        /// <summary>
        /// Handles the start of the local aim input for the owning client.<br/>
        /// Typical usage: bound to the Input System <c>started</c> callback for hold-to-aim behavior.<br/>
        /// Context: ignored for non-owners so remote players do not drive local presentation state.<br/>
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void AimAction_Began(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            SetAimState(true);
        }

        /// <summary>
        /// Handles the end of the local aim input for the owning client.<br/>
        /// Typical usage: bound to the Input System <c>canceled</c> callback for hold-to-aim behavior.<br/>
        /// Context: ignored for non-owners so remote players do not drive local presentation state.<br/>
        /// </summary>
        /// <param name="ctx">Input callback context provided by the Input System.</param>
        private void AimAction_Ended(InputAction.CallbackContext ctx)
        {
            if (!IsOwner)
                return;

            SetAimState(false);
        }

        /// <summary>
        /// Resolves the currently selected weapon prop from the local inventory.<br/>
        /// Typical usage: called by local presentation code that needs to forward aim state to the equipped weapon.<br/>
        /// Context: returns null when no selectable item is present or the active item does not host a WeaponProp component.<br/>
        /// </summary>
        /// <returns>The active weapon prop when available; otherwise null.</returns>
        private WeaponProp GetActiveWeaponProp()
        {
            if (inventory == null)
                return null;

            if (!inventory.TryGetSelectedItemInstance(out _, out var itemInstance) || itemInstance == null)
                return null;

            return itemInstance.ItemComponent != null ? itemInstance.ItemComponent.GetComponent<WeaponProp>() : null;
        }

        [ServerRpc]
        private void SetSyncLookInputRPC(Vector3 value)
        {
            _syncLookInput.Value = value;
        }

        private void SyncLookInput_OnChange(Vector3 prevRotation, Vector3 nextRotation, bool asServer)
        {
            if (IsOwner) return;
            proceduralAnimationFPSData.smoothLookInput = true;
            proceduralAnimationFPSData.targetLookInput = nextRotation;
            proceduralAnimationFPSData.deltaLookInput = nextRotation - prevRotation;
            _syncedAimRotation = Quaternion.Euler(nextRotation.y, nextRotation.x, 0);
        }

        private bool IsGrounded()
        {
            return _groundColliderIds.Count > 0;
        }

        void Update()
        {
            if (!IsClientInitialized || !IsOwner) return;

            lookInput = lookAction == null ? Vector2.zero : lookAction.action.ReadValue<Vector2>();
            recoilAnimation.UpdateDeltaInput(lookInput);
            lookInput += recoilAnimation.GetRecoilDelta();

            _lookRotation.x += lookInput.x * Time.deltaTime * lookSensitivity;
            _lookRotation.y = Mathf.Clamp(_lookRotation.y - lookInput.y * Time.deltaTime * lookSensitivity, -90f, 90f);
            _lookRotation.z = KMath.FloatInterp(_lookRotation.z, 0, 8f, Time.deltaTime);

            _aimRotation = Quaternion.Euler(0f, _lookRotation.x, 0f);
            _syncedAimRotation = Quaternion.Euler(_lookRotation.y, _lookRotation.x, 0f);
            _aimRotation.Normalize();

            _characterCamera.pitchInput = _lookRotation.y;
            _characterCamera.yawInput = _aimRotation.eulerAngles.y;
            proceduralAnimationFPSData.lookInput = _lookRotation;
            proceduralAnimationFPSData.deltaLookInput = lookInput;

            if(updateLookInput) {
                SetSyncLookInputRPC(_lookRotation);
                updateLookInput = false;
            }
        }

        /// <summary>
        /// Unity physics tick.<br/>
        /// Typical usage: owner-only simulation step (yaw, grounded/jump windows, and planar movement) for the client-authoritative body.<br/>
        /// Context: running speed is controlled via Shift using a per-player run blend which ramps up smoothly and drops down faster.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            _dt = Time.fixedDeltaTime;
            if (_coyoteTicksMax == 0 && _jumpBufferTicksMax == 0)
                motor.ComputeTickWindows(_dt, out _coyoteTicksMax, out _jumpBufferTicksMax);

            Vector2 move = moveAction.action.ReadValue<Vector2>();

            bool canRun = staminaController != null ? staminaController.CanRun : staminaObserver == null || staminaObserver.HasStamina;
            float targetRunBlend = _runHeld && canRun ? 1f : 0f;
            float runBlendSpeed = _runHeld ? runBlendIncreasePerSecond : runBlendDecreasePerSecond;
            _runBlend = Mathf.MoveTowards(_runBlend, targetRunBlend, runBlendSpeed * _dt);

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
            Vector3 planarAccel = motor.ComputePlanarAcceleration(move, yaw, currentVel, isGrounded, _dt, _runBlend);
            rb.AddForce(planarAccel, ForceMode.Acceleration);

            survivorRemoteAnimator.SetPitchAndYaw(_lookRotation.x, _lookRotation.y);
            proceduralAnimationFPSData.lookInput = _lookRotation;
            proceduralAnimationFPSData.moveInput = move;
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

                if(lookAction != null) {
                    lookAction.action.performed -= LookAction_Performed;
                    lookAction.action.canceled -= LookAction_Canceled;
                    lookAction.action.Disable();
                }

                aimAction.action.started -= AimAction_Began;
                aimAction.action.canceled -= AimAction_Ended;
                aimAction.action.Disable();

                runAction.action.started -= RunAction_Began;
                runAction.action.canceled -= RunAction_Ended;
                runAction.action.Disable();
            }
            if(_characterCamera != null)
                Destroy(_characterCamera.gameObject);
        }
    }
}
