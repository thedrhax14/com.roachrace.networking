using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using RoachRace.Networking.Effects;
using UnityEngine;
using UnityEngine.Serialization;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Spawn-and-forget explosion.
    /// - On server start: applies AoE status effect(s) once.
    /// - On client start: enables a visual GameObject (particles, light, etc.).
    /// - On server: keeps visuals disabled.
    /// - Despawns itself after a specified lifetime.
    /// </summary>
    [Obsolete("Deprecated: use NetworkExplosionLifecycleOnSpawn + NetworkExplosionForceOnSpawn + NetworkExplosionStatusEffectsOnSpawn instead. This wrapper remains for backwards compatibility with existing prefabs.", false)]
    public class NetworkExplosionOnSpawn : NetworkBehaviour
    {
        [Serializable]
        private struct ExplosionEffect
        {
            public StatusEffectDefinition definition;
            [Min(0)] public int stacks;

            [Tooltip("If stack scaling is enabled, this is the number of stacks applied at the edge of the radius. Can be 0 to apply none at the edge.")]
            [Min(0)] public int edgeStacks;
        }

        [Header("Status Effects")]
        [Tooltip("Effects applied to every valid target inside the radius (same effect regardless of distance).")]
        [SerializeField] private List<ExplosionEffect> effects = new();

        [Header("Strength By Distance")]
        [Tooltip("If enabled, stacks are interpolated between each effect's 'edgeStacks' and 'stacks' based on distance to the explosion center.")]
        [SerializeField] private bool scaleStacksByDistance = false;

        [Tooltip("Curve evaluated by normalized distance (0=center, 1=edge). Used as strength multiplier for stack interpolation.")]
        [SerializeField] private AnimationCurve stackFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        // Legacy serialized fields (kept to avoid breaking existing prefabs/scenes).
        [FormerlySerializedAs("effect")]
        [SerializeField, HideInInspector] private StatusEffectDefinition _legacyEffect;

        [FormerlySerializedAs("stacks")]
        [SerializeField, HideInInspector] private int _legacyStacks = 1;

        [Tooltip("If true, removes all existing instances of this effect definition before adding.")]
        [SerializeField] private bool removeExistingFirst;

        [Header("Targeting")]

        [Tooltip("World-space radius used for damage overlap.")]
        [SerializeField] private float radius = 4f;

        [Header("Physics Force")]
        [Tooltip("If enabled, applies a physical explosion force to nearby rigidbodies on the server.")]
        [SerializeField] private bool applyPhysicsForce = true;

        [Tooltip("Base explosion force passed to Rigidbody.AddExplosionForce.")]
        [SerializeField] private float explosionForce = 600f;

        [Tooltip("Upwards modifier passed to Rigidbody.AddExplosionForce.")]
        [SerializeField] private float upwardsModifier = 0.0f;

        [Tooltip("Force mode used when applying the explosion.")]
        [SerializeField] private ForceMode forceMode = ForceMode.Impulse;

        [Tooltip("Multiplier by normalized distance (0=center, 1=edge). Output is clamped to [0..1].")]
        [SerializeField] private AnimationCurve forceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Tooltip("Only colliders on these layers can be affected.")]
        [SerializeField] private LayerMask damageLayers = ~0;

        [Tooltip("Include trigger colliders in the overlap.")]
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Optionally ignore damage to the object that spawned/owns this explosion.")]
        [SerializeField] private bool ignoreInstigator = false;

        [Header("Line of Sight")]
        [Tooltip("If enabled, targets must have line-of-sight from the explosion center to receive damage.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Layers which can block the explosion line-of-sight check.")]
        [SerializeField] private LayerMask lineOfSightBlockers = ~0;

        [Tooltip("Small offset used to avoid raycasts immediately hitting the origin collider.")]
        [SerializeField] private float lineOfSightStartOffset = 0.05f;

        [Header("Visuals")]
        [Tooltip("Enabled on clients, disabled on server.")]
        [SerializeField] private GameObject visualRoot;

        [Header("Lifetime")]
        [Tooltip("How long the explosion object stays alive before despawning (seconds).")]
        [SerializeField] private float despawnAfterSeconds = 2f;

        private bool _didExplode;
        private bool _didApplyClientForce;

        private struct TargetData
        {
            public StatusEffectTickRunner TickRunner;
            public float MinNormalizedDistance;
        }

        private struct RigidbodyData
        {
            public Rigidbody Rigidbody;
            public float MinNormalizedDistance;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            SetVisualsActive(false);
            ExplodeOnce();
            StartCoroutine(DespawnAfterDelay());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Client-side only visuals.
            // In host mode, this will run and re-enable visuals for the local player.
            SetVisualsActive(true);

            // Apply explosion force on pure clients too (for local physics), but not on host.
            ApplyClientExplosionForceOnce();
        }

        private void SetVisualsActive(bool active)
        {
            if (visualRoot == null) return;
            visualRoot.SetActive(active);
        }

        private void ExplodeOnce()
        {
            if (!IsServerInitialized) return;
            if (_didExplode) return;
            _didExplode = true;

            if (radius <= 0f) return;
            bool hasEffectsList = effects != null && effects.Count > 0;
            bool hasLegacy = _legacyEffect != null;
            if (!hasEffectsList && !hasLegacy)
            {
                Debug.LogError($"[{nameof(NetworkExplosionOnSpawn)}] No effects are assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            Collider[] hits = Physics.OverlapSphere(transform.position, radius, damageLayers, triggerInteraction);
            if (hits == null || hits.Length == 0) return;

            var targets = new Dictionary<UnityEngine.Object, TargetData>(hits.Length);
            int instigatorOwnerId = GetInstigatorOwnerId();
            Dictionary<Rigidbody, RigidbodyData> rigidbodies = null;
            if (applyPhysicsForce)
                rigidbodies = new Dictionary<Rigidbody, RigidbodyData>(hits.Length);

            foreach (Collider hit in hits)
            {
                if (hit == null) continue;

                Vector3 closestPoint = hit.ClosestPoint(transform.position);
                if (requireLineOfSight && IsLineOfSightBlocked(closestPoint, hit, hit.attachedRigidbody))
                    continue;

                float dist = Vector3.Distance(transform.position, closestPoint);
                float normalized = Mathf.Clamp01(dist / Mathf.Max(radius, 0.0001f));

                StatusEffectTickRunner tickRunner = hit.GetComponentInChildren<StatusEffectTickRunner>(true);
                UnityEngine.Object targetRunner = tickRunner;

                if (targetRunner != null)
                {
                    if (targets.TryGetValue(targetRunner, out TargetData existing))
                    {
                        if (normalized < existing.MinNormalizedDistance)
                        {
                            existing.MinNormalizedDistance = normalized;
                            targets[targetRunner] = existing;
                        }
                    }
                    else
                    {
                        targets[targetRunner] = new TargetData
                        {
                            TickRunner = tickRunner,
                            MinNormalizedDistance = normalized
                        };
                    }
                }

                if (applyPhysicsForce && rigidbodies != null)
                {
                    Rigidbody rb = hit.attachedRigidbody;
                    if (rb != null)
                    {
                        if (ignoreInstigator && IsSameOwner(rb, instigatorOwnerId))
                            continue;

                        if (rigidbodies.TryGetValue(rb, out RigidbodyData rbExisting))
                        {
                            if (normalized < rbExisting.MinNormalizedDistance)
                            {
                                rbExisting.MinNormalizedDistance = normalized;
                                rigidbodies[rb] = rbExisting;
                            }
                        }
                        else
                        {
                            rigidbodies[rb] = new RigidbodyData
                            {
                                Rigidbody = rb,
                                MinNormalizedDistance = normalized
                            };
                        }
                    }
                }
            }

            if (targets.Count == 0 && (rigidbodies == null || rigidbodies.Count == 0))
                return;

            foreach (var kvp in targets)
            {
                UnityEngine.Object targetRunner = kvp.Key;
                TargetData data = kvp.Value;

                if (ignoreInstigator && IsSameOwner(targetRunner, instigatorOwnerId))
                    continue;

                float strength01 = 1f;
                if (scaleStacksByDistance)
                {
                    strength01 = stackFalloff != null
                        ? Mathf.Clamp01(stackFalloff.Evaluate(data.MinNormalizedDistance))
                        : 1f;
                }

                // Apply the same effect regardless of distance from the explosion (within radius).
                if (data.TickRunner != null)
                {
                    if (!data.TickRunner.IsServerInitialized) continue;
                    ApplyEffects(data.TickRunner, strength01);
                }
            }

            if (applyPhysicsForce && rigidbodies != null && rigidbodies.Count > 0)
                ApplyExplosionForce(rigidbodies);
        }

        private void ApplyExplosionForce(Dictionary<Rigidbody, RigidbodyData> rigidbodies)
        {
            if (rigidbodies == null || rigidbodies.Count == 0) return;
            if (radius <= 0f) return;

            float baseForce = Mathf.Max(0f, explosionForce);
            if (baseForce <= 0f) return;

            Vector3 origin = transform.position;

            foreach (var kvp in rigidbodies)
            {
                RigidbodyData data = kvp.Value;
                Rigidbody rb = data.Rigidbody;
                if (rb == null) continue;

                float multiplier = 1f;
                if (forceFalloff != null)
                    multiplier = Mathf.Clamp01(forceFalloff.Evaluate(Mathf.Clamp01(data.MinNormalizedDistance)));

                float finalForce = baseForce * multiplier;
                if (finalForce <= 0f) continue;

                rb.AddExplosionForce(finalForce, origin, radius, upwardsModifier, forceMode);
            }
        }

        private void ApplyClientExplosionForceOnce()
        {
            if (!IsClientInitialized) return;
            if (IsServerInitialized) return; // host: server already applied force
            if (!applyPhysicsForce) return;
            if (_didApplyClientForce) return;
            _didApplyClientForce = true;

            if (radius <= 0f) return;

            Collider[] hits = Physics.OverlapSphere(transform.position, radius, damageLayers, triggerInteraction);
            if (hits == null || hits.Length == 0) return;

            int instigatorOwnerId = GetInstigatorOwnerId();
            var rigidbodies = new Dictionary<Rigidbody, RigidbodyData>(hits.Length);

            foreach (Collider hit in hits)
            {
                if (hit == null) continue;

                Vector3 closestPoint = hit.ClosestPoint(transform.position);
                if (requireLineOfSight && IsLineOfSightBlocked(closestPoint, hit, hit.attachedRigidbody))
                    continue;

                float dist = Vector3.Distance(transform.position, closestPoint);
                float normalized = Mathf.Clamp01(dist / Mathf.Max(radius, 0.0001f));

                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null) continue;

                if (ignoreInstigator && IsSameOwner(rb, instigatorOwnerId))
                    continue;

                if (rigidbodies.TryGetValue(rb, out RigidbodyData existing))
                {
                    if (normalized < existing.MinNormalizedDistance)
                    {
                        existing.MinNormalizedDistance = normalized;
                        rigidbodies[rb] = existing;
                    }
                }
                else
                {
                    rigidbodies[rb] = new RigidbodyData
                    {
                        Rigidbody = rb,
                        MinNormalizedDistance = normalized
                    };
                }
            }

            if (rigidbodies.Count == 0) return;
            ApplyExplosionForce(rigidbodies);
        }

        private void ApplyEffects(StatusEffectTickRunner tickRunner, float strength01)
        {
            if (tickRunner == null) return;

            if (effects != null && effects.Count > 0)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    var entry = effects[i];
                    if (entry.definition == null) continue;
                    int entryStacks = GetStacksToApply(entry, strength01);
                    if (entryStacks <= 0) continue;

                    if (removeExistingFirst)
                        tickRunner.RemoveEffects(entry.definition);

                    tickRunner.AddEffect(entry.definition, entryStacks);
                }

                return;
            }

            // Legacy fallback.
            if (_legacyEffect == null) return;

            int stacks = Mathf.Max(1, _legacyStacks);
            if (scaleStacksByDistance)
                stacks = Mathf.RoundToInt(Mathf.Lerp(0f, stacks, Mathf.Clamp01(strength01)));

            if (stacks <= 0) return;

            if (removeExistingFirst)
                tickRunner.RemoveEffects(_legacyEffect);

            tickRunner.AddEffect(_legacyEffect, stacks);
        }

        private int GetStacksToApply(ExplosionEffect entry, float strength01)
        {
            int centerStacks = Mathf.Max(0, entry.stacks);
            if (!scaleStacksByDistance)
                return centerStacks;

            int edgeStacks = Mathf.Max(0, entry.edgeStacks);
            float s = Mathf.Clamp01(strength01);
            return Mathf.RoundToInt(Mathf.Lerp(edgeStacks, centerStacks, s));
        }

        private bool IsLineOfSightBlocked(Vector3 targetPoint, Collider targetCollider, Rigidbody targetRigidbody)
        {
            Vector3 origin = transform.position;
            Vector3 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f) return false;

            Vector3 direction = toTarget / distance;
            float startOffset = Mathf.Clamp(lineOfSightStartOffset, 0f, distance * 0.5f);

            // Offset the ray start so we don't immediately hit the origin collider.
            origin += direction * startOffset;
            distance -= startOffset;

            if (distance <= 0.0001f) return false;

            if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance, lineOfSightBlockers, QueryTriggerInteraction.Ignore))
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

            // Also ignore hits on the explosion object itself.
            if (hit.collider != null && hit.collider.transform != null && hit.collider.transform.IsChildOf(transform))
                return false;

            return true;
        }

        private int GetInstigatorOwnerId()
        {
            // If callers set the NetworkObject owner when spawning this explosion, we can use it to ignore the instigator.
            // This intentionally compares owner id (not object id) because the instigator object id is not known here.
            if (NetworkObject == null || NetworkObject.Owner == null)
                return -1;

            return NetworkObject.Owner.ClientId;
        }

        private static bool IsSameOwner(UnityEngine.Object target, int instigatorOwnerId)
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
        public void EditorExplodeAgain()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[{nameof(NetworkExplosionOnSpawn)}] EditorExplodeAgain() only works in Play Mode.", gameObject);
                return;
            }

            if (!IsServerInitialized)
            {
                Debug.LogWarning($"[{nameof(NetworkExplosionOnSpawn)}] EditorExplodeAgain() requires a server instance.", gameObject);
                return;
            }

            _didExplode = false;
            ExplodeOnce();
        }
#endif

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (radius < 0f) radius = 0f;
            if (despawnAfterSeconds < 0f) despawnAfterSeconds = 0f;
            if (lineOfSightStartOffset < 0f) lineOfSightStartOffset = 0f;

            if (explosionForce < 0f) explosionForce = 0f;
            if (forceFalloff == null)
                forceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

            _legacyStacks = Mathf.Max(1, _legacyStacks);
            if (stackFalloff == null)
                stackFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            if (effects != null)
            {
                for (int i = 0; i < effects.Count; i++)
                {
                    var e = effects[i];
                    e.stacks = Mathf.Max(0, e.stacks);
                    e.edgeStacks = Mathf.Max(0, e.edgeStacks);
                    effects[i] = e;
                }
            }
        }
#endif
    }
}
