using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Data;
using RoachRace.UI.Models;
using RoachRace.UI.Components;
using UnityEngine;
using FishNet;

namespace RoachRace.Networking
{
    /// <summary>
    /// NetworkBehaviour representing a player in the game.
    /// Implements INetworkPlayer interface and syncs player data across network.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour, INetworkPlayer
    {
        [Header("Dependencies")]
        [SerializeField] private RoomModel roomModel;
        [SerializeField] private GameStateModel gameStateModel;
        [SerializeField] private PlayerStatsModel playerStatsModel;
        [SerializeField] private GamePlayersModel gamePlayersModel;

        [Header("Player Data")]
        private readonly SyncVar<string> playerName = new("Player");

        private readonly SyncVar<Team> team = new(Team.Survivor);
        private readonly SyncVar<long> ping = new(0);

        private readonly SyncVar<string> imageUrl = new("");
        private Player _playerData;

        #region NetworkBehaviour Lifecycle

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            // Initialize player data on server
            InitializePlayerData();
            AddPlayerToRoom();
            
            // Give room ownership to first player
            AssignRoomOwnershipToFirstPlayer();
            
            Debug.Log($"[NetworkPlayer] Server started for player: {playerName.Value} (ID: {OwnerId})");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (!IsServerInitialized)
            {
                // Client receives synced data
                InitializePlayerData();
                AddPlayerToRoom();
                
                Debug.Log($"[NetworkPlayer] Client started for player: {playerName.Value} (ID: {OwnerId})");
            }
            
            // Auto-register with RoomWindow if owned by this client
            if (IsOwner)
            {
                InitializeLocalPlayer();
            }
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (IsOwner)
            {
                InitializeLocalPlayer();
            }
        }

        void InitializeLocalPlayer()
        {
            InstanceFinder.TimeManager.OnRoundTripTimeUpdated += SetPing;
#if UNITY_EDITOR
            SetPlayerName(System.Environment.UserName);
#endif
            RegisterWithRoomWindow();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            CleanupPlayerData();
            RemovePlayerFromRoom();
            
            // Check if the server should be shut down (e.g. if host left in lobby or last player left in game)
            CheckServerShutdownCondition();
            
            Debug.Log($"[NetworkPlayer] Network player stopped on server: {playerName.Value} (ID: {OwnerId})");
        }

        private void CheckServerShutdownCondition()
        {
            NetworkRoomManager roomManager = FindFirstObjectByType<NetworkRoomManager>();
            if (roomManager == null) return;

            // Logic:
            // 1. If Lobby and Owner left -> Stop Server
            // 2. If Not Lobby and PlayerCount == 0 -> Stop Server

            bool isLobby = gameStateModel != null && gameStateModel.CurrentState.Value == GameState.Lobby;
            bool isOwner = roomManager.Owner == Owner;

            if (isLobby)
            {
                // In Lobby: Only stop if the owner leaves
                if (isOwner)
                {
                    Debug.Log($"[NetworkPlayer] Room owner left in Lobby (ID: {OwnerId}). Shutting down server immediately.");
                    ServerManager.StopConnection(true);
                }
            }
            else
            {
                // In Game/GameOver: Stop if everyone leaves (player count is 0)
                // Note: RemovePlayerFromRoom() is called before this, so count should be accurate
                if (roomModel != null && roomModel.CurrentRoom.Value != null && roomModel.CurrentRoom.Value.GetPlayerCount() == 0)
                {
                    Debug.Log($"[NetworkPlayer] Last player left (ID: {OwnerId}). Shutting down server.");
                    ServerManager.StopConnection(true);
                }
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            
            if (!IsServerInitialized)
            {
                CleanupPlayerData();
                RemovePlayerFromRoom();
            }
            if (IsOwner)
            {
                InstanceFinder.TimeManager.OnRoundTripTimeUpdated -= SetPing;
            }
            Debug.Log($"[NetworkPlayer] Client stopped for player: {playerName.Value} (ID: {OwnerId})");
        }

        [ServerRpc]
        void SetPing(long ping)
        {
            this.ping.Value = ping;
        }

        #endregion

        #region Player Management

        private void InitializePlayerData()
        {
            // Unsubscribe first to prevent duplicates
            CleanupPlayerData();

            playerName.OnChange += OnPlayerNameChanged;
            team.OnChange += OnTeamChanged;
            imageUrl.OnChange += OnImaegeUrlChanged;
            ping.OnChange += OnPingChanged;
            _playerData = new Player(playerName.Value, imageUrl.Value, team.Value, this);
            
            UpdateGamePlayersModel();
        }

        private void OnPingChanged(long prev, long next, bool asServer)
        {
            gamePlayersModel.SetPlayerPing(playerName.Value, next);
        }

        private void CleanupPlayerData()
        {
            playerName.OnChange -= OnPlayerNameChanged;
            team.OnChange -= OnTeamChanged;
            imageUrl.OnChange -= OnImaegeUrlChanged;
            ping.OnChange -= OnPingChanged;
            
            RemoveFromGamePlayersModel();
        }

        private void AddPlayerToRoom()
        {
            if (roomModel != null && _playerData != null)
            {
                // Ensure room exists before adding player
                if (roomModel.CurrentRoom.Value == null)
                {
                    // Create room if it doesn't exist yet
                    RoomInfo room = new("");
                    roomModel.SetRoom(room);
                    Debug.Log("[NetworkPlayer] Created room lazily");
                }
                
                roomModel.AddPlayer(_playerData);
            }
        }

        private void RemovePlayerFromRoom()
        {
            if (roomModel != null)
            {
                roomModel.RemovePlayerByNetworkId(OwnerId);
            }
        }

        private void UpdatePlayerInRoom()
        {
            if (roomModel != null && roomModel.CurrentRoom.Value != null)
            {
                Player existingPlayer = roomModel.CurrentRoom.Value.FindPlayerByNetworkId(OwnerId);
                if (existingPlayer != null)
                {
                    existingPlayer.playerName = playerName.Value;
                    existingPlayer.team = team.Value;
                    existingPlayer.imageUrl = imageUrl.Value;
                    
                    // Notify observers of the change
                    roomModel.CurrentRoom.Notify(roomModel.CurrentRoom.Value);
                }
            }
        }

        private void UpdateGamePlayersModel()
        {
            if (gamePlayersModel != null)
            {
                string pName = playerName.Value;
                if (string.IsNullOrEmpty(pName)) return;

                var observable = gamePlayersModel.GetPlayerStats(pName);
                PlayerStats stats = observable.Value;

                if (stats == null)
                {
                    stats = new PlayerStats(OwnerId, pName, team.Value, 3);
                }
                else
                {
                    stats.networkId = OwnerId;
                    stats.playerName = pName;
                    stats.team = team.Value;
                }
                gamePlayersModel.UpdatePlayer(pName, stats);
            }
        }

        private void RemoveFromGamePlayersModel()
        {
            if (gamePlayersModel != null && !string.IsNullOrEmpty(playerName.Value))
            {
                gamePlayersModel.RemovePlayer(playerName.Value);
            }
        }

        #endregion

        #region SyncVar Callbacks

        private void OnPlayerNameChanged(string oldValue, string newValue, bool asServer)
        {
            if (_playerData != null)
            {
                _playerData.playerName = newValue;
            }
            
            UpdatePlayerInRoom();

            if (gamePlayersModel != null)
            {
                if (!string.IsNullOrEmpty(oldValue) && oldValue != newValue)
                {
                    gamePlayersModel.RemovePlayer(oldValue);
                }
                UpdateGamePlayersModel();
            }
            
            Debug.Log($"[NetworkPlayer] Name changed: {oldValue} -> {newValue}");
        }

        private void OnTeamChanged(Team oldValue, Team newValue, bool asServer)
        {
            if (_playerData != null)
            {
                _playerData.team = newValue;
            }
            
            UpdatePlayerInRoom();
            UpdateGamePlayersModel();
            
            Debug.Log($"[NetworkPlayer] Team changed: {oldValue} -> {newValue}");
        }

        private void OnImaegeUrlChanged(string prev, string next, bool asServer)
        {
            if (_playerData != null)
            {
                _playerData.imageUrl = next;
            }
            
            UpdatePlayerInRoom();
            
            Debug.Log($"[NetworkPlayer] ImageUrl changed: {prev} -> {next}");
        }

        #endregion

        #region INetworkPlayer Implementation

        public int NetworkId => OwnerId;

        public bool IsLocalPlayer => IsOwner;

        [ServerRpc(RequireOwnership = false)]
        public void SetTeam(Team newTeam)
        {
            team.Value = newTeam;
        }

        public void ToggleTeam()
        {
            Team newTeam = team.Value == Team.Ghost ? Team.Survivor : Team.Ghost;
            SetTeam(newTeam);
            Debug.Log($"[NetworkPlayer] Toggling team: {team.Value} -> {newTeam}");
        }

        [ServerRpc]
        public void SetPlayerName(string name)
        {
            playerName.Value = name;
        }

        [ServerRpc]
        public void SetImageUrl(string url)
        {
            imageUrl.Value = url;
        }

        [ServerRpc(RequireOwnership = false)]
        public void Kick()
        {
            if (IsServerStarted)
            {
                NetworkConnection conn = Owner;
                if (conn != null)
                {
                    Debug.Log($"[NetworkPlayer] Kicking player: {playerName.Value} (ID: {OwnerId})");
                    ServerManager.Kick(conn, FishNet.Managing.Server.KickReason.Unset);
                }
            }
        }

        public int GetPing()
        {
            if (Owner != null)
            {
                return -1;
            }
            return 0;
        }

        #endregion

        #region RoomWindow Integration

        private void RegisterWithRoomWindow()
        {
            RoomWindow roomWindow = FindFirstObjectByType<RoomWindow>();
            if (roomWindow != null)
            {
                roomWindow.SetOwnedPlayer(this);
                Debug.Log($"[NetworkPlayer] Registered owned player with RoomWindow: {playerName.Value}");
            }
        }

        private void AssignRoomOwnershipToFirstPlayer()
        {
            if (!IsServerStarted) {
                Debug.LogWarning("[NetworkPlayer] Cannot assign room ownership - not running on server", gameObject);
                return;
            }

            // Only assign ownership in Lobby
            if (gameStateModel != null && gameStateModel.CurrentState.Value != GameState.Lobby)
            {
                Debug.Log($"[NetworkPlayer] Game is in progress ({gameStateModel.CurrentState.Value}), skipping room ownership assignment.");
                return;
            }

            if(roomModel == null)
            {
                Debug.LogWarning("[NetworkPlayer] Cannot assign room ownership - RoomModel or CurrentRoom is null", gameObject);
                return;
            }

            // Check if this is the first player (room has 1 player after adding this one)
            if (roomModel.CurrentRoom.Value != null)
            {
                if(roomModel.CurrentRoom.Value.GetPlayerCount() == 1)
                {
                    NetworkRoomManager roomManager = FindFirstObjectByType<NetworkRoomManager>();
                    if (roomManager != null)
                    {
                        roomManager.GiveOwnership(Owner);
                        Debug.Log($"[NetworkPlayer] Assigned room ownership to first player (ID: {OwnerId})");
                    }
                    else
                    {
                        Debug.LogWarning("[NetworkPlayer] Cannot assign room ownership - NetworkRoomManager not found", gameObject);
                    }
                }
                else
                {
                    Debug.LogWarning($"[NetworkPlayer] Room has multiple players ({roomModel.CurrentRoom.Value.GetPlayerCount()}), not assigning ownership to player (ID: {OwnerId})", gameObject);
                }
            }
            else
            {
                Debug.LogWarning($"[NetworkPlayer] Room already has players, not assigning ownership to player (ID: {OwnerId})", gameObject);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize player with default values (called by server on spawn)
        /// </summary>
        [Server]
        public void Initialize(string name, Team playerTeam, string avatar = "")
        {
            playerName.Value = name;
            team.Value = playerTeam;
            imageUrl.Value = avatar;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Get the player's name
        /// </summary>
        public string PlayerName => playerName.Value;

        /// <summary>
        /// Get the player's team
        /// </summary>
        public Team Team => team.Value;

        /// <summary>
        /// Get the player's avatar image URL
        /// </summary>
        public string ImageUrl => imageUrl.Value;

        #endregion
    }
}
