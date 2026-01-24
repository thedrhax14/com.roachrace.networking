using FishNet.Object;
using UnityEngine;
using RoachRace.Networking.Combat.Movement;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Physics-based projectile controller.
    /// Delegates movement logic to a ProjectileMovementProfile ScriptableObject.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ProjectileController : NetworkBehaviour
    {
        [Header("Movement Configuration")]
        [Tooltip("The movement profile defining how this projectile moves (Linear, Wavy, Ballistic, etc.)")]
        [SerializeField] private ProjectileMovementProfile movementProfile;
        
        [Tooltip("Multiplier for the profile's base speed.")]
        [SerializeField] private float speedMultiplier = 1.0f;

        [Tooltip("The local direction in which the projectile moves. Default is Forward (0, 0, 1).")]
        [SerializeField] private Vector3 moveDirection = Vector3.forward;

        [Header("Debug")]
        [SerializeField] private bool visualizeAngularVelocity = false;

        // Exposed for the Editor to use in simulation
        public ProjectileMovementProfile MovementProfile => movementProfile;
        public float SpeedMultiplier => speedMultiplier;
        public Vector3 MoveDirection => moveDirection;

        private Rigidbody _rb;
        private float _timeAlive;
        
        // Random seeds for noise profiles
        private float _seedX;
        private float _seedY;
        private float _seedZ;

        private Vector3 initialPosition = Vector3.zero;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            if (_rb == null)
            {
                Debug.LogError($"[{nameof(ProjectileController)}] Rigidbody is not assigned on GameObject '{gameObject.name}'!", gameObject);
                throw new System.NullReferenceException($"[{nameof(ProjectileController)}] Rigidbody is null.");
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Random seeds for this instance
            _seedX = Random.Range(0f, 1000f);
            _seedY = Random.Range(0f, 1000f);
            _seedZ = Random.Range(0f, 1000f);
            _timeAlive = 0f;
            initialPosition = transform.position;

            InitializeRocket();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (!IsServerInitialized)
            {
                _rb.isKinematic = true; // Clients don't simulate physics
            }
        }

        private void InitializeRocket()
        {
            _rb.isKinematic = false;
            
            if (movementProfile != null)
            {
                movementProfile.OnInitialize(_rb, transform, moveDirection, speedMultiplier);
            }
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized) return;
            
            _timeAlive += Time.fixedDeltaTime;

            if (movementProfile != null)
            {
                movementProfile.OnFixedUpdate(_rb, transform, moveDirection, speedMultiplier, _timeAlive, _seedX, _seedY, _seedZ, initialPosition);
            }
        }

        /// <summary>
        /// Server-only: Redirect rocket toward a new target
        /// </summary>
        public void RedirectToward(Vector3 targetPosition)
        {
            if (!IsServerInitialized) return;
            
            Vector3 worldDirectionToTarget = (targetPosition - transform.position).normalized;
            
            // Force the velocity immediately
            float currentSpeed = (movementProfile != null) ? movementProfile.defaultSpeed * speedMultiplier : 50f;
            SetRbVelocity(_rb, worldDirectionToTarget * currentSpeed);
            
            // Align rotation so that our local moveDirection points toward target
            Vector3 localDir = (moveDirection.sqrMagnitude > 0.0001f) ? moveDirection.normalized : Vector3.forward;
            Vector3 currentWorldDir = transform.TransformDirection(localDir);
            
            Quaternion rotationOffset = Quaternion.FromToRotation(currentWorldDir, worldDirectionToTarget);
            transform.rotation = rotationOffset * transform.rotation;
        }

        private static void SetRbVelocity(Rigidbody rb, Vector3 velocity)
        {
    #if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = velocity;
    #else
            rb.velocity = velocity;
    #endif
        }
        void OnDrawGizmos()
        {
            if (visualizeAngularVelocity && Application.isPlaying && _rb != null)
            {
                // Draw Angular Velocity (Yellow)
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _rb.angularVelocity);
                
                // Draw a small sphere at the tip to visualize magnitude better
                if (_rb.angularVelocity.sqrMagnitude > 0.01f)
                {
                    Gizmos.DrawSphere(transform.position + _rb.angularVelocity, 0.1f);
                }
            }
        }


#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (speedMultiplier < 0f) speedMultiplier = 0f;
        }
    #endif
    }
}