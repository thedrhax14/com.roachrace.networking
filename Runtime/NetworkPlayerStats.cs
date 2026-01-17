using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Per-player NetworkBehaviour that tracks game statistics
    /// Client-owned, syncs stats to all clients
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public class NetworkPlayerStats : NetworkBehaviour
    {
        private readonly SyncVar<int> respawnsLeft = new(3);
        private readonly SyncVar<int> deaths = new(0);
        private readonly SyncVar<bool> reachedEnd = new(false);
        private readonly SyncVar<float> survivalTime = new(0f);

        private NetworkPlayer networkPlayer;
        private NetworkGameManager gameManager;
        private float spawnTime;
        private PlayerStats _cachedStats;

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            // Find game manager
            gameManager = FindFirstObjectByType<NetworkGameManager>();
            if (gameManager != null)
            {
                respawnsLeft.Value = gameManager.MaxRespawns;
                gameManager.RegisterPlayerStats(this);
            }
            
            networkPlayer = GetComponent<NetworkPlayer>();
            spawnTime = Time.time;
            
            Debug.Log($"[NetworkPlayerStats] Server initialized for player {OwnerId}");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            networkPlayer = GetComponent<NetworkPlayer>();
            
            Debug.Log($"[NetworkPlayerStats] Client initialized - Respawns: {respawnsLeft.Value}");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            
            if (gameManager != null)
            {
                gameManager.UnregisterPlayerStats(OwnerId);
            }
        }

        /// <summary>
        /// Reset stats for a new game
        /// </summary>
        [Server]
        public void ResetStats()
        {
            respawnsLeft.Value = gameManager != null ? gameManager.MaxRespawns : 3;
            deaths.Value = 0;
            reachedEnd.Value = false;
            survivalTime.Value = 0f;
            spawnTime = Time.time;
            
            Debug.Log($"[NetworkPlayerStats] Stats reset for player {OwnerId}");
        }

        /// <summary>
        /// Called when player dies - decrements respawns and checks game over
        /// </summary>
        [Server]
        public void OnPlayerDeath()
        {
            deaths.Value++;
            
            // Only survivors have limited respawns
            if (Team == Team.Survivor)
            {
                respawnsLeft.Value--;
                Debug.Log($"[NetworkPlayerStats] Player {OwnerId} died - Respawns left: {respawnsLeft.Value}");
                
                if (gameManager != null)
                {
                    gameManager.CheckGameOver();
                }
            }
        }

        /// <summary>
        /// Called when survivor reaches the end zone
        /// </summary>
        [Server]
        public void OnReachedEnd()
        {
            if (Team != Team.Survivor)
                return;

            reachedEnd.Value = true;
            survivalTime.Value = Time.time - spawnTime;
            
            Debug.Log($"[NetworkPlayerStats] Player {OwnerId} reached end - Time: {survivalTime.Value:F1}s");
            
            if (gameManager != null)
            {
                gameManager.CheckGameOver();
            }
        }

        /// <summary>
        /// Request respawn - only works if respawns available
        /// </summary>
        [ServerRpc]
        public void RequestRespawnServerRpc()
        {
            if (Team == Team.Survivor && respawnsLeft.Value <= 0)
            {
                Debug.LogWarning($"[NetworkPlayerStats] Player {OwnerId} has no respawns left");
                return;
            }

            // Respawn logic would go here (handled by your respawn system)
            spawnTime = Time.time;
            Debug.Log($"[NetworkPlayerStats] Player {OwnerId} respawned");
        }

        /// <summary>
        /// Check if player can respawn (has respawns left or is ghost)
        /// </summary>
        [Server]
        public bool CanRespawn()
        {
            // Ghosts can always respawn
            if (Team == Team.Ghost)
                return true;

            // Survivors need respawns left
            return respawnsLeft.Value > 0;
        }

        /// <summary>
        /// Convert survivor to ghost team when out of respawns
        /// </summary>
        [Server]
        public void ConvertToGhost()
        {
            if (networkPlayer == null || Team == Team.Ghost)
                return;

            Debug.Log($"[NetworkPlayerStats] Converting player {OwnerId} to Ghost team");
            networkPlayer.SetTeam(Team.Ghost);
            
            // Check if this was the last survivor - may trigger game over
            if (gameManager != null)
            {
                gameManager.CheckGameOver();
            }
        }

        /// <summary>
        /// Get current player stats snapshot
        /// </summary>
        public PlayerStats GetPlayerStats()
        {
            if (_cachedStats == null)
            {
                _cachedStats = new PlayerStats
                {
                    networkId = OwnerId
                };
            }

            // Update dynamic fields
            _cachedStats.playerName = networkPlayer != null ? networkPlayer.PlayerName : "Unknown";
            _cachedStats.team = networkPlayer != null ? networkPlayer.Team : Team.Survivor;
            _cachedStats.respawnsLeft = respawnsLeft.Value;
            _cachedStats.deaths = deaths.Value;
            _cachedStats.reachedEnd = reachedEnd.Value;
            _cachedStats.survivalTime = survivalTime.Value;
            
            return _cachedStats;
        }

        // Public properties for external access
        public int RespawnsLeft => respawnsLeft.Value;
        public int Deaths => deaths.Value;
        public bool ReachedEnd => reachedEnd.Value;
        public float SurvivalTime => survivalTime.Value;
        public Team Team => networkPlayer != null ? networkPlayer.Team : Team.Survivor;
    }
}
