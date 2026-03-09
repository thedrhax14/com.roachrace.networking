using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Utility.Template;
using UnityEngine;

namespace RoachRace.Networking.Input
{
    /// <summary>
    /// Replicates the owning client's current camera/look origin and direction so other systems
    /// can read where the player is looking without sending aim vectors on every gameplay RPC.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPlayerLookState : TickNetworkBehaviour
    {
        [Header("Source")]
        [Tooltip("Optional explicit transform to use as the local aim source. Falls back to Camera.main, then this transform.")]
        [SerializeField] private Transform aimSource;

        private readonly SyncVar<Vector3> _lookOrigin = new(Vector3.zero);
        private readonly SyncVar<Vector3> _lookDirection = new(Vector3.forward);

        private Vector3 _localOrigin;
        private Vector3 _localDirection = Vector3.forward;

        public Vector3 LookOrigin => IsOwner ? _localOrigin : _lookOrigin.Value;
        public Vector3 LookDirection => IsOwner ? _localDirection : _lookDirection.Value;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick);
        }

        protected override void TimeManager_OnTick()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            Transform source = ResolveAimSource();
            Vector3 origin = source.position;
            Vector3 direction = source.forward;
            if (direction.sqrMagnitude <= 0.0001f)
                direction = transform.forward;
            direction.Normalize();

            _localOrigin = origin;
            _localDirection = direction;

            if (IsServerInitialized)
            {
                _lookOrigin.Value = origin;
                _lookDirection.Value = direction;
                return;
            }

            SubmitLookServerRpc(origin, direction);
        }

        /// <summary>
        /// Gets the best currently available look origin and direction for gameplay systems that need aim data.
        /// On pure clients, the returned <paramref name="origin"/> comes from the current source transform position so
        /// local interactions use the latest camera or aim-source position without waiting for replication. On the
        /// server, the returned <paramref name="origin"/> comes from the replicated SyncVar so authoritative gameplay
        /// uses the last network-approved look origin submitted by the owning client. Direction can prefer the owner's
        /// latest local value or the replicated value depending on <paramref name="preferLocal"/>.
        /// </summary>
        /// <param name="origin">
        /// Receives the look origin. This is the live source-transform position on a pure client and the replicated
        /// SyncVar position on the server.
        /// </param>
        /// <param name="direction">
        /// Receives the look direction. This is the owner's locally cached direction when <paramref name="preferLocal"/>
        /// is true and this object is owned locally; otherwise it is the replicated direction.
        /// </param>
        /// <param name="preferLocal">
        /// When true, the owning client prefers its locally cached direction instead of the replicated direction. This
        /// only affects direction selection; origin still follows the client/server split described above.
        /// </param>
        /// <returns>
        /// <c>true</c> when a valid direction could be resolved. <c>false</c> when no usable direction is available,
        /// in which case both <paramref name="origin"/> and <paramref name="direction"/> are returned as default.
        /// </returns>
        public bool TryGetLook(out Vector3 origin, out Vector3 direction, bool preferLocal = true)
        {
            if (IsClientInitialized && !IsServerInitialized)
            {
                Transform source = IsOwner ? ResolveAimSource() : (aimSource != null ? aimSource : transform);
                origin = source.position;
            }
            else
            {
                origin = _lookOrigin.Value;
            }

            if (preferLocal && IsOwner)
            {
                direction = _localDirection;
            }
            else
            {
                direction = _lookDirection.Value;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = transform.forward;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    origin = default;
                    direction = default;
                    return false;
                }
            }

            direction.Normalize();
            return true;
        }

        private Transform ResolveAimSource()
        {
            if (aimSource != null)
                return aimSource;

            if (Camera.main != null)
                return Camera.main.transform;

            return transform;
        }

        [ServerRpc(RequireOwnership = true)]
        private void SubmitLookServerRpc(Vector3 origin, Vector3 direction, NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            if (direction.sqrMagnitude <= 0.0001f)
                direction = transform.forward;

            _lookOrigin.Value = origin;
            _lookDirection.Value = direction.normalized;
        }
    }
}