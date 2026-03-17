using System;
using System.Collections;
using FishNet;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.Networking.Combat;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Server-side controller lifecycle for a connected player.<br>
    /// Typical usage: add this component to the NetworkPlayer prefab, assign the survivor/ghost controller prefabs in the Inspector, and let the server call <see cref="ServerSpawnControllerForCurrentTeam"/> at match start and <see cref="ServerRequestRespawn"/> after death.<br>
    /// Configuration/context: spawn points are sourced from the scene's <see cref="RoachRacePlayerSpawner"/> (team-specific arrays), and death detection relies on the spawned controller having a <see cref="NetworkHealth"/> component and registering this lifecycle via <see cref="NetworkHealth.AddServerDeathObserver"/>.<br>
    /// Server/client constraints: all spawning and respawn decisions are performed on the server; clients observe results via FishNet replication.
    /// </summary>
    public sealed class NetworkPlayerControllerLifecycle : NetworkBehaviour, INetworkHealthServerDeathObserver
    {
        [Header("Controller Prefabs")]
        [SerializeField] private NetworkObject survivorControllerPrefab;
        [SerializeField] private NetworkObject ghostControllerPrefab;

        [Header("Respawn")]
        [SerializeField] private float respawnDelaySeconds = 3f;

        private NetworkPlayer _networkPlayer;
        private NetworkPlayerStats _stats;
        private NetworkObject _currentController;
        private NetworkHealth _currentControllerHealth;
        private bool _respawnInProgress;

        /// <summary>
        /// FishNet server lifecycle hook.<br>
        /// Typical usage: validates required dependencies and prefab assignments before any spawning can occur.<br>
        /// Server/client constraints: runs only on the server instance.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();

            _networkPlayer = GetComponent<NetworkPlayer>();
            _stats = GetComponent<NetworkPlayerStats>();

            if (_networkPlayer == null)
                throw new InvalidOperationException($"{nameof(NetworkPlayerControllerLifecycle)} requires {nameof(NetworkPlayer)} on the same object.");
            if (_stats == null)
                throw new InvalidOperationException($"{nameof(NetworkPlayerControllerLifecycle)} requires {nameof(NetworkPlayerStats)} on the same object.");

            if (survivorControllerPrefab == null)
                throw new InvalidOperationException($"{nameof(NetworkPlayerControllerLifecycle)} missing survivorControllerPrefab.");
            if (ghostControllerPrefab == null)
                throw new InvalidOperationException($"{nameof(NetworkPlayerControllerLifecycle)} missing ghostControllerPrefab.");
        }

        /// <summary>
        /// Spawns (or re-spawns) the player's controller for their current team.<br>
        /// Typical usage: called by the server at match start, and internally after a respawn delay once the current controller dies.<br>
        /// Configuration/context: uses <see cref="RoachRacePlayerSpawner"/> to select a team spawn point (round-robin).<br>
        /// Server/client constraints: must be called on the server; the spawned NetworkObject is owned by <see cref="NetworkBehaviour.Owner"/>.
        /// </summary>
        [Server]
        public void ServerSpawnControllerForCurrentTeam()
        {
            if (_respawnInProgress)
                return;

            var team = _networkPlayer.Team;
            var spawnPoint = FindSpawnPointForTeam(team);
            var prefab = GetControllerPrefabForTeam(team);

            if (prefab == null)
            {
                Debug.LogError($"No controller prefab for team {team}", gameObject);
                return;
            }

            if (spawnPoint == null)
            {
                Debug.LogError("No spawn point available for controller spawn.", gameObject);
                return;
            }

            var controller = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
            InstanceFinder.ServerManager.Spawn(controller, Owner, gameObject.scene);
            SetCurrentController(controller);
        }

        /// <summary>
        /// Spawns (or re-spawns) the player's controller for their current team at an explicit pose.<br>
        /// Typical usage: used by match start sequences to replace an animated "spawn pod" with the real controllable controller at the same position/rotation.<br>
        /// Configuration/context: controller prefab selection mirrors <see cref="ServerSpawnControllerForCurrentTeam"/>.<br>
        /// Server/client constraints: must be called on the server; the spawned NetworkObject is owned by <see cref="NetworkBehaviour.Owner"/>.
        /// </summary>
        /// <param name="position">World position to spawn at.</param>
        /// <param name="rotation">World rotation to spawn with.</param>
        [Server]
        public void ServerSpawnControllerForCurrentTeamAt(Vector3 position, Quaternion rotation)
        {
            if (_respawnInProgress)
                return;

            var team = _networkPlayer.Team;
            var prefab = GetControllerPrefabForTeam(team);

            if (prefab == null)
            {
                Debug.LogError($"No controller prefab for team {team}", gameObject);
                return;
            }

            var controller = Instantiate(prefab, position, rotation);
            InstanceFinder.ServerManager.Spawn(controller, Owner, gameObject.scene);
            SetCurrentController(controller);
        }

        /// <summary>
        /// Requests a respawn of the player's controller, optionally delayed by <c>respawnDelaySeconds</c>.<br>
        /// Typical usage: invoked after death (via <see cref="INetworkHealthServerDeathObserver"/>) or via a client-owned stats RPC (see <see cref="NetworkPlayerStats.RequestRespawnServerRpc"/>).<br>
        /// Server/client constraints: must be executed on the server; this method is a no-op while a respawn is already in progress.
        /// </summary>
        [Server]
        public void ServerRequestRespawn()
        {
            if (_respawnInProgress)
                return;

            if (_networkPlayer.Team == Team.Survivor && !_stats.CanRespawn())
                return;

            _respawnInProgress = true;
            if (respawnDelaySeconds <= 0f)
            {
                PerformRespawn();
                return;
            }

            StartCoroutine(RespawnAfterDelayCoroutine());
        }

        /// <summary>
        /// Sets the current controller reference and (re)subscribes to server death observers.<br>
        /// Typical usage: called immediately after spawning a new controller so the lifecycle can observe its death and trigger respawn logic.<br>
        /// Configuration/context: the controller must have a <see cref="NetworkHealth"/> component for death notifications to work.<br>
        /// Server/client constraints: server-only.
        /// </summary>
        /// <param name="controller">The newly spawned controller NetworkObject, or null to clear the tracked controller.</param>
        [Server]
        private void SetCurrentController(NetworkObject controller)
        {
            UnsubscribeFromCurrentHealth();
            _currentController = controller;

            if (_currentController == null)
                return;

            _currentControllerHealth = _currentController.GetComponentInChildren<NetworkHealth>();
            if (_currentControllerHealth != null)
            {
                _currentControllerHealth.AddServerDeathObserver(this);
            }
            else
            {
                Debug.LogError(
                    $"Spawned controller '{_currentController.name}' has no {nameof(NetworkHealth)}; respawn will not trigger.",
                    _currentController.gameObject);
            }
        }

        /// <summary>
        /// Removes the server death observer subscription from the currently tracked controller (if any).<br>
        /// Typical usage: called before swapping controllers to avoid duplicate subscriptions and leaks.<br>
        /// Server/client constraints: server-only.
        /// </summary>
        [Server]
        private void UnsubscribeFromCurrentHealth()
        {
            if (_currentControllerHealth != null)
                _currentControllerHealth.RemoveServerDeathObserver(this);
            _currentControllerHealth = null;
        }

        /// <inheritdoc />
        [Server]
        public void OnNetworkHealthServerDied(NetworkHealth health, DamageInfo damageInfo)
        {
            Debug.Log($"[{nameof(NetworkPlayerControllerLifecycle)}] Observed death of controller '{health.gameObject.name}' for player '{gameObject.name}'", gameObject);
            if (_respawnInProgress)
                return;

            _stats.OnPlayerDeath();

            if (_networkPlayer.Team == Team.Survivor && !_stats.CanRespawn())
                _stats.ConvertToGhost();

            if (_networkPlayer.Team == Team.Survivor && !_stats.CanRespawn())
                return;

            ServerRequestRespawn();
        }

        /// <summary>
        /// Coroutine which waits for <c>respawnDelaySeconds</c> and then spawns a new controller.<br>
        /// Typical usage: started by <see cref="ServerRequestRespawn"/> when a delay is configured.<br>
        /// Server/client constraints: runs on the server.
        /// </summary>
        /// <returns>IEnumerator used by Unity coroutines.</returns>
        [Server]
        private IEnumerator RespawnAfterDelayCoroutine()
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
            PerformRespawn();
        }

        /// <summary>
        /// Completes a respawn by clearing the in-progress flag and spawning a controller for the current team.<br>
        /// Typical usage: called after any respawn delay elapses (or immediately when delay is zero).<br>
        /// Server/client constraints: server-only.
        /// </summary>
        [Server]
        private void PerformRespawn()
        {
            _respawnInProgress = false;
            ServerSpawnControllerForCurrentTeam();
        }

        /// <summary>
        /// Gets the controller prefab to spawn for the specified team.<br>
        /// Typical usage: survivors spawn the survivor controller; ghosts spawn the ghost controller.<br>
        /// Configuration/context: prefabs are assigned in the Inspector on the NetworkPlayer prefab.<br>
        /// Server/client constraints: selection can be called anywhere, but spawning happens server-side.
        /// </summary>
        /// <param name="team">The team to select the controller prefab for.</param>
        /// <returns>The controller prefab NetworkObject to instantiate.</returns>
        private NetworkObject GetControllerPrefabForTeam(Team team)
        {
            return team == Team.Ghost ? ghostControllerPrefab : survivorControllerPrefab;
        }

        /// <summary>
        /// Resolves a spawn point for the specified team via the scene's <see cref="RoachRacePlayerSpawner"/>.<br>
        /// Typical usage: used during server spawning to select the next spawn transform for the team.<br>
        /// Configuration/context: requires a <see cref="RoachRacePlayerSpawner"/> in the active scene with spawn arrays populated.<br>
        /// Server/client constraints: intended for server use.
        /// </summary>
        /// <param name="team">The team to resolve a spawn point for.</param>
        /// <returns>The selected spawn transform, or null if no spawner exists.</returns>
        private Transform FindSpawnPointForTeam(Team team)
        {
            var spawner = FindFirstObjectByType<RoachRacePlayerSpawner>();
            if (spawner == null)
                return null;

            return spawner.GetSpawnPoint(team);
        }
    }
}
