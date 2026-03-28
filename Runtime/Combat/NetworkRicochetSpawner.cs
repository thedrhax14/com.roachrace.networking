using System.Collections.Generic;
using RoachRace.Networking.Input;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Client-side ricochet raycast visualizer.
    /// Intended to be driven by local animation events on every client instance (owner and observers)
    /// so no per-shot network messages are required.
    /// Spawns an optional local prefab at each hit point, aligns spawned object up-vector to hit normal,
    /// and visualizes ricochet traces using a LineRenderer prefab.<br>
    /// The optional bullet visual is delegated to a prefab-attached follower component that consumes the
    /// generated trace points.
    /// </summary>
    public sealed class NetworkRicochetSpawner : MonoBehaviour
    {
        [Header("Raycast")]
        [SerializeField] private float segmentDistance = 25f;
        [SerializeField] private int ricochetCount = 3;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private float surfaceSpawnOffset = 0.02f;

        [Header("Spawn")]
        [SerializeField] private RicochetBulletVisual bulletPrefab;

        [Header("Trace Visualization")]
        [SerializeField] private Transform traceStartPoint;

        [Header("Dependencies")]
        [SerializeField] private NetworkPlayerLookState lookState;

        public float SegmentDistance => segmentDistance;
        public int RicochetCount => ricochetCount;
        public LayerMask HitMask => hitMask;
        public QueryTriggerInteraction TriggerInteraction => triggerInteraction;

        /// <summary>
        /// Casts the ricochet ray path, spawns local impact prefabs, creates the trace line, and hands
        /// the computed trace to the bullet visual prefab component.<br>
        /// Intended for local client-side animation events only.
        /// </summary>
        public void CastAndSpawn()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            return;
#else
            ValidateCriticalDependencies();

            if (!lookState.TryGetLook(out Vector3 origin, out Vector3 direction))
            {
                Debug.LogError($"[{nameof(NetworkRicochetSpawner)}] Failed to resolve shared aim data from {nameof(NetworkPlayerLookState)} on '{gameObject.name}'.", gameObject);
                throw new System.InvalidOperationException($"[{nameof(NetworkRicochetSpawner)}] Could not resolve look origin/direction from {nameof(NetworkPlayerLookState)} on GameObject '{gameObject.name}'.");
            }

            List<Vector3> rayOrigins = new(ricochetCount + 1);
            List<RaycastHit> hits = new(ricochetCount + 1);
            BuildRicochetPath(origin, direction, rayOrigins, hits);

            Vector3[] tracePoints = BuildTracePoints(hits, origin);
            if (!AreValidTracePoints(tracePoints)) return;
                SpawnBulletVisual(tracePoints, hits);
#endif
        }

        /// <summary>
        /// Spawns the configured bullet prefab at the trace start and hands the computed path to its
        /// visual follower component.<br>
        /// The prefab must include <see cref="RicochetBulletVisual"/>.
        /// </summary>
        /// <param name="tracePoints">Polyline points the bullet should follow. Must contain at least 2 points.</param>
        /// <param name="hits">Raycast hits corresponding to the trace points. Expected count is <c>tracePoints.Length - 1</c>.</param>
        private void SpawnBulletVisual(Vector3[] tracePoints, List<RaycastHit> hits)
        {
            if (bulletPrefab == null)
                return;

            if (!AreValidTracePoints(tracePoints))
                return;

            Quaternion startRotation = Quaternion.identity;
            Vector3 firstSegment = tracePoints[1] - tracePoints[0];
            if (firstSegment.sqrMagnitude > 0.0001f)
                startRotation = Quaternion.LookRotation(firstSegment.normalized, Vector3.up);

            RaycastHit[] hitArray = System.Array.Empty<RaycastHit>();
            if (hits != null && hits.Count > 0)
            {
                hitArray = new RaycastHit[hits.Count];
                hits.CopyTo(hitArray, 0);
            }

            Instantiate(bulletPrefab, tracePoints[0], startRotation).Play(tracePoints, hitArray);
        }

        private void BuildRicochetPath(Vector3 origin, Vector3 direction, List<Vector3> rayOrigins, List<RaycastHit> hits)
        {
            Vector3 currentOrigin = origin;
            Vector3 currentDirection = NormalizeDirection(direction);

            int segmentCount = ricochetCount + 1;
            for (int i = 0; i < segmentCount; i++)
            {
                rayOrigins.Add(currentOrigin);

                if (!Physics.Raycast(currentOrigin, currentDirection, out RaycastHit hit, i == 0 ? 1000 : segmentDistance, hitMask, triggerInteraction))
                    break;

                hits.Add(hit);
                currentDirection = Vector3.Reflect(currentDirection, hit.normal).normalized;
                currentOrigin = hit.point + currentDirection * surfaceSpawnOffset;
            }
        }

        private Vector3[] BuildTracePoints(List<RaycastHit> hits, Vector3 origin)
        {
            if (hits == null || hits.Count == 0)
                return System.Array.Empty<Vector3>();

            Vector3[] points = new Vector3[hits.Count + 1];
            points[0] = traceStartPoint != null ? traceStartPoint.position : origin;

            for (int i = 0; i < hits.Count; i++)
                points[i + 1] = hits[i].point;

            return points;
        }

        private static bool AreValidTracePoints(Vector3[] tracePoints)
        {
            return tracePoints != null && tracePoints.Length >= 2;
        }

        private static Vector3 NormalizeDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return Vector3.forward;

            return direction.normalized;
        }

        private void ValidateCriticalDependencies()
        {
            if (lookState == null)
            {
                lookState = GetComponentInParent<NetworkPlayerLookState>();
            }

            if (lookState == null)
            {
                Debug.LogError($"[{nameof(NetworkRicochetSpawner)}] lookState is not assigned! Please assign it in the Inspector or place {nameof(NetworkPlayerLookState)} on a parent object.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkRicochetSpawner)}] lookState is null on GameObject '{gameObject.name}'. This component requires access to {nameof(NetworkPlayerLookState)} for shared aim data.");
            }

            if (traceStartPoint == null)
            {
                Debug.LogError($"[{nameof(NetworkRicochetSpawner)}] traceStartPoint is not assigned! Please assign it in the Inspector.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkRicochetSpawner)}] traceStartPoint is null on GameObject '{gameObject.name}'. This component requires a custom transform for the first trace point.");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            segmentDistance = Mathf.Max(0.01f, segmentDistance);
            ricochetCount = Mathf.Max(0, ricochetCount);
            surfaceSpawnOffset = Mathf.Max(0f, surfaceSpawnOffset);
        }
#endif
    }
}