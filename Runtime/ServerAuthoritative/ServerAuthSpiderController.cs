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

        [Header("Spider Mechanics")]
        [SerializeField] private float surfaceAlignSpeed = 8f;
        [SerializeField] private float surfaceAlignDamping = 2.5f;
        [SerializeField] private float maxAlignTorque = 120f;
        [SerializeField] private float groundCheckDistance = 0.3f;
        [SerializeField] private LayerMask groundMask = -1;

        private CapsuleCollider _capsule;

        NetworkSpiderPartsController spiderPartsController;

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

            ApplyMovement(_latestInput, delta);
            AlignToSurface(_latestInput, delta);
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

                Vector3 currentVel = rb.linearVelocity;
                Vector3 velocityChange = targetVelocity - Vector3.Project(currentVel, _surfaceNormal);
                velocityChange = Vector3.ClampMagnitude(velocityChange, groundAcceleration * delta);

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
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

            Vector3 cameraForward = Quaternion.Euler(0f, rd.CameraYaw, 0f) * Vector3.forward;

            Rigidbody rb = Rigidbody;
            Quaternion currentRotation = rb.rotation;
            Quaternion targetRotation = Quaternion.LookRotation(cameraForward.normalized, _surfaceNormal);

            rb.MoveRotation(Quaternion.Slerp(currentRotation, targetRotation, delta * surfaceAlignSpeed));
        }
    }
}
