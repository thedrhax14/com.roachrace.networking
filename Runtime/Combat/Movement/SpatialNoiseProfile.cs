using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat.Movement
{
    [CreateAssetMenu(fileName = "SpatialNoiseProfile", menuName = "RoachRace/Combat/Movement/Spatial Noise Profile")]
    public class SpatialNoiseProfile : ProjectileMovementProfile
    {
        [Header("Spatial Noise")]
        [Tooltip("Scale of the noise field in world units.")]
        public float spatialScale = 0.5f;

        [Tooltip("Strength of the torque applied by noise.")]
        public float torqueStrength = 5.0f;

        [Header("Physics Simulation")]
        [Tooltip("Mass used for editor path simulation.")]
        public float simulatedMass = 1.0f;
        [Tooltip("Angular drag used for editor path simulation.")]
        public float simulatedAngularDrag = 2.0f;

        public override void OnInitialize(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier)
        {
            rb.useGravity = false;
            ApplyForwardVelocity(rb, t, moveDirection, defaultSpeed * speedMultiplier);
        }

        public override void OnFixedUpdate(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier, float timeAlive, float seedX, float seedY, float seedZ, Vector3 initialPosition)
        {
            // Calculate spatial noise based on position relative to start
            Vector3 relativePos = t.position - initialPosition;
            AppyTorqueSteering(rb, relativePos, seedX, seedY, seedZ);
            // Keep moving forward
            ApplyForwardVelocity(rb, t, moveDirection, defaultSpeed * speedMultiplier);
        }

        private void AppyTorqueSteering(Rigidbody rb, Vector3 pos, float sx, float sy, float sz)
        {
            if (torqueStrength <= 0.001f) return;

            float scale = Mathf.Max(0.01f, spatialScale);
            float pitch = (Mathf.PerlinNoise(pos.x * scale + sx, pos.y * scale + sy) * 2f) - 1f;
            float yaw = (Mathf.PerlinNoise(pos.z * scale + sx, pos.y * scale + sy) * 2f) - 1f;
            float roll = (Mathf.PerlinNoise(pos.x * scale + sz, pos.z * scale + sx) * 2f) - 1f;

            // Apply torque around local axes
            Vector3 torque = new Vector3(pitch, yaw, roll) * torqueStrength;
            rb.AddRelativeTorque(torque, forceMode);
            Debug.DrawRay(new Vector3(0, Time.fixedTime % 5f, 0), torque, Color.yellow, 1.0f);
        }

        public override void SimulatePath(List<Vector3> points, Vector3 origin, Vector3 forward, Vector3 up, Vector3 right, float speedMultiplier, float duration, float stepSize, float seedX, float seedY)
        {
            points.Clear();
            points.Add(origin);

            Vector3 currentPos = origin;
            Quaternion currentRot = Quaternion.LookRotation(forward, up);
            Vector3 currentAngularVel = Vector3.zero; // Local angular velocity
            
            float speed = defaultSpeed * speedMultiplier;

            int steps = Mathf.CeilToInt(duration / Mathf.Max(0.001f, stepSize));
            
            for (int i = 0; i < steps; i++)
            {
                // Calculate relative position for noise lookup
                Vector3 relativePos = currentPos - origin;
                
                float scale = Mathf.Max(0.01f, spatialScale);
                float pitch = (Mathf.PerlinNoise(relativePos.x * scale + seedX, relativePos.y * scale + seedY) * 2f) - 1f;
                float yaw = (Mathf.PerlinNoise(relativePos.z * scale + seedX, relativePos.y * scale + seedY) * 2f) - 1f;
                float roll = (Mathf.PerlinNoise(relativePos.x * scale + seedY, relativePos.z * scale + seedX) * 2f) - 1f;

                Vector3 localTorque = new Vector3(pitch, yaw, roll) * torqueStrength;
                
                // Physics Integration (Torque -> Angular Vel)
                // alpha = torque / mass
                Vector3 angularAccel = localTorque / Mathf.Max(0.001f, simulatedMass);
                currentAngularVel += angularAccel * stepSize;
                
                // Apply Drag
                currentAngularVel *= Mathf.Clamp01(1.0f - (simulatedAngularDrag * stepSize));

                // Apply Rotation (Angular Vel -> Rotation)
                // Note: Angular velocity is in Radians, Quaternion.Euler expects Degrees
                Vector3 deltaEuler = currentAngularVel * (stepSize * Mathf.Rad2Deg);
                currentRot *= Quaternion.Euler(deltaEuler);

                // Move forward
                Vector3 fwd = currentRot * Vector3.forward;
                currentPos += fwd * (speed * stepSize);

                points.Add(currentPos);
            }
        }
    }
}
