namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observer interface for server-only health damage notifications from <see cref="NetworkHealthObserver"/>.<br/>
    /// Typical usage: server-side combat or presentation bridge systems subscribe to authoritative health loss before relaying it to owner-only UI or analytics systems.<br/>
    /// Server/client constraints: callbacks are invoked only on the server after health has decreased.
    /// </summary>
    public interface INetworkHealthServerDamageObserver
    {
        /// <summary>
        /// Called on the server after authoritative health decreases on <paramref name="healthObserver"/>.<br/>
        /// Typical usage: inspect <paramref name="damageInfo"/> to route victim/attacker feedback or accumulate combat statistics.<br/>
        /// Server/client constraints: server-only; should not perform client-only work directly.
        /// </summary>
        /// <param name="healthObserver">The health observer whose health decreased.</param>
        /// <param name="damageInfo">Resolved server-side damage attribution and health totals for this hit.</param>
        void OnNetworkHealthServerDamaged(NetworkHealthObserver healthObserver, NetworkHealthDamageInfo damageInfo);
    }
}