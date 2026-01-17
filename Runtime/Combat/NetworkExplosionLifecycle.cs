using System.Collections;
using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Handles visuals and lifetime for spawn-and-forget explosion objects.
    /// - Server disables visuals.
    /// - Clients enable visuals.
    /// - Server despawns after a delay.
    /// </summary>
    public class NetworkExplosionLifecycle : NetworkBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Enabled on clients, disabled on server.")]
        [SerializeField] private GameObject visualRoot;

        [Header("Lifetime")]
        [Tooltip("How long the explosion object stays alive before despawning (seconds).")]
        [SerializeField] private float despawnAfterSeconds = 2f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            SetVisualsActive(false);
            StartCoroutine(DespawnAfterDelay());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetVisualsActive(true);
        }

        private void SetVisualsActive(bool active)
        {
            if (visualRoot == null) return;
            visualRoot.SetActive(active);
        }

        private IEnumerator DespawnAfterDelay()
        {
            if (!IsServerInitialized) yield break;

            float seconds = Mathf.Max(0f, despawnAfterSeconds);
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);

            if (IsServerInitialized && NetworkObject != null && NetworkObject.IsSpawned)
                ServerManager.Despawn(NetworkObject);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (despawnAfterSeconds < 0f) despawnAfterSeconds = 0f;
        }
#endif
    }
}
