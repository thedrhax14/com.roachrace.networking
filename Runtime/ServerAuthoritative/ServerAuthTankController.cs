using FishNet.Connection;
using FishNet.Object;
using NUnit.Framework;
using RoachRace.Controls;
using UnityEngine;

namespace RoachRace.Networking
{
    [RequireComponent(typeof(Rigidbody))]
    public class ServerAuthTankController : ServerAuthMonsterController
    {
        [Header("Forces")]
        [SerializeField] private float driveForce = 30f;
        [SerializeField] private float turnTorque = 60f;

        [Header("Drive Feel")]
        [SerializeField] private float turnSensitivity = 0.75f;
        [SerializeField] private float maxTurnSpeed = 180f;
        [SerializeField] private float maxVelocityToTurn = 2f;

        [Header("Slope Assist")]
        [Tooltip("When enabled, drive acceleration is applied along the ground plane and gets extra uphill acceleration to counter gravity.")]
        [SerializeField] private bool slopeAssistEnabled = true;

        [Tooltip("Multiplier for how strongly we compensate uphill gravity along the ground plane. 1 = cancel gravity component exactly.")]
        [SerializeField] private float slopeAssistStrength = 1.0f;

        [Tooltip("Maximum extra acceleration (m/s^2) added from slope assist.")]
        [SerializeField] private float maxSlopeAssistAcceleration = 25f;

        [Tooltip("Raycast distance used to find the ground normal for slope assist.")]
        [SerializeField] private float groundCheckDistance = 2.5f;

        [Tooltip("Which layers count as ground for slope assist.")]
        [SerializeField] private LayerMask groundLayers = ~0;

        public GameObject Wheels;

        private Vector2 _latestInput;

        protected override void Awake()
        {
            base.Awake();

            Rigidbody rb = Rigidbody;
            if (rb.mass < 50f)
                rb.mass = 1500f;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Wheels.SetActive(true);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Wheels.SetActive(IsServerInitialized);
        }

        protected override void OnOwnerClientTick()
        {
            if (!IsOwner) return;

            var host = RoachRaceInputActionsHost.Instance;
            Vector2 input = host != null ? host.Player.Move.ReadValue<Vector2>() : Vector2.zero;
            SubmitInputServerRpc(input);
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            _latestInput = Vector2.zero;
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector2 input, NetworkConnection sender = null)
        {
            if (sender != Owner) {
                _latestInput = Vector2.zero;
                return;
            }
            _latestInput = input;
        }

        protected override void OnServerTick(float delta)
        {
            if (!IsServerInitialized) return;
            HandleMovement(_latestInput, delta);
        }

        private void HandleMovement(Vector2 input, float delta)
        {
            float move = Mathf.Clamp(input.y, -1f, 1f);
            float turn = Mathf.Clamp(input.x, -1f, 1f) * Mathf.Clamp01(turnSensitivity);

            Rigidbody rb = Rigidbody;
            Quaternion rot = rb.rotation;
            Vector3 currentUp = rot * Vector3.up;

            float yawSpeedDeg = Vector3.Dot(rb.angularVelocity, currentUp) * Mathf.Rad2Deg;
            if (Mathf.Abs(yawSpeedDeg) > maxTurnSpeed && Mathf.Sign(turn) == Mathf.Sign(yawSpeedDeg))
                turn = 0f;

            Vector3 groundNormal = currentUp;
            if (slopeAssistEnabled)
            {
                Vector3 origin = rb.position + (currentUp * 0.25f);
                if (Physics.Raycast(origin, -currentUp, out RaycastHit hit, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
                    groundNormal = hit.normal;
            }

            Vector3 forward = rot * Vector3.forward;
            Vector3 forwardOnGround = Vector3.ProjectOnPlane(forward, groundNormal);
            if (forwardOnGround.sqrMagnitude < 0.0001f)
                forwardOnGround = Vector3.ProjectOnPlane(forward, currentUp);
            forwardOnGround = forwardOnGround.sqrMagnitude > 0.0001f ? forwardOnGround.normalized : forward;

            Vector3 driveAccel = forwardOnGround * (move * driveForce);
            Vector3 slopeAssistAccel = Vector3.zero;

            if (slopeAssistEnabled && Mathf.Abs(move) > 0.001f && slopeAssistStrength > 0f && maxSlopeAssistAcceleration > 0f)
            {
                Vector3 gravityAlongGround = Vector3.ProjectOnPlane(Physics.gravity, groundNormal);
                Vector3 moveDir = forwardOnGround * Mathf.Sign(move);

                float uphillAccelNeeded = Mathf.Max(0f, -Vector3.Dot(gravityAlongGround, moveDir));
                float assist = uphillAccelNeeded * slopeAssistStrength * Mathf.Abs(move);
                assist = Mathf.Min(assist, maxSlopeAssistAcceleration);
                slopeAssistAccel = moveDir * assist;
            }

            Vector3 totalAccel = driveAccel + slopeAssistAccel;
            rb.AddForce(totalAccel, ForceMode.Acceleration);
            Debug.DrawRay(rb.position, totalAccel, slopeAssistAccel == Vector3.zero ? Color.green : Color.yellow, Time.fixedDeltaTime);

            if (rb.linearVelocity.magnitude > maxVelocityToTurn) return; // don't turn if we're moving

            Vector3 torque = transform.up * (turn * Mathf.Max(0f, turnTorque));
            rb.AddTorque(torque, ForceMode.Acceleration);
            Debug.DrawRay(rb.position, torque, Color.blue, Time.fixedDeltaTime);
        }
    }
}
