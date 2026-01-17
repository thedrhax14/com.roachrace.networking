using FishNet.Transporting.Tugboat;
using FishNet.Managing;
using FishNet.Transporting;
using RoachRace.Data;
using RoachRace.UI;
using RoachRace.UI.Core;
using RoachRace.UI.Models;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Threading.Tasks;

namespace RoachRace.Networking
{
    /// <summary>
    /// Observes server connection information and automatically connects FishNet client
    /// when valid IP and port are received
    /// </summary>
    public class NetworkConnectionObserver : MonoBehaviour, IObserver<ServerConnectionInfo>
    {
        [Header("Dependencies")]
        [SerializeField]
        private ServersModel serversModel;

        [SerializeField]
        private Tugboat tugboat;

        [Header("Settings")]
        [Tooltip("Automatically connect when valid connection info is received")]
        [SerializeField]
        private bool autoConnect = true;

    #if UNITY_EDITOR
        [Header("Editor Dev")]
        [Tooltip("When enabled, selecting localhost will automatically start a host (server + client) in the Editor.")]
        [SerializeField] private bool autoHostLocalhostInEditor = true;
    #endif

        [Tooltip("Port to use when running as dedicated server")]
        [SerializeField]
        private ushort serverPort = 7777;

        private void Start()
        {       
            // Subscribe to server state changes to quit app when server stops
            NetworkManager nm = FindFirstObjectByType<NetworkManager>();
            if (nm != null)
            {
                nm.ServerManager.OnServerConnectionState += OnServerConnectionState;
            }
            else
            {
                Debug.LogError("[NetworkConnectionObserver] NetworkManager not found! Application quit on server stop will not work.");
            }
            // Auto-start server if running on Linux headless (dedicated server)
            if (IsLinuxServer())
            {
                StartDedicatedServer();
            }
        }

        private async void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            Debug.Log($"[NetworkConnectionObserver] Server connection state changed: {args.ConnectionState}");
            // Only quit if we are on Linux Server and the server stopped
            if (IsLinuxServer() && args.ConnectionState == LocalConnectionState.Stopped)
            {
                string deleteUrl = System.Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_URL");
                if (!string.IsNullOrEmpty(deleteUrl))
                {
                    Debug.Log($"[NetworkConnectionObserver] Server stopped.");
                    await SendDeleteRequest(deleteUrl);
                }
                else
                {
                    Debug.Log("[NetworkConnectionObserver] Server stopped. No ARBITRIUM_DELETE_URL found. Quitting application...");
                    Application.Quit();
                }
            }
        }

        private async Task SendDeleteRequest(string url)
        {
            Debug.Log($"[NetworkConnectionObserver] Sending delete request to Edgegap: {url}");
            using (UnityWebRequest webRequest = UnityWebRequest.Delete(url))
            {
                // Add authorization token if available
                string token = System.Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_TOKEN");
                if (!string.IsNullOrEmpty(token))
                {
                    webRequest.SetRequestHeader("authorization", token);
                    webRequest.SetRequestHeader("Accept", "*/*");
                    Debug.Log("[NetworkConnectionObserver] Added authorization token to delete request");
                }
                else
                {
                    Debug.LogWarning("[NetworkConnectionObserver] ARBITRIUM_DELETE_TOKEN not found. Request might fail if auth is required.");
                }

                Debug.Log("[NetworkConnectionObserver] Sending delete request...");
                var operation = webRequest.SendWebRequest();

                while (!operation.isDone)
                {
                    await Task.Yield();
                }

                Debug.Log($"[NetworkConnectionObserver] Delete request completed: {webRequest.responseCode}, Result: {webRequest.downloadHandler?.text}");
                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[NetworkConnectionObserver] Failed to send delete request: {webRequest.error}");
                    // Fallback to Quit if request fails
                    Application.Quit();
                }
                else
                {
                    Debug.Log("[NetworkConnectionObserver] Delete request sent successfully. Server should shut down shortly.");
                }
            }
        }

        private bool IsLinuxServer()
        {
            // Check if running on Linux and in headless mode (no graphics)
            return Application.platform == RuntimePlatform.LinuxServer && Application.isEditor == false;
        }

        private void StartDedicatedServer()
        {
            Debug.Log("[NetworkConnectionObserver] Detected Linux server environment - starting dedicated server");
            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot start dedicated server.");
                return;
            }

            // Set empty client address (not used for server)
            tugboat.SetClientAddress("");
            
            // Set server port
            tugboat.SetPort(serverPort);

            Debug.Log($"[NetworkConnectionObserver] Starting dedicated server on port {serverPort}");
            bool success = tugboat.StartConnection(true); // true = server mode

            if (success)
            {
                Debug.Log("[NetworkConnectionObserver] Dedicated server started successfully");
            }
            else
            {
                Debug.LogError("[NetworkConnectionObserver] Failed to start dedicated server");
            }
        }

        private void OnEnable()
        {
            if (serversModel != null)
            {
                serversModel.ConnectionInfo.Attach(this);
            }
        }

        private void OnDisable()
        {
            if (serversModel != null)
            {
                serversModel.ConnectionInfo.Detach(this);
            }
        }

        private void OnDestroy()
        {
            // Clean up event subscription
            if (IsLinuxServer())
            {
                NetworkManager nm = FindFirstObjectByType<NetworkManager>();
                if (nm != null)
                {
                    nm.ServerManager.OnServerConnectionState -= OnServerConnectionState;
                }
            }
        }

        public void OnNotify(ServerConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
            {
                Debug.Log("[NetworkConnectionObserver] Connection info cleared");
                return;
            }

            if (!connectionInfo.IsValid())
            {
                Debug.LogWarning($"[NetworkConnectionObserver] Invalid connection info received: {connectionInfo}");
                return;
            }

            Debug.Log($"[NetworkConnectionObserver] Valid connection info received: {connectionInfo}");

            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot connect.");
                return;
            }

            // Update Tugboat connection settings
            tugboat.SetClientAddress(connectionInfo.ip);
            tugboat.SetPort((ushort)connectionInfo.port);

            Debug.Log($"[NetworkConnectionObserver] Updated Tugboat - IP: {connectionInfo.ip}, Port: {connectionInfo.port}");

            // Auto-connect if enabled
            if (autoConnect)
            {
#if UNITY_EDITOR
                if (autoHostLocalhostInEditor && IsEditorLocalhost(connectionInfo))
                {
                    // Best-effort: start server first to avoid connection race.
                    if (tugboat.GetConnectionState(true) == LocalConnectionState.Stopped)
                        StartServer();
                }
#endif
                ConnectToServer();
            }
        }

#if UNITY_EDITOR
        private static bool IsEditorLocalhost(ServerConnectionInfo info)
        {
            if (info == null) return false;
            if (!Application.isEditor) return false;
            return info.ip == "localhost" || info.ip == "127.0.0.1";
        }
#endif

        /// <summary>
        /// Manually trigger connection to server using current Tugboat settings
        /// </summary>
        public void ConnectToServer()
        {
            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot connect.");
                return;
            }

            // Stop any existing client connection before starting new one
            if (tugboat.GetConnectionState(false) != FishNet.Transporting.LocalConnectionState.Stopped)
            {
                Debug.Log("[NetworkConnectionObserver] Stopping existing client connection...");
                tugboat.StopConnection(false);
            }

            Debug.Log($"[NetworkConnectionObserver] Connecting to server: {tugboat.GetClientAddress()}:{tugboat.GetPort()}");
            bool success = tugboat.StartConnection(false); // false = client mode

            if (success)
            {
                Debug.Log("[NetworkConnectionObserver] Client connection started successfully");
            }
            else
            {
                Debug.LogError("[NetworkConnectionObserver] Failed to start client connection");
            }
        }

        /// <summary>
        /// Disconnect from server
        /// </summary>
        public void DisconnectFromServer()
        {
            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot disconnect.");
                return;
            }

            Debug.Log("[NetworkConnectionObserver] Disconnecting from server...");
            tugboat.StopConnection(false); // false = client mode
        }

        /// <summary>
        /// Start the server
        /// </summary>
        public void StartServer()
        {
            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot start server.");
                return;
            }

            // Stop any existing server connection before starting new one
            if (tugboat.GetConnectionState(true) != FishNet.Transporting.LocalConnectionState.Stopped)
            {
                Debug.Log("[NetworkConnectionObserver] Stopping existing server connection...");
                tugboat.StopConnection(true);
            }

            Debug.Log($"[NetworkConnectionObserver] Starting server on port: {tugboat.GetPort()}");
            bool success = tugboat.StartConnection(true); // true = server mode

            if (success)
            {
                Debug.Log("[NetworkConnectionObserver] Server started successfully");
            }
            else
            {
                Debug.LogError("[NetworkConnectionObserver] Failed to start server");
            }
        }

        /// <summary>
        /// Stop the server
        /// </summary>
        public void StopServer()
        {
            if (tugboat == null)
            {
                Debug.LogError("[NetworkConnectionObserver] Tugboat reference is null. Cannot stop server.");
                return;
            }

            Debug.Log("[NetworkConnectionObserver] Stopping server...");
            tugboat.StopConnection(true); // true = server mode
        }

        private void OnValidate()
        {
            // Try to find Tugboat in scene if not assigned
            if (tugboat == null)
            {
                tugboat = FindFirstObjectByType<Tugboat>();
            }
        }
    }
}
