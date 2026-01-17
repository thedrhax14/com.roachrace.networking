using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.UI.Core;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Custom player spawner for RoachRace.
    /// Handles delayed player spawning on game start and NetworkRoomManager ownership.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/RoachRacePlayerSpawner")]
    public class RoachRacePlayerSpawner : MonoBehaviour, IObserver<GameState>
    {
        [Header("Dependencies")]
        [Tooltip("Prefab to spawn for the player.")]
        [SerializeField] private NetworkPlayer playerPrefab;
        [SerializeField] private NetworkObject survivorControllerPrefab, ghostControllerPrefab;

        [Tooltip("Prefab to spawn for the NetworkRoomManager.")]
        [SerializeField] private NetworkRoomManager roomManagerPrefab;
        
        [Tooltip("Reference to the RoomModel to retrieve player team data.")]
        [SerializeField] private RoomModel roomModel;
        
        [Tooltip("Reference to the GameStateModel to detect round start.")]
        [SerializeField] private GameStateModel gameStateModel;
        
        [Header("Spawn Points")]
        [SerializeField] private Transform[] survivorSpawns;
        [SerializeField] private Transform[] ghostSpawns;

        private NetworkManager _networkManager;
        private int _nextSurvivorSpawn;
        private int _nextGhostSpawn;

        private void Awake()
        {
            _networkManager = InstanceFinder.NetworkManager;
            if (_networkManager == null)
            {
                _networkManager = GetComponentInParent<NetworkManager>();
            }
        }

        private void Start()
        {
            if (_networkManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            }
            
            if (gameStateModel != null)
            {
                // Subscribe to game state changes
                gameStateModel.CurrentState.Attach(this);
            }
            else
            {
                Debug.LogWarning($"[{nameof(RoachRacePlayerSpawner)}] GameStateModel is not assigned! Player spawning will not trigger automatically.");
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            }
            
            if (gameStateModel != null)
            {
                gameStateModel.CurrentState.Detach(this);
            }
        }

        /// <summary>
        /// Handles new connections.
        /// Spawns NetworkRoomManager if it doesn't exist and assigns ownership.
        /// Spawns player in Lobby.
        /// </summary>
        private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            // Only run on server
            if (!asServer || _networkManager == null || !_networkManager.IsServerStarted) return;

            // Check if NetworkRoomManager exists
            NetworkRoomManager roomManager = FindFirstObjectByType<NetworkRoomManager>();
            
            if (roomManager == null)
            {
                if (roomManagerPrefab != null)
                {
                    // Spawn NetworkRoomManager and assign ownership to this connection
                    NetworkRoomManager networkRoomManager = Instantiate(roomManagerPrefab);
                    _networkManager.ServerManager.Spawn(networkRoomManager.GetComponent<NetworkObject>(), conn);
                    Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Spawning {nameof(NetworkRoomManager)} and assigning ownership to client {conn.ClientId}");
                }
                else
                {
                    Debug.LogError($"[{nameof(RoachRacePlayerSpawner)}] RoomManagerPrefab is not assigned! Cannot spawn NetworkRoomManager.");
                }
            }
            else if (!roomManager.Owner.IsActive)
            {
                // If it exists but owner is not active (e.g. disconnected), transfer ownership
                roomManager.NetworkObject.GiveOwnership(conn);
                Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Assigned existing NetworkRoomManager ownership to client {conn.ClientId}");
            }

            // Spawn player in Lobby
            SpawnPlayerForConnection(conn);
        }

        public void OnNotify(GameState state)
        {
            if (state == GameState.InProgress)
            {
                Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Detected game state InProgress. Spawning players...");
                // Only run on server
                if (_networkManager == null) return;
                if(_networkManager.IsServerStarted) SpawnPlayers();
                else Debug.LogWarning($"[{nameof(RoachRacePlayerSpawner)}] NetworkManager is not started as server. Cannot spawn players.");
            }
        }

        /// <summary>
        /// Spawns players for all active connections.
        /// Removes ownership from NetworkRoomManager.
        /// </summary>
        public void SpawnPlayers()
        {
            Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Spawning/Repositioning players for game start...");

            // Remove ownership from RoomManager
            NetworkRoomManager roomManager = FindFirstObjectByType<NetworkRoomManager>();
            if (roomManager != null)
            {
                roomManager.NetworkObject.RemoveOwnership();
                Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Removed NetworkRoomManager ownership");
            }

            // Reset spawn indices
            _nextSurvivorSpawn = 0;
            _nextGhostSpawn = 0;

            // Iterate through all connected clients
            foreach (var conn in _networkManager.ServerManager.Clients.Values)
            {
                if (conn.IsActive)
                {
                    SpawnPlayerForConnection(conn);
                    SpawnControllerForConnection(conn);
                }
            }
        }

        private void SpawnControllerForConnection(NetworkConnection conn)
        {
            // Find player data in RoomModel to determine team
            Player playerData = null;
            if (roomModel != null && roomModel.CurrentRoom.Value != null)
            {
                playerData = roomModel.CurrentRoom.Value.FindPlayerByNetworkId(conn.ClientId);
            }

            Team team = playerData != null ? playerData.team : Team.Survivor;
            NetworkObject prefabToSpawn = null;

            if (team == Team.Survivor)
            {
                prefabToSpawn = survivorControllerPrefab;
            }
            else if (team == Team.Ghost)
            {
                prefabToSpawn = ghostControllerPrefab;
            }

            if (prefabToSpawn == null)
            {
                Debug.LogError($"[{nameof(RoachRacePlayerSpawner)}] Controller prefab for team {team} is missing!", gameObject);
                return;
            }

            Transform spawnPoint = GetSpawnPoint(team);
            
            NetworkObject nob = _networkManager.GetPooledInstantiated(prefabToSpawn, spawnPoint.position, spawnPoint.rotation, true);
            _networkManager.ServerManager.Spawn(nob, conn);
            
            _networkManager.SceneManager.AddOwnerToDefaultScene(nob);
            Debug.Log($"[{nameof(RoachRacePlayerSpawner)}] Spawned {team} controller for client {conn.ClientId}");
        }

        private void SpawnPlayerForConnection(NetworkConnection conn)
        {
            // Find player data in RoomModel
            Player playerData = null;
            if (roomModel != null && roomModel.CurrentRoom.Value != null)
            {
                playerData = roomModel.CurrentRoom.Value.FindPlayerByNetworkId(conn.ClientId);
            }

            Team team = Team.Survivor; // Default
            string name = $"Player {conn.ClientId}";
            string imageUrl = "";

            if (playerData != null)
            {
                team = playerData.team;
                name = playerData.playerName;
                imageUrl = playerData.imageUrl;
            }

            // Always spawn at zero
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;

            // Check if player object already exists for this connection
            NetworkObject existingPlayer = null;
            foreach (var obj in conn.Objects)
            {
                if (obj.GetComponent<NetworkPlayer>() != null)
                {
                    existingPlayer = obj;
                    break;
                }
            }

            if (existingPlayer != null)
            {
                // Ensure it's at zero
                existingPlayer.transform.SetPositionAndRotation(pos, rot);
                
                // Re-initialize data to ensure consistency
                NetworkPlayer np = existingPlayer.GetComponent<NetworkPlayer>();
                if (np != null)
                {
                    np.Initialize(name, team, imageUrl);
                }
            }
            else
            {
                // Instantiate and Spawn using FishNet pooling
                NetworkObject nob = _networkManager.GetPooledInstantiated(playerPrefab, pos, rot, true);
                _networkManager.ServerManager.Spawn(nob, conn);

                // Initialize NetworkPlayer data
                if (nob.TryGetComponent<NetworkPlayer>(out var np))
                {
                    np.Initialize(name, team, imageUrl);
                }
                
                // Add to default scene if needed (optional, but good practice)
                _networkManager.SceneManager.AddOwnerToDefaultScene(nob);
            }
        }

        private Transform GetSpawnPoint(Team team)
        {
            if (team == Team.Survivor)
            {
                if (survivorSpawns == null || survivorSpawns.Length == 0) return transform;
                Transform t = survivorSpawns[_nextSurvivorSpawn];
                _nextSurvivorSpawn = (_nextSurvivorSpawn + 1) % survivorSpawns.Length;
                return t;
            }
            else if(team == Team.Ghost)
            {
                if (ghostSpawns == null || ghostSpawns.Length == 0) return transform;
                Transform t = ghostSpawns[_nextGhostSpawn];
                _nextGhostSpawn = (_nextGhostSpawn + 1) % ghostSpawns.Length;
                return t;
            }
            return transform;
        }
    }
}
