using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Applies physical explosion force to nearby rigidbodies.
    /// - Server applies once on spawn.
    /// - Pure clients also apply once (local-only physics).
    /// - Host does not double-apply (client pass is skipped when server is initialized).
    /// </summary>
    public class NetworkExplosionForce : NetworkExplosionOverlapBase
    {
        [Header("Physics Force")]
        [Tooltip("If enabled, applies a physical explosion force.")]
        [SerializeField] private bool applyPhysicsForce = true;

        [Tooltip("Base explosion force passed to Rigidbody.AddExplosionForce.")]
        [SerializeField] private float explosionForce = 600f;

        [Tooltip("Upwards modifier passed to Rigidbody.AddExplosionForce.")]
        [SerializeField] private float upwardsModifier = 0.0f;

        [Tooltip("Force mode used when applying the explosion.")]
        [SerializeField] private ForceMode forceMode = ForceMode.Impulse;

        [Tooltip("Multiplier by normalized distance (0=center, 1=edge). Output is clamped to [0..1].")]
        [SerializeField] private AnimationCurve forceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        private bool _didApplyServer;
        private bool _didApplyClient;

        public override void OnStartServer()
        {
            base.OnStartServer();
            ApplyServerForceOnce();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyClientForceOnce();
        }

        private void ApplyServerForceOnce()
        {
            if (!IsServerInitialized) return;
            if (!applyPhysicsForce) return;
            if (_didApplyServer) return;
            _didApplyServer = true;

            ApplyForceInternal();
        }

        private void ApplyClientForceOnce()
        {
            if (!IsClientInitialized) return;
            if (IsServerInitialized) return; // host: server already applied
            if (!applyPhysicsForce) return;
            if (_didApplyClient) return;
            _didApplyClient = true;

            ApplyForceInternal();
        }

        private void ApplyForceInternal()
        {
            if (radius <= 0f) return;

            Collider[] hits = Overlap();
            if (hits == null || hits.Length == 0) return;

            var rigidbodies = new Dictionary<Rigidbody, RigidbodyData>(hits.Length);
            CollectRigidbodies(hits, rigidbodies);
            if (rigidbodies.Count == 0) return;

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

#if UNITY_EDITOR
        public void EditorApplyForceAgain()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[{nameof(NetworkExplosionForce)}] EditorApplyForceAgain() only works in Play Mode.", gameObject);
                return;
            }

            // Match runtime behavior: host applies server-side only.
            if (IsServerInitialized)
            {
                _didApplyServer = false;
                ApplyServerForceOnce();
                return;
            }

            if (IsClientInitialized)
            {
                _didApplyClient = false;
                ApplyClientForceOnce();
            }
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (explosionForce < 0f) explosionForce = 0f;
            if (forceFalloff == null)
                forceFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
#endif
    }
}
