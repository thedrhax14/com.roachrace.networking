using System;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RoachRace.Networking.SpiderParts
{
    [DisallowMultipleComponent]
    public sealed class NetworkSpiderPartsController : NetworkBehaviour
    {
        [Serializable]
        private struct SpiderPartEntry
        {
            [Tooltip("Optional display name for debugging.")]
            public string partName;

            [Tooltip("Networked prefab to spawn when this part is detached. Must have a NetworkObject + NetworkSpiderPart.")]
            public NetworkObject detachedPrefab;

            [Tooltip("Where this part returns to when recalled. Server-only reference. It also used to find Renderers to hide/show. This does NOT disable scripts.")]
            public Transform returnAnchor;

            [Tooltip("Optional offset applied when magnetizing (in surface-local space, rotated by magnet rotation).")]
            public Vector3 magnetOffset;
        }

        [Header("Parts")]
        [SerializeField] private SpiderPartEntry[] parts;

        [Header("Magnetize")]
        [SerializeField, Min(0.01f)] private float magnetSeparationRadius = 0.25f;

        [Header("Collisions")]
        [Tooltip("If true, disables collisions between detached parts ONLY while returning (recall) to guarantee they can reach anchors without bouncing off each other.")]
        [SerializeField] private bool ignorePartToPartCollisionsWhileReturning = true;

        [Tooltip("Optional. Colliders on the spider/controller to ignore collisions with while parts are detached.")]
        [SerializeField] private Collider[] ignoreAgainstColliders;

        [Header("Sync")]
        [Tooltip("Bitmask of detached parts (bit i => parts[i] is detached).")]
        public readonly SyncVar<int> DetachedMask = new(0);

        [Tooltip("True while a recall is in progress (parts are Returning).")]
        public readonly SyncVar<bool> RecallInProgress = new(false);

        private NetworkSpiderPart[] _activeParts;
        private Renderer[][] _cachedRenderers;

        private void Awake()
        {
            CacheRenderers();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DetachedMask.OnChange += OnDetachedMaskChanged;
            ApplyDetachedMaskVisuals(DetachedMask.Value);
        }

        public override void OnStopClient()
        {
            DetachedMask.OnChange -= OnDetachedMaskChanged;
            base.OnStopClient();
        }

        private void OnDetachedMaskChanged(int prev, int next, bool asServer)
        {
            ApplyDetachedMaskVisuals(next);
        }

        private void CacheRenderers()
        {
            if (parts == null)
            {
                _cachedRenderers = Array.Empty<Renderer[]>();
                return;
            }

            _cachedRenderers = new Renderer[parts.Length][];
            for (int i = 0; i < parts.Length; i++)
            {
                var root = parts[i].returnAnchor;
                if (root == null)
                {
                    _cachedRenderers[i] = Array.Empty<Renderer>();
                    continue;
                }

                _cachedRenderers[i] = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            }
        }

        private void ApplyDetachedMaskVisuals(int mask)
        {
            if (_cachedRenderers == null || _cachedRenderers.Length == 0)
                return;

            int count = Mathf.Min(_cachedRenderers.Length, parts != null ? parts.Length : 0);
            for (int i = 0; i < count; i++)
            {
                bool detached = (mask & (1 << i)) != 0;
                var renderers = _cachedRenderers[i];
                if (renderers == null) continue;

                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null)
                        renderers[r].enabled = !detached;
                }
            }
        }

        [Server]
        public bool HasAnyDetached() => DetachedMask.Value != 0;

        [Server]
        public bool IsRecallInProgressServer() => RecallInProgress.Value;

        [Server]
        public void ServerDisassemble(Vector3 magnetPoint, Quaternion magnetRotation)
        {
            if (parts == null || parts.Length == 0)
            {
                Debug.LogWarning($"[{nameof(NetworkSpiderPartsController)}] No parts configured on '{gameObject.name}'.", gameObject);
                return;
            }

            _activeParts ??= new NetworkSpiderPart[parts.Length];

            // Disassemble cancels any recall lock.
            RecallInProgress.Value = false;
            if (ignorePartToPartCollisionsWhileReturning)
                SetPartToPartCollisionIgnoredServer(ignore: false);

            for (int i = 0; i < parts.Length; i++)
            {
                ref SpiderPartEntry entry = ref parts[i];

                if (entry.detachedPrefab == null)
                {
                    Debug.LogError($"[{nameof(NetworkSpiderPartsController)}] Part {i} has no detachedPrefab assigned on '{gameObject.name}'.", gameObject);
                    continue;
                }

                if (entry.returnAnchor == null)
                {
                    Debug.LogError($"[{nameof(NetworkSpiderPartsController)}] Part {i} has no returnAnchor assigned on '{gameObject.name}'.", gameObject);
                    continue;
                }

                if (_activeParts[i] == null)
                {
                    NetworkObject nob = Instantiate(entry.detachedPrefab, entry.returnAnchor.position, entry.returnAnchor.rotation);
                    InstanceFinder.ServerManager.Spawn(nob);

                    if (!nob.TryGetComponent(out NetworkSpiderPart part))
                    {
                        Debug.LogError($"[{nameof(NetworkSpiderPartsController)}] Spawned detached prefab '{nob.name}' but it has no {nameof(NetworkSpiderPart)} component.", nob.gameObject);
                        InstanceFinder.ServerManager.Despawn(nob);
                        Destroy(nob.gameObject);
                        continue;
                    }

                    _activeParts[i] = part;

                    int idx = i;
                    part.Returned += p => OnPartReturnedServer(idx, p);

                    DetachedMask.Value |= (1 << i);

                    // Make detached physics stable.
                    ApplyIgnoreAgainstSpiderForNewPartServer(i);
                }

                Vector3 offset = entry.magnetOffset;
                if (offset == Vector3.zero && parts.Length > 1)
                {
                    // Default: spread parts in a circle around magnet point.
                    float t = (parts.Length <= 1) ? 0f : (float)i / parts.Length;
                    float angle = t * Mathf.PI * 2f;
                    offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * magnetSeparationRadius;
                }

                Vector3 targetPos = magnetPoint + (magnetRotation * offset);
                _activeParts[i].SetMagneticTarget(targetPos, magnetRotation);
            }
        }

        [Server]
        public void ServerRecall()
        {
            if (parts == null || parts.Length == 0)
                return;

            _activeParts ??= new NetworkSpiderPart[parts.Length];

            if (DetachedMask.Value == 0)
            {
                RecallInProgress.Value = false;
                if (ignorePartToPartCollisionsWhileReturning)
                    SetPartToPartCollisionIgnoredServer(ignore: false);
                return;
            }

            RecallInProgress.Value = true;

            if (ignorePartToPartCollisionsWhileReturning)
                SetPartToPartCollisionIgnoredServer(ignore: true);

            for (int i = 0; i < parts.Length; i++)
            {
                NetworkSpiderPart part = _activeParts[i];
                if (part == null) continue;

                Transform anchor = parts[i].returnAnchor;
                if (anchor == null)
                {
                    Debug.LogError($"[{nameof(NetworkSpiderPartsController)}] Part {i} returnAnchor is null on '{gameObject.name}'.", gameObject);
                    continue;
                }

                // Disable collisions while returning to guarantee the part reaches its anchor.
                part.SetReturnTarget(anchor.position, anchor.rotation);
            }
        }

        [Server]
        private void ApplyIgnoreAgainstSpiderForNewPartServer(int newIndex)
        {
            if (!IsServerInitialized) return;
            if (_activeParts == null) return;
            if (newIndex < 0 || newIndex >= _activeParts.Length) return;

            NetworkSpiderPart newPart = _activeParts[newIndex];
            if (newPart == null) return;

            Collider[] newCols = newPart.GetCollidersServer();
            if (newCols == null || newCols.Length == 0) return;

            // Ignore against spider/controller colliders if provided.
            if (ignoreAgainstColliders != null && ignoreAgainstColliders.Length > 0)
            {
                IgnoreCollisionPairs(newCols, ignoreAgainstColliders, ignore: true);
            }
        }

        [Server]
        private void SetPartToPartCollisionIgnoredServer(bool ignore)
        {
            if (!IsServerInitialized) return;
            if (_activeParts == null) return;

            for (int i = 0; i < _activeParts.Length; i++)
            {
                NetworkSpiderPart a = _activeParts[i];
                if (a == null) continue;
                Collider[] aCols = a.GetCollidersServer();
                if (aCols == null || aCols.Length == 0) continue;

                for (int j = i + 1; j < _activeParts.Length; j++)
                {
                    NetworkSpiderPart b = _activeParts[j];
                    if (b == null) continue;
                    Collider[] bCols = b.GetCollidersServer();
                    if (bCols == null || bCols.Length == 0) continue;

                    IgnoreCollisionPairs(aCols, bCols, ignore);
                }
            }
        }

        [Server]
        private static void IgnoreCollisionPairs(Collider[] a, Collider[] b, bool ignore)
        {
            if (a == null || b == null) return;
            for (int i = 0; i < a.Length; i++)
            {
                Collider ca = a[i];
                if (ca == null) continue;
                for (int j = 0; j < b.Length; j++)
                {
                    Collider cb = b[j];
                    if (cb == null) continue;
                    Physics.IgnoreCollision(ca, cb, ignore);
                }
            }
        }

        [Server]
        private void OnPartReturnedServer(int partIndex, NetworkSpiderPart part)
        {
            if (!IsServerInitialized) return;

            if (partIndex < 0 || parts == null || partIndex >= parts.Length)
                return;

            // Clear state first, then despawn.
            DetachedMask.Value &= ~(1 << partIndex);
            if (_activeParts != null && partIndex < _activeParts.Length)
                _activeParts[partIndex] = null;

            if (part != null && part.NetworkObject != null)
                InstanceFinder.ServerManager.Despawn(part.NetworkObject);

            if (part != null)
                Destroy(part.gameObject);

            // If all parts returned, clear recall flag and restore collisions.
            if (DetachedMask.Value == 0)
            {
                RecallInProgress.Value = false;
                if (ignorePartToPartCollisionsWhileReturning)
                    SetPartToPartCollisionIgnoredServer(ignore: false);
            }
        }
    }
}
