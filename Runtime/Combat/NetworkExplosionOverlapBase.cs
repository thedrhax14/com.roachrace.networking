using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Shared targeting/overlap/LoS helpers for explosion-like behaviours.
    /// Intended to keep overlap + line-of-sight logic DRY across multiple explosion components.
    /// </summary>
    public abstract class NetworkExplosionOverlapBase : NetworkBehaviour
    {
        [Header("Targeting")]
        [Tooltip("World-space radius used for overlap.")]
        [SerializeField] protected float radius = 4f;

        [Tooltip("Only colliders on these layers can be affected.")]
        [SerializeField] protected LayerMask damageLayers = ~0;

        [Tooltip("Include trigger colliders in the overlap.")]
        [SerializeField] protected QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Optionally ignore affecting the object that spawned/owns this explosion.")]
        [SerializeField] protected bool ignoreInstigator = false;

        [Header("Line of Sight")]
        [Tooltip("If enabled, targets must have line-of-sight from the explosion center to be affected.")]
        [SerializeField] protected bool requireLineOfSight = false;

        [Tooltip("Layers which can block the explosion line-of-sight check.")]
        [SerializeField] protected LayerMask lineOfSightBlockers = ~0;

        [Tooltip("Small offset used to avoid raycasts immediately hitting the origin collider.")]
        [SerializeField] protected float lineOfSightStartOffset = 0.05f;

        protected struct RigidbodyData
        {
            public Rigidbody Rigidbody;
            public float MinNormalizedDistance;
            public Collider ClosestCollider;
            public Vector3 ClosestPoint;
        }

        protected Collider[] Overlap()
        {
            if (radius <= 0f)
                return null;

            return Physics.OverlapSphere(transform.position, radius, damageLayers, triggerInteraction);
        }

        protected bool IsLineOfSightBlocked(Vector3 targetPoint, Collider targetCollider, Rigidbody targetRigidbody)
        {
            Vector3 origin = transform.position;
            Vector3 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f) return false;

            Vector3 direction = toTarget / distance;
            float startOffset = Mathf.Clamp(lineOfSightStartOffset, 0f, distance * 0.5f);

            origin += direction * startOffset;
            distance -= startOffset;

            if (distance <= 0.0001f) return false;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
                return false;

            // Ignore hits on the explosion object itself.
            if (hit.collider != null && hit.collider.transform != null && hit.collider.transform.IsChildOf(transform))
                return false;

            // If the first hit is the target itself (or another collider on the same rigidbody), line-of-sight is clear.
            if (targetCollider != null)
            {
                if (hit.collider == targetCollider)
                    return false;

                if (targetRigidbody != null)
                {
                    if (hit.rigidbody == targetRigidbody)
                        return false;

                    if (hit.collider != null && hit.collider.attachedRigidbody == targetRigidbody)
                        return false;
                }

                if (hit.collider != null && hit.collider.transform != null && targetCollider.transform != null)
                {
                    if (hit.collider.transform.IsChildOf(targetCollider.transform) || targetCollider.transform.IsChildOf(hit.collider.transform))
                        return false;
                }
            }

            return true;
        }

        protected int GetInstigatorOwnerId()
        {
            if (NetworkObject == null || NetworkObject.Owner == null)
                return -1;

            return NetworkObject.Owner.ClientId;
        }

        protected static bool IsSameOwner(Object target, int instigatorOwnerId)
        {
            if (instigatorOwnerId < 0) return false;

            if (target is not Component component)
                return false;

            if (component.TryGetComponent<NetworkObject>(out var no) && no.Owner != null)
                return no.Owner.ClientId == instigatorOwnerId;

            var parentNo = component.GetComponentInParent<NetworkObject>();
            if (parentNo != null && parentNo.Owner != null)
                return parentNo.Owner.ClientId == instigatorOwnerId;

            return false;
        }

        protected void CollectRigidbodies(Collider[] hits, System.Collections.Generic.Dictionary<Rigidbody, RigidbodyData> rigidbodies)
        {
            if (hits == null || rigidbodies == null)
                return;

            int instigatorOwnerId = GetInstigatorOwnerId();

            foreach (Collider hit in hits)
            {
                if (hit == null) continue;

                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null) continue;

                Vector3 closestPoint = hit.ClosestPoint(transform.position);
                if (requireLineOfSight && IsLineOfSightBlocked(closestPoint, hit, rb))
                    continue;

                if (ignoreInstigator && IsSameOwner(rb, instigatorOwnerId))
                    continue;

                float dist = Vector3.Distance(transform.position, closestPoint);
                float normalized = Mathf.Clamp01(dist / Mathf.Max(radius, 0.0001f));

                if (rigidbodies.TryGetValue(rb, out RigidbodyData existing))
                {
                    if (normalized < existing.MinNormalizedDistance)
                    {
                        existing.MinNormalizedDistance = normalized;
                        existing.ClosestCollider = hit;
                        existing.ClosestPoint = closestPoint;
                        rigidbodies[rb] = existing;
                    }
                }
                else
                {
                    rigidbodies[rb] = new RigidbodyData
                    {
                        Rigidbody = rb,
                        MinNormalizedDistance = normalized,
                        ClosestCollider = hit,
                        ClosestPoint = closestPoint,
                    };
                }
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (radius < 0f) radius = 0f;
            if (lineOfSightStartOffset < 0f) lineOfSightStartOffset = 0f;
        }
#endif
    }
}
