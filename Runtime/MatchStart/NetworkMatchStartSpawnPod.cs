using System;
using System.Collections.Generic;
using FishNet.Object;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Networked match start "spawn pod" behavior.<br></br>
    /// Purpose: provide timing (animation duration) for server-driven swapping into controllable controllers, and (for ghosts)
    /// optionally spawn a non-networked ragdoll locally on each client at swap time.<br></br>
    /// Typical usage:<br></br>
    /// - Attach to the Survivor/Ghost pod prefabs assigned in <see cref="NetworkMatchStartPodSpawner"/>.<br></br>
    /// - Configure <see cref="SwapDelaySeconds"/> to match the animation length.<br></br>
    /// - For ghost pods, assign <see cref="ghostRagdollPrefab"/> and (optionally) a <see cref="poseSourceRoot"/> to copy a pose into ragdoll bones.<br></br>
    /// Notes:<br></br>
    /// - Ragdoll is explicitly NOT networked; it is instantiated locally on each client via an Observers RPC.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Match Start Spawn Pod")]
    public sealed class NetworkMatchStartSpawnPod : NetworkBehaviour
    {
        [Header("Pod Config")]
        [SerializeField] private Team team = Team.Survivor;

        [Tooltip("Seconds after spawn before the server should swap this pod into the real controller.")]
        [SerializeField] private float swapDelaySeconds = 2f;

        [Header("Ghost Ragdoll (Client-only)")]
        [Tooltip("Non-networked ragdoll prefab to instantiate locally when this pod swaps to controller. Only used for Ghost pods.")]
        [SerializeField] private GameObject ghostRagdollPrefab;

        [Tooltip("Optional root transform under this pod used as the pose source for ragdoll pose matching. If null, uses this.transform.")]
        [SerializeField] private Transform poseSourceRoot;

        /// <summary>
        /// The team this pod represents (configured per prefab).
        /// </summary>
        public Team Team => team;

        /// <summary>
        /// Delay after spawn before swapping to controller.
        /// </summary>
        public float SwapDelaySeconds => Mathf.Max(0f, swapDelaySeconds);

        /// <summary>
        /// Instructs all observing clients to spawn a non-networked ghost ragdoll at the pod pose.<br></br>
        /// Typical usage: called by the server immediately before despawning the pod and spawning the ghost controller.<br></br>
        /// Server/client constraints: server calls; clients execute instantiate locally.
        /// </summary>
        [ObserversRpc]
        public void ObserversSpawnGhostRagdoll()
        {
            if (team != Team.Ghost)
                return;

            if (ghostRagdollPrefab == null)
                return;

            Transform sourceRoot = poseSourceRoot != null ? poseSourceRoot : transform;

            GameObject ragdoll = Instantiate(ghostRagdollPrefab, transform.position, transform.rotation);
            ragdoll.name = $"GhostRagdoll_{gameObject.name}";

            try
            {
                CopyPoseByName(sourceRoot, ragdoll.transform);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(NetworkMatchStartSpawnPod)}] Failed to copy pose into ghost ragdoll on '{gameObject.name}': {ex.Message}",
                    gameObject);
            }
        }

        private static void CopyPoseByName(Transform sourceRoot, Transform targetRoot)
        {
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            if (targetRoot == null) throw new ArgumentNullException(nameof(targetRoot));

            Dictionary<string, Transform> sourceByName = new();
            CollectTransformsByName(sourceRoot, sourceByName);

            // Copy locals for any matching names.
            var stack = new Stack<Transform>();
            stack.Push(targetRoot);

            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));

                if (!sourceByName.TryGetValue(t.name, out Transform src))
                    continue;

                t.localPosition = src.localPosition;
                t.localRotation = src.localRotation;
                t.localScale = src.localScale;
            }
        }

        private static void CollectTransformsByName(Transform root, Dictionary<string, Transform> byName)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));

                // If duplicates exist, first wins; keep deterministic.
                if (!byName.ContainsKey(t.name))
                    byName.Add(t.name, t);
            }
        }
    }
}
