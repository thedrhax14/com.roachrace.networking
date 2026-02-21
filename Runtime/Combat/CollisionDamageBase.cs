using FishNet.Object;
using RoachRace.Data;
using RoachRace.Networking.Extensions;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    public abstract class CollisionDamageBase : NetworkBehaviour
    {
        [Header("Impact Damage")]
        [SerializeField] protected float minImpactImpulse = 6f;
        [SerializeField] protected float impulseToDamage = 0.08f;

        [Header("Multipliers")]
        [SerializeField] protected float selfDamageMultiplier = 1f;
        [SerializeField] protected float outgoingDamageMultiplier = 1f;

        [Header("Fallback")]
        [SerializeField] protected float fallbackMass = 1000f;

        protected void ApplyImpactDamage(
            float impulseMagnitude,
            float selfMass,
            float otherMass,
            Vector3 contactPoint,
            Vector3 contactNormal,
            GameObject otherObject,
            int selfInstigatorId,
            int otherInstigatorId,
            DamageType damageType = DamageType.Impact)
        {
            if (!IsServerInitialized)
                return;

            if (impulseMagnitude < minImpactImpulse)
                return;

            float baseDamage = impulseMagnitude * impulseToDamage;

            var (selfDamage, otherDamage) =
                PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                    baseDamage,
                    selfMass,
                    otherMass,
                    selfDamageMultiplier,
                    outgoingDamageMultiplier
                );

            DealDamage(
                gameObject,
                selfDamage,
                contactPoint,
                contactNormal,
                selfInstigatorId,
                damageType
            );

            DealDamage(
                otherObject,
                otherDamage,
                contactPoint,
                -contactNormal,
                otherInstigatorId,
                damageType
            );
        }

        private void DealDamage(
            GameObject target,
            int amount,
            Vector3 point,
            Vector3 normal,
            int instigatorId,
            DamageType type)
        {
            if (amount <= 0f)
                return;

            if (target.GetComponentInChildren<NetworkHealth>() is not NetworkHealth health)
                return;

            if (!health.IsAlive)
                return;

            var damageInfo = new DamageInfo
            {
                Amount = amount,
                Type = type,
                Point = point,
                Normal = normal,
                InstigatorId = instigatorId,
                Source = default
            };

            health.TryConsume(damageInfo);
        }

        /// <summary>
        /// Attempts to get the owner connection ID from the NetworkObject on the GameObject.
        /// Returns -1 if no NetworkObject is found (environment/server-owned).
        /// </summary>
        protected static int GetInstigatorId(GameObject obj)
        {
            return obj.TryGetComponent(out NetworkObject netObj)
                ? netObj.OwnerId
                : -1;
        }
    }
}
