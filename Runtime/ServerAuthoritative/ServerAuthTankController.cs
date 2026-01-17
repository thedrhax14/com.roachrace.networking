using FishNet.Connection;
using FishNet.Object;
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

        public GameObject Wheels;

        private Vector2 _latestInput;

        protected override void Awake()
        {
            base.Awake();

            Rigidbody rb = Rigidbody;
            if (rb.mass < 50f)
                rb.mass = 1500f;
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

            if (Wheels != null && Wheels.activeSelf != IsServerInitialized)
                Wheels.SetActive(IsServerInitialized);
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

            Vector3 worldUp = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(rot * Vector3.forward, worldUp);
            if (forward.sqrMagnitude > 0.0001f)
                forward.Normalize();
            else
                forward = rot * Vector3.forward;

            rb.AddForce(forward * (move * Mathf.Max(0f, driveForce)), ForceMode.Acceleration);

            rb.AddTorque(worldUp * (turn * Mathf.Max(0f, turnTorque)), ForceMode.Acceleration);
        }
    }
}
