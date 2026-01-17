using RoachRace.Controls;

namespace RoachRace.Networking.Resources
{
    /// <summary>
    /// Server-authoritative stamina resource.
    /// 
    /// Notes:
    /// - This component is intentionally minimal: it provides Current/Max replication and server-side consume/add.
    /// - Regeneration/decay should be implemented by gameplay systems which call Add/TryConsume.
    /// </summary>
    public sealed class NetworkStamina : NetworkFloatResource { }
}
