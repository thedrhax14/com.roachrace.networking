using RoachRace.Data;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Observer interface for server-only death notifications from <see cref="NetworkHealth"/>.<br>
    /// Typical usage: server-side systems (eg controller lifecycle/respawn) implement this interface and subscribe via <see cref="NetworkHealth.AddServerDeathObserver"/> after spawning a controller.<br>
    /// Server/client constraints: callbacks are invoked only on the server when death handling begins.
    /// </summary>
    public interface INetworkHealthServerDeathObserver
    {
        /// <summary>
        /// Called on the server when <paramref name="health"/> reaches 0 and death handling begins.<br>
        /// Typical usage: decrement respawn counters, convert team, and schedule a respawn/spawn of a new controller.<br>
        /// Server/client constraints: server-only; should not perform client-only work.
        /// </summary>
        /// <param name="health">The health component which began death handling.</param>
        /// <param name="damageInfo">Fatal damage context.</param>
        void OnNetworkHealthServerDied(NetworkHealth health, DamageInfo damageInfo);
    }
}
