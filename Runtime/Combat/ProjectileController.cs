using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Physics-based projectile controller that flies forward automatically.
    /// Designed for spawned projectiles, not player-controlled movement.
    /// It also assumes the projectile (or a child) has NetworkHealth and
    /// NetworkHealth is configured to spawn death effects and despawn the
    /// projectile.
    /// 
    /// Features:
    /// - Automatic forward propulsion via Rigidbody velocity
    /// - Server-authoritative physics when networked (FishNet)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ProjectileController : NetworkBehaviour
    {
        [Header("Projectile Movement")]
        [Tooltip("Forward velocity magnitude (m/s)")]
        [SerializeField] private float speed = 50f;

        [Tooltip("If true, sets initial velocity once (like a thrown ball) and then lets physics (gravity/drag) take over.")]
        [SerializeField] private bool ballisticOnce = false;

        [Tooltip("Only used when Ballistic Once is enabled. If true, the Rigidbody uses gravity.")]
        [SerializeField] private bool ballisticUseGravity = true;

        [Tooltip("If enabled, continuously re-applies forward velocity each FixedUpdate")]
        [SerializeField] private bool maintainForwardVelocity = true;

        [Header("Randomization")]
        [Tooltip("How much Perlin noise affects rocket direction (0..2). 0 = perfectly straight.")]
        [Range(0f, 2f)]
        [SerializeField] private float perlinNoiseAmount = 0f;

        [Tooltip("How fast the Perlin noise evolves over time.")]
        [SerializeField] private float perlinNoiseFrequency = 1.25f;

        private Rigidbody _rb;
        private float _noiseSeedX;
        private float _noiseSeedY;
        private float _noiseTimeOffset;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            if (_rb == null)
            {
                Debug.LogError($"[{nameof(ProjectileController)}] Rigidbody is not assigned on GameObject '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException($"[{nameof(ProjectileController)}] Rigidbody is null. {nameof(ProjectileController)} requires a Rigidbody component.");
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Random per-rocket seeds so multiple rockets don't wobble in sync.
            // This runs on the server only; clients are kinematic and just observe results.
            _noiseSeedX = Random.Range(0f, 1000f);
            _noiseSeedY = Random.Range(0f, 1000f);
            _noiseTimeOffset = Random.Range(0f, 1000f);

            InitializeRocket();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            // Client-side visual setup (if needed)
            if (!IsServerInitialized)
            {
                _rb.isKinematic = true; // Clients don't simulate physics
            }
        }

        private void InitializeRocket()
        {
            // Physics setup
            _rb.isKinematic = false;

            // Ballistic mode behaves like a thrown object.
            if (ballisticOnce)
                _rb.useGravity = ballisticUseGravity;
            
            // Launch forward based on spawn rotation
            SetRbVelocity(_rb, GetNoisyForwardDirection() * speed);
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized) return; // Server handles physics

            // Ballistic mode: only set velocity once in InitializeRocket().
            if (ballisticOnce) return;

            if (!maintainForwardVelocity) return;
            SetRbVelocity(_rb, GetNoisyForwardDirection() * speed);
        }

        private Vector3 GetNoisyForwardDirection()
        {
            Vector3 forward = transform.forward;
            if (perlinNoiseAmount <= 0.0001f)
                return forward;

            float t = (Time.time + _noiseTimeOffset) * Mathf.Max(0f, perlinNoiseFrequency);

            // PerlinNoise returns [0..1]; remap to [-1..1].
            float nx = (Mathf.PerlinNoise(_noiseSeedX, t) * 2f) - 1f;
            float ny = (Mathf.PerlinNoise(_noiseSeedY, t) * 2f) - 1f;

            // Apply lateral + vertical sway. Amount is scaled by perlinNoiseAmount.
            Vector3 noise = (transform.right * nx + transform.up * ny) * perlinNoiseAmount;
            Vector3 dir = forward + noise;

            return dir.sqrMagnitude > 0.0001f ? dir.normalized : forward;
        }

        /// <summary>
        /// Server-only: Update rocket's forward velocity (for guided rockets, etc.)
        /// </summary>
        public void SetVelocity(Vector3 newVelocity)
        {
            if (!IsServerInitialized) return;
            SetRbVelocity(_rb, newVelocity);
        }

        /// <summary>
        /// Server-only: Redirect rocket toward a new target
        /// </summary>
        public void RedirectToward(Vector3 targetPosition)
        {
            if (!IsServerInitialized) return;
            
            Vector3 direction = (targetPosition - transform.position).normalized;
            SetRbVelocity(_rb, direction * speed);
            transform.rotation = Quaternion.LookRotation(direction);
        }

        private static void SetRbVelocity(Rigidbody rb, Vector3 velocity)
        {
    #if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = velocity;
    #else
            rb.velocity = velocity;
    #endif
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (speed < 0f) speed = 0f;
            if (perlinNoiseFrequency < 0f) perlinNoiseFrequency = 0f;
            perlinNoiseAmount = Mathf.Clamp(perlinNoiseAmount, 0f, 2f);
        }
#endif
    }
}
