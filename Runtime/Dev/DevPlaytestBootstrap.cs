#if UNITY_EDITOR
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
using RoachRace.Data;
using RoachRace.Networking;
using RoachRace.UI.Dev;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Dev
{
    /// <summary>
    /// Editor-only play-mode bootstrapper.
    /// 
    /// Attach to a GameObject in your startup scene.
    /// When enabled, it can auto-host + connect to localhost and optionally start the match
    /// as a selected team without navigating UI.
    /// </summary>
    public sealed class DevPlaytestBootstrap : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private DevPlaytestConfig config;

        [Header("Dependencies")]
        [SerializeField] private ServersModel serversModel;

        [Tooltip("Optional. If not assigned, the bootstrap will FindFirstObjectByType at runtime.")]
        [SerializeField] private NetworkConnectionObserver connectionObserver;

        [Tooltip("Optional. If not assigned, the bootstrap will FindFirstObjectByType at runtime.")]
        [SerializeField] private NetworkGameManager gameManager;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float timeoutSeconds = 10f;

        private IEnumerator Start()
        {
            if (config == null || config.Mode == DevPlaytestConfig.PlaytestMode.Disabled)
                yield break;

            if (!config.UseLocalHost)
            {
                Debug.LogWarning($"[{nameof(DevPlaytestBootstrap)}] UseLocalHost is disabled. Nothing to bootstrap.", gameObject);
                yield break;
            }

            if (serversModel == null)
            {
                Debug.LogError($"[{nameof(DevPlaytestBootstrap)}] ServersModel is not assigned!", gameObject);
                yield break;
            }

            // Select localhost server (re-uses the existing ServersModel -> NetworkConnectionObserver path).
            var localServer = new ServerDeployment
            {
                request_id = "localhost",
                server_name = "Local Host",
                created_by = System.Environment.UserName,
                number_of_players = 0,
                public_ip = "localhost",
                udp_port = config.LocalHostPort
            };

            serversModel.SelectServer(localServer);
            Debug.Log($"[{nameof(DevPlaytestBootstrap)}] Selected localhost:{config.LocalHostPort} (mode={config.Mode})", gameObject);

            // Give observers a frame to react.
            yield return null;

            // Wait for network to start.
            connectionObserver ??= FindFirstObjectByType<NetworkConnectionObserver>();
            if (connectionObserver == null)
            {
                Debug.LogError($"[{nameof(DevPlaytestBootstrap)}] NetworkConnectionObserver not found in scene.", gameObject);
                yield break;
            }

            // Wait until client is started.
            float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, timeoutSeconds);
            NetworkManager nm = FindFirstObjectByType<NetworkManager>();
            while (Time.realtimeSinceStartup < deadline)
            {
                if (nm != null)
                {
                    bool clientStarted = nm.ClientManager != null && nm.ClientManager.Started;
                    bool serverStarted = nm.ServerManager != null && nm.ServerManager.Started;
                    if (clientStarted && serverStarted)
                        break;
                }

                nm ??= FindFirstObjectByType<NetworkManager>();
                yield return null;
            }

            // Ensure we have an owned NetworkPlayer, then set desired team.
            Team desiredTeam = config.DesiredTeam;
            NetworkPlayer localPlayer = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                localPlayer = FindLocalPlayer();
                if (localPlayer != null)
                    break;
                yield return null;
            }

            if (localPlayer == null)
            {
                Debug.LogWarning($"[{nameof(DevPlaytestBootstrap)}] Timed out waiting for owned NetworkPlayer.", gameObject);
                yield break;
            }

            localPlayer.SetTeam(desiredTeam);
            Debug.Log($"[{nameof(DevPlaytestBootstrap)}] Requested team={desiredTeam} for local player.", gameObject);

            // In host mode, the ServerRpc may not have been processed yet when we immediately start the match.
            // Wait until the local SyncVar reflects the desired team (or timeout) to avoid capturing the wrong
            // team in any server-side game start snapshots.
            while (Time.realtimeSinceStartup < deadline && localPlayer != null && localPlayer.Team != desiredTeam)
                yield return null;

            if (localPlayer == null)
            {
                Debug.LogWarning($"[{nameof(DevPlaytestBootstrap)}] Local NetworkPlayer was destroyed before team could be applied.", gameObject);
                yield break;
            }

            if (localPlayer.Team != desiredTeam)
                Debug.LogWarning($"[{nameof(DevPlaytestBootstrap)}] Team did not update to {desiredTeam} before timeout. Current={localPlayer.Team}.", gameObject);

            if (!config.ShouldStartInProgress)
                yield break;

            // Start game.
            gameManager ??= FindFirstObjectByType<NetworkGameManager>();
            if (gameManager == null)
            {
                Debug.LogWarning($"[{nameof(DevPlaytestBootstrap)}] NetworkGameManager not found; cannot start match.", gameObject);
                yield break;
            }

            // Best-effort immediate start in editor.
            if (config.SkipCountdown)
            {
                gameManager.DevStartGameImmediate();
                Debug.Log($"[{nameof(DevPlaytestBootstrap)}] DevStartGameImmediate() called.", gameObject);
            }
            else
            {
                gameManager.StartGame();
                Debug.Log($"[{nameof(DevPlaytestBootstrap)}] StartGame() called.", gameObject);
            }
        }

        private static NetworkPlayer FindLocalPlayer()
        {
            var players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                NetworkPlayer p = players[i];
                if (p == null) continue;
                if (!p.IsOwner) continue;
                return p;
            }
            return null;
        }
    }
}
#endif
