using UnityEngine;

namespace RoachRace.Networking.Combat
{
    public sealed class KinematicCollisionDamage : CollisionDamageBase
    {
        [SerializeField] private float selfMass = 35f;

        public void ApplyKinematicImpact(
            Vector3 selfVelocity,
            Rigidbody otherRb,
            GameObject otherObject,
            Vector3 contactPoint,
            Vector3 contactNormal)
        {
            float otherMass = otherRb ? otherRb.mass : fallbackMass;

            Vector3 relativeVelocity = otherRb
                ? otherRb.linearVelocity - selfVelocity
                : -selfVelocity;

            float impulse = relativeVelocity.magnitude * selfMass;

            ApplyImpactDamage(
                impulseMagnitude: impulse,
                selfMass: selfMass,
                otherMass: otherMass,
                contactPoint: contactPoint,
                contactNormal: contactNormal,
                otherObject: otherObject,
                selfInstigatorId: GetInstigatorId(gameObject),
                otherInstigatorId: GetInstigatorId(otherObject)
            );
        }
    }
}
