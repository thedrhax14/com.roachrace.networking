using System;
using FishNet.Connection;
using FishNet.Object;
using RoachRace.Controls;
using UnityEngine;
using UnityEngine.InputSystem;

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
            public float CameraPitch;
            public float VerticalInput;
        }

        [Header("Movement")]
        [SerializeField] private float maxSpeed = 8f;
        [SerializeField] private float acceleration = 20f;
        [SerializeField] private float maxVerticalSpeed = 4f;
        [SerializeField] private float verticalAcceleration = 14f;
        [SerializeField] private InputActionReference altitudeInput;

        [Header("Rotation")]
        [SerializeField] private float maxTiltAngle = 35f;
        [SerializeField] private float yawAlignSpeed = 10f;
        [SerializeField] private float tiltAlignSpeed = 12f;

        [Header("Input")]
        [Tooltip("Deadzone used to consider the drone 'stationary' for applying look pitch visual tilt.")]
        [SerializeField] private float planarInputDeadzone = 0.05f;
        [Tooltip("How quickly we blend from look-pitch tilt to move tilt as planar input increases beyond the deadzone.")]
        [SerializeField] private float planarInputBlendRange = 0.15f;

        private float verticalInput = 0f;

        private Rigidbody rb;
        private Camera cam;

        private void Awake()
        {
            cam = Camera.main;
            if (!TryGetComponent(out rb) || rb == null)
            {
                Debug.LogError($"[{nameof(ClientAuthoritativeDroneMotor)}] Rigidbody is not assigned and was not found! Please add a Rigidbody component.", gameObject);
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeDroneMotor)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }

            if (altitudeInput == null || altitudeInput.action == null)
            {
                Debug.LogError($"[{nameof(ClientAuthoritativeDroneMotor)}] altitudeInput is not assigned! Please assign an InputActionReference.", gameObject);
                throw new System.NullReferenceException($"[{nameof(ClientAuthoritativeDroneMotor)}] altitudeInput is null on GameObject '{gameObject.name}'.");
            }
            altitudeInput.action.Enable();
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

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (altitudeInput == null || altitudeInput.action == null)
                return;

            altitudeInput.action.performed -= OnAltitudeInput;
            altitudeInput.action.canceled -= OnAltitudeInput;

            if (IsOwner)
            {
                altitudeInput.action.performed += OnAltitudeInput;
                altitudeInput.action.canceled += OnAltitudeInput;
            }
            else
            {
                verticalInput = 0f;
            }
        }

        void OnAltitudeInput(InputAction.CallbackContext context)
        {
            if(context.performed) {
                verticalInput = context.ReadValue<float>();
            } else if(context.canceled) {
                verticalInput = 0f;
            }
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

            // Camera.main can be null in Awake (or change at runtime).
            if (cam == null)
                cam = Camera.main;

            float yaw = transform.eulerAngles.y;
            float pitch = 0f;
            if (cam != null)
            {
                Vector3 e = cam.transform.eulerAngles;
                yaw = e.y;
                pitch = e.x;
            }

            return new DroneInputData
            {
                Input = input,
                CameraYaw = yaw,
                CameraPitch = pitch,
                VerticalInput = verticalInput
            };
        }

        private void ApplyMovement(DroneInputData input, float dt)
        {
            Vector3 velocity = rb.linearVelocity;

            // Planar movement (based on camera yaw).
            Vector3 wishPlanarDir = Vector3.zero;
            if (input.Input.sqrMagnitude > 0.0001f)
            {
                Quaternion yawRot = Quaternion.Euler(0f, input.CameraYaw, 0f);
                Vector3 forward = yawRot * Vector3.forward;
                Vector3 right = yawRot * Vector3.right;
                wishPlanarDir = forward * input.Input.y + right * input.Input.x;
                if (wishPlanarDir.sqrMagnitude > 0.0001f)
                    wishPlanarDir.Normalize();
            }

            Vector3 horizontalVel = new(velocity.x, 0f, velocity.z);
            if (wishPlanarDir.sqrMagnitude > 0f)
            {
                Vector3 targetHorizontalVel = wishPlanarDir * maxSpeed;
                Vector3 velocityChange = targetHorizontalVel - horizontalVel;
                velocityChange = Vector3.ClampMagnitude(velocityChange, acceleration * dt);
                rb.AddForce(new Vector3(velocityChange.x, 0f, velocityChange.z), ForceMode.VelocityChange);
            }

            // Vertical movement (ascend/descend).
            float desiredYVel = Mathf.Clamp(input.VerticalInput, -1f, 1f) * maxVerticalSpeed;
            float deltaYVel = desiredYVel - velocity.y;
            float maxDeltaY = verticalAcceleration * dt;
            deltaYVel = Mathf.Clamp(deltaYVel, -maxDeltaY, maxDeltaY);
            rb.AddForce(new Vector3(0f, deltaYVel, 0f), ForceMode.VelocityChange);
        }

        private void ApplyVisualRotation(DroneInputData input, float dt)
        {
            float yaw = input.CameraYaw;

            // Tilt into desired movement direction (based on input, not current velocity).
            float planarMag = input.Input.magnitude;

            float moveWeight;
            if (planarMag <= planarInputDeadzone)
            {
                moveWeight = 0f;
            }
            else
            {
                float denom = Mathf.Max(planarInputBlendRange, 0.0001f);
                moveWeight = Mathf.Clamp01((planarMag - planarInputDeadzone) / denom);
            }

            float movePitch = input.Input.y * maxTiltAngle;
            float lookPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, input.CameraPitch), -maxTiltAngle, maxTiltAngle);
            float targetPitch = Mathf.Lerp(lookPitch, movePitch, moveWeight);

            // Roll has no "look" equivalent; reduce roll near-stationary to prevent tiny input noise from rolling.
            float targetRoll = -input.Input.x * maxTiltAngle * moveWeight;
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

        void OnDestroy()
        {
            altitudeInput.action.performed -= OnAltitudeInput;
            altitudeInput.action.canceled -= OnAltitudeInput;
        }
    }
}
