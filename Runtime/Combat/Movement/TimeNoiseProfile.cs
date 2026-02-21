using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat.Movement
{
    [CreateAssetMenu(fileName = "TimeNoiseProfile", menuName = "RoachRace/Combat/Movement/Time Noise Profile")]
    public class TimeNoiseProfile : ProjectileMovementProfile
    {
        [Header("Torque / Steering")]
        [Tooltip("Strength of the torque applied by noise.")]
        public AnimationCurve torqueStrength = AnimationCurve.Linear(0, 5, 1, 0);

        [Header("Physics Simulation")]
        [Tooltip("Mass used for editor path simulation.")]
        public float simulatedMass = 1.0f;
        [Tooltip("Angular drag used for editor path simulation.")]
        public float simulatedAngularDrag = 2.0f;

        [Header("Perlin Noise")]
        [Tooltip("How fast the Perlin noise cycles over time.")]
        public float noiseFrequency = 1.25f;

        public override void OnInitialize(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier)
        {
            rb.useGravity = false;
            // Set initial velocity
            ApplyForwardVelocity(rb, t, moveDirection, defaultSpeed * speedMultiplier);
        }

        public override void OnFixedUpdate(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier, float timeAlive, float seedX, float seedY, float seedZ, Vector3 initialPosition)
        {
            float speed = defaultSpeed * speedMultiplier;
            AppyTorqueSteering(rb, timeAlive, seedX, seedY, seedZ);
            ApplyForwardVelocity(rb, t, moveDirection, speed);
        }

        private void AppyTorqueSteering(Rigidbody rb, float time, float sx, float sy, float sz)
        {
            if (torqueStrength == null || torqueStrength.length == 0) return;

            float tStr = time * noiseFrequency;
            // Noise values [-1, 1]
            float pitch = (Mathf.PerlinNoise(sx, tStr) * 2f) - 1f;
            float yaw = (Mathf.PerlinNoise(sy, tStr) * 2f) - 1f;
            float roll = (Mathf.PerlinNoise(sz, tStr) * 2f) - 1f;

            // Apply torque around local X (pitch) and Y (yaw) axes
            Vector3 torque = new Vector3(pitch, yaw, roll) * torqueStrength.Evaluate(time);
            rb.AddRelativeTorque(torque, forceMode);
            Debug.DrawRay(new Vector3(0, Time.fixedTime % 5f, 0), torque, Color.yellow, 1.0f);
        }

        public override void SimulatePath(List<Vector3> points, Vector3 origin, Vector3 forward, Vector3 up, Vector3 right, float speedMultiplier, float duration, float stepSize, float seedX, float seedY)
        {
            points.Clear();
            points.Add(origin);

            Vector3 currentPos = origin;
            Quaternion currentRot = Quaternion.LookRotation(forward, up);
            Vector3 currentAngularVel = Vector3.zero;

            float time = 0f;
            float speed = defaultSpeed * speedMultiplier;

            int steps = Mathf.CeilToInt(duration / Mathf.Max(0.001f, stepSize));

            for (int i = 0; i < steps; i++)
            {
                // Calculate torque-like rotation
                float tStr = time * noiseFrequency;
                float pitch = (Mathf.PerlinNoise(seedX, tStr) * 2f) - 1f;
                float yaw = (Mathf.PerlinNoise(seedY, tStr) * 2f) - 1f;

                Vector3 localTorque = new Vector3(pitch, yaw, 0f) * torqueStrength.Evaluate(time);
                
                // Physics Integration (Torque -> Angular Vel)
                Vector3 angularAccel = localTorque / Mathf.Max(0.001f, simulatedMass);
                currentAngularVel += angularAccel * stepSize;
                
                // Drag
                currentAngularVel *= Mathf.Clamp01(1.0f - (simulatedAngularDrag * stepSize));
                
                // Rotate
                Vector3 deltaEuler = currentAngularVel * (stepSize * Mathf.Rad2Deg);
                currentRot *= Quaternion.Euler(deltaEuler);

                // Move forward
                Vector3 fwd = currentRot * Vector3.forward;
                currentPos += fwd * (speed * stepSize);
                
                points.Add(currentPos);
                time += stepSize;
            }
        }
    }
}
