using FishNet.Object;
using RoachRace.Networking.Effects;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Applies a status effect when objects are pinned or pressed together.<br>
    /// Use this on server-authoritative rigidbodies that should continuously apply a configured effect during sustained contact.<br>
    /// Stack distribution is mass-based so lighter objects receive more of the configured effect.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsCollisionCrushStatusEffect : NetworkBehaviour
    {
        [Header("Effect")]
        [SerializeField] private StatusEffectEntry statusEffect = new();

        [Header("Impact Filtering")]
        [Tooltip("Minimum sustained impulse per second to trigger the status effect")]
        [SerializeField] private float minCrushImpulsePerSecond = 2f;

        [Header("Multipliers")]
        [Tooltip("Multiplier for stacks this object receives")]
        [SerializeField] private float selfStackMultiplier = 1f;

        [Tooltip("Multiplier for stacks this object applies to others")]
        [SerializeField] private float outgoingStackMultiplier = 1f;

        private Rigidbody _rb;

        /// <summary>
        /// Caches the required rigidbody and fails fast if it is missing.<br>
        /// This keeps the collision logic safe to execute later on the server.
        /// </summary>
        private void Awake()
        {
            if (!TryGetComponent<Rigidbody>(out _rb))
            {
                Debug.LogError($"[{nameof(PhysicsCollisionCrushStatusEffect)}] Rigidbody component is missing on '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException(
                    $"[{nameof(PhysicsCollisionCrushStatusEffect)}] Rigidbody is missing on GameObject '{gameObject.name}'. " +
                    "This component requires a Rigidbody to function.");
            }

        }

        /* -----------------------------------------------------------
         *  CRUSH STATUS EFFECTS (pinned against wall / heavy object)
         * ----------------------------------------------------------- */
        /// <summary>
        /// Applies the configured status effect to both objects while the collision remains strong enough.<br>
        /// The effect is only applied on the server, and low-energy contact is ignored by the impulse filter.
        /// </summary>
        /// <param name="collision">The collision reported by Unity physics.</param>
        private void OnCollisionStay(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            if (statusEffect.effect == null)
            {
                Debug.LogError($"[{nameof(PhysicsCollisionCrushStatusEffect)}] Status effect is not assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            float impulsePerSecond = collision.impulse.magnitude / Time.fixedDeltaTime;

            if (impulsePerSecond < minCrushImpulsePerSecond)
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

            var (selfStacks, otherStacks) = PhysicsCollisionDamageCalculator.CalculateStacksDistribution(
                statusEffect.stacks,
                _rb.mass,
                otherMass,
                selfStackMultiplier,
                outgoingStackMultiplier
            );

            if (GetComponentInChildren<StatusEffectTickRunner>(true) is StatusEffectTickRunner self)
            {
                self.AddEffect(statusEffect.effect, selfStacks, GetInstigatorId(collision.gameObject));
            }

            if (collision.gameObject.GetComponentInChildren<StatusEffectTickRunner>(true) is StatusEffectTickRunner other)
            {
                other.AddEffect(statusEffect.effect, otherStacks, GetInstigatorId(gameObject));
            }
        }

        /// <summary>
        /// Gets the owner connection id for a GameObject when a <see cref="NetworkObject"/> is present.<br>
        /// Returns -1 for non-networked objects so the effect can still be applied as environment contact.
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
