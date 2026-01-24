using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat.Movement
{
    /// <summary>
    /// Strategy for how a projectile moves and steers over time.
    /// Handles both runtime physics logic and editor preview simulation.
    /// </summary>
    public abstract class ProjectileMovementProfile : ScriptableObject
    {
        [Header("Base Settings")]
        [Tooltip("Base speed in m/s (can be overridden by the projectile controller if needed)")]
        public float defaultSpeed = 50f;
        public ForceMode forceMode = ForceMode.Force;

        /// <summary>
        /// Called once when the projectile initializes.
        /// Use this to apply initial impulses or setup state.
        /// </summary>
        public virtual void OnInitialize(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier)
        {
            // Default behavior: just ensure we keep moving if we are not ballistic
            // Concrete classes can override (e.g., ImpulseProfile applies force here)
        }

        /// <summary>
        /// Called during FixedUpdate to apply continuous movement.
        /// </summary>
        public abstract void OnFixedUpdate(Rigidbody rb, Transform t, Vector3 moveDirection, float speedMultiplier, float timeAlive, float seedX, float seedY, float seedZ, Vector3 initialPosition);

        protected void ApplyForwardVelocity(Rigidbody rb, Transform t, Vector3 moveDirection, float speed)
        {
            rb.AddForce(t.TransformDirection(moveDirection.normalized) * speed, forceMode);
        }

        /// <summary>
        /// Editor Helper: Generates points for the dashed line preview.
        /// </summary>
        public abstract void SimulatePath(
            List<Vector3> points, 
            Vector3 origin, 
            Vector3 forward, 
            Vector3 up, 
            Vector3 right,
            float speedMultiplier, 
            float duration, 
            float stepSize,
            float seedX, 
            float seedY);
    }
}
