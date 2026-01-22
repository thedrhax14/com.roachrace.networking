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

        [Header("Movement")]
        [SerializeField] private float _maxSpeed = 8f;
        [SerializeField] private float _acceleration = 20f;
        [Tooltip("Extra stop force applied ONLY when there is no input. Use this to make 'stick released = stop quickly' without increasing Rigidbody linear damping (which also slows movement while input is held).")]
        [SerializeField] private float _brake = 8f;

        [Header("Rotation")]
        [SerializeField] private float _maxTiltAngle = 35f;
        [SerializeField] private float _yawAlignSpeed = 10f;
        [SerializeField] private float _tiltAlignSpeed = 12f;

        [Header("Damping")]
        [Tooltip("Optional always-on horizontal damping (server-side) applied in code. Leave at 0 if you are already using Rigidbody linear damping/drag for drift control, otherwise you'll double-damp and the drone may feel sluggish.")]
        [SerializeField] private float _horizontalDrag = 0f;
        [SerializeField] private float _verticalDamping = 12f;

        [SerializeField] private DroneInputData _latestInput;

        public Vector2 LatestMoveInput => _latestInput.Input;
        public float LatestMoveInputMagnitude => _latestInput.Input.magnitude;

        protected override void Awake()
        {
            base.Awake();

            Rigidbody rb = Rigidbody;
            rb.useGravity = false;
            // Keep physics stable at low tick rates by avoiding uncontrolled spin.
            rb.angularDamping = Mathf.Max(rb.angularDamping, 2f);
        }

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

            ApplyMovement(_latestInput, delta);
            ApplyVisualRotation(_latestInput, delta);
        }

        private void ApplyMovement(DroneInputData rd, float delta)
        {
            Rigidbody rb = Rigidbody;

            Vector3 velocity = rb.linearVelocity;

            // Optional: kill tiny vertical drift since gravity is disabled.
            velocity.y = Mathf.Lerp(velocity.y, 0f, 1f - Mathf.Exp(-_verticalDamping * delta));

            Vector3 wishDir = Vector3.zero;
            if (rd.Input.sqrMagnitude > 0.0001f)
            {
                float yaw = rd.CameraYaw;
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

                Vector3 forward = yawRot * Vector3.forward;
                Vector3 right = yawRot * Vector3.right;
                wishDir = forward * rd.Input.y + right * rd.Input.x;

                if (wishDir.sqrMagnitude > 0.0001f)
                    wishDir.Normalize();
            }

            Vector3 horizontalVel = new(velocity.x, 0f, velocity.z);

            if (wishDir.sqrMagnitude > 0.0f)
            {
                Vector3 targetHorizontalVel = wishDir * _maxSpeed;
                Vector3 velocityChange = targetHorizontalVel - horizontalVel;
                velocityChange = Vector3.ClampMagnitude(velocityChange, _acceleration * delta);
                rb.AddForce(new Vector3(velocityChange.x, 0f, velocityChange.z), ForceMode.VelocityChange);
            }
            else
            {
                // No input: brake towards a stop.
                Vector3 brakeAccel = -horizontalVel * _brake;
                rb.AddForce(new Vector3(brakeAccel.x, 0f, brakeAccel.z), ForceMode.Acceleration);
            }

            if (_horizontalDrag > 0f)
            {
                Vector3 dragAccel = -horizontalVel * _horizontalDrag;
                rb.AddForce(new Vector3(dragAccel.x, 0f, dragAccel.z), ForceMode.Acceleration);
            }

            Debug.DrawRay(transform.position, rb.linearVelocity, Color.green, Time.fixedDeltaTime);
        }

        private void ApplyVisualRotation(DroneInputData rd, float delta)
        {
            Rigidbody rb = Rigidbody;

            float yaw = rd.CameraYaw;

            // Tilt into desired movement direction (based on input, not current velocity).
            float targetPitch = rd.Input.y * _maxTiltAngle;
            float targetRoll = -rd.Input.x * _maxTiltAngle;
            Quaternion tiltRot = Quaternion.Euler(targetPitch, 0f, targetRoll);

            float yawT = 1f - Mathf.Exp(-_yawAlignSpeed * delta);
            float tiltT = 1f - Mathf.Exp(-_tiltAlignSpeed * delta);

            // Blend yaw/tilt separately to keep yaw responsive while smoothing tilt.
            Quaternion current = rb.rotation;
            Quaternion yawOnly = Quaternion.Euler(0f, yaw, 0f);
            Quaternion yawBlended = Quaternion.Slerp(Quaternion.Euler(0f, current.eulerAngles.y, 0f), yawOnly, yawT);

            // Apply tilt relative to the yaw-blended frame.
            Quaternion desired = yawBlended * tiltRot;
            Quaternion newRotation = Quaternion.Slerp(current, desired, tiltT);

            rb.MoveRotation(newRotation);
        }
    }
}