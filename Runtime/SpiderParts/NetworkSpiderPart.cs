using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RoachRace.Networking.SpiderParts
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class NetworkSpiderPart : NetworkBehaviour
    {
        public enum SpiderPartState : byte
        {
            Sleeping = 0,
            Magnetic = 1,
            Returning = 2,
        }

        [Header("Movement")]
        [SerializeField, Min(0.01f)] private float maxSpeed = 10f;
        [SerializeField, Min(0.01f)] private float acceleration = 35f;
        [SerializeField, Min(0.001f)] private float arrivalRadius = 0.2f;

        [Tooltip("When within this radius, the part will snap+sleep immediately (no extra settling wait). Should be <= arrivalRadius.")]
        [SerializeField, Min(0.001f)] private float attachRadius = 0.06f;

        [Tooltip("If true, Returning keeps constant speed all the way to attachRadius (no slow-down inside arrivalRadius).")]
        [SerializeField] private bool keepReturnSpeedNearTarget = true;

        [SerializeField, Min(0f)] private float settleSeconds = 0.25f;
        [SerializeField, Min(0f)] private float retargetDeadzone = 0.05f;

        [Tooltip("Max linear speed allowed to count toward settling once inside arrivalRadius.")]
        [SerializeField, Min(0f)] private float settleLinearSpeedThreshold = 0.35f;

        [Tooltip("Max angular speed (rad/s) allowed to count toward settling once inside arrivalRadius.")]
        [SerializeField, Min(0f)] private float settleAngularSpeedThreshold = 1.5f;

        [Tooltip("If true, disables gravity while in Magnetic/Returning so parts don't fall away from the target.")]
        [SerializeField] private bool disableGravityWhileControlled = true;

        [Tooltip("If true, disables this part's colliders while Returning to guarantee it can reach the anchor.")]
        [SerializeField] private bool disableCollisionsWhileReturning = true;

        [Header("Magnetic Kick")]
        [Tooltip("If true, applies a small random kick/spin when switching into Magnetic state.")]
        [SerializeField] private bool applyRandomKickOnMagnetize = true;

        [Tooltip("Random starting linear speed range (m/s) when magnetized.")]
        [SerializeField] private Vector2 magnetizeLinearSpeedRange = new(0.2f, 1.2f);

        [Tooltip("Random starting angular speed range (rad/s) when magnetized.")]
        [SerializeField] private Vector2 magnetizeAngularSpeedRange = new(2f, 10f);

        [Header("Return Shake")]
        [Tooltip("If true, applies small random torque impulses while Returning to create a shake effect.")]
        [SerializeField] private bool applyReturnShake = true;

        [Tooltip("Seconds between shake impulses while Returning.")]
        [SerializeField, Min(0.01f)] private float returnShakeIntervalSeconds = 0.08f;

        [Tooltip("Torque impulse strength range for shake (N·m).")]
        [SerializeField] private Vector2 returnShakeTorqueRange = new(0.5f, 2.0f);

        [Tooltip("Limits angular speed while shaking (rad/s).")]
        [SerializeField, Min(0f)] private float returnShakeMaxAngularSpeed = 12f;

        [Header("Rotation")]
        [SerializeField] private bool rotateToTarget = true;
        [SerializeField, Min(0.01f)] private float rotationSpeed = 12f;

        [Header("Dependencies")]
        [SerializeField] private Rigidbody rb;

        [Header("Colliders")]
        [Tooltip("Optional. If not assigned, will auto-resolve from children.")]
        [SerializeField] private Collider[] colliders;

        public readonly SyncVar<SpiderPartState> State = new(SpiderPartState.Sleeping);

        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _hasTarget;
        private float _inArrivalTime;
        private float _nextReturnShakeTime;

        /// <summary>
        /// Server-only event fired when this part completes a Return.
        /// </summary>
        public event Action<NetworkSpiderPart> Returned;

        private void Awake()
        {
            if (rb == null && !TryGetComponent(out rb))
                rb = GetComponent<Rigidbody>();

            if (colliders == null || colliders.Length == 0)
                colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            // Keep attachRadius sensible.
            attachRadius = Mathf.Clamp(attachRadius, 0.001f, Mathf.Max(0.001f, arrivalRadius));

            // Ensure ranges are ordered.
            if (magnetizeLinearSpeedRange.y < magnetizeLinearSpeedRange.x)
                (magnetizeLinearSpeedRange.x, magnetizeLinearSpeedRange.y) = (magnetizeLinearSpeedRange.y, magnetizeLinearSpeedRange.x);
            if (magnetizeAngularSpeedRange.y < magnetizeAngularSpeedRange.x)
                (magnetizeAngularSpeedRange.x, magnetizeAngularSpeedRange.y) = (magnetizeAngularSpeedRange.y, magnetizeAngularSpeedRange.x);

            if (returnShakeTorqueRange.y < returnShakeTorqueRange.x)
                (returnShakeTorqueRange.x, returnShakeTorqueRange.y) = (returnShakeTorqueRange.y, returnShakeTorqueRange.x);
        }
#endif

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (rb == null)
            {
                Debug.LogError($"[{nameof(NetworkSpiderPart)}] Rigidbody is missing on '{gameObject.name}'.", gameObject);
                throw new NullReferenceException($"[{nameof(NetworkSpiderPart)}] Rigidbody is null on '{gameObject.name}'.");
            }

            rb.isKinematic = true;
            rb.useGravity = true;

            if (colliders == null || colliders.Length == 0)
                colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        }

        [Server]
        public Collider[] GetCollidersServer() => colliders;

        [Server]
        public void SetCollisionsEnabledServer(bool enabled)
        {
            if (colliders == null) return;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = enabled;
            }
        }

        [Server]
        public void SetMagneticTarget(Vector3 position, Quaternion rotation)
        {
            if (_hasTarget && Vector3.SqrMagnitude(_targetPosition - position) <= retargetDeadzone * retargetDeadzone && State.Value == SpiderPartState.Magnetic)
                return;

            SpiderPartState prevState = State.Value;

            _targetPosition = position;
            _targetRotation = rotation;
            _hasTarget = true;
            _inArrivalTime = 0f;

            rb.isKinematic = false;

            // Clear any prior impulses so we start from a controlled state.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Optional: add a small random kick/spin for a fun "throw" feel.
            if (applyRandomKickOnMagnetize && prevState != SpiderPartState.Magnetic)
            {
                float linSpeed = UnityEngine.Random.Range(magnetizeLinearSpeedRange.x, magnetizeLinearSpeedRange.y);
                float angSpeed = UnityEngine.Random.Range(magnetizeAngularSpeedRange.x, magnetizeAngularSpeedRange.y);

                Vector3 linDir = UnityEngine.Random.insideUnitSphere;
                if (linDir.sqrMagnitude < 0.0001f) linDir = Vector3.up;
                linDir.Normalize();

                Vector3 angAxis = UnityEngine.Random.onUnitSphere;
                if (angAxis.sqrMagnitude < 0.0001f) angAxis = Vector3.up;
                angAxis.Normalize();

                rb.linearVelocity = Vector3.ClampMagnitude(linDir * linSpeed, maxSpeed);
                rb.angularVelocity = angAxis * angSpeed;
            }
            if (disableGravityWhileControlled)
                rb.useGravity = false;

            if (disableCollisionsWhileReturning)
                SetCollisionsEnabledServer(true);
            State.Value = SpiderPartState.Magnetic;
        }

        [Server]
        public void SetReturnTarget(Vector3 position, Quaternion rotation)
        {
            if (_hasTarget && Vector3.SqrMagnitude(_targetPosition - position) <= retargetDeadzone * retargetDeadzone && State.Value == SpiderPartState.Returning)
                return;

            _targetPosition = position;
            _targetRotation = rotation;
            _hasTarget = true;
            _inArrivalTime = 0f;
            _nextReturnShakeTime = Time.time + returnShakeIntervalSeconds;

            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (disableGravityWhileControlled)
                rb.useGravity = false;

            if (disableCollisionsWhileReturning)
                SetCollisionsEnabledServer(false);
            State.Value = SpiderPartState.Returning;
        }

        [Server]
        public void SleepNow()
        {
            if (rb == null)
                return;

            // Avoid warnings: setting velocities on kinematic bodies is not supported.
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = true;

            // When sleeping (stuck), collisions should be on.
            if (disableCollisionsWhileReturning)
                SetCollisionsEnabledServer(true);
            _hasTarget = false;
            _inArrivalTime = 0f;
            State.Value = SpiderPartState.Sleeping;
        }

        private void FixedUpdate()
        {
            if (!IsServerInitialized)
                return;
            if (rb == null)
                return;
            if (!_hasTarget)
                return;

            SpiderPartState state = State.Value;
            if (state == SpiderPartState.Sleeping)
                return;

            Vector3 pos = rb.position;
            Vector3 toTarget = _targetPosition - pos;
            float dist = toTarget.magnitude;

            // While returning, add a subtle shake (server authoritative; replicated via NetworkTransform).
            if (state == SpiderPartState.Returning && applyReturnShake && returnShakeIntervalSeconds > 0f && Time.time >= _nextReturnShakeTime)
            {
                _nextReturnShakeTime = Time.time + returnShakeIntervalSeconds;

                float torqueMag = UnityEngine.Random.Range(returnShakeTorqueRange.x, returnShakeTorqueRange.y);
                Vector3 axis = UnityEngine.Random.onUnitSphere;
                if (axis.sqrMagnitude < 0.0001f) axis = Vector3.up;
                axis.Normalize();

                // Impulse torque gives a quick "shake".
                rb.AddTorque(axis * torqueMag, ForceMode.Impulse);

                if (returnShakeMaxAngularSpeed > 0f)
                    rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, returnShakeMaxAngularSpeed);
            }

            // If very close, attach immediately (per-part, no waiting).
            if (dist <= attachRadius)
            {
                rb.MovePosition(_targetPosition);
                if (rotateToTarget)
                    rb.MoveRotation(_targetRotation);

                if (state == SpiderPartState.Returning)
                {
                    SleepNow();
                    Returned?.Invoke(this);
                }
                else
                {
                    SleepNow();
                }

                return;
            }

            // Returning: keep chasing at constant speed right up until attach.
            if (state == SpiderPartState.Returning && keepReturnSpeedNearTarget)
            {
                _inArrivalTime = 0f;

                Vector3 dir = toTarget / Mathf.Max(0.0001f, dist);
                Vector3 desiredVelocity = dir * maxSpeed;
                Vector3 velocityDelta = desiredVelocity - rb.linearVelocity;
                Vector3 accel = Vector3.ClampMagnitude(velocityDelta / Time.fixedDeltaTime, acceleration);
                rb.AddForce(accel, ForceMode.Acceleration);

                rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxSpeed);

                if (rotateToTarget)
                {
                    Quaternion nextRot = Quaternion.Slerp(rb.rotation, _targetRotation, rotationSpeed * Time.fixedDeltaTime);
                    rb.MoveRotation(nextRot);
                }

                return;
            }

            if (dist > arrivalRadius)
            {
                _inArrivalTime = 0f;

                Vector3 dir = toTarget / Mathf.Max(0.0001f, dist);
                Vector3 desiredVelocity = dir * maxSpeed;
                Vector3 currentVelocity = rb.linearVelocity;

                Vector3 velocityDelta = desiredVelocity - currentVelocity;
                Vector3 accel = Vector3.ClampMagnitude(velocityDelta / Time.fixedDeltaTime, acceleration);
                rb.AddForce(accel, ForceMode.Acceleration);

                // Clamp to avoid runaway speeds from collision impulses.
                rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxSpeed);

                if (rotateToTarget)
                {
                    Quaternion nextRot = Quaternion.Slerp(rb.rotation, _targetRotation, rotationSpeed * Time.fixedDeltaTime);
                    rb.MoveRotation(nextRot);
                }

                return;
            }

            // Inside arrival radius: keep nudging toward the point (scaled down) while damping,
            // and only start the settle timer once motion is mostly stopped.
            Vector3 dirInRadius = (dist > 0.0001f) ? (toTarget / dist) : Vector3.zero;
            float speedScale = Mathf.Clamp01(dist / Mathf.Max(0.0001f, arrivalRadius));
            Vector3 desiredVelInRadius = dirInRadius * (maxSpeed * speedScale);

            Vector3 velDeltaInRadius = desiredVelInRadius - rb.linearVelocity;
            Vector3 accelInRadius = Vector3.ClampMagnitude(velDeltaInRadius / Time.fixedDeltaTime, acceleration);
            rb.AddForce(accelInRadius, ForceMode.Acceleration);

            // Damping to remove residual jitter/spin.
            rb.AddForce(-rb.linearVelocity * 8f, ForceMode.Acceleration);
            rb.AddTorque(-rb.angularVelocity * 2f, ForceMode.Acceleration);

            // Clamp to avoid runaway speeds from collision impulses.
            rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, maxSpeed);

            bool withinSettleSpeeds =
                rb.linearVelocity.magnitude <= settleLinearSpeedThreshold &&
                rb.angularVelocity.magnitude <= settleAngularSpeedThreshold;

            if (!withinSettleSpeeds)
            {
                _inArrivalTime = 0f;
                return;
            }

            _inArrivalTime += Time.fixedDeltaTime;
            if (_inArrivalTime < settleSeconds)
                return;

            // Snap to final pose and finish.
            rb.MovePosition(_targetPosition);
            if (rotateToTarget)
                rb.MoveRotation(_targetRotation);

            if (state == SpiderPartState.Returning)
            {
                SleepNow();
                Returned?.Invoke(this);
            }
            else
            {
                SleepNow();
            }
        }
    }
}
