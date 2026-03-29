using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Owner-local payload describing damage successfully dealt by the local player/source.<br/>
    /// Typical usage: attacker-side networking bridges and UI consumers use this to show hit markers or damage numbers without depending on live server objects.<br/>
    /// Configuration/context: target identifiers may be -1 when the victim has no player owner or when only the network object is known.
    /// </summary>
    public readonly struct DealtDamageInfo
    {
        /// <summary>
        /// Creates a dealt-damage payload for one authoritative hit.<br/>
        /// Typical usage: <see cref="NetworkDamageFeedback"/> constructs this after the server confirms damage and routes it to the attacker owner.<br/>
        /// Configuration/context: <paramref name="damageAmount"/> should be a positive magnitude.
        /// </summary>
        /// <param name="damageAmount">Positive magnitude of the applied damage.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon/effect key used for attribution. Empty when not applicable.</param>
        /// <param name="targetConnectionId">ClientId of the damaged target owner, or -1 when unknown/non-player.</param>
        /// <param name="targetObjectId">NetworkObjectId of the damaged target, or -1 when unknown.</param>
        /// <param name="targetHealthAfterHit">Resolved target health after the hit was applied.</param>
        /// <param name="targetWorldPosition">World-space position of the damaged target when the hit was applied.</param>
        /// <param name="isFatal">Whether the hit reduced the target to zero or below.</param>
        public DealtDamageInfo(int damageAmount, string weaponIconKey, int targetConnectionId, int targetObjectId, int targetHealthAfterHit, Vector3 targetWorldPosition, bool isFatal)
        {
            DamageAmount = damageAmount;
            WeaponIconKey = weaponIconKey;
            TargetConnectionId = targetConnectionId;
            TargetObjectId = targetObjectId;
            TargetHealthAfterHit = targetHealthAfterHit;
            TargetWorldPosition = targetWorldPosition;
            IsFatal = isFatal;
        }

        /// <summary>
        /// Positive magnitude of the applied damage.<br/>
        /// Typical usage: drive attacker hit markers or damage dealt counters.
        /// </summary>
        public int DamageAmount { get; }

        /// <summary>
        /// Optional UI-facing weapon/effect attribution key.<br/>
        /// Typical usage: map to an icon or label in attacker-side UI.
        /// </summary>
        public string WeaponIconKey { get; }

        /// <summary>
        /// ClientId of the damaged target owner.<br/>
        /// Typical usage: correlate a hit with player/team registries when attacker UI needs target identity.
        /// </summary>
        public int TargetConnectionId { get; }

        /// <summary>
        /// NetworkObjectId of the damaged target.<br/>
        /// Typical usage: correlate the hit with a tracked victim object if such a registry exists client-side.
        /// </summary>
        public int TargetObjectId { get; }

        /// <summary>
        /// Resolved target health after the hit was applied.<br/>
        /// Typical usage: distinguish shield-break/low-health/fatal thresholds in attacker-side UI.
        /// </summary>
        public int TargetHealthAfterHit { get; }

        /// <summary>
        /// World-space position of the damaged target when the hit was applied.<br/>
        /// Typical usage: attacker-side UI can project floating text above the target and drift it upward over time.
        /// </summary>
        public Vector3 TargetWorldPosition { get; }

        /// <summary>
        /// Whether the hit reduced the target to zero or below.<br/>
        /// Typical usage: trigger stronger confirmation effects for kills/final blows.
        /// </summary>
        public bool IsFatal { get; }
    }
}