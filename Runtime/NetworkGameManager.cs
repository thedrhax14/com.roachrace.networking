using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Data;
using RoachRace.UI.Models;
using RoachRace.UI.Components;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using RoachRace.UI.Core;

namespace RoachRace.Networking
{
    /// <summary>
    /// NetworkBehaviour that manages game state and win/loss conditions
    /// Server-owned, syncs state to all clients
    /// </summary>
    public class NetworkGameManager : NetworkBehaviour, IGameManager
    {
        [Header("Dependencies")]
        [SerializeField] private GameStateModel gameStateModel;

        [Header("Game Settings")]
        [SerializeField] private int maxRespawns = 3;
        [SerializeField] private float gameStartDelaySeconds = 3f;

        [Header("Editor Dev")]
        [Tooltip("If true, DevStartGameImmediate() will skip countdown and start instantly in the Editor.")]
        [SerializeField] private bool allowDevImmediateStart = true;

        private readonly SyncVar<GameState> currentState = new(GameState.Lobby);
        private readonly SyncList<PlayerStats> allPlayerStats = new();
        private readonly SyncVar<long> gameStartTimestampMs = new(0);
        
        private float gameStartTime;
        private Dictionary<int, NetworkPlayerStats> playerStatsMap = new();

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            if (gameStateModel == null)
            {
                Debug.LogError("[NetworkGameManager] GameStateModel is not assigned! Please assign it in the Inspector.", gameObject);
                throw new System.NullReferenceException($"[NetworkGameManager] GameStateModel is null on GameObject '{gameObject.name}'. This component requires a GameStateModel to function.");
            }
            
            // Initialize game state
            currentState.Value = GameState.Lobby;
            gameStartTime = 0f;
            playerStatsMap = new ();
            
            gameStateModel.SetMaxRespawns(maxRespawns);
            
            Debug.Log($"[{nameof(NetworkGameManager)}] Server initialized");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (gameStateModel == null)
            {
                Debug.LogError("[NetworkGameManager] GameStateModel is not assigned! Please assign it in the Inspector.", gameObject);
                throw new System.NullReferenceException($"[NetworkGameManager] GameStateModel is null on GameObject '{gameObject.name}'. This component requires a GameStateModel to function.");
            }
            
            // Unsubscribe first to prevent duplicate subscriptions
            currentState.OnChange -= OnGameStateChanged;
            allPlayerStats.OnChange -= OnPlayerStatsChanged;
            gameStartTimestampMs.OnChange -= OnGameStartTimestampChanged;

            // Subscribe to state changes
            currentState.OnChange += OnGameStateChanged;
            allPlayerStats.OnChange += OnPlayerStatsChanged;
            gameStartTimestampMs.OnChange += OnGameStartTimestampChanged;
            
            // Update model with current state
            gameStateModel.SetGameState(currentState.Value);
            gameStateModel.SetMaxRespawns(maxRespawns);
            UpdateCountdownFromTimestamp(gameStartTimestampMs.Value);
            
            // Register with GameOverWindow
            RegisterWithGameOverWindow();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            
            // Unsubscribe from state changes
            currentState.OnChange -= OnGameStateChanged;
            allPlayerStats.OnChange -= OnPlayerStatsChanged;
            gameStartTimestampMs.OnChange -= OnGameStartTimestampChanged;
            
            Debug.Log("[NetworkGameManager] Client stopped - Unsubscribed from events");
        }

        private void Update()
        {
            // Client updates countdown display based on timestamp
            if (!IsServerStarted && gameStartTimestampMs.Value > 0)
            {
                UpdateCountdownFromTimestamp(gameStartTimestampMs.Value);
            }
        }

        /// <summary>
        /// Register a player's stats component
        /// Called by NetworkPlayerStats when spawned on server
        /// </summary>
        [Server]
        public void RegisterPlayerStats(NetworkPlayerStats playerStats)
        {
            if (!playerStatsMap.ContainsKey(playerStats.OwnerId)) 
                playerStatsMap[playerStats.OwnerId] = playerStats;
        }

        /// <summary>
        /// Unregister a player's stats component
        /// </summary>
        [Server]
        public void UnregisterPlayerStats(int networkId)
        {
            if (playerStatsMap.ContainsKey(networkId))
                playerStatsMap.Remove(networkId);
        }

        /// <summary>
        /// Start the game countdown - called by room owner
        /// </summary>
        [Server]
        public void StartGame()
        {
            if (currentState.Value != GameState.Lobby)
            {
                Debug.LogWarning($"[{nameof(NetworkGameManager)}] Cannot start game - not in lobby");
                return;
            }

            // Set state to Starting
            currentState.Value = GameState.Starting;

            // Set game start timestamp (UTC)
            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            gameStartTimestampMs.Value = nowMs + (long)(gameStartDelaySeconds * 1000);
            
            Debug.Log($"[{nameof(NetworkGameManager)}] <color=green>Game countdown started</color> - will start at {gameStartTimestampMs.Value} (in {gameStartDelaySeconds}s)");
            
            // Start coroutine to trigger game start on server at exact time
            StartCoroutine(WaitForGameStartCoroutine());
        }

        /// <summary>
        /// Editor-only: start the game immediately without countdown.
        /// Intended for rapid iteration while play-testing.
        /// </summary>
        [Server]
        public void DevStartGameImmediate()
        {
            // Keep this method compiled for IDE tooling; gate behavior at runtime.
            if (!Application.isEditor) return;
            if (!allowDevImmediateStart) return;
            if (currentState.Value != GameState.Lobby)
                return;

            // Mirror OnGameStarted() behavior without waiting.
            currentState.Value = GameState.InProgress;
            gameStartTime = Time.time;
            gameStartTimestampMs.Value = 0;

            allPlayerStats.Clear();
            foreach (var kvp in playerStatsMap)
            {
                kvp.Value.ResetStats();
                allPlayerStats.Add(kvp.Value.GetPlayerStats());
            }

            Debug.Log($"[{nameof(NetworkGameManager)}] <color=cyan>DEV</color>: Immediate start with {allPlayerStats.Count} players");
        }

        /// <summary>
        /// Server waits for countdown to finish then starts game
        /// </summary>
        [Server]
        private System.Collections.IEnumerator WaitForGameStartCoroutine()
        {
            while (System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < gameStartTimestampMs.Value)
            {
                yield return null;
            }
            
            // Countdown finished, start the game
            OnGameStarted();
        }

        /// <summary>
        /// Called when game actually starts after countdown
        /// Server performs map generation and initialization here
        /// </summary>
        [Server]
        private void OnGameStarted()
        {
            currentState.Value = GameState.InProgress;
            gameStartTime = Time.time;
            gameStartTimestampMs.Value = 0; // Clear timestamp
            
            // Initialize all player stats
            allPlayerStats.Clear();
            foreach (var kvp in playerStatsMap)
            {
                // Reset stats for new game
                kvp.Value.ResetStats();
                allPlayerStats.Add(kvp.Value.GetPlayerStats());
            }
            
            // Server-specific initialization (map generation, etc.)
            Debug.Log($"[{nameof(NetworkGameManager)}] SERVER: Game started with {allPlayerStats.Count} players - Starting map generation...");
            // TODO: Trigger map generation process here
            
            if(!Application.isEditor) gameStateModel.SetGameState(currentState.Value);
            Debug.Log($"[{nameof(NetworkGameManager)}] Game started with {allPlayerStats.Count} players");
        }

        /// <summary>
        /// Check win/loss condition when a survivor dies or reaches the end
        /// </summary>
        [Server]
        public void CheckGameOver()
        {
            if (currentState.Value != GameState.InProgress)
                return;

            // Get all survivors
            var survivors = playerStatsMap.Values
                .Where(p => p.Team == Team.Survivor)
                .ToList();

            if (survivors.Count == 0)
            {
                // No survivors in game - draw or ghosts win
                EndGame(false);
                return;
            }

            // Check if any survivors can still play
            bool anySurvivorCanPlay = survivors.Any(s => s.RespawnsLeft > 0 || s.ReachedEnd);
            
            if (!anySurvivorCanPlay)
            {
                // All survivors out of respawns and none reached end - ghosts win
                EndGame(false);
                return;
            }

            // Check if any survivor reached the end
            bool anySurvivorReachedEnd = survivors.Any(s => s.ReachedEnd);
            if (anySurvivorReachedEnd)
            {
                // At least one survivor reached end - survivors win
                EndGame(true);
            }
        }

        /// <summary>
        /// End the game and show results
        /// </summary>
        [Server]
        private void EndGame(bool survivorsWon)
        {
            currentState.Value = GameState.GameOver;
            
            // Collect final stats
            allPlayerStats.Clear();
            foreach (var kvp in playerStatsMap)
            {
                allPlayerStats.Add(kvp.Value.GetPlayerStats());
            }
            
            float totalGameTime = Time.time - gameStartTime;
            
            // Create result and sync to clients via ObserversRpc
            PlayerStats[] statsArray = allPlayerStats.ToArray();
            GameResult result = new GameResult(statsArray, survivorsWon, totalGameTime);
            
            SyncGameResultRpc(result);
            
            Debug.Log($"[NetworkGameManager] Game ended - Survivors won: {survivorsWon}, Time: {totalGameTime:F1}s");

            // Auto-shutdown server after 10 seconds
            StartCoroutine(AutoShutdownCoroutine());
        }

        private System.Collections.IEnumerator AutoShutdownCoroutine()
        {
            Debug.Log("[NetworkGameManager] Server will shut down in 10 seconds...");
            yield return new WaitForSeconds(10f);
            
            Debug.Log("[NetworkGameManager] Shutting down server...");
            ServerManager.StopConnection(true);
        }

        /// <summary>
        /// Sync game result to all clients
        /// </summary>
        [ObserversRpc]
        private void SyncGameResultRpc(GameResult result)
        {
            if (gameStateModel != null)
            {
                gameStateModel.SetGameResult(result);
            }
            Debug.Log($"[NetworkGameManager] Game result received - Survivors won: {result.survivorsWon}");
        }

        #region IGameManager Implementation

        /// <summary>
        /// Start the game - implements IGameManager
        /// </summary>
        void IGameManager.StartGame()
        {
            if (IsServerStarted)
            {
                StartGame();
            }
        }

        /// <summary>
        /// Leave game - implements IGameManager
        /// </summary>
        void IGameManager.LeaveGame()
        {
            if (IsServerStarted)
            {
                // If we are the server (Host), stop the server completely
                ServerManager.StopConnection(true);
                Debug.Log("[NetworkGameManager] Host left game (stopped server)");
            }
            else if (IsClientStarted)
            {
                // If we are just a client, disconnect
                ClientManager.StopConnection();
                Debug.Log("[NetworkGameManager] Client left game (stopped connection)");
            }
        }

        #endregion

        #region GameOverWindow Integration

        private void RegisterWithGameOverWindow()
        {
            GameOverWindow gameOverWindow = FindFirstObjectByType<GameOverWindow>();
            if (gameOverWindow != null) gameOverWindow.SetGameManager(this);
        }

        #endregion

        /// <summary>
        /// Called when game start timestamp changes on client
        /// </summary>
        private void OnGameStartTimestampChanged(long prev, long next, bool asServer)
        {
            UpdateCountdownFromTimestamp(next);
        }

        /// <summary>
        /// Update countdown display based on UTC timestamp
        /// </summary>
        private void UpdateCountdownFromTimestamp(long timestampMs)
        {
            if (timestampMs <= 0)
            {
                if (gameStateModel != null)
                {
                    gameStateModel.SetCountdown(0f);
                }
                return;
            }

            long nowMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float remainingSeconds = (timestampMs - nowMs) / 1000f;
            
            if (gameStateModel != null)
            {
                gameStateModel.SetCountdown(Mathf.Max(0f, remainingSeconds));
            }
        }

        /// <summary>
        /// Called when game state changes on client
        /// </summary>
        private void OnGameStateChanged(GameState prev, GameState next, bool asServer)
        {
            if (prev == next) return;
            if (gameStateModel != null)
            {
                gameStateModel.SetGameState(next);
            }
            Debug.Log($"[NetworkGameManager] State changed: {prev} -> {next}");
        }

        /// <summary>
        /// Called when player stats list changes on client
        /// </summary>
        private void OnPlayerStatsChanged(SyncListOperation op, int index, PlayerStats oldValue, PlayerStats newValue, bool asServer)
        {
            // Stats updated, result will be synced via RPC when game ends
        }

        /// <summary>
        /// Get maximum respawns setting
        /// </summary>
        public int MaxRespawns => maxRespawns;
    }
}
