using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Prefab-attached visual follower for ricochet bullet traces.<br>
    /// Attach this component to the bullet prefab, then call <see cref="Play(Vector3[])"/> with the
    /// trace points produced by <see cref="NetworkRicochetSpawner"/>.<br>
    /// The component is local-only and moves the visual object along the supplied polyline without
    /// affecting gameplay state.
    /// </summary>
    public sealed class RicochetBulletVisual : MonoBehaviour
    {
        [SerializeField] private float defaultTravelSpeed = 60f;
        [SerializeField] private Light lightSource;
        [SerializeField] private float destroyAfterLightSeconds = 1f;

        private Vector3[] path;
        private int segmentIndex;
        private float traveledDistance;
        private float segmentLength;
        private float selfDestructTimer = -1f;
        private bool hasReachedEnd;

        /// <summary>
        /// Starts following the given trace path.
        /// </summary>
        /// <param name="path">World-space polyline to follow. Must contain at least two points.</param>
        public void Play(Vector3[] path)
        {
            if (path == null || path.Length < 2)
                return;

            this.path = path;
            transform.position = path[0];
            SetInitialRotation(path);
            ConfigurePhysicsForVisualOnly();
            segmentIndex = 0;
            traveledDistance = 0f;
            segmentLength = GetSegmentLength(path, segmentIndex);
            selfDestructTimer = -1f;
            hasReachedEnd = false;
        }

        /// <summary>
        /// Advances the visual bullet along the configured path each frame and handles delayed cleanup.
        /// </summary>
        private void Update()
        {
            if (path == null || path.Length < 2)
                return;

            if (hasReachedEnd)
            {
                UpdatePostImpactDestruction();
                return;
            }

            UpdatePathMovement();
        }

        /// <summary>
        /// Stops any pending cleanup when the component is disabled.
        /// </summary>
        private void OnDisable()
        {
            path = null;
            selfDestructTimer = -1f;
            hasReachedEnd = false;
        }

        /// <summary>
        /// Sets the bullet orientation to match the first path segment when possible.
        /// </summary>
        /// <param name="path">Polyline used to determine the initial facing direction.</param>
        private void SetInitialRotation(Vector3[] path)
        {
            Vector3 firstSegment = path[1] - path[0];
            if (firstSegment.sqrMagnitude <= 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(firstSegment.normalized, Vector3.up);
        }

        /// <summary>
        /// Forces a Rigidbody on the prefab into a visual-only state so the scripted motion stays in control.
        /// </summary>
        private void ConfigurePhysicsForVisualOnly()
        {
            if (!TryGetComponent(out Rigidbody rigidbody))
                return;

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.detectCollisions = false;
        }

        /// <summary>
        /// Moves the bullet along the configured path at a constant speed.
        /// </summary>
        private void UpdatePathMovement()
        {
            float travelSpeed = defaultTravelSpeed;
            traveledDistance += travelSpeed * Time.deltaTime;

            while (segmentIndex < path.Length - 1 && traveledDistance > segmentLength)
            {
                traveledDistance -= segmentLength;
                segmentIndex++;
                if (segmentIndex >= path.Length - 1)
                    break;

                segmentLength = GetSegmentLength(path, segmentIndex);
            }

            if (segmentIndex >= path.Length - 1)
            {
                transform.position = path[path.Length - 1];
                DestroyLightSource();
                hasReachedEnd = true;
                selfDestructTimer = destroyAfterLightSeconds > 0f ? destroyAfterLightSeconds : 1f;
                return;
            }

            float t = Mathf.Clamp01(traveledDistance / segmentLength);
            Vector3 segmentStart = path[segmentIndex];
            Vector3 segmentEnd = path[segmentIndex + 1];
            Vector3 newPosition = Vector3.Lerp(segmentStart, segmentEnd, t);

            Vector3 moveDirection = segmentEnd - segmentStart;
            if (moveDirection.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(moveDirection.normalized, Vector3.up);

            transform.position = newPosition;
        }

        /// <summary>
        /// Counts down to self-destruction after the light source has been removed.
        /// </summary>
        private void UpdatePostImpactDestruction()
        {
            if (selfDestructTimer < 0f)
                selfDestructTimer = destroyAfterLightSeconds > 0f ? destroyAfterLightSeconds : 1f;

            selfDestructTimer -= Time.deltaTime;
            if (selfDestructTimer > 0f)
                return;

            Destroy(gameObject);
        }

        /// <summary>
        /// Destroys the configured light source, or finds one on the prefab if no explicit reference exists.<br>
        /// If the light lives on the same GameObject as the bullet, only the Light component is destroyed so
        /// the bullet can remain visible for the final delay.
        /// </summary>
        private void DestroyLightSource()
        {
            Light resolvedLight = lightSource;
            if (resolvedLight == null)
                resolvedLight = GetComponentInChildren<Light>();

            if (resolvedLight == null)
                return;

            if (resolvedLight.gameObject == gameObject)
            {
                Destroy(resolvedLight);
                return;
            }

            Destroy(resolvedLight.gameObject);
        }

        /// <summary>
        /// Returns the length of the requested path segment.
        /// </summary>
        /// <param name="path">World-space polyline.</param>
        /// <param name="segmentIndex">Index of the starting point of the segment.</param>
        /// <returns>Length of the segment in world units.</returns>
        private static float GetSegmentLength(Vector3[] path, int segmentIndex)
        {
            if (segmentIndex < 0 || segmentIndex >= path.Length - 1)
                return 0.0001f;

            float segmentLength = Vector3.Distance(path[segmentIndex], path[segmentIndex + 1]);
            return segmentLength > 0.0001f ? segmentLength : 0.0001f;
        }
    }
}
