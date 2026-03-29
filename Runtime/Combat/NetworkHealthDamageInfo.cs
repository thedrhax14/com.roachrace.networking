namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Server-side payload describing an applied health loss event on <see cref="NetworkHealthObserver"/>.<br/>
    /// Typical usage: combat/network systems create this when authoritative health decreases so downstream observers can attribute damage before any client RPC/UI bridging is added.<br/>
    /// Configuration/context: values originate from inventory delta attribution and resolved health totals; <see cref="InstigatorConnectionId"/> and <see cref="InstigatorObjectId"/> may be -1 for environment or unknown sources.
    /// </summary>
    public readonly struct NetworkHealthDamageInfo
    {
        /// <summary>
        /// Creates a damage payload for a single authoritative health reduction.<br/>
        /// Typical usage: <see cref="NetworkHealthObserver"/> constructs this immediately after recalculating health from inventory totals.<br/>
        /// Configuration/context: <paramref name="damageAmount"/> should be a positive magnitude representing health lost.
        /// </summary>
        /// <param name="previousHealth">Health total before the damage was applied.</param>
        /// <param name="currentHealth">Health total after the damage was applied.</param>
        /// <param name="damageAmount">Positive magnitude of the applied health loss.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon/effect key used for attribution. Empty when not applicable.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection, or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object, or -1 for environment/unknown.</param>
        public NetworkHealthDamageInfo(int previousHealth, int currentHealth, int damageAmount, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId)
        {
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            DamageAmount = damageAmount;
            WeaponIconKey = weaponIconKey;
            InstigatorConnectionId = instigatorConnectionId;
            InstigatorObjectId = instigatorObjectId;
        }

        /// <summary>
        /// Health total before the authoritative damage was applied.<br/>
        /// Typical usage: compare against <see cref="CurrentHealth"/> to confirm the amount of loss.
        /// </summary>
        public int PreviousHealth { get; }

        /// <summary>
        /// Health total after the authoritative damage was applied.<br/>
        /// Typical usage: consumers can use this to determine whether the hit was fatal or to update server-side state.
        /// </summary>
        public int CurrentHealth { get; }

        /// <summary>
        /// Positive magnitude of the applied health loss.<br/>
        /// Typical usage: use this for hit markers, floating damage numbers, or damage accumulation systems.
        /// </summary>
        public int DamageAmount { get; }

        /// <summary>
        /// Optional UI-facing weapon/effect key used for attribution.<br/>
        /// Typical usage: feed killfeed or hit-indicator icon lookup logic without exposing a live network object.
        /// </summary>
        public string WeaponIconKey { get; }

        /// <summary>
        /// ClientId of the instigator connection responsible for the damage.<br/>
        /// Typical usage: route owner-only feedback to the attacking player when present.
        /// </summary>
        public int InstigatorConnectionId { get; }

        /// <summary>
        /// NetworkObjectId of the instigator object responsible for the damage.<br/>
        /// Typical usage: attribute the hit to a projectile, trap, or actor when extra server-side context is needed.
        /// </summary>
        public int InstigatorObjectId { get; }

        /// <summary>
        /// Whether the damage reduced health to zero or below.<br/>
        /// Typical usage: branch between generic hit feedback and fatal-hit handling.
        /// </summary>
        public bool IsFatal => CurrentHealth <= 0;
    }
}