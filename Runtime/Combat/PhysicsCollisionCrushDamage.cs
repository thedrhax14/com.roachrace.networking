using FishNet.Object;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Applies continuous crush damage when objects are pinned or pressed together.
    /// This is separate from impact damage and should be added to specific colliders
    /// that can crush (e.g., hydraulic presses, closing doors, heavy objects).
    /// 
    /// Example Use Cases:
    /// - Hydraulic Presses: Industrial crushers that apply continuous pressure
    /// - Closing Doors: Heavy doors that crush players caught between them
    /// - Pinning Mechanics: Heavy objects pinning lighter objects against walls
    /// - Vehicle Wheels: Running over objects with sustained contact
    /// 
    /// Damage Distribution:
    /// Uses inverted mass ratios where lighter objects take MORE crush damage.
    /// Example: 35kg player vs 2000kg hydraulic press → player takes 98% damage, press takes 2%
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsCollisionCrushDamage : NetworkBehaviour
    {
        [Header("Crush Damage")]
        [Tooltip("Minimum sustained impulse per second to trigger crush damage")]
        [SerializeField] private float minCrushImpulsePerSecond = 2f;
        
        [Tooltip("Converts impulse per second to damage rate")]
        [SerializeField] private float crushDamagePerImpulse = 0.04f;

        [Header("Multipliers")]
        [Tooltip("Multiplier for damage this object receives")]
        [SerializeField] private float selfDamageMultiplier = 1f;
        
        [Tooltip("Multiplier for damage this object deals to others")]
        [SerializeField] private float outgoingDamageMultiplier = 1f;

        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            
            if (_rb == null)
            {
                Debug.LogError($"[PhysicsCollisionCrushDamage] Rigidbody component is missing on '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException(
                    $"[PhysicsCollisionCrushDamage] Rigidbody is null on GameObject '{gameObject.name}'. " +
                    "This component requires a Rigidbody to function.");
            }
        }

        /* -----------------------------------------------------------
         *  CRUSH DAMAGE (pinned against wall / heavy object)
         * ----------------------------------------------------------- */
        private void OnCollisionStay(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            // Average impulse applied per physics step
            float impulsePerSecond = collision.impulse.magnitude / Time.fixedDeltaTime;

            if (impulsePerSecond < minCrushImpulsePerSecond)
                return;

            float damage = impulsePerSecond * crushDamagePerImpulse * Time.fixedDeltaTime;

            ApplySymmetricDamage(
                collision,
                damage,
                DamageType.Crush
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
