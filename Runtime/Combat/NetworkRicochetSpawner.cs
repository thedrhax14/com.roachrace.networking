using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Client-side ricochet raycast visualizer.
    /// Intended to be driven by local animation events on every client instance (owner and observers)
    /// so no per-shot network messages are required.
    /// Spawns an optional local prefab at each hit point, aligns spawned object up-vector to hit normal,
    /// and visualizes ricochet traces using a LineRenderer prefab.
    /// </summary>
    public sealed class NetworkRicochetSpawner : MonoBehaviour
    {
        [Header("Raycast")]
        [SerializeField] private Transform rayOrigin;
        [SerializeField] private Vector3 localRayDirection = Vector3.forward;
        [SerializeField] private float segmentDistance = 25f;
        [SerializeField] private int ricochetCount = 3;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [SerializeField] private float surfaceSpawnOffset = 0.02f;

        [Header("Spawn")]
        [SerializeField] private GameObject spawnPrefab;

        [Header("Trace Visualization")]
        [SerializeField] private LineRenderer traceLinePrefab;
        [SerializeField] private float traceDestroyAfterSeconds = 1.5f;
        [SerializeField] private Transform traceStartPoint;

        [Header("Dependencies")]
        [SerializeField] private ClientAuthoritativeHumanMotor _motor;

        public Transform RayOrigin => rayOrigin != null ? rayOrigin : transform;
        public Vector3 LocalRayDirection => localRayDirection;
        public float SegmentDistance => segmentDistance;
        public int RicochetCount => ricochetCount;
        public LayerMask HitMask => hitMask;
        public QueryTriggerInteraction TriggerInteraction => triggerInteraction;

        public void CastAndSpawn()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            return;
#else
            Vector3 origin = RayOrigin.position;
            Vector3 direction = _motor.AimRotation * localRayDirection;

            ValidateCriticalDependencies();

            List<Vector3> rayOrigins = new(ricochetCount + 1);
            List<RaycastHit> hits = new(ricochetCount + 1);
            BuildRicochetPath(origin, direction, rayOrigins, hits);

            SpawnLocalHitPrefabs(hits, direction);

            Vector3[] tracePoints = BuildTracePoints(hits, origin);
            if (!AreValidTracePoints(tracePoints)) return;

            SpawnTraceLine(tracePoints);
#endif
        }

        private void SpawnLocalHitPrefabs(List<RaycastHit> hits, Vector3 initialDirection)
        {
            if (spawnPrefab == null || hits == null || hits.Count == 0)
                return;

            Vector3 incomingDirection = NormalizeDirection(initialDirection);
            for (int i = 0; i < hits.Count; i++)
            {
                RaycastHit hit = hits[i];
                Vector3 spawnPosition = hit.point + hit.normal * surfaceSpawnOffset;

                Vector3 forwardOnSurface = Vector3.ProjectOnPlane(incomingDirection, hit.normal);
                Quaternion spawnRotation = forwardOnSurface.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(forwardOnSurface.normalized, hit.normal)
                    : Quaternion.FromToRotation(Vector3.up, hit.normal);

                Instantiate(spawnPrefab, spawnPosition, spawnRotation);
                incomingDirection = Vector3.Reflect(incomingDirection, hit.normal).normalized;
            }
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

        private void SpawnTraceLine(Vector3[] tracePoints)
        {
            if (!AreValidTracePoints(tracePoints) || traceLinePrefab == null)
                return;

            LineRenderer lineRenderer = Instantiate(traceLinePrefab);
            lineRenderer.positionCount = tracePoints.Length;
            lineRenderer.SetPositions(tracePoints);

            if (traceDestroyAfterSeconds > 0f)
                Destroy(lineRenderer.gameObject, traceDestroyAfterSeconds);
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
            if (traceLinePrefab == null)
            {
                Debug.LogError($"[{nameof(NetworkRicochetSpawner)}] traceLinePrefab is not assigned! Please assign it in the Inspector.", gameObject);
                throw new System.NullReferenceException($"[{nameof(NetworkRicochetSpawner)}] traceLinePrefab is null on GameObject '{gameObject.name}'. This component requires a LineRenderer prefab to visualize traces.");
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
            traceDestroyAfterSeconds = Mathf.Max(0f, traceDestroyAfterSeconds);
        }
#endif
    }
}