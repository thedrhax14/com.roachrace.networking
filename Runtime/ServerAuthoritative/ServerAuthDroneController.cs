using System;
using FishNet.Connection;
using FishNet.Object;
using RoachRace.Controls;
using UnityEngine;

namespace RoachRace.Networking
{
    [RequireComponent(typeof(Rigidbody))]
    public class ServerAuthDroneController : ServerAuthMonsterController
    {
        [Serializable]
        public struct DroneInputData
        {
            public Vector2 Input;
            public float CameraYaw;
        }

        [SerializeField] private float _maxTiltAngle = 35f;
        [SerializeField] private float _stabilizeForce = 10f;
        [SerializeField] private float _stabilizeDamper = 2f;
        [SerializeField] private float _hoverMultiplier = 1f;
        [SerializeField] private float _yawStabilizeForce = 8f;
        [SerializeField] private float _yawStabilizeDamper = 1.5f;

        [SerializeField] private DroneInputData _latestInput;

        public Vector2 LatestMoveInput => _latestInput.Input;
        public float LatestMoveInputMagnitude => _latestInput.Input.magnitude;

        protected override void OnOwnerClientTick()
        {
            if (!IsOwner) return;

            var host = RoachRaceInputActionsHost.Instance;
            Vector2 input = host != null ? host.Player.Move.ReadValue<Vector2>() : Vector2.zero;
            float cameraYaw = (Camera.main != null) ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;
            SubmitInputServerRpc(input, cameraYaw);
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            _latestInput.Input = Vector2.zero;
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector2 input, float cameraYaw, NetworkConnection sender = null)
        {
            if (sender != Owner) {
                _latestInput.Input = Vector2.zero;
                return;
            }
            _latestInput.Input = input;
            _latestInput.CameraYaw = cameraYaw;
        }

        protected override void OnServerTick(float delta)
        {
            if (!IsServerInitialized) return;

            ApplyForces(_latestInput);
        }

        private void ApplyForces(DroneInputData rd)
        {
            Rigidbody rb = Rigidbody;

            float cosTilt = Mathf.Clamp(Vector3.Dot(Vector3.up, transform.up), 0.1f, 1f);
            float tiltCompensation = 1f / cosTilt;

            Vector3 counterGravityForce = _hoverMultiplier * rb.mass * -Physics.gravity.y * tiltCompensation * transform.up;
            rb.AddForce(counterGravityForce, ForceMode.Force);

            float targetPitch = rd.Input.y * _maxTiltAngle;
            float targetRoll = -rd.Input.x * _maxTiltAngle;

            Vector3 currentEuler = transform.eulerAngles;
            float currentPitch = NormalizeAngle(currentEuler.x);
            float currentRoll = NormalizeAngle(currentEuler.z);

            float currentYaw = currentEuler.y;
            float yawError = Mathf.DeltaAngle(currentYaw, rd.CameraYaw);

            float pitchError = targetPitch - currentPitch;
            float rollError = targetRoll - currentRoll;

            Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);

            float pitchTorque = (pitchError * _stabilizeForce) - (localAngVel.x * _stabilizeDamper);
            float rollTorque = (rollError * _stabilizeForce) - (localAngVel.z * _stabilizeDamper);
            float yawTorque = (yawError * _yawStabilizeForce) - (localAngVel.y * _yawStabilizeDamper);

            rb.AddRelativeTorque(new Vector3(pitchTorque, yawTorque, rollTorque), ForceMode.Acceleration);
        }

        private float NormalizeAngle(float angle)
        {
            if (angle > 180f)
                angle -= 360f;
            return angle;
        }
    }
}