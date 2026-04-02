using System;
using RoachRace.Networking.Effects;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Marks a collider as a status-effect hitbox for precision damage routing.<br>
    /// Typical usage: attach one component per player collider area (for example, body = 1x and head = 2x), then have raycast-driven combat code read <see cref="EffectMultiplier"/> before applying the effect to the character's authoritative <see cref="StatusEffectTickRunner"/>.<br>
    /// Configuration/context: place this on the collider GameObject itself, and keep the actual tick runner on the same character hierarchy rather than on the collider object.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Combat/Status Effect Hitbox")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class StatusEffectHitbox : MonoBehaviour
    {
        [Header("Status Effect")]
        [Tooltip("Multiplier applied to incoming status-effect stacks when this hitbox is struck. Body can stay at 1 and head can be set to 2 or any other tuned value.")]
        [SerializeField, Min(0f)] private float effectMultiplier = 1f;

        [Tooltip("Optional authoring label for the hitbox area, such as Body or Head. This is only for debugging and inspector clarity.")]
        [SerializeField] private string hitboxId = "Body";
        [Tooltip("Optional colliders to ignore when this hitbox is struck, such as the character's own weapon colliders.")]
        [SerializeField] private Collider[] ignoredColliders;

        private Collider _hitCollider;
        private StatusEffectTickRunner _tickRunner;

        /// <summary>
        /// Gets the multiplier applied to incoming status-effect stacks when this hitbox is struck.<br>
        /// Typical usage: set the body hitbox to 1 and the head hitbox to 2 for precision damage tuning.<br>
        /// Configuration/context: values below 0 are clamped to 0 in the Inspector and at validation time.
        /// </summary>
        public float EffectMultiplier => effectMultiplier;

        /// <summary>
        /// Gets the optional authoring label for this hitbox area.<br>
        /// Typical usage: debugging, inspector organization, or combat logging where you want to distinguish body from head hits.<br>
        /// Configuration/context: this value does not participate in combat calculations.
        /// </summary>
        public string HitboxId => hitboxId;

        /// <summary>
        /// Gets the collider that owns this hitbox component.<br>
        /// Typical usage: raycast status-effect code can inspect this when it needs the exact collider that was struck.<br>
        /// Configuration/context: resolved during <see cref="Awake"/> and guaranteed to exist by <see cref="RequireComponentAttribute"/>.
        /// </summary>
        public Collider HitCollider => _hitCollider;

        /// <summary>
        /// Gets the authoritative status-effect runner found in the parent hierarchy.<br>
        /// Typical usage: once a raycast resolves this hitbox, apply the scaled effect to the returned runner instead of trying to find one from the collider again.<br>
        /// Configuration/context: the runner is expected on the same character hierarchy, not on the hitbox object itself.
        /// </summary>
        public StatusEffectTickRunner TickRunner => _tickRunner;

        /// <summary>
        /// Validates the collider and parent tick-runner wiring before gameplay starts.<br>
        /// Typical usage: Unity invokes this during object initialization so the component can fail fast if the hitbox is not attached to a usable collider or character hierarchy.<br>
        /// Configuration/context: the hitbox must live on a collider GameObject and have a <see cref="StatusEffectTickRunner"/> somewhere above it in the hierarchy.
        /// </summary>
        private void Awake()
        {
            if (!TryGetComponent(out _hitCollider) || _hitCollider == null)
            {
                Debug.LogError($"[{nameof(StatusEffectHitbox)}] Missing required reference on '{gameObject.name}': collider.", gameObject);
                throw new InvalidOperationException($"[{nameof(StatusEffectHitbox)}] Missing required reference on '{gameObject.name}': collider.");
            }

            _tickRunner = GetComponentInParent<StatusEffectTickRunner>(includeInactive: true);
            if (_tickRunner == null)
            {
                Debug.LogError($"[{nameof(StatusEffectHitbox)}] Missing required parent {nameof(StatusEffectTickRunner)} on '{gameObject.name}'.", gameObject);
                throw new InvalidOperationException($"[{nameof(StatusEffectHitbox)}] Missing required parent {nameof(StatusEffectTickRunner)} on '{gameObject.name}'.");
            }
            foreach (Collider ignored in ignoredColliders)
            {
                if (ignored == null)
                {
                    Debug.LogWarning($"[{nameof(StatusEffectHitbox)}] Null entry found in ignoredColliders on '{gameObject.name}'. Please remove null entries from the array.", gameObject);
                    continue;
                }
                if (ignored == _hitCollider)
                {
                    Debug.LogError($"[{nameof(StatusEffectHitbox)}] Hitbox collider cannot be in its own ignoredColliders list on '{gameObject.name}'.", gameObject);
                    throw new InvalidOperationException($"[{nameof(StatusEffectHitbox)}] Hitbox collider cannot be in its own ignoredColliders list on '{gameObject.name}'.");
                }
                Physics.IgnoreCollision(_hitCollider, ignored);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Keeps the hitbox multiplier non-negative while editing the prefab or scene.<br>
        /// Typical usage: prevents accidental negative tuning in the Inspector from producing invalid combat multipliers.<br>
        /// Configuration/context: editor-only validation hook.
        /// </summary>
        private void OnValidate()
        {
            effectMultiplier = Mathf.Max(0f, effectMultiplier);
        }
#endif

        /// <summary>
        /// Scales an incoming stack count by this hitbox's multiplier.<br>
        /// Typical usage: raycast-driven combat code can call this before applying the final effect to the authoritative runner.<br>
        /// Configuration/context: the result is rounded to the nearest whole stack because the existing status-effect pipeline is discrete.
        /// </summary>
        /// <param name="baseStacks">Incoming base stack count before hitbox scaling.</param>
        /// <returns>The scaled stack count, clamped to 0 or higher.</returns>
        public int ScaleStacks(int baseStacks)
        {
            if (baseStacks <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, Mathf.RoundToInt(baseStacks * effectMultiplier));
        }

        /// <summary>
        /// Applies a status effect through the parent runner after scaling the requested stack count by this hitbox multiplier.<br>
        /// Typical usage: raycast combat code resolves a hit collider, gets this component, and calls this method instead of manually finding the runner.
        /// Configuration/context: this is server-side gameplay routing only; the runner still enforces its own server initialization checks.
        /// </summary>
        /// <param name="definition">The status effect definition to apply.</param>
        /// <param name="stacks">Incoming stack count before hitbox scaling.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection, or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object, or -1 for environment/unknown.</param>
        /// <param name="hasSourceWorldPosition">Whether a world-space source position was supplied for this effect.</param>
        /// <param name="sourceWorldPosition">World-space source position of the effect when <paramref name="hasSourceWorldPosition"/> is true.</param>
        /// <param name="hasTargetWorldPosition">Whether a world-space target hit point was supplied for this effect.</param>
        /// <param name="targetWorldPosition">World-space hit point on the damaged target when <paramref name="hasTargetWorldPosition"/> is true.</param>
        /// <returns>The handle returned by the underlying tick runner, or -1 when the effect was not applied.</returns>
        public int ApplyEffect(StatusEffectDefinition definition, int stacks = 1, int instigatorConnectionId = -1, int instigatorObjectId = -1, bool hasSourceWorldPosition = false, Vector3 sourceWorldPosition = default, bool hasTargetWorldPosition = false, Vector3 targetWorldPosition = default)
        {
            if (definition == null)
            {
                Debug.LogError($"[{nameof(StatusEffectHitbox)}] Cannot apply effect because definition is null on '{gameObject.name}'.", gameObject);
                return -1;
            }

            if (_tickRunner == null)
            {
                Debug.LogError($"[{nameof(StatusEffectHitbox)}] Cannot apply effect '{definition.name}' because parent {nameof(StatusEffectTickRunner)} is missing on '{gameObject.name}'.", gameObject);
                return -1;
            }

            int scaledStacks = ScaleStacks(stacks);
            if (scaledStacks <= 0)
            {
                return -1;
            }

            return _tickRunner.AddEffect(definition, scaledStacks, instigatorConnectionId, instigatorObjectId, hasSourceWorldPosition, sourceWorldPosition, hasTargetWorldPosition, targetWorldPosition);
        }

        /// <summary>
        /// Tries to resolve a status-effect hitbox from a collider hit.<br>
        /// Typical usage: raycast combat code can call this on the hit collider and, when successful, route the effect through the returned hitbox.
        /// Configuration/context: searches the hit collider and its parents so nested collider setups can still resolve a single hitbox component.
        /// </summary>
        /// <param name="collider">The collider that was hit.</param>
        /// <param name="hitbox">The resolved hitbox component when one is found.</param>
        /// <returns><c>true</c> when a hitbox component was found; otherwise <c>false</c>.</returns>
        public static bool TryResolve(Collider collider, out StatusEffectHitbox hitbox)
        {
            hitbox = null;

            if (collider == null)
            {
                return false;
            }

            return collider.TryGetComponent(out hitbox) || collider.GetComponentInParent<StatusEffectHitbox>(includeInactive: true) is StatusEffectHitbox parentHitbox && (hitbox = parentHitbox) != null;
        }
    }
}