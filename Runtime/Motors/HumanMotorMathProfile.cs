using UnityEngine;

namespace RoachRace.Networking
{
    [CreateAssetMenu(menuName = "RoachRace/Networking/Human Motor Math Profile", fileName = "HumanMotorMathProfile")]
    public class HumanMotorMathProfile : ScriptableObject
    {
        [Header("Tuning")]
        [SerializeField] private float walkSpeed = 1.3f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float turnSpeed = 720f; // deg/sec

        [Header("Movement Forces")]
        [Tooltip("Maximum planar acceleration (m/s^2) applied while there is movement input.")]
        [SerializeField, Min(0f)] private float maxPlanarAcceleration = 25f;

        [Tooltip("Maximum planar deceleration (m/s^2) applied when there is no movement input.")]
        [SerializeField, Min(0f)] private float maxPlanarDeceleration = 35f;

        [Header("Air Control")]
        [Tooltip("Multiplier applied to planar acceleration/deceleration when not grounded.")]
        [SerializeField, Range(0f, 1f)] private float airControlMultiplier = 0.35f;

        [Header("Jump")]
        [Tooltip("Upward velocity change applied when jumping.")]
        [SerializeField, Min(0f)] private float jumpVelocityChange = 5.5f;

        [Tooltip("Layer(s) considered ground for jump checks.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Ground Check (Collisions)")]
        [Tooltip("Minimum dot(normal, up) to be considered ground. 1 = flat ground, 0 = vertical wall.")]
        [SerializeField, Range(0f, 1f)] private float groundNormalMinDot = 0.55f;

        [Tooltip("Allow jump shortly after leaving ground (seconds). Helps with small ground-check flickers.")]
        [SerializeField, Range(0f, 0.25f)] private float coyoteTimeSeconds = 0.10f;

        [Tooltip("Buffer jump input briefly (seconds) so a press isn't lost if ground-check flickers.")]
        [SerializeField, Range(0f, 0.25f)] private float jumpBufferSeconds = 0.10f;

        [Header("Constraints")]
        [SerializeField] private bool lockPitchAndRoll = true;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float TurnSpeed => turnSpeed;
        public float MaxPlanarAcceleration => maxPlanarAcceleration;
        public float MaxPlanarDeceleration => maxPlanarDeceleration;
        public float AirControlMultiplier => airControlMultiplier;
        public float JumpVelocityChange => jumpVelocityChange;
        public LayerMask GroundMask => groundMask;
        public float GroundNormalMinDot => groundNormalMinDot;
        public float CoyoteTimeSeconds => coyoteTimeSeconds;
        public float JumpBufferSeconds => jumpBufferSeconds;
        public bool LockPitchAndRoll => lockPitchAndRoll;

        public void ComputeTickWindows(float tickDeltaSeconds, out int coyoteTicksMax, out int jumpBufferTicksMax)
        {
            float tickDelta = Mathf.Max(0.000001f, tickDeltaSeconds);
            coyoteTicksMax = Mathf.Clamp(Mathf.CeilToInt(coyoteTimeSeconds / tickDelta), 0, 10);
            jumpBufferTicksMax = Mathf.Clamp(Mathf.CeilToInt(jumpBufferSeconds / tickDelta), 0, 10);
        }

        public float StepBodyYaw(float currentBodyYaw, float targetYaw, float dt)
        {
            return Mathf.MoveTowardsAngle(currentBodyYaw, targetYaw, turnSpeed * dt);
        }

        public void UpdateJumpWindows(bool isGrounded, bool jumpPressed, ref int coyoteTicksRemaining, ref int jumpBufferTicksRemaining,
            int coyoteTicksMax, int jumpBufferTicksMax)
        {
            if (isGrounded)
                coyoteTicksRemaining = coyoteTicksMax;
            else
                coyoteTicksRemaining = Mathf.Max(0, coyoteTicksRemaining - 1);

            if (jumpPressed)
                jumpBufferTicksRemaining = jumpBufferTicksMax;
            else
                jumpBufferTicksRemaining = Mathf.Max(0, jumpBufferTicksRemaining - 1);
        }

        public bool TryConsumeJump(ref int coyoteTicksRemaining, ref int jumpBufferTicksRemaining, out Vector3 velocityChange)
        {
            velocityChange = default;

            bool canJumpNow = coyoteTicksRemaining > 0;
            if (jumpBufferTicksRemaining <= 0 || !canJumpNow || jumpVelocityChange <= 0f)
                return false;

            velocityChange = Vector3.up * jumpVelocityChange;
            jumpBufferTicksRemaining = 0;
            coyoteTicksRemaining = 0;
            return true;
        }

        public Vector3 ComputePlanarAcceleration(Vector2 moveInput, float yawDegrees, Vector3 currentVelocity, bool isGrounded, float dt)
        {
            Vector2 clampedInput = Vector2.ClampMagnitude(moveInput, 1f);
            float inputMag = Mathf.Clamp01(clampedInput.magnitude);
            bool hasInput = inputMag > 0.0001f;

            const float axisDeadZone = 0.01f;
            bool hasForwardInput = clampedInput.y > axisDeadZone;
            float moveSpeed = hasForwardInput ? runSpeed : walkSpeed;

            Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 moveWorld = yawRot * new Vector3(clampedInput.x, 0f, clampedInput.y);

            Vector3 desiredPlanarVel = hasInput ? (moveWorld * (moveSpeed * inputMag)) : Vector3.zero;
            Vector3 currentPlanarVel = new(currentVelocity.x, 0f, currentVelocity.z);

            float safeDt = Mathf.Max(0.000001f, dt);
            Vector3 desiredPlanarAccel = (desiredPlanarVel - currentPlanarVel) / safeDt;
            float accelLimit = hasInput ? maxPlanarAcceleration : maxPlanarDeceleration;

            if (!isGrounded)
                accelLimit *= airControlMultiplier;

            if (accelLimit > 0f)
                desiredPlanarAccel = Vector3.ClampMagnitude(desiredPlanarAccel, accelLimit);
            else
                desiredPlanarAccel = Vector3.zero;

            return desiredPlanarAccel;
        }

        public bool IsLayerInGroundMask(int layer)
        {
            return (groundMask.value & (1 << layer)) != 0;
        }

        public bool TryGetGroundContact(Rigidbody body, CapsuleCollider capsule, Collision collision, out ContactPoint groundContact)
        {
            groundContact = default;
            if (body == null || capsule == null || collision == null)
                return false;

            Collider other = collision.collider;
            if (other == null)
                return false;

            if (!IsLayerInGroundMask(other.gameObject.layer))
                return false;

            float centerY = body.position.y + capsule.center.y;
            float maxPointY = centerY + 0.05f;
            float minDot = groundNormalMinDot;

            int contactCount = collision.contactCount;
            for (int i = 0; i < contactCount; i++)
            {
                ContactPoint cp = collision.GetContact(i);
                if (cp.point.y > maxPointY)
                    continue;

                float upDot = Vector3.Dot(cp.normal, Vector3.up);
                if (upDot >= minDot)
                {
                    groundContact = cp;
                    return true;
                }
            }

            return false;
        }
    }
}
