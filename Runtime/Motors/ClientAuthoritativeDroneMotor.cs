using FishNet.Object;
using RoachRace.Controls;
using UnityEngine;

namespace RoachRace.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class ClientAuthoritativeDroneMotor : NetworkBehaviour
    {
        [System.Serializable]
        public struct DroneInputData
        {
            public Vector2 Input;
            public float CameraYaw;
        }

        [Header("Movement")]
        [SerializeField] private float maxSpeed = 8f;
        [SerializeField] private float acceleration = 20f;
        [Tooltip("Extra stop force applied ONLY when there is no input. Use this to make 'stick released = stop quickly' without increasing Rigidbody drag/damping (which also slows movement while input is held).")]
        [SerializeField] private float brake = 8f;

        [Header("Rotation")]
        [SerializeField] private float maxTiltAngle = 35f;
        [SerializeField] private float yawAlignSpeed = 10f;
        [SerializeField] private float tiltAlignSpeed = 12f;

        private Rigidbody rb;

        private void Awake()
        {
            if (!TryGetComponent(out rb) || rb == null)
            {
                Debug.LogError($"[{nameof(ClientAuthoritativeDroneMotor)}] Rigidbody is not assigned and was not found! Please add a Rigidbody component.", gameObject);
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeDroneMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Non-owners should not simulate physics locally.
            // Owner simulates locally and NetworkTransform (client-authoritative) replicates to server/others.
            if (rb != null)
                rb.isKinematic = !IsOwner;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Client authoritative: server does not simulate this body.
            if (rb != null)
                rb.isKinematic = true;
        }

        private void FixedUpdate()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            float dt = Time.fixedDeltaTime;
            DroneInputData input = ReadInput();

            ApplyMovement(input, dt);
            ApplyVisualRotation(input, dt);
        }

        private DroneInputData ReadInput()
        {
            var host = RoachRaceInputActionsHost.Instance;
            Vector2 input = host != null ? host.Player.Move.ReadValue<Vector2>() : Vector2.zero;

            float cameraYaw = transform.eulerAngles.y;
            Camera cam = Camera.main;
            if (cam != null)
                cameraYaw = cam.transform.eulerAngles.y;

            return new DroneInputData
            {
                Input = input,
                CameraYaw = cameraYaw
            };
        }

        private void ApplyMovement(DroneInputData input, float dt)
        {
            Vector3 velocity = rb.linearVelocity;
            rb.linearVelocity = velocity;

            Vector3 wishDir = Vector3.zero;
            if (input.Input.sqrMagnitude > 0.0001f)
            {
                Quaternion yawRot = Quaternion.Euler(0f, input.CameraYaw, 0f);

                Vector3 forward = yawRot * Vector3.forward;
                Vector3 right = yawRot * Vector3.right;
                wishDir = forward * input.Input.y + right * input.Input.x;

                if (wishDir.sqrMagnitude > 0.0001f)
                    wishDir.Normalize();
            }

            Vector3 horizontalVel = new(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            if (wishDir.sqrMagnitude > 0f)
            {
                Vector3 targetHorizontalVel = wishDir * maxSpeed;
                Vector3 velocityChange = targetHorizontalVel - horizontalVel;
                velocityChange = Vector3.ClampMagnitude(velocityChange, acceleration * dt);
                rb.AddForce(new Vector3(velocityChange.x, 0f, velocityChange.z), ForceMode.VelocityChange);
            }
            else
            {
                // No input: brake towards a stop.
                Vector3 brakeAccel = -horizontalVel * brake;
                rb.AddForce(new Vector3(brakeAccel.x, 0f, brakeAccel.z), ForceMode.Acceleration);
            }
        }

        private void ApplyVisualRotation(DroneInputData input, float dt)
        {
            float yaw = input.CameraYaw;

            // Tilt into desired movement direction (based on input, not current velocity).
            float targetPitch = input.Input.y * maxTiltAngle;
            float targetRoll = -input.Input.x * maxTiltAngle;
            Quaternion tiltRot = Quaternion.Euler(targetPitch, 0f, targetRoll);

            float yawT = 1f - Mathf.Exp(-yawAlignSpeed * dt);
            float tiltT = 1f - Mathf.Exp(-tiltAlignSpeed * dt);

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
