using FishNet.Object;
using RoachRace.Networking.Effects;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Applies a status effect to colliding objects when the impact is strong enough.<br>
    /// Use this on server-authoritative rigidbodies that also have a <see cref="StatusEffectTickRunner"/> on the relevant hierarchy.<br>
    /// Stack distribution is mass-based so lighter objects receive more of the configured effect.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodyCollisionStatusEffect : NetworkBehaviour
    {
        [Header("Effect")]
        [SerializeField] private StatusEffectEntry statusEffect = new();

        [Header("Impact Filtering")]
        [Tooltip("Minimum impulse required to trigger the status effect")]
        [SerializeField] private float minImpactImpulse = 6f;

        [Header("Multipliers")]
        [Tooltip("Multiplier for stacks this object receives")]
        [SerializeField] private float selfStackMultiplier = 1f;

        [Tooltip("Multiplier for stacks this object applies to others")]
        [SerializeField] private float outgoingStackMultiplier = 1f;

        [Header("Fall Filtering")]
        [Tooltip("Minimum downward velocity to consider collision as a potential impact trigger")]
        [SerializeField] private float minDownwardVelocity = -3f;

        private Rigidbody _rb;

        /// <summary>
        /// Caches the required rigidbody and fails fast if it is missing.<br>
        /// This keeps the impact logic safe to execute later on the server.
        /// </summary>
        private void Awake()
        {
            if (!TryGetComponent<Rigidbody>(out _rb))
            {
                Debug.LogError($"[{nameof(RigidbodyCollisionStatusEffect)}] Rigidbody component is missing on '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(RigidbodyCollisionStatusEffect)}] Rigidbody is missing on GameObject '{gameObject.name}'. " +
                    "This component requires a Rigidbody to function.");
            }
            
        }

        /* -----------------------------------------------------------
         *  IMPACT STATUS EFFECTS (falls, throws, hits)
         * ----------------------------------------------------------- */
        /// <summary>
        /// Applies the configured status effect to both objects when the collision impulse passes the impact filter.<br>
        /// The effect is only applied on the server, and the fall filter suppresses low-energy sliding contacts.
        /// </summary>
        /// <param name="collision">The collision reported by Unity physics.</param>
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            if (statusEffect.effect == null)
            {
                Debug.LogError($"[{nameof(RigidbodyCollisionStatusEffect)}] Status effect is not assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            Debug.DrawRay(transform.position, collision.impulse, Color.red, 2f);
            float impulse = collision.impulse.magnitude;

            if (impulse < minImpactImpulse)
                return;

            // Prevent footstep / sliding noise
            if (_rb.linearVelocity.y > minDownwardVelocity && collision.relativeVelocity.magnitude < 1f)
                return;

            ApplySymmetricStatusEffects(collision);
        }

        /* -----------------------------------------------------------
         *  SHARED STATUS EFFECT DISTRIBUTION
         * ----------------------------------------------------------- */
        /// <summary>
        /// Distributes the configured stack count between the colliding bodies and applies the status effect.<br>
        /// The object itself receives effect stacks through its own <see cref="StatusEffectTickRunner"/> if present.
        /// </summary>
        /// <param name="collision">The collision being processed.</param>
        private void ApplySymmetricStatusEffects(Collision collision)
        {
            Rigidbody otherRb = collision.rigidbody;
            float otherMass = otherRb ? otherRb.mass : 1000f;
            Vector3 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : (transform.position + collision.transform.position) * 0.5f;

            // Calculate status-effect distribution using the calculator.
            var (selfStacks, otherStacks) = PhysicsCollisionDamageCalculator.CalculateStacksDistribution(
                statusEffect.stacks,
                _rb.mass,
                otherMass,
                selfStackMultiplier,
                outgoingStackMultiplier
            );

            if (GetComponentInChildren<StatusEffectTickRunner>() is StatusEffectTickRunner self)
            {
                self.AddEffect(statusEffect.effect, selfStacks, GetInstigatorId(collision.gameObject), hasSourceWorldPosition: true, sourceWorldPosition: collision.gameObject.transform.position, hasTargetWorldPosition: true, targetWorldPosition: hitPoint);
            }

            if (collision.gameObject.GetComponentInChildren<StatusEffectTickRunner>() is StatusEffectTickRunner other)
            {
                other.AddEffect(statusEffect.effect, otherStacks, GetInstigatorId(gameObject), hasSourceWorldPosition: true, sourceWorldPosition: transform.position, hasTargetWorldPosition: true, targetWorldPosition: hitPoint);
            }
        }

        /// <summary>
        /// Gets the owner connection id for a GameObject when a <see cref="NetworkObject"/> is present.<br>
        /// Returns -1 for non-networked objects so the effect can still be applied as environment damage.
        /// </summary>
        /// <param name="obj">The object to inspect.</param>
        /// <returns>The owner id, or -1 when no network object exists.</returns>
        private int GetInstigatorId(GameObject obj)
        {
            if (obj.TryGetComponent<NetworkObject>(out var networkObject))
            {
                return networkObject.OwnerId;
            }
            return -1;
        }
    }
}