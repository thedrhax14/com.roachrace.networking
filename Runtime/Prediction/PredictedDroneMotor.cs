using FishNet.Managing.Timing;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using RoachRace.Controls;
using RoachRace.Networking.Combat;
using UnityEngine;

namespace RoachRace.Networking
{
    public class PredictedDroneMotor : ServerAuthMonsterController
    {
        #region Types
        public struct ReplicateData : IReplicateData
        {
            public Vector3 Move;     // x=strafe, y=forward
            public float Yaw;        // target yaw in degrees

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Vector3 AngularVelocity;

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        [Header("Tuning")]
        [SerializeField] private float thrust = 18f;
        [SerializeField] private float maxSpeed = 15f;
        [SerializeField] private float linearDrag = 2.5f;

        [SerializeField] private float torque = 10f;       // yaw acceleration gain (deg/s^2-ish; we integrate ourselves)
        [SerializeField] private float yawResponsiveness = 8f; // how quickly to turn toward target yaw (1/sec)
        [SerializeField] private float maxYawRate = 180f;  // deg/sec

        [SerializeField] private float maxTiltAngle = 25f;       // degrees
        [SerializeField] private float tiltResponsiveness = 6f;  // how quickly to correct planar velocity via tilt (1/sec)
        [SerializeField] private float maxTiltRate = 220f;       // deg/sec
        [SerializeField] private float tiltTorque = 12f;         // pitch/roll accel gain
        [SerializeField] private float angularDrag = 6f;

        [Header("Collision / Interaction")]
        [SerializeField] private float droneMass = 6f;
        [SerializeField] private float skin = 0.03f;
        [SerializeField] private float bounce = 0.0f;      // 0 = slide, 1 = bouncy
        [SerializeField] private float pushImpulseScale = 0.8f;
        [SerializeField] private LayerMask collisionMask = ~0;

        // Predicted state (authoritative on server, predicted on owner).
        private Vector3 _vel;
        private Vector3 _angVel;          // world angular velocity (deg/sec)
        float upwardInput = 1;

        private BoxCollider _boxCollider;

        // Cached tick dt.
        private float _dt;

        protected override void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            // We simulate on Tick. PostTick is optional if you want visuals after sim.
            SetTickCallbacks(TickCallback.Tick);

            _dt = (float)TimeManager.TickDelta;
        }

        protected override void TimeManager_OnTick()
        {
            // 1) replicate predicted sim
            PerformReplicate(BuildMoveData());

            // 2) build reconcile (server sends; client keeps local fallback)
            CreateReconcile();
        }

        private ReplicateData BuildMoveData()
        {
            // Only the controller builds inputs.
            if (!IsOwner) return default;
            var host = RoachRaceInputActionsHost.Instance;
            Vector2 input = host != null ? host.Player.Move.ReadValue<Vector2>() : Vector2.zero;
            Vector3 move = new (-input.x, upwardInput, -input.y);
            if(upwardInput > 0) upwardInput -= Time.fixedDeltaTime * 2; // decay over 2 seconds. Simulate gentle lift off at start
            else upwardInput = 0f;
            float cameraYaw = (Camera.main != null) ? Camera.main.transform.eulerAngles.y : transform.eulerAngles.y;

            return new ReplicateData
            {
                Move = move,
                Yaw = cameraYaw
            };
        }

        public override void CreateReconcile()
        {
            // Server and client both create; server sends out.
            // (Optionally reduce frequency on server like the demo.)

            ReconcileData rd = new()
            {
                Position = transform.position,
                Velocity = _vel,
                Rotation = transform.rotation,
                AngularVelocity = _angVel
            };

            PerformReconcile(rd);
        }

        // -----------------------------
        // PREDICTION
        // -----------------------------

        [Replicate]
        private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            // Ensure dt is always current if tick rate changes at runtime.
            _dt = (float)TimeManager.TickDelta;

            SimulateRotation(rd);
            SimulateMovementAndCollisions(rd);
        }

        [Reconcile]
        private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            transform.SetPositionAndRotation(rd.Position, rd.Rotation);
            _vel = rd.Velocity;
            _angVel = rd.AngularVelocity;
        }

        // -----------------------------
        // SIMULATION
        // -----------------------------

        private void SimulateRotation(ReplicateData rd)
        {
            // Avoid Euler-based pitch/roll error (it becomes unstable near 180 yaw).
            // Instead, compute a target rotation (camera yaw + target tilt), then drive angular velocity
            // by yaw error + up-vector (tilt) error.
            Quaternion targetRot = ComputeTargetRotation(rd);

            float currentYaw = transform.eulerAngles.y;
            float targetYaw = rd.Yaw;
            float yawError = Mathf.DeltaAngle(currentYaw, targetYaw);
            float desiredYawRate = Mathf.Clamp(yawError * yawResponsiveness, -maxYawRate, maxYawRate);
            Vector3 desiredYawOmega = Vector3.up * desiredYawRate;

            Vector3 targetUp = targetRot * Vector3.up;
            Quaternion tiltDelta = Quaternion.FromToRotation(transform.up, targetUp);
            tiltDelta.ToAngleAxis(out float tiltAngleDeg, out Vector3 tiltAxis);
            if (tiltAngleDeg > 180f)
                tiltAngleDeg -= 360f;
            if (float.IsNaN(tiltAxis.x) || tiltAxis.sqrMagnitude < 1e-8f)
                tiltAxis = Vector3.right;
            else
                tiltAxis.Normalize();

            float desiredTiltRate = Mathf.Clamp(tiltAngleDeg * tiltResponsiveness, -maxTiltRate, maxTiltRate);
            Vector3 desiredTiltOmega = tiltAxis * desiredTiltRate;

            Vector3 desiredOmega = desiredYawOmega + desiredTiltOmega;

            Vector3 currentYawOmega = Vector3.Project(_angVel, Vector3.up);
            Vector3 currentTiltOmega = _angVel - currentYawOmega;

            Vector3 desiredYawComponent = Vector3.Project(desiredOmega, Vector3.up);
            Vector3 desiredTiltComponent = desiredOmega - desiredYawComponent;

            Vector3 yawAccel = (desiredYawComponent - currentYawOmega) * torque;
            Vector3 tiltAccel = (desiredTiltComponent - currentTiltOmega) * tiltTorque;
            Vector3 angAccel = yawAccel + tiltAccel;

            // Integrate angular velocity (deg/sec).
            _angVel += angAccel * _dt;

            // Angular drag.
            _angVel -= _dt * angularDrag * _angVel;

            // Integrate rotation from world angular velocity.
            float omegaMag = _angVel.magnitude;
            if (omegaMag > 1e-6f)
            {
                Quaternion dq = Quaternion.AngleAxis(omegaMag * _dt, _angVel / omegaMag);
                transform.rotation = dq * transform.rotation;
            }
        }

        private Quaternion ComputeTargetRotation(ReplicateData rd)
        {
            float targetYaw = rd.Yaw;
            ComputeTargetTiltAngles(rd, targetYaw, out float targetPitch, out float targetRoll);
            Quaternion yawRot = Quaternion.Euler(0f, targetYaw, 0f);
            Quaternion tiltRot = Quaternion.Euler(targetPitch, 0f, targetRoll);
            return yawRot * tiltRot;
        }

        private void ComputeTargetTiltAngles(ReplicateData rd, float yawDegrees, out float targetPitch, out float targetRoll)
        {
            // Desired planar velocity (world), based on yaw heading.
            Quaternion yawRot = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 desiredPlanarDir = yawRot * new Vector3(rd.Move.x, 0f, rd.Move.z);
            float inputMag = Mathf.Clamp01(desiredPlanarDir.magnitude);
            if (inputMag > 1e-6f)
                desiredPlanarDir /= inputMag;

            Vector3 desiredPlanarVel = desiredPlanarDir * (inputMag * maxSpeed);
            Vector3 currentPlanarVel = Vector3.ProjectOnPlane(_vel, Vector3.up);
            Vector3 velError = desiredPlanarVel - currentPlanarVel;
            Vector3 localError = Quaternion.Inverse(yawRot) * velError;

            float pitchFromVel = -localError.z / Mathf.Max(maxSpeed, 0.001f);
            float rollFromVel = localError.x / Mathf.Max(maxSpeed, 0.001f);

            targetPitch = Mathf.Clamp(pitchFromVel * maxTiltAngle, -maxTiltAngle, maxTiltAngle);
            targetRoll = Mathf.Clamp(rollFromVel * maxTiltAngle, -maxTiltAngle, maxTiltAngle);
        }

        private void SimulateMovementAndCollisions(ReplicateData rd)
        {
            // Bank-then-thrust: planar movement comes from tilt; thrust is scaled by how closely we match the desired tilt.
            Quaternion targetRot = ComputeTargetRotation(rd);
            Vector3 targetUp = targetRot * Vector3.up;
            float tiltError = Vector3.Angle(transform.up, targetUp);
            float tiltAlignment = 1f - Mathf.Clamp01(tiltError / Mathf.Max(maxTiltAngle, 0.001f));

            // Use the *un-normalized* planar component of up.
            // Its magnitude is ~sin(tilt), so more bank -> more planar acceleration.
            Vector3 planarThrust = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            if (planarThrust.sqrMagnitude < 1e-8f)
                planarThrust = Vector3.zero;

            float planarInput = Mathf.Clamp01(new Vector2(rd.Move.x, rd.Move.z).magnitude);

            Vector3 accel = Vector3.zero;
            accel += planarThrust * (thrust * planarInput * tiltAlignment);
            accel += Vector3.up * (thrust * Mathf.Clamp01(rd.Move.y));
            
            // Integrate velocity
            _vel += accel * _dt;

            // Linear drag
            _vel -= _dt * linearDrag * _vel;

            // Clamp
            float sp = _vel.magnitude;
            if (sp > maxSpeed)
                _vel *= maxSpeed / sp;

            Vector3 delta = _vel * _dt;

            // Sweep and resolve (can do multiple iterations for robust sliding)
            MoveWithSweeps(ref delta, iterations: 2);

            // Apply whatever delta remains
            transform.position += delta;
        }

        private void MoveWithSweeps(ref Vector3 delta, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                if (delta.sqrMagnitude < 1e-8f)
                    return;

                GetCapsuleWorldPoints(out Vector3 a, out Vector3 b, out float r);

                // Sweep for collisions
                if (!Physics.CapsuleCast(a, b, r, delta.normalized, out RaycastHit hit, delta.magnitude + skin, collisionMask, QueryTriggerInteraction.Ignore))
                    return; // no collision;

                if (TryGetComponent(out KinematicCollisionDamage damage))
                {
                    damage.ApplyKinematicImpact(
                        selfVelocity: _vel,
                        otherRb: hit.rigidbody,
                        otherObject: hit.collider.gameObject,
                        contactPoint: hit.point,
                        contactNormal: hit.normal
                    );
                }

                // Move up to contact (minus skin)
                float moveDist = Mathf.Max(hit.distance - skin, 0f);
                Vector3 movePart = delta.normalized * moveDist;
                transform.position += movePart;

                // Remaining movement after contact
                Vector3 remaining = delta - movePart;

                // 1) Exchange momentum with rigidbody (Drone -> Object)
                if (hit.rigidbody != null)
                {
                    // Impulse direction based on drone velocity at impact
                    Vector3 impulse = _vel * droneMass * pushImpulseScale;
                    hit.rigidbody.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);

                    // 2) Object -> Drone (explicit): use rb velocity to add impulse to drone
                    // This is the "physical object affects drone" part, without letting solver own you.
                    Vector3 relVel = hit.rigidbody.linearVelocity - _vel;
                    Vector3 objImpulse = 0.15f * hit.rigidbody.mass * relVel; // tune transfer
                    _vel += objImpulse / droneMass;
                }

                // 3) Drone collision response (slide + optional bounce)
                Vector3 n = hit.normal;

                // Remove inward component
                float vn = Vector3.Dot(_vel, n);
                if (vn < 0f)
                {
                    // bounce=0 => cancel inward, bounce>0 => reflect a bit
                    _vel = _vel - (1f + bounce) * vn * n;
                }

                // Slide remaining delta along surface
                remaining = Vector3.ProjectOnPlane(remaining, n);
                delta = remaining;

                // Small depenetration nudge
                transform.position += n * skin;
            }
        }

        private void GetCapsuleWorldPoints(out Vector3 a, out Vector3 b, out float r)
        {
            // BoxCollider points in world-space
            r = _boxCollider.size.x * 0.5f * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

            float h = Mathf.Max(_boxCollider.size.y * transform.lossyScale.y, r * 2f);
            float half = (h * 0.5f) - r;

            Vector3 center = transform.TransformPoint(_boxCollider.center);
            Vector3 up = transform.up;

            a = center + up * half;
            b = center - up * half;
        }

        // Call this from explosions, hits, etc.
        public void ApplyExternalImpulse(Vector3 impulse)
        {
            _vel += impulse / droneMass;
        }
    }
}