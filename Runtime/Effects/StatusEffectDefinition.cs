using System;
using RoachRace.Data;
using RoachRace.Interaction;
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
        [Tooltip("Required. Target asset (ItemDefinition) which determines what is consumed/added each tick.")]
        [SerializeField] private ItemDefinition targetAsset;

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

        public string EffectId => effectId;
        public ItemDefinition TargetAsset => targetAsset;
        public float TickIntervalSeconds => tickIntervalSeconds;
        public float DurationSeconds => durationSeconds;
        public StackMode StackingMode => stackMode;
        public int MaxStacks => maxStacks;
        public MaxStacksReachedAction OnMaxStacksReached => onMaxStacksReached;
        public float DeltaPerTick => deltaPerTick;

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
