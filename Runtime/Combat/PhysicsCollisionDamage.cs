using FishNet.Object;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Bidirectional physics collision damage - applies damage on impact collisions.
    /// This distributes damage based on mass ratios and applies it to both the host object
    /// and collision target.
    /// 
    /// Example Use Cases:
    /// - Thrown Props: Heavy objects that damage both the thrower and the target on impact
    /// - Vehicles: Cars/drones that take damage when crashing into walls or other vehicles
    /// - Falling Objects: Crates/barrels that damage players and themselves when dropped
    /// - Interactive Physics Objects: Explosive barrels, loose debris, physics weapons
    /// 
    /// Damage Distribution:
    /// Uses inverted mass ratios where lighter objects take MORE damage.
    /// Example: 35kg drone vs 2000kg prop → drone takes 98% damage, prop takes 2%
    /// This creates realistic physics where hitting a wall hurts you more than it hurts the wall.
    ///   
    /// Note: For crush damage (sustained pressure), use PhysicsCollisionCrushDamage instead.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsCollisionDamage : NetworkBehaviour
    {
        [Header("Impact Damage")]
        [Tooltip("Minimum impulse required to trigger impact damage")]
        [SerializeField] private float minImpactImpulse = 6f;
        
        [Tooltip("Converts impulse magnitude to damage (damage = impulse * impulseToDamage)")]
        [SerializeField] private float impulseToDamage = 0.08f;

        [Header("Multipliers")]
        [Tooltip("Multiplier for damage this object receives")]
        [SerializeField] private float selfDamageMultiplier = 1f;
        
        [Tooltip("Multiplier for damage this object deals to others")]
        [SerializeField] private float outgoingDamageMultiplier = 1f;

        [Header("Fall Filtering")]
        [Tooltip("Minimum downward velocity to consider collision as potential fall damage")]
        [SerializeField] private float minDownwardVelocity = -3f;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            
            if (_rb == null)
            {
                Debug.LogError($"[PhysicsCollisionDamage] Rigidbody component is missing on '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException(
                    $"[PhysicsCollisionDamage] Rigidbody is null on GameObject '{gameObject.name}'. " +
                    "This component requires a Rigidbody to function.");
            }
        }

        /* -----------------------------------------------------------
         *  IMPACT DAMAGE (falls, throws, hits)
         * ----------------------------------------------------------- */
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized)
                return;
            Debug.DrawRay(transform.position, collision.impulse, Color.red, 2f);
            Debug.Log($"[{gameObject.name}] Collision impulse magnitude: {collision.impulse.magnitude}", gameObject);
            float impulse = collision.impulse.magnitude;

            if (impulse < minImpactImpulse)
                return;

            // Prevent footstep / sliding noise
            if (_rb.linearVelocity.y > minDownwardVelocity && collision.relativeVelocity.magnitude < 1f)
                return;

            ApplySymmetricDamage(
                collision,
                impulse * impulseToDamage,
                DamageType.Impact
            );
        }

        /* -----------------------------------------------------------
         *  SHARED DAMAGE DISTRIBUTION
         * ----------------------------------------------------------- */
        private void ApplySymmetricDamage(
            Collision collision,
            float baseDamage,
            DamageType type)
        {
            ContactPoint contact = collision.GetContact(0);

            Rigidbody otherRb = collision.rigidbody;
            float otherMass = otherRb ? otherRb.mass : 1000f;

            // Calculate damage distribution using the calculator
            var (selfDamage, otherDamage) = PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                baseDamage,
                _rb.mass,
                otherMass,
                selfDamageMultiplier,
                outgoingDamageMultiplier
            );

            // Damage self
            if (GetComponentInChildren<NetworkHealth>() is NetworkHealth selfHealth)
            {
                if (selfHealth.IsAlive && selfDamage > 0)
                {
                    var selfDamageInfo = new DamageInfo
                    {
                        Amount = selfDamage,
                        Type = type,
                        Point = contact.point,
                        Normal = contact.normal,
                        InstigatorId = GetInstigatorId(collision.gameObject),
                        Source = default
                    };
                    
                    selfHealth.TryConsume(selfDamageInfo);
                }
            }

            // Damage other
            if (collision.gameObject.GetComponentInChildren<NetworkHealth>() is NetworkHealth otherHealth)
            {
                if (otherHealth.IsAlive && otherDamage > 0)
                {
                    var otherDamageInfo = new DamageInfo
                    {
                        Amount = otherDamage,
                        Type = type,
                        Point = contact.point,
                        Normal = -contact.normal,
                        InstigatorId = GetInstigatorId(gameObject),
                        Source = default
                    };
                    
                    otherHealth.TryConsume(otherDamageInfo);
                }
            }
        }

        /// <summary>
        /// Attempts to get the NetworkObject ID from the GameObject.
        /// Returns -1 if no NetworkObject is found.
        /// </summary>
        private int GetInstigatorId(GameObject obj)
        {
            if (obj.TryGetComponent<NetworkObject>(out var networkObject))
            {
                return networkObject.ObjectId;
            }
            return -1;
        }
    }
}