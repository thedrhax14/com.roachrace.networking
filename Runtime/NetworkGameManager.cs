using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using RoachRace.Data;
using RoachRace.UI.Models;
using RoachRace.UI.Components;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        [Tooltip("Optional: host-selected match settings used to decide whether to run the intro sequence.")]
        [SerializeField] private GameSettingsModel gameSettingsModel;

        [Tooltip("Optional: Spawn sequence model used by intro Timeline/pod sequence.")]
        [SerializeField] private SpawnSequenceModel spawnSequenceModel;

        [Tooltip("Optional: server-side pod spawner used when intro is enabled.")]
        [SerializeField] private NetworkMatchStartPodSpawner matchStartPodSpawner;

        [Header("Scenes")]
        [Tooltip("Additive gameplay scene to load/unload using FishNet SceneManager (eg contains RoachRaceNetMapGen).")]
        [SerializeField] private string mapGenSceneName = "MapGen";

        [Header("Game Settings")]
        [SerializeField] private int maxRespawns = 3;
        [SerializeField] private float gameStartDelaySeconds = 3f;

        [Header("Editor Dev")]
        [Tooltip("If true, DevStartGameImmediate() will skip countdown and start instantly in the Editor.")]
        [SerializeField] private bool allowDevImmediateStart = true;

        private readonly SyncVar<GameState> currentState = new(GameState.Lobby);
        private readonly SyncList<PlayerStats> allPlayerStats = new();
        private readonly SyncVar<long> gameStartTimestampMs = new(0);

        private readonly SyncVar<bool> introEnabled = new(true);
        private readonly SyncVar<float> introDurationSeconds = new(10f);

        private readonly SyncVar<int> spawnSequencePhase = new((int)SpawnSequenceModel.SpawnSequencePhase.None);
        private readonly SyncVar<long> introStartTimestampMsUtc = new(0);
        
        private float gameStartTime;
        private Dictionary<int, NetworkPlayerStats> playerStatsMap = new();
        private bool _mapGenSceneRequested;

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

            if (gameSettingsModel == null)
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkGameManager)}] Optional reference missing on '{gameObject.name}': gameSettingsModel (intro will default to disabled)",
                    gameObject);
            }

            if (spawnSequenceModel == null)
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkGameManager)}] Optional reference missing on '{gameObject.name}': spawnSequenceModel (clients may not see intro state)",
                    gameObject);
            }

            if (matchStartPodSpawner == null)
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkGameManager)}] Optional reference missing on '{gameObject.name}': matchStartPodSpawner (intro will fall back to immediate controller spawn)",
                    gameObject);
            }
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
            introEnabled.OnChange -= OnIntroEnabledChanged;
            introDurationSeconds.OnChange -= OnIntroDurationSecondsChanged;
            spawnSequencePhase.OnChange -= OnSpawnSequencePhaseChanged;
            introStartTimestampMsUtc.OnChange -= OnIntroStartTimestampChanged;

            // Subscribe to state changes
            currentState.OnChange += OnGameStateChanged;
            allPlayerStats.OnChange += OnPlayerStatsChanged;
            gameStartTimestampMs.OnChange += OnGameStartTimestampChanged;
            introEnabled.OnChange += OnIntroEnabledChanged;
            introDurationSeconds.OnChange += OnIntroDurationSecondsChanged;
            spawnSequencePhase.OnChange += OnSpawnSequencePhaseChanged;
            introStartTimestampMsUtc.OnChange += OnIntroStartTimestampChanged;
            
            // Update model with current state
            gameStateModel.SetGameState(currentState.Value);
            gameStateModel.SetMaxRespawns(maxRespawns);
            UpdateCountdownFromTimestamp(gameStartTimestampMs.Value);

            if (spawnSequenceModel != null)
            {
                spawnSequenceModel.ApplyIntroConfig(introEnabled.Value, introDurationSeconds.Value);
                spawnSequenceModel.SetIntroStartTimestampMsUtc(introStartTimestampMsUtc.Value);
                spawnSequenceModel.SetPhase((SpawnSequenceModel.SpawnSequencePhase)spawnSequencePhase.Value);
            }
            
            // Register with GameOverWindow
            RegisterWithGameOverWindow();

            // Ensure MapGen is unloaded locally when disconnecting, since MainMenu is UI-only.
            ClientManager.OnClientConnectionState -= OnClientConnectionState;
            ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            
            // Unsubscribe from state changes
            currentState.OnChange -= OnGameStateChanged;
            allPlayerStats.OnChange -= OnPlayerStatsChanged;
            gameStartTimestampMs.OnChange -= OnGameStartTimestampChanged;
            introEnabled.OnChange -= OnIntroEnabledChanged;
            introDurationSeconds.OnChange -= OnIntroDurationSecondsChanged;
            spawnSequencePhase.OnChange -= OnSpawnSequencePhaseChanged;
            introStartTimestampMsUtc.OnChange -= OnIntroStartTimestampChanged;

            ClientManager.OnClientConnectionState -= OnClientConnectionState;
            
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

            ServerApplyMatchIntroSettingsFromModel();

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

            ServerApplyMatchIntroSettingsFromModel();

            allPlayerStats.Clear();
            foreach (var kvp in playerStatsMap)
            {
                kvp.Value.ResetStats();
                allPlayerStats.Add(kvp.Value.GetPlayerStats());
            }

            ServerLoadMapGenSceneIfNeeded();

            ServerHandleGameStartSpawnsOrIntroSequence();

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

            ServerLoadMapGenSceneIfNeeded();

            ServerHandleGameStartSpawnsOrIntroSequence();
            
            // Server-specific initialization (map generation, etc.)
            Debug.Log($"[{nameof(NetworkGameManager)}] SERVER: Game started with {allPlayerStats.Count} players - Starting map generation...");
            // TODO: Trigger map generation process here
            
            if(!Application.isEditor) gameStateModel.SetGameState(currentState.Value);
            Debug.Log($"[{nameof(NetworkGameManager)}] Game started with {allPlayerStats.Count} players");
        }

        [Server]
        private void ServerHandleGameStartSpawns()
        {
            var roomManager = FindFirstObjectByType<NetworkRoomManager>();
            if (roomManager != null)
                roomManager.NetworkObject.RemoveOwnership();

            var spawner = FindFirstObjectByType<RoachRacePlayerSpawner>();
            if (spawner != null)
                spawner.ResetSpawnIndices();

            var lifecycles = FindObjectsByType<NetworkPlayerControllerLifecycle>(FindObjectsSortMode.None);
            foreach (var lifecycle in lifecycles)
                lifecycle.ServerSpawnControllerForCurrentTeam();
        }

        [Server]
        private void ServerHandleGameStartSpawnsOrIntroSequence()
        {
            // If intro is disabled, immediately spawn controllers.
            if (!introEnabled.Value)
            {
                ServerHandleGameStartSpawns();

                if (spawnSequenceModel != null)
                {
                    spawnSequenceModel.ApplyIntroConfig(false, 0f);
                    spawnSequenceModel.SetIntroStartTimestampMsUtc(0);
                    spawnSequenceModel.SetPhase(SpawnSequenceModel.SpawnSequencePhase.ControllersSpawned);
                }

                spawnSequencePhase.Value = (int)SpawnSequenceModel.SpawnSequencePhase.ControllersSpawned;
                introStartTimestampMsUtc.Value = 0;

                Debug.Log($"[{nameof(NetworkGameManager)}] Intro disabled; spawning controllers immediately on '{gameObject.name}'", gameObject);
                return;
            }

            // Prepare spawn system (ownership/spawn indices) but do not spawn controllers yet.
            var roomManager = FindFirstObjectByType<NetworkRoomManager>();
            if (roomManager != null)
                roomManager.NetworkObject.RemoveOwnership();

            var spawner = FindFirstObjectByType<RoachRacePlayerSpawner>();
            if (spawner != null)
                spawner.ResetSpawnIndices();

            if (spawnSequenceModel != null)
            {
                spawnSequenceModel.ApplyIntroConfig(true, introDurationSeconds.Value);
                spawnSequenceModel.SetIntroStartTimestampMsUtc(0);
                spawnSequenceModel.SetPhase(SpawnSequenceModel.SpawnSequencePhase.WaitingForFirstChunk);
            }

            spawnSequencePhase.Value = (int)SpawnSequenceModel.SpawnSequencePhase.WaitingForFirstChunk;
            introStartTimestampMsUtc.Value = 0;

            if (matchStartPodSpawner == null)
                matchStartPodSpawner = FindFirstObjectByType<NetworkMatchStartPodSpawner>();

            if (matchStartPodSpawner == null)
            {
                Debug.LogError($"[{nameof(NetworkGameManager)}] Intro enabled but no {nameof(NetworkMatchStartPodSpawner)} found on '{gameObject.name}'. Falling back to immediate controller spawn.", gameObject);
                ServerHandleGameStartSpawns();
                return;
            }

            StartCoroutine(ServerWaitForFirstChunkThenSpawnPodsCoroutine());
        }

        /// <summary>
        /// Server-only setter for the network-synced spawn sequence phase.<br></br>
        /// Purpose: keep <see cref="SpawnSequenceModel"/> in sync on all clients without relying on local-only events.
        /// </summary>
        /// <param name="phase">New phase.</param>
        [Server]
        public void ServerSetSpawnSequencePhase(SpawnSequenceModel.SpawnSequencePhase phase)
        {
            spawnSequencePhase.Value = (int)phase;
            if (spawnSequenceModel != null)
                spawnSequenceModel.SetPhase(phase);

            Debug.Log($"[{nameof(NetworkGameManager)}] Spawn sequence phase -> {phase} on '{gameObject.name}'", gameObject);
        }

        [Server]
        private void ServerApplyMatchIntroSettingsFromModel()
        {
            bool enabled = true;
            float durationSeconds = 10f;

            if (gameSettingsModel != null)
            {
                enabled = gameSettingsModel.IntroEnabled.Value;
                durationSeconds = gameSettingsModel.IntroDurationSeconds.Value;
            }
            else
            {
                // Preserve existing behavior when settings are not wired yet.
                enabled = false;
                durationSeconds = 0f;
                Debug.LogWarning($"[{nameof(NetworkGameManager)}] Missing {nameof(GameSettingsModel)} on '{gameObject.name}'. Defaulting to intro disabled.", gameObject);
            }

            introEnabled.Value = enabled;
            introDurationSeconds.Value = Mathf.Max(0f, durationSeconds);

            Debug.Log(
                $"[{nameof(NetworkGameManager)}] Match intro settings on '{gameObject.name}': enabled={introEnabled.Value}, durationSeconds={introDurationSeconds.Value:0.###}",
                gameObject);

            if (spawnSequenceModel != null)
                spawnSequenceModel.ApplyIntroConfig(introEnabled.Value, introDurationSeconds.Value);
        }

        [Server]
        private System.Collections.IEnumerator ServerWaitForFirstChunkThenSpawnPodsCoroutine()
        {
            // Wait until the MapGen "first chunk" hook transitions the model into Cinematic.
            const float timeoutSeconds = 30f;
            float startTime = Time.time;

            if (spawnSequenceModel == null)
            {
                Debug.LogError($"[{nameof(NetworkGameManager)}] Intro enabled but missing {nameof(SpawnSequenceModel)} on '{gameObject.name}'. Spawning pods immediately.", gameObject);
                matchStartPodSpawner.ServerSpawnPodsAfterDelay(introDurationSeconds.Value);
                yield break;
            }

            while (spawnSequenceModel.Phase.Value != SpawnSequenceModel.SpawnSequencePhase.Cinematic)
            {
                if (Time.time - startTime > timeoutSeconds)
                {
                    Debug.LogError($"[{nameof(NetworkGameManager)}] Timed out waiting for first chunk/cinematic on '{gameObject.name}'. Spawning pods anyway.", gameObject);
                    break;
                }

                yield return null;
            }

            Debug.Log(
                $"[{nameof(NetworkGameManager)}] Cinematic observed; scheduling pods after {introDurationSeconds.Value:0.###}s on '{gameObject.name}'",
                gameObject);

            // Synchronize cinematic start across clients (late joiners, ordering).
            if (spawnSequenceModel.Phase.Value == SpawnSequenceModel.SpawnSequencePhase.Cinematic)
            {
                spawnSequencePhase.Value = (int)SpawnSequenceModel.SpawnSequencePhase.Cinematic;
                long ts = spawnSequenceModel.IntroStartTimestampMsUtc.Value;
                if (ts <= 0)
                    ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                introStartTimestampMsUtc.Value = ts;
            }
            else
            {
                // Timeout fallback: still advance the synced phase so presentation can proceed.
                spawnSequencePhase.Value = (int)SpawnSequenceModel.SpawnSequencePhase.Cinematic;
                introStartTimestampMsUtc.Value = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            matchStartPodSpawner.ServerSpawnPodsAfterDelay(introDurationSeconds.Value);
        }

        private void OnIntroEnabledChanged(bool prev, bool next, bool asServer)
        {
            if (spawnSequenceModel == null)
                return;

            spawnSequenceModel.ApplyIntroConfig(next, introDurationSeconds.Value);
        }

        private void OnIntroDurationSecondsChanged(float prev, float next, bool asServer)
        {
            if (spawnSequenceModel == null)
                return;

            spawnSequenceModel.ApplyIntroConfig(introEnabled.Value, next);
        }

        private void OnSpawnSequencePhaseChanged(int prev, int next, bool asServer)
        {
            if (spawnSequenceModel == null)
                return;

            spawnSequenceModel.SetPhase((SpawnSequenceModel.SpawnSequencePhase)next);
        }

        private void OnIntroStartTimestampChanged(long prev, long next, bool asServer)
        {
            if (spawnSequenceModel == null)
                return;

            spawnSequenceModel.SetIntroStartTimestampMsUtc(next);
        }

        /// <summary>
        /// Loads the configured MapGen scene additively for all connections using FishNet SceneManager.<br>
        /// Typical usage: called when the match transitions into InProgress so MapGen objects exist only during gameplay.<br>
        /// Server/client constraints: server-only; clients follow the server's scene load.
        /// </summary>
        [Server]
        private void ServerLoadMapGenSceneIfNeeded()
        {
            if (_mapGenSceneRequested)
                return;

            if (string.IsNullOrWhiteSpace(mapGenSceneName))
                return;

            _mapGenSceneRequested = true;

            var sld = new SceneLoadData(mapGenSceneName)
            {
                ReplaceScenes = ReplaceOption.None
            };

            SceneManager.LoadGlobalScenes(sld);
        }

        /// <summary>
        /// Unloads the configured MapGen scene for all connections using FishNet SceneManager.<br>
        /// Typical usage: called when leaving gameplay back to menu so generated content is destroyed without manual cleanup.<br>
        /// Server/client constraints: server-only; clients follow the server's unload.
        /// </summary>
        [Server]
        private void ServerUnloadMapGenSceneIfLoaded()
        {
            if (!_mapGenSceneRequested)
                return;

            if (string.IsNullOrWhiteSpace(mapGenSceneName))
                return;

            _mapGenSceneRequested = false;
            var sud = new SceneUnloadData(mapGenSceneName);
            SceneManager.UnloadGlobalScenes(sud);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;

            ClientUnloadMapGenSceneIfLoaded();
        }

        private void ClientUnloadMapGenSceneIfLoaded()
        {
            if (string.IsNullOrWhiteSpace(mapGenSceneName))
                return;

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(mapGenSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(mapGenSceneName);
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
            ServerUnloadMapGenSceneIfLoaded();
            yield return new WaitForSeconds(0.25f);
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
                // If we are the server (Host), unload gameplay scene then stop the server.
                ServerUnloadMapGenSceneIfLoaded();
                StartCoroutine(LeaveGameStopServerCoroutine());
                Debug.Log("[NetworkGameManager] Host left game (unloaded MapGen then stopping server)");
            }
            else if (IsClientStarted)
            {
                // If we are just a client, unload MapGen locally then disconnect.
                ClientUnloadMapGenSceneIfLoaded();
                ClientManager.StopConnection();
                Debug.Log("[NetworkGameManager] Client left game (stopped connection)");
            }
        }

        private System.Collections.IEnumerator LeaveGameStopServerCoroutine()
        {
            yield return new WaitForSeconds(0.25f);
            ServerManager.StopConnection(true);
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
