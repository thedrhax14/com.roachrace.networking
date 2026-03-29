namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observer interface for owner-local health damage notifications from <see cref="NetworkDamageFeedback"/>.<br/>
    /// Typical usage: owner-side presentation bridges subscribe so they can route authoritative damage notifications into UI models without polling network state.<br/>
    /// Server/client constraints: callbacks are invoked only on the owning client after the server sends a TargetRpc.
    /// </summary>
    public interface IIncomingDamageObserver
    {
        /// <summary>
        /// Called on the owning client after the server confirms incoming damage for this player/controller.<br/>
        /// Typical usage: update hit indicators, floating damage widgets, or recent-damage models using <paramref name="damageInfo"/>.<br/>
        /// Server/client constraints: owning-client only; do not assume this runs on server or remote observers.
        /// </summary>
        /// <param name="feedbackController">The local feedback controller that received the owner-only damage RPC.</param>
        /// <param name="damageInfo">Resolved authoritative damage information forwarded from the server.</param>
        void OnIncomingDamage(NetworkDamageFeedback feedbackController, NetworkHealthDamageInfo damageInfo);
    }
}