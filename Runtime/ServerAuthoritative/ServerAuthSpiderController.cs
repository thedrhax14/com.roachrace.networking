using System;
using FishNet.Connection;
using FishNet.Object;
using RoachRace.Controls;
using RoachRace.Networking.SpiderParts;
using UnityEngine;

namespace RoachRace.Networking
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class ServerAuthSpiderController : ServerAuthMonsterController
    {
        [Serializable]
        public struct SpiderInputData
        {
            public Vector2 Input;
            public bool Jump;
            public bool Run;
            public float CameraYaw;
        }

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float runSpeed = 8f;
        [SerializeField] private float maxSpeed = 15f;
        [SerializeField] private float groundAcceleration = 20f;
        [SerializeField] private float airAcceleration = 5f;

        [Header("Spider Mechanics")]
        [SerializeField] private float gravityStrength = 20f;
        [SerializeField] private float surfaceAlignSpeed = 8f;
        [SerializeField] private float surfaceAlignDamping = 2.5f;
        [SerializeField] private float maxAlignTorque = 120f;
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private float stickyForce = 15f;
        [SerializeField] private LayerMask groundMask = -1;

        private CapsuleCollider _capsule;

        NetworkSpiderPartsController spiderPartsController;

        private bool _isGrounded;
        private Vector3 _surfaceNormal = Vector3.up;

        [SerializeField] private SpiderInputData _latestInput;

        protected override void Awake()
        {
            base.Awake();
            _capsule = GetComponent<CapsuleCollider>();
            if (_capsule == null)
            {
                Debug.LogError($"[{nameof(ServerAuthSpiderController)}] CapsuleCollider is missing on '{gameObject.name}'.", gameObject);
                throw new NullReferenceException($"[{nameof(ServerAuthSpiderController)}] CapsuleCollider is null on '{gameObject.name}'.");
            }

            spiderPartsController = GetComponentInChildren<NetworkSpiderPartsController>();
        }

        protected override void OnOwnerClientTick()
        {
            if (!IsOwner) return;
            float cameraYaw = (Camera.main != null) ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;

            var host = RoachRaceInputActionsHost.Instance;
            Vector2 input = host != null ? host.Player.Move.ReadValue<Vector2>() : Vector2.zero;
            // Jump/Run not currently wired for monsters; keep consistent with predicted controller.
            SubmitInputServerRpc(input, false, false, cameraYaw);
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            _latestInput.Input = Vector2.zero;
            _latestInput.Jump = false;
            _latestInput.Run = false;
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector2 input, bool jump, bool run, float cameraYaw, NetworkConnection sender = null)
        {
            if (sender != Owner) {
                _latestInput.Input = Vector2.zero;
                _latestInput.Jump = false;
                _latestInput.Run = false;
                return;
            }

            // If parts are recalling, ignore movement inputs entirely.
            if (spiderPartsController.IsRecallInProgressServer())
            {
                _latestInput.Input = Vector2.zero;
                _latestInput.Jump = false;
                _latestInput.Run = false;
                return;
            }

            _latestInput.Input = input;
            _latestInput.Jump = jump;
            _latestInput.Run = run;
            _latestInput.CameraYaw = cameraYaw;
        }

        protected override void OnServerTick(float delta)
        {
            if (!IsServerInitialized) return;

            // Lock movement while parts are returning.
            if (spiderPartsController.IsRecallInProgressServer())
            {
                Rigidbody.linearVelocity = Vector3.zero;
                Rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            CheckGround(delta);
            ApplyMovement(_latestInput, delta);
            AlignToSurface(_latestInput, delta);
            ApplyGravity();

            if (_latestInput.Jump && _isGrounded)
                Rigidbody.AddForce(_surfaceNormal * 8f, ForceMode.VelocityChange);
        }

        private void CheckGround(float delta)
        {
            float checkDist = _capsule.height * 0.5f + groundCheckDistance;
            Vector3 origin = transform.position + _capsule.center * 0.1f;

            Vector3[] checkPoints =
            {
                origin,
                origin + _capsule.radius * 0.5f * transform.right,
                origin - _capsule.radius * 0.5f * transform.right,
                origin + _capsule.radius * 0.5f * transform.forward,
                origin - _capsule.radius * 0.5f * transform.forward
            };

            bool foundGround = false;
            Vector3 avgNormal = Vector3.zero;
            int hitCount = 0;

            for (int i = 0; i < checkPoints.Length; i++)
            {
                if (Physics.Raycast(checkPoints[i], -transform.up, out RaycastHit hit, checkDist, groundMask))
                {
                    foundGround = true;
                    avgNormal += hit.normal;
                    hitCount++;
                }
            }

            _isGrounded = foundGround;

            if (hitCount > 0)
            {
                _surfaceNormal = (avgNormal / hitCount).normalized;
            }
            else
            {
                _surfaceNormal = Vector3.Lerp(_surfaceNormal, Vector3.up, delta * 2f);
            }
        }

        private void ApplyMovement(SpiderInputData rd, float delta)
        {
            float currentSpeed = rd.Run ? runSpeed : walkSpeed;
            Rigidbody rb = Rigidbody;

            if (rd.Input.sqrMagnitude > 0.01f)
            {
                Vector3 camForward = Vector3.ProjectOnPlane(transform.forward, _surfaceNormal).normalized;
                Vector3 camRight = Vector3.ProjectOnPlane(transform.right, _surfaceNormal).normalized;

                Vector3 targetVelocity = (camForward * rd.Input.y + camRight * rd.Input.x) * currentSpeed;

                float accel = _isGrounded ? groundAcceleration : airAcceleration;
                Vector3 currentVel = rb.linearVelocity;
                Vector3 velocityChange = targetVelocity - Vector3.Project(currentVel, _surfaceNormal);
                velocityChange = Vector3.ClampMagnitude(velocityChange, accel * delta);

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
            else if (_isGrounded)
            {
                Vector3 currentVel = rb.linearVelocity;
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(currentVel, _surfaceNormal);
                rb.AddForce(-horizontalVelocity * 5f, ForceMode.Acceleration);
            }

            if (_isGrounded)
            {
                rb.AddForce(-_surfaceNormal * stickyForce, ForceMode.Acceleration);
            }

            Vector3 currentVelocity = rb.linearVelocity;
            Vector3 tangentialVelocity = Vector3.ProjectOnPlane(currentVelocity, _surfaceNormal);

            if (tangentialVelocity.magnitude > maxSpeed)
            {
                Vector3 clampedTangential = tangentialVelocity.normalized * maxSpeed;
                Vector3 normalVelocity = currentVelocity - tangentialVelocity;
                rb.linearVelocity = clampedTangential + normalVelocity;
            }
        }

        private void AlignToSurface(SpiderInputData rd, float delta)
        {
            Vector3 desiredForward = Vector3.ProjectOnPlane(transform.forward, _surfaceNormal);

            if (_isGrounded)
            {
                Vector3 cameraForward = Quaternion.Euler(0f, rd.CameraYaw, 0f) * Vector3.forward;
                desiredForward = Vector3.ProjectOnPlane(cameraForward, _surfaceNormal);
            }

            if (desiredForward.sqrMagnitude < 0.0001f)
                desiredForward = Vector3.ProjectOnPlane(transform.forward, _surfaceNormal);

            Rigidbody rb = Rigidbody;
            Quaternion currentRotation = rb.rotation;
            Quaternion targetRotation = Quaternion.LookRotation(desiredForward.normalized, _surfaceNormal);

            rb.MoveRotation(Quaternion.Slerp(currentRotation, targetRotation, delta * surfaceAlignSpeed));
        }

        private void ApplyGravity()
        {
            if (!_isGrounded)
            {
                Rigidbody.AddForce(-_surfaceNormal * gravityStrength, ForceMode.Acceleration);
            }
        }
    }
}
