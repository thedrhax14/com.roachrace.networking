using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Server-side spawner for match start "spawn pods" (team-specific animated prefabs).<br></br>
    /// Purpose: spawn an immersive, animated placeholder at each player's spawn point before their real controller is spawned.<br></br>
    /// Typical usage:<br></br>
    /// - Add this component to a server-authoritative scene object (e.g., alongside NetworkGameManager).<br></br>
    /// - Assign survivor/ghost pod prefabs (NetworkObject prefabs) in the Inspector.<br></br>
    /// - On server, call <see cref="ServerSpawnPodsAfterDelay"/> (default 10s) once the intro cinematic has started (e.g., after first MapGen chunk).<br></br>
    /// Notes:<br></br>
    /// - This class ONLY spawns pods. Swapping pods to controllable controllers is handled in a later step.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Match Start Pod Spawner")]
    public sealed class NetworkMatchStartPodSpawner : NetworkBehaviour
    {
        [Header("Dependencies")]
        [Tooltip("Optional: SpawnSequenceModel to update phase when pods are spawned.")]
        [SerializeField] private SpawnSequenceModel spawnSequenceModel;

        [Tooltip("Optional: NetworkGameManager to synchronize spawn-sequence phase to clients.")]
        [SerializeField] private NetworkGameManager networkGameManager;

        [Header("Pod Prefabs (NetworkObject)")]
        [Tooltip("Prefab spawned for Survivor players during match start.")]
        [SerializeField] private NetworkObject survivorPodPrefab;

        [Tooltip("Prefab spawned for Ghost players during match start.")]
        [SerializeField] private NetworkObject ghostPodPrefab;

        [Header("Timing")]
        [Tooltip("Default delay (seconds) before pods spawn, if callers do not provide an override.")]
        [SerializeField] private float defaultDelaySeconds = 10f;

        private readonly Dictionary<int, NetworkObject> _podsByClientId = new();
        private readonly Dictionary<int, NetworkPlayerControllerLifecycle> _lifecyclesByClientId = new();
        private readonly Dictionary<int, Coroutine> _swapRoutinesByClientId = new();
        private Coroutine _spawnRoutine;

        /// <summary>
        /// FishNet server lifecycle hook.<br></br>
        /// Typical usage: validates required prefab assignments before any spawn is attempted.<br></br>
        /// Server/client constraints: runs only on the server instance.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (survivorPodPrefab == null)
            {
                Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] Missing required reference on '{gameObject.name}': survivorPodPrefab", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] Missing required reference on '{gameObject.name}': survivorPodPrefab");
            }

            if (ghostPodPrefab == null)
            {
                Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] Missing required reference on '{gameObject.name}': ghostPodPrefab", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] Missing required reference on '{gameObject.name}': ghostPodPrefab");
            }
        }

        /// <summary>
        /// Starts a server coroutine to spawn pods for all current players after a delay.<br></br>
        /// Typical usage: called once at match start after the intro cinematic begins.<br></br>
        /// Server/client constraints: server-only.
        /// </summary>
        /// <param name="delaySeconds">Delay before spawning pods. If null, uses <see cref="defaultDelaySeconds"/>.</param>
        [Server]
        public void ServerSpawnPodsAfterDelay(float? delaySeconds = null)
        {
            float delay = delaySeconds ?? defaultDelaySeconds;
            delay = Mathf.Max(0f, delay);

            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }

            _spawnRoutine = StartCoroutine(SpawnAfterDelayCoroutine(delay));

            Debug.Log($"[{nameof(NetworkMatchStartPodSpawner)}] Scheduled pod spawn in {delay:0.###}s on '{gameObject.name}'", gameObject);
        }

        /// <summary>
        /// Spawns pods for all current players immediately.<br></br>
        /// Typical usage: called by server code when the intro timer elapses.<br></br>
        /// Server/client constraints: server-only.
        /// </summary>
        [Server]
        public void ServerSpawnPodsNow()
        {
            var spawner = FindFirstObjectByType<RoachRacePlayerSpawner>();
            if (spawner == null)
            {
                Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] Missing {nameof(RoachRacePlayerSpawner)} in scene on '{gameObject.name}'", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] Missing {nameof(RoachRacePlayerSpawner)} in scene on '{gameObject.name}'");
            }

            var lifecycles = FindObjectsByType<NetworkPlayerControllerLifecycle>(FindObjectsSortMode.None);
            if (lifecycles == null || lifecycles.Length == 0)
            {
                Debug.LogWarning($"[{nameof(NetworkMatchStartPodSpawner)}] No {nameof(NetworkPlayerControllerLifecycle)} found; no pods will be spawned on '{gameObject.name}'", gameObject);
                return;
            }

            // Clear previous tracking (match start sequence should be one-shot).
            _lifecyclesByClientId.Clear();

            // Spawn simultaneously from the server perspective; order does not matter.
            for (int i = 0; i < lifecycles.Length; i++)
            {
                NetworkPlayerControllerLifecycle lifecycle = lifecycles[i];
                if (lifecycle == null)
                    continue;

                NetworkPlayer networkPlayer = lifecycle.GetComponent<NetworkPlayer>();
                if (networkPlayer == null)
                {
                    Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] Missing {nameof(NetworkPlayer)} on '{lifecycle.gameObject.name}'", lifecycle.gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] Missing {nameof(NetworkPlayer)} on '{lifecycle.gameObject.name}'");
                }

                if (lifecycle.Owner == null || !lifecycle.Owner.IsActive)
                {
                    Debug.LogWarning($"[{nameof(NetworkMatchStartPodSpawner)}] Skipping pod spawn for '{lifecycle.gameObject.name}' (inactive owner) on '{gameObject.name}'", gameObject);
                    continue;
                }

                int clientId = lifecycle.Owner.ClientId;
                if (_podsByClientId.TryGetValue(clientId, out NetworkObject existingPod) && existingPod != null)
                    continue;

                _lifecyclesByClientId[clientId] = lifecycle;

                Team team = networkPlayer.Team;
                Transform spawnPoint = spawner.GetSpawnPoint(team);
                if (spawnPoint == null)
                {
                    Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] No spawn point available for team {team} on '{gameObject.name}'", gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] No spawn point available for team {team} on '{gameObject.name}'");
                }

                NetworkObject podPrefab = GetPodPrefabForTeam(team);
                if (podPrefab == null)
                {
                    Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] No pod prefab configured for team {team} on '{gameObject.name}'", gameObject);
                    throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] No pod prefab configured for team {team} on '{gameObject.name}'");
                }

                NetworkObject pod = Instantiate(podPrefab, spawnPoint.position, spawnPoint.rotation);
                InstanceFinder.ServerManager.Spawn(pod, lifecycle.Owner, lifecycle.gameObject.scene);

                _podsByClientId[clientId] = pod;

                float swapDelaySeconds = 0f;
                if (pod.TryGetComponent(out NetworkMatchStartSpawnPod podBehaviour))
                {
                    swapDelaySeconds = podBehaviour.SwapDelaySeconds;
                }

                ScheduleSwap(clientId, swapDelaySeconds);

                Debug.Log(
                    $"[{nameof(NetworkMatchStartPodSpawner)}] Spawned {team} pod '{pod.gameObject.name}' for client {clientId} at '{spawnPoint.name}' on '{gameObject.name}'",
                    gameObject);
            }

            if (spawnSequenceModel != null)
                spawnSequenceModel.SetPhase(SpawnSequenceModel.SpawnSequencePhase.PodsActive);

            if (networkGameManager == null)
                networkGameManager = FindFirstObjectByType<NetworkGameManager>();

            if (networkGameManager != null)
                networkGameManager.ServerSetSpawnSequencePhase(SpawnSequenceModel.SpawnSequencePhase.PodsActive);
            else
                Debug.LogWarning($"[{nameof(NetworkMatchStartPodSpawner)}] Could not find {nameof(NetworkGameManager)} to sync phase on '{gameObject.name}'", gameObject);
        }

        [Server]
        private void ScheduleSwap(int clientId, float delaySeconds)
        {
            if (_swapRoutinesByClientId.TryGetValue(clientId, out Coroutine existing) && existing != null)
            {
                StopCoroutine(existing);
            }

            _swapRoutinesByClientId[clientId] = StartCoroutine(SwapAfterDelayCoroutine(clientId, Mathf.Max(0f, delaySeconds)));
        }

        [Server]
        private IEnumerator SwapAfterDelayCoroutine(int clientId, float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            _swapRoutinesByClientId.Remove(clientId);
            ServerSwapPodToController(clientId);
        }

        [Server]
        private void ServerSwapPodToController(int clientId)
        {
            if (!_podsByClientId.TryGetValue(clientId, out NetworkObject pod) || pod == null)
                return;

            if (!_lifecyclesByClientId.TryGetValue(clientId, out NetworkPlayerControllerLifecycle lifecycle) || lifecycle == null)
            {
                Debug.LogError($"[{nameof(NetworkMatchStartPodSpawner)}] Cannot swap pod for client {clientId} on '{gameObject.name}': missing lifecycle", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodSpawner)}] Cannot swap pod for client {clientId} on '{gameObject.name}': missing lifecycle");
            }

            Vector3 position = pod.transform.position;
            Quaternion rotation = pod.transform.rotation;

            // Ghost pods can spawn a client-only ragdoll at swap time.
            if (pod.TryGetComponent(out NetworkMatchStartSpawnPod podBehaviour) && podBehaviour.Team == Team.Ghost)
            {
                podBehaviour.ObserversSpawnGhostRagdoll();
            }

            InstanceFinder.ServerManager.Despawn(pod);
            _podsByClientId.Remove(clientId);

            lifecycle.ServerSpawnControllerForCurrentTeamAt(position, rotation);

            if (_podsByClientId.Count == 0 && spawnSequenceModel != null)
                spawnSequenceModel.SetPhase(SpawnSequenceModel.SpawnSequencePhase.ControllersSpawned);

            if (_podsByClientId.Count == 0)
            {
                if (networkGameManager == null)
                    networkGameManager = FindFirstObjectByType<NetworkGameManager>();

                if (networkGameManager != null)
                    networkGameManager.ServerSetSpawnSequencePhase(SpawnSequenceModel.SpawnSequencePhase.ControllersSpawned);
                else
                    Debug.LogWarning($"[{nameof(NetworkMatchStartPodSpawner)}] Could not find {nameof(NetworkGameManager)} to sync final phase on '{gameObject.name}'", gameObject);
            }

            Debug.Log($"[{nameof(NetworkMatchStartPodSpawner)}] Swapped pod to controller for client {clientId} on '{gameObject.name}'", gameObject);
        }

        /// <summary>
        /// Clears tracked pod references and despawns any spawned pods.<br></br>
        /// Typical usage: called when aborting a match start sequence or leaving gameplay.<br></br>
        /// Server/client constraints: server-only.
        /// </summary>
        [Server]
        public void ServerDespawnAllPods()
        {
            foreach (var routine in _swapRoutinesByClientId)
            {
                if (routine.Value != null)
                    StopCoroutine(routine.Value);
            }
            _swapRoutinesByClientId.Clear();

            foreach (var kvp in _podsByClientId)
            {
                if (kvp.Value == null)
                    continue;

                InstanceFinder.ServerManager.Despawn(kvp.Value);
            }

            _podsByClientId.Clear();
            _lifecyclesByClientId.Clear();

            Debug.Log($"[{nameof(NetworkMatchStartPodSpawner)}] Despawned all pods on '{gameObject.name}'", gameObject);
        }

        private NetworkObject GetPodPrefabForTeam(Team team)
        {
            return team == Team.Ghost ? ghostPodPrefab : survivorPodPrefab;
        }

        private IEnumerator SpawnAfterDelayCoroutine(float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            _spawnRoutine = null;
            ServerSpawnPodsNow();
        }
    }
}
