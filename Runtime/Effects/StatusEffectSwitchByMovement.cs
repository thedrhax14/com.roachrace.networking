using FishNet.Utility.Template;
using RoachRace.Networking;
using UnityEngine;

namespace RoachRace.Networking.Effects
{
    /// <summary>
    /// Server-only tick-driven helper which switches between two status effects based on whether the entity is "moving".
    /// 
    /// Intended use:
    /// - Drain health while moving
    /// - Regenerate health while idle
    /// 
    /// Movement detection:
    /// - If a <see cref="ServerAuthDroneController"/> is present, uses its latest move input magnitude.
    /// - Otherwise falls back to Rigidbody speed.
    /// </summary>
    public class StatusEffectSwitchByMovement : TickNetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private StatusEffectTickRunner runner;
        [SerializeField] private ServerAuthDroneController droneController;
        [SerializeField] private Rigidbody targetRigidbody;

        [Header("Movement")]
        [Tooltip("If a drone controller is present, moving is defined as input magnitude >= this threshold.")]
        [SerializeField, Min(0f)] private float inputMagnitudeThreshold = 0.05f;

        [Tooltip("Fallback movement threshold (meters/second) when no drone controller is present.")]
        [SerializeField, Min(0f)] private float speedThreshold = 0.1f;

        [Tooltip("If true, uses XZ speed only (ignores vertical).")]
        [SerializeField] private bool horizontalOnly = true;

        [Header("Effects")]
        [Tooltip("Effect to apply while moving (eg. HP drain).")]
        [SerializeField] private StatusEffectDefinition movingEffect;
        [SerializeField, Min(1)] private int movingStacks = 1;

        [Tooltip("Effect to apply while idle (eg. HP regen).")]
        [SerializeField] private StatusEffectDefinition idleEffect;
        [SerializeField, Min(1)] private int idleStacks = 1;

        private int _movingHandle = -1;
        private int _idleHandle = -1;
        private bool _lastMoving;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (runner == null && !TryGetComponent(out runner))
            {
                Debug.LogError($"[{nameof(StatusEffectSwitchByMovement)}] Runner is not assigned and was not found on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(StatusEffectSwitchByMovement)}] Missing {nameof(StatusEffectTickRunner)} on '{gameObject.name}'.");
            }

            if (droneController == null)
                TryGetComponent(out droneController);

            if (targetRigidbody == null)
                TryGetComponent(out targetRigidbody);

            if (movingEffect == null)
            {
                Debug.LogError($"[{nameof(StatusEffectSwitchByMovement)}] MovingEffect is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(StatusEffectSwitchByMovement)}] MovingEffect is null on '{gameObject.name}'.");
            }

            if (idleEffect == null)
            {
                Debug.LogError($"[{nameof(StatusEffectSwitchByMovement)}] IdleEffect is not assigned on '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(StatusEffectSwitchByMovement)}] IdleEffect is null on '{gameObject.name}'.");
            }

            // Initialize to idle or moving immediately.
            bool isMoving = ComputeIsMoving();
            ApplyState(isMoving, force: true);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            // Clean up any handles we own.
            if (runner != null)
            {
                if (_movingHandle != -1) runner.RemoveEffect(_movingHandle);
                if (_idleHandle != -1) runner.RemoveEffect(_idleHandle);
            }

            _movingHandle = -1;
            _idleHandle = -1;
        }

        protected override void TimeManager_OnTick()
        {
            if (!IsServerInitialized) return;
            if (runner == null) return;

            bool isMoving = ComputeIsMoving();
            ApplyState(isMoving, force: false);
        }

        private bool ComputeIsMoving()
        {
            if (droneController != null)
                return droneController.LatestMoveInputMagnitude >= inputMagnitudeThreshold;

            if (targetRigidbody == null)
                return false;

            Vector3 v = targetRigidbody.linearVelocity;
            if (horizontalOnly)
                v.y = 0f;

            return v.magnitude >= speedThreshold;
        }

        private void ApplyState(bool isMoving, bool force)
        {
            if (!force && isMoving == _lastMoving)
                return;

            _lastMoving = isMoving;

            if (isMoving)
            {
                // Ensure idle is removed.
                if (_idleHandle != -1)
                {
                    runner.RemoveEffect(_idleHandle);
                    _idleHandle = -1;
                }

                if (_movingHandle == -1)
                    _movingHandle = runner.AddEffect(movingEffect, movingStacks);
            }
            else
            {
                // Ensure moving is removed.
                if (_movingHandle != -1)
                {
                    runner.RemoveEffect(_movingHandle);
                    _movingHandle = -1;
                }

                if (_idleHandle == -1)
                    _idleHandle = runner.AddEffect(idleEffect, idleStacks);
            }
        }
    }
}
