using System;
using System.Collections.Generic;
using RoachRace.Networking.Effects;
using UnityEngine;
using UnityEngine.Serialization;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Applies AoE status effects to any valid StatusEffectTickRunner within radius.
    /// Server-only.
    /// </summary>
    public class NetworkExplosionStatusEffects : NetworkExplosionOverlapBase
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

        private bool _didApply;

        private struct TargetData
        {
            public StatusEffectTickRunner TickRunner;
            public float MinNormalizedDistance;
            public Collider ClosestCollider;
            public Rigidbody ClosestRigidbody;
            public Vector3 ClosestPoint;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyEffectsOnce();
        }

        private void ApplyEffectsOnce()
        {
            if (!IsServerInitialized) return;
            if (_didApply) return;
            _didApply = true;

            if (radius <= 0f) return;
            bool hasEffectsList = effects != null && effects.Count > 0;
            bool hasLegacy = _legacyEffect != null;
            if (!hasEffectsList && !hasLegacy)
            {
                Debug.LogError($"[{nameof(NetworkExplosionStatusEffects)}] No effects are assigned on '{gameObject.name}'.", gameObject);
                return;
            }

            Collider[] hits = Overlap();
            if (hits == null || hits.Length == 0) return;

            var targets = new Dictionary<UnityEngine.Object, TargetData>(hits.Length);
            int instigatorOwnerId = GetInstigatorOwnerId();

            foreach (Collider hit in hits)
            {
                if (hit == null) continue;

                Vector3 closestPoint = hit.ClosestPoint(transform.position);
                Rigidbody rb = hit.attachedRigidbody;
                if (requireLineOfSight && IsLineOfSightBlocked(closestPoint, hit, rb))
                    continue;

                float dist = Vector3.Distance(transform.position, closestPoint);
                float normalized = Mathf.Clamp01(dist / Mathf.Max(radius, 0.0001f));

                StatusEffectTickRunner tickRunner = hit.GetComponentInChildren<StatusEffectTickRunner>(true);
                if (tickRunner == null) continue;

                if (ignoreInstigator && IsSameOwner(tickRunner, instigatorOwnerId))
                    continue;

                UnityEngine.Object key = tickRunner;
                if (targets.TryGetValue(key, out TargetData existing))
                {
                    if (normalized < existing.MinNormalizedDistance)
                    {
                        existing.MinNormalizedDistance = normalized;
                        existing.ClosestCollider = hit;
                        existing.ClosestRigidbody = rb;
                        existing.ClosestPoint = closestPoint;
                        targets[key] = existing;
                    }
                }
                else
                {
                    targets[key] = new TargetData
                    {
                        TickRunner = tickRunner,
                        MinNormalizedDistance = normalized,
                        ClosestCollider = hit,
                        ClosestRigidbody = rb,
                        ClosestPoint = closestPoint,
                    };
                }
            }

            if (targets.Count == 0) return;

            foreach (var kvp in targets)
            {
                TargetData data = kvp.Value;
                if (data.TickRunner == null) continue;
                if (!data.TickRunner.IsServerInitialized) continue;

                float strength01 = 1f;
                if (scaleStacksByDistance)
                {
                    strength01 = stackFalloff != null
                        ? Mathf.Clamp01(stackFalloff.Evaluate(data.MinNormalizedDistance))
                        : 1f;
                }

                ApplyEffects(data.TickRunner, strength01);
            }
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

#if UNITY_EDITOR
        public void EditorApplyEffectsAgain()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[{nameof(NetworkExplosionStatusEffects)}] EditorApplyEffectsAgain() only works in Play Mode.", gameObject);
                return;
            }

            if (!IsServerInitialized)
            {
                Debug.LogWarning($"[{nameof(NetworkExplosionStatusEffects)}] EditorApplyEffectsAgain() requires a server instance.", gameObject);
                return;
            }

            _didApply = false;
            ApplyEffectsOnce();
        }

        protected override void OnValidate()
        {
            base.OnValidate();

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
