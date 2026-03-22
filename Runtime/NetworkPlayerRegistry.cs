using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Server-side index which maps FishNet ClientId (<see cref="FishNet.Object.NetworkBehaviour.OwnerId"/>) to the active
    /// <see cref="NetworkPlayer"/> instance for that client.<br/>
    /// <br/>
    /// Typical usage:<br/>
    /// - <see cref="NetworkPlayer"/> registers itself in <see cref="NetworkPlayer.OnStartServer"/> and unregisters in
    /// <see cref="NetworkPlayer.OnStopServer"/>.<br/>
    /// - Server systems (e.g., killfeed/death log builders) use <see cref="TryGetPlayer"/> to resolve a ClientId into
    /// displayable player info (name/avatar/team) without relying on scene hierarchy assumptions.<br/>
    /// <br/>
    /// Notes:<br/>
    /// - This is intentionally server-first: clients do not need this mapping for UI rendering, because death log entries
    /// already contain resolved display strings.
    /// </summary>
    public static class NetworkPlayerRegistry
    {
        private static readonly Dictionary<int, NetworkPlayer> _byClientId = new();

        /// <summary>
        /// Registers a player instance for its current <see cref="FishNet.Object.NetworkBehaviour.OwnerId"/> on the server.<br/>
        /// If a player already exists for the same id, it will be replaced.
        /// </summary>
        /// <param name="player">Active player instance to register.</param>
        public static void Register(NetworkPlayer player)
        {
            if (player == null) return;

            int clientId = player.OwnerId;
            if (clientId < 0) return;

            _byClientId[clientId] = player;
        }

        /// <summary>
        /// Unregisters a player instance from the registry on the server.<br/>
        /// This only removes the mapping if the currently registered value matches the given instance.
        /// </summary>
        /// <param name="player">Player instance being stopped/destroyed.</param>
        public static void Unregister(NetworkPlayer player)
        {
            if (player == null) return;

            int clientId = player.OwnerId;
            if (clientId < 0) return;

            if (_byClientId.TryGetValue(clientId, out NetworkPlayer existing) && ReferenceEquals(existing, player))
                _byClientId.Remove(clientId);
        }

        /// <summary>
        /// Attempts to resolve a FishNet ClientId into the active <see cref="NetworkPlayer"/> instance on the server.
        /// </summary>
        /// <param name="clientId">FishNet ClientId (connection id).</param>
        /// <param name="player">Resolved player instance when found.</param>
        /// <returns><c>true</c> when an active player is registered for the id.</returns>
        public static bool TryGetPlayer(int clientId, out NetworkPlayer player)
        {
            if (clientId < 0)
            {
                player = null;
                return false;
            }

            if (_byClientId.TryGetValue(clientId, out player) && player != null)
                return true;

            player = null;
            return false;
        }

        /// <summary>
        /// Clears all mappings.<br/>
        /// Typical usage: development/debugging or play mode domain reload edge-cases.
        /// </summary>
        public static void Clear()
        {
            _byClientId.Clear();
        }
    }
}
