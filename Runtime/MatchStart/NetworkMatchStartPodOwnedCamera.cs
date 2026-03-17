using System;
using FishNet.Connection;
using FishNet.Object;
using Unity.Cinemachine;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Enables a match-start spawn pod POV <see cref="CinemachineCamera"/> only for the owning client.<br></br>
    /// Purpose: when a player's pod animates (e.g., Survivor wake-up), the owning player should see through the pod camera,
    /// while non-owners should not have that camera active.<br></br>
    /// Typical usage:<br></br>
    /// - Attach this component to the spawn pod prefab (NetworkObject).<br></br>
    /// - Assign the child <see cref="CinemachineCamera"/> used for the pod POV.<br></br>
    /// - Configure look input + view limits using Cinemachine components on the camera (inspector-driven).
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Match Start Pod Owned Camera")]
    public sealed class NetworkMatchStartPodOwnedCamera : NetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private CinemachineCamera virtualCamera;

        private void Awake()
        {
            if (virtualCamera == null)
            {
                Debug.LogError($"[{nameof(NetworkMatchStartPodOwnedCamera)}] Missing required reference on '{gameObject.name}': virtualCamera", gameObject);
                throw new InvalidOperationException($"[{nameof(NetworkMatchStartPodOwnedCamera)}] Missing required reference on '{gameObject.name}': virtualCamera");
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyOwnershipState();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            ApplyOwnershipState();
        }

        private void OnDisable()
        {
            // Ensure the camera doesn't remain active if the component is disabled.
            if (virtualCamera != null)
            {
                virtualCamera.enabled = false;
            }
        }

        private void ApplyOwnershipState()
        {
            if (!IsClientInitialized)
                return;

            bool owner = IsOwner;

            virtualCamera.enabled = owner;
            if (owner)
            {
                // Ensure this pod camera is used immediately.
                virtualCamera.Prioritize();

                Debug.Log($"[{nameof(NetworkMatchStartPodOwnedCamera)}] Enabled pod camera for owner on '{gameObject.name}'", gameObject);
            }
            else
            {
                Debug.Log($"[{nameof(NetworkMatchStartPodOwnedCamera)}] Disabled pod camera for non-owner on '{gameObject.name}'", gameObject);
            }
        }
    }
}
