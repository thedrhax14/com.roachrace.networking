using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Drives survivor remote character animation (eg CAS/Animator) using:
    /// - Derived kinematics (from VisualRoot world motion) for locomotion.
    /// - Replicated state/events for crouch + fire/reload/use-item.
    ///
    /// Intended usage: attach to the survivor controller NetworkObject which owns the 3P model.
    /// Local player can hide the 3P model; this component is remote-first.
    ///
    /// TODO(RoachRace): Wire survivor gameplay systems (input/weapon/inventory) to call
    /// SetCrouching / TriggerFire / TriggerReload / TriggerUseItem on the owning client.
    /// (This script only replicates + plays animation; it does not detect gameplay actions.)
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Animation/Survivor Remote Animator")]
    public class SurvivorRemoteAnimator : NetworkBehaviour
    {
        [Header("Scene References")]
        [Tooltip("World-space root used to measure motion and to find facing direction. Usually the same VisualRoot you smooth.")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("Animator which reads locomotion floats and receives triggers.")]
        [SerializeField] private Animator animator;

        [Header("Locomotion Params")]
        [SerializeField] private string moveXParam = "MoveX";
        [SerializeField] private string moveYParam = "MoveY";
        [SerializeField] private string gaitParam = "Gait";
        [SerializeField] private string isMovingParam = "IsMoving";

        [Tooltip("Below this planar speed, treat as idle.")]
        [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.08f;

        [Tooltip("Planar speed above which gait becomes Sprint (2).")]
        [SerializeField, Min(0f)] private float sprintSpeedThreshold = 4.2f;

        [Tooltip("How quickly to smooth derived velocity (bigger = snappier).")]
        [SerializeField, Min(0f)] private float velocitySmoothing = 12f;

        [Tooltip("How quickly animator floats follow targets (bigger = snappier).")]
        [SerializeField, Min(0f)] private float paramSmoothing = 10f;

        [Header("State Params")]
        [SerializeField] private string crouchBoolParam = "Crouch";
        [SerializeField] private string isFirstPersonParam = "IsFirstPerson";

        [Header("Event Params")]
        [SerializeField] private string fireTriggerParam = "Fire";
        [SerializeField] private string reloadTriggerParam = "Reload";
        [SerializeField] private string useItemTriggerParam = "UseItem";
        [SerializeField] private string useItemIdParam = "UseItemId";

        private readonly SyncVar<bool> _isCrouching = new(false);

        private int _moveXHash;
        private int _moveYHash;
        private int _gaitHash;
        private int _isMovingHash;
        private int _crouchHash;
        private int _isFirstPersonHash;

        private int _fireTriggerHash;
        private int _reloadTriggerHash;
        private int _useItemTriggerHash;
        private int _useItemIdHash;

        private Vector3 _lastPos;
        private bool _hasLast;
        private Vector3 _smoothedPlanarVel;

        private float _moveX;
        private float _moveY;
        private float _gait;

        private void Awake()
        {
            if (visualRoot == null)
                visualRoot = transform;

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogError($"[{nameof(SurvivorRemoteAnimator)}] Animator is missing on '{gameObject.name}'.", gameObject);
                    throw new NullReferenceException($"[{nameof(SurvivorRemoteAnimator)}] animator is null on '{gameObject.name}'.");
                }
            }

            _moveXHash = Animator.StringToHash(moveXParam);
            _moveYHash = Animator.StringToHash(moveYParam);
            _gaitHash = Animator.StringToHash(gaitParam);
            _isMovingHash = Animator.StringToHash(isMovingParam);
            _crouchHash = Animator.StringToHash(crouchBoolParam);
            _isFirstPersonHash = Animator.StringToHash(isFirstPersonParam);

            _fireTriggerHash = Animator.StringToHash(fireTriggerParam);
            _reloadTriggerHash = Animator.StringToHash(reloadTriggerParam);
            _useItemTriggerHash = Animator.StringToHash(useItemTriggerParam);
            _useItemIdHash = Animator.StringToHash(useItemIdParam);

            animator.SetBool(_isFirstPersonHash, true);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!IsClientInitialized)
                return;

            _isCrouching.OnChange -= OnCrouchChanged;
            _isCrouching.OnChange += OnCrouchChanged;

            // Apply initial state.
            ApplyCrouch(_isCrouching.Value);

            _hasLast = false;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            _isCrouching.OnChange -= OnCrouchChanged;
            _hasLast = false;
        }

        private void LateUpdate()
        {
            if (!IsClientInitialized)
                return;

            if (visualRoot == null || animator == null)
                return;

            float dt = Time.deltaTime;
            if (dt <= 0.000001f)
                return;

            Vector3 pos = visualRoot.position;
            if (!_hasLast)
            {
                _lastPos = pos;
                _hasLast = true;
                return;
            }

            Vector3 vel = (pos - _lastPos) / dt;
            _lastPos = pos;

            Vector3 planarVel = Vector3.ProjectOnPlane(vel, Vector3.up);
            float alphaVel = 1f - Mathf.Exp(-velocitySmoothing * dt);
            _smoothedPlanarVel = Vector3.Lerp(_smoothedPlanarVel, planarVel, alphaVel);

            float speed = _smoothedPlanarVel.magnitude;
            bool isMoving = speed > idleSpeedThreshold;

            Vector3 forward = Vector3.ProjectOnPlane(visualRoot.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 planarDir = isMoving && _smoothedPlanarVel.sqrMagnitude > 0.0001f
                ? _smoothedPlanarVel.normalized
                : Vector3.zero;

            float targetMoveX = Mathf.Clamp(Vector3.Dot(planarDir, right), -1f, 1f);
            float targetMoveY = Mathf.Clamp(Vector3.Dot(planarDir, forward), -1f, 1f);

            // CAS-style input axes: diagonals should also reach +/-1 on both axes.
            float maxAbs = Mathf.Max(Mathf.Abs(targetMoveX), Mathf.Abs(targetMoveY));
            if (maxAbs > 0.0001f)
            {
                float inv = 1f / maxAbs;
                targetMoveX = Mathf.Clamp(targetMoveX * inv, -1f, 1f);
                targetMoveY = Mathf.Clamp(targetMoveY * inv, -1f, 1f);
            }

            float targetGait;
            if (!isMoving)
                targetGait = 0f;
            else if (speed >= sprintSpeedThreshold)
                targetGait = 2f;
            else
                targetGait = 1f;

            float alphaParam = 1f - Mathf.Exp(-paramSmoothing * dt);
            _moveX = Mathf.Lerp(_moveX, targetMoveX, alphaParam);
            _moveY = Mathf.Lerp(_moveY, targetMoveY, alphaParam);
            _gait = Mathf.Lerp(_gait, targetGait, alphaParam);

            animator.SetBool(_isMovingHash, isMoving);
            animator.SetFloat(_moveXHash, _moveX);
            animator.SetFloat(_moveYHash, _moveY);
            animator.SetFloat(_gaitHash, _gait);
        }

        private void OnCrouchChanged(bool prev, bool next, bool asServer)
        {
            ApplyCrouch(next);
        }

        private void ApplyCrouch(bool crouching)
        {
            if (animator == null)
                return;

            animator.SetBool(_crouchHash, crouching);
        }

        // ----- Public API (call from gameplay on the owning client) -----

        public void SetCrouching(bool crouching)
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            SetCrouchingServerRpc(crouching);
        }

        public void TriggerFire()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            FireServerRpc();
        }

        public void TriggerReload()
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            ReloadServerRpc();
        }

        public void TriggerUseItem(int itemAnimId)
        {
            if (!IsClientInitialized || !IsOwner)
                return;

            // Keep payload small.
            byte id = (byte)Mathf.Clamp(itemAnimId, 0, 255);
            UseItemServerRpc(id);
        }

        // ----- Networking -----

        [ServerRpc]
        private void SetCrouchingServerRpc(bool crouching, NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            _isCrouching.Value = crouching;
        }

        [ServerRpc]
        private void FireServerRpc(NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            FireObserversRpc();
        }

        [ObserversRpc]
        private void FireObserversRpc()
        {
            if (animator != null)
                animator.SetTrigger(_fireTriggerHash);
        }

        [ServerRpc]
        private void ReloadServerRpc(NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            ReloadObserversRpc();
        }

        [ObserversRpc]
        private void ReloadObserversRpc()
        {
            if (animator != null)
                animator.SetTrigger(_reloadTriggerHash);
        }

        [ServerRpc]
        private void UseItemServerRpc(byte itemAnimId, NetworkConnection sender = null)
        {
            if (sender != Owner)
                return;

            UseItemObserversRpc(itemAnimId);
        }

        [ObserversRpc]
        private void UseItemObserversRpc(byte itemAnimId)
        {
            if (animator == null)
                return;

            animator.SetInteger(_useItemIdHash, itemAnimId);
            animator.SetTrigger(_useItemTriggerHash);
        }
    }
}
