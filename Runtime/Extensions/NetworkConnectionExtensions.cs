using FishNet.Connection;
using FishNet.Object;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking.Extensions
{
    public static class NetworkExtensions
    {
        /// <summary>
        /// Creates a <see cref="DamageInfo"/> struct with <paramref name="instigatorId"/> as the instigator ClientId.
        /// </summary>
        /// <param name="instigatorId">ClientId of the instigator connection (real user), or -1 for environment/unknown.</param>
        /// <param name="amount">Amount of damage to deal</param>
        /// <param name="type">Type of damage</param>
        /// <param name="point">World position where damage occurred</param>
        /// <param name="normal">Surface normal at impact point</param>
        /// <param name="source">Damage source details (attacker name, weapon, etc)</param>
        /// <returns>A properly initialized DamageInfo struct</returns>
        public static DamageInfo CreateDamageInfo(
            int instigatorId,
            int amount,
            DamageType type = DamageType.Contact,
            Vector3 point = default,
            Vector3 normal = default,
            DamageSource source = default)
            {
                return new DamageInfo
                {
                    InstigatorId = instigatorId,
                    Amount = amount,
                    Type = type,
                    Point = point,
                    Normal = normal,
                    Source = source
                };
            }
    }
    
    /// <summary>
    /// Extension methods for NetworkConnection to simplify common operations.
    /// </summary>
    public static class NetworkConnectionExtensions
    {
        /// <summary>
        /// Creates a <see cref="DamageInfo"/> struct with the connection's ClientId as the instigator.
        /// </summary>
        /// <param name="conn">The network connection that initiated the damage</param>
        /// <param name="amount">Amount of damage to deal</param>
        /// <param name="type">Type of damage</param>
        /// <param name="point">World position where damage occurred</param>
        /// <param name="normal">Surface normal at impact point</param>
        /// <param name="source">Damage source details (attacker name, weapon, etc)</param>
        /// <returns>A properly initialized DamageInfo struct</returns>
        public static DamageInfo CreateDamageInfo(
            this NetworkConnection conn,
            int amount,
            DamageType type = DamageType.Contact,
            Vector3 point = default,
            Vector3 normal = default,
            DamageSource source = default)
        {
            return new DamageInfo
            {
                InstigatorId = (int)conn.ClientId,
                Amount = amount,
                Type = type,
                Point = point,
                Normal = normal,
                Source = source
            };
        }
    }

    /// <summary>
    /// Extension methods for NetworkObject to simplify common operations.
    /// </summary>
    public static class NetworkObjectExtensions
    {
        /// <summary>
        /// Creates a <see cref="DamageInfo"/> struct with the NetworkObject owner's ClientId as the instigator.
        /// Useful for collision and physics-based damage systems.
        /// Returns -1 as instigator if object is server-owned (environment).
        /// </summary>
        /// <param name="netObj">The NetworkObject that initiated the damage (attacker)</param>
        /// <param name="amount">Amount of damage to deal</param>
        /// <param name="type">Type of damage</param>
        /// <param name="point">World position where damage occurred</param>
        /// <param name="normal">Surface normal at impact point</param>
        /// <param name="source">Damage source details (attacker name, weapon, etc)</param>
        /// <returns>A properly initialized DamageInfo struct</returns>
        public static DamageInfo CreateDamageInfo(
            this NetworkObject netObj,
            int amount,
            DamageType type = DamageType.Contact,
            Vector3 point = default,
            Vector3 normal = default,
            DamageSource source = default)
        {
            return new DamageInfo
            {
                InstigatorId = netObj.OwnerId,
                Amount = amount,
                Type = type,
                Point = point,
                Normal = normal,
                Source = source
            };
        }
    }
}
