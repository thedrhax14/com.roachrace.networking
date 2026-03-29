namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observer interface for owner-local dealt-damage notifications from <see cref="NetworkDamageFeedback"/>.<br/>
    /// Typical usage: owner-side presentation bridges subscribe so they can route authoritative dealt-damage notifications into UI models without polling combat state.<br/>
    /// Server/client constraints: callbacks are invoked only on the owning client after the server sends an attacker-side TargetRpc.
    /// </summary>
    public interface IDealtDamageObserver
    {
        /// <summary>
        /// Called on the owning client after the server confirms damage dealt by this player/source.<br/>
        /// Typical usage: update hit markers, damage numbers, or recent-damage models using <paramref name="damageInfo"/>.<br/>
        /// Server/client constraints: owning-client only; do not assume this runs on server or remote observers.
        /// </summary>
        /// <param name="feedbackController">The local feedback controller that received the authoritative dealt-damage notification.</param>
        /// <param name="damageInfo">Resolved authoritative dealt-damage information forwarded from the server.</param>
        void OnDealtDamage(NetworkDamageFeedback feedbackController, DealtDamageInfo damageInfo);
    }
}