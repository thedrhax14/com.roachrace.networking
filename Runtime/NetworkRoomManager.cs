using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Data;
using RoachRace.UI.Models;
using RoachRace.UI.Components;
using UnityEngine;
using FishNet.Connection;

namespace RoachRace.Networking
{
    /// <summary>
    /// NetworkBehaviour that manages room information across the network
    /// </summary>
    public class NetworkRoomManager : NetworkBehaviour, INetworkRoom
    {
        [Header("Dependencies")]
        [SerializeField] private RoomModel roomModel;

        private readonly SyncVar<string> roomName = new("");

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            // Read room name from environment variable FIRST
            string serverName = System.Environment.GetEnvironmentVariable("SERVER_NAME");
            roomName.Value = string.IsNullOrWhiteSpace(serverName) ? "missing_env_variable" : serverName;
            
            // THEN ensure room exists with the correct name
            EnsureRoomExists();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (!IsServerInitialized)
            {
                // Unsubscribe first to prevent duplicates
                roomName.OnChange -= OnRoomNameChanged;
                // Subscribe to changes
                roomName.OnChange += OnRoomNameChanged;
                EnsureRoomExists();
                
                Debug.Log($"[NetworkRoomManager] Client started - Joined room: {roomName.Value}");
            }            
            // Always register with RoomWindow so UI can call LeaveRoom/StartGame
            RegisterWithRoomWindow();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            
            if (!IsServerInitialized)
            {
                roomName.OnChange -= OnRoomNameChanged;
            }
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (roomModel != null)
            {
                Debug.Log($"[{nameof(NetworkRoomManager)}] Room ownership updated");
                if(IsOwner) Debug.Log($"[{nameof(NetworkRoomManager)}] <color=green>We are the room owner</color>");
                else Debug.Log($"[{nameof(NetworkRoomManager)}] <color=yellow>We are NOT the room owner</color>");
                roomModel.SetRoomOwner(IsOwner);
            }
        }

        /// <summary>
        /// Ensure room exists in RoomModel (lazy initialization)
        /// </summary>
        private void EnsureRoomExists()
        {
            if (roomModel == null)
                return;

            if (roomModel.CurrentRoom.Value == null)
            {
                RoomInfo room = new(roomName.Value);
                roomModel.SetRoom(room);
            }
        }

        /// <summary>
        /// Public method to ensure room exists (can be called by other components)
        /// </summary>
        public void EnsureRoomInitialized()
        {
            EnsureRoomExists();
        }

        /// <summary>
        /// Set the room name (server only)
        /// </summary>
        [Server]
        public void SetRoomName(string name)
        {
            roomName.Value = name;
            
            if (roomModel != null)
            {
                roomModel.SetRoomName(name);
            }
        }

        /// <summary>
        /// Called when room name changes
        /// </summary>
        private void OnRoomNameChanged(string oldValue, string newValue, bool asServer)
        {
            if (roomModel != null) roomModel.SetRoomName(newValue);
        }

        /// <summary>
        /// Get current room name
        /// </summary>
        public string GetRoomName()
        {
            return roomName.Value;
        }

        #region INetworkRoom Implementation

        public string RoomName => roomName.Value;

        [ServerRpc]
        public void StartGame()
        {
            Debug.Log("[NetworkRoomManager] Starting game...");
            
            // Find and notify game manager to start
            NetworkGameManager gameManager = FindFirstObjectByType<NetworkGameManager>();
            if (gameManager != null)
            {
                gameManager.StartGame();
            }
            else
            {
                Debug.LogWarning("[NetworkRoomManager] NetworkGameManager not found in scene!");
            }
        }

        public void LeaveRoom()
        {
            if (IsServerStarted)
            {
                // If we are the server (Host), stop the server completely
                ServerManager.StopConnection(true);
                Debug.Log("[NetworkRoomManager] Host left room (stopped server)");
            }
            else if (IsClientStarted)
            {
                // If we are just a client, disconnect
                ClientManager.StopConnection();
                Debug.Log("[NetworkRoomManager] Client left room (stopped connection)");
            }
        }

        #endregion

        #region RoomWindow Integration

        private void RegisterWithRoomWindow()
        {
            RoomWindow roomWindow = FindFirstObjectByType<RoomWindow>();
            if (roomWindow != null) roomWindow.SetNetworkRoom(this);
        }

        #endregion
    }
}
