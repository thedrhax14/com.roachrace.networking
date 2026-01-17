using System;
using RoachRace.Controls;
using RoachRace.Data;
using UnityEngine;

namespace RoachRace.Networking.Effects
{
    [CreateAssetMenu(menuName = "RoachRace/Effects/Status Effect Definition", fileName = "StatusEffectDefinition")]
    public class StatusEffectDefinition : ScriptableObject
    {
        public enum StackMode
        {
            /// <summary>
            /// Only one active instance exists; AddEffect increases stacks on that instance.
            /// </summary>
            Single,

            /// <summary>
            /// Each AddEffect creates a separate active instance.
            /// </summary>
            Multiple
        }

        public enum MaxStacksReachedAction
        {
            /// <summary>
            /// Ignore additional stacks once max is reached.
            /// </summary>
            Ignore,

            /// <summary>
            /// Keep stacks capped, but refresh the effect duration when max is reached.
            /// </summary>
            RefreshDuration
        }

        [Header("Identity")]
        [Tooltip("Optional stable id for debugging / lookups.")]
        [SerializeField] private string effectId = "";

        [Header("Target")]
        [Tooltip("Optional. Preferred targeting method. If set, the runner will resolve the resource by definition reference (instead of enum kind).")]
        [SerializeField] private PlayerResourceDefinition targetResource;

        [Header("Timing")]
        [Tooltip("Seconds between ticks. If 0, the effect applies once on add.")]
        [SerializeField, Min(0f)] private float tickIntervalSeconds = 1f;

        [Tooltip("Total duration in seconds. If 0, lasts forever until removed.")]
        [SerializeField, Min(0f)] private float durationSeconds = 0f;

        [Header("Stacks")]
        [Tooltip("How this effect stacks when applied multiple times.")]
        [SerializeField] private StackMode stackMode = StackMode.Single;

        [Tooltip("Max stacks allowed. 0 = unlimited.")]
        [SerializeField, Min(0)] private int maxStacks = 0;

        [Tooltip("What to do when trying to add stacks beyond MaxStacks.")]
        [SerializeField] private MaxStacksReachedAction onMaxStacksReached = MaxStacksReachedAction.RefreshDuration;

        [Header("Effect")]
        [Tooltip("Delta applied per tick. Positive = recover/add, Negative = deplete/damage.")]
        [SerializeField] private float deltaPerTick = -1f;

        [Tooltip("If true and the target is a NetworkHealth, negative delta will use IDamageable.TryConsume(DamageInfo) instead of PlayerResource.TryConsume(float).")]
        [SerializeField] private bool useDamageInfoForHealth = true;

        [Tooltip("DamageType used when applying health damage via DamageInfo.")]
        [SerializeField] private DamageType healthDamageType = DamageType.Environment;

        [Header("Behavior")]
        [Tooltip("If false, the runner will log when it cannot find the target resource.")]
        [SerializeField] private bool silentIfTargetMissing = true;

        public string EffectId => effectId;
        public PlayerResourceDefinition TargetResource => targetResource;
        public float TickIntervalSeconds => tickIntervalSeconds;
        public float DurationSeconds => durationSeconds;
        public StackMode StackingMode => stackMode;
        public int MaxStacks => maxStacks;
        public MaxStacksReachedAction OnMaxStacksReached => onMaxStacksReached;
        public float DeltaPerTick => deltaPerTick;
        public bool UseDamageInfoForHealth => useDamageInfoForHealth;
        public DamageType HealthDamageType => healthDamageType;
        public bool SilentIfTargetMissing => silentIfTargetMissing;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (tickIntervalSeconds < 0f) tickIntervalSeconds = 0f;
            if (durationSeconds < 0f) durationSeconds = 0f;
            if (maxStacks < 0) maxStacks = 0;
        }
#endif
    }
}
