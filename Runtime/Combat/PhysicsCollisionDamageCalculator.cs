using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Calculates physics-based collision damage distribution between two objects.
    /// Uses inverted mass ratios where lighter objects take MORE damage.
    /// </summary>
    public static class PhysicsCollisionDamageCalculator
    {
        /// <summary>
        /// Calculates how damage should be distributed between self and other object.
        /// Uses inverted mass ratios: lighter objects take MORE damage.
        /// </summary>
        /// <param name="baseDamage">Total damage from the collision impulse</param>
        /// <param name="selfMass">Mass of the object taking damage</param>
        /// <param name="otherMass">Mass of the object being collided with (use 1000f for static/infinite mass)</param>
        /// <param name="selfMultiplier">Additional multiplier for self damage</param>
        /// <param name="otherMultiplier">Additional multiplier for other damage</param>
        /// <returns>Tuple of (selfDamage, otherDamage) as integers</returns>
        public static (int selfDamage, int otherDamage) CalculateDamageDistribution(
            float baseDamage,
            float selfMass,
            float otherMass,
            float selfMultiplier = 1f,
            float otherMultiplier = 1f)
        {
            // Inverted mass ratios: lighter objects take MORE damage
            // If you're light hitting heavy, you take most of the damage
            float totalMass = selfMass + otherMass;
            float selfRatio = otherMass / totalMass;  // Inverted: use OTHER mass for SELF damage
            float otherRatio = selfMass / totalMass;  // Inverted: use SELF mass for OTHER damage

            int selfDamage = Mathf.RoundToInt(baseDamage * selfRatio * selfMultiplier);
            int otherDamage = Mathf.RoundToInt(baseDamage * otherRatio * otherMultiplier);

            return (selfDamage, otherDamage);
        }

        /// <summary>
        /// Converts collision impulse to base damage value.
        /// </summary>
        public static float ImpulseToDamage(float impulseMagnitude, float conversionFactor)
        {
            return impulseMagnitude * conversionFactor;
        }

        /// <summary>
        /// Converts sustained impulse per second to crush damage per frame.
        /// </summary>
        public static float CrushDamagePerFrame(float impulsePerSecond, float conversionFactor, float deltaTime)
        {
            return impulsePerSecond * conversionFactor * deltaTime;
        }
    }
}
