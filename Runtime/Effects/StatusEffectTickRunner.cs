using System;
using System.Collections.Generic;
using FishNet.Managing.Timing;
using FishNet.Utility.Template;
using RoachRace.Controls;
using RoachRace.Data;
using RoachRace.Networking.Combat;
using UnityEngine;

namespace RoachRace.Networking.Effects
{
    /// <summary>
    /// Tick-driven version of StatusEffectRunner.
    /// 
    /// Runs effect application on FishNet's TimeManager tick cadence via <see cref="TickNetworkBehaviour"/>.
    /// Use this when you want consistent tick timing (independent of Unity frame rate).
    /// 
    /// Notes:
    /// - Effects are NOT replicated; only their impact on synced resources (eg NetworkHealth) is.
    /// - Intended for server-authoritative gameplay; tick processing runs only when server is initialized.
    /// </summary>
    public class StatusEffectTickRunner : TickNetworkBehaviour
    {
        [Serializable]
        private struct AutoEffect
        {
            public StatusEffectDefinition definition;
            public int stacks;
        }

        [Header("Auto Apply")]
        [Tooltip("Effects to apply automatically when the object starts on the server.")]
        [SerializeField] private List<AutoEffect> autoApply = new();

        [Tooltip("If true, auto-applied effects are cleared on server stop.")]
        [SerializeField] private bool clearOnStopServer = true;

        private readonly List<ActiveEffect> _effects = new();
        private int _nextHandleId = 1;

        private sealed class ActiveEffect
        {
            public int HandleId;
            public StatusEffectDefinition Definition;
            public int Stacks;
            public int InstigatorId;

            public uint StartTick;
            public uint EndTick;
            public uint NextTick;
            public uint IntervalTicks;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (autoApply == null) return;
            for (int i = 0; i < autoApply.Count; i++)
            {
                var entry = autoApply[i];
                if (entry.definition == null) continue;
                AddEffect(entry.definition, Mathf.Max(1, entry.stacks));
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();

            if (clearOnStopServer)
                _effects.Clear();
        }

        protected override void TimeManager_OnTick()
        {
            if (!IsServerInitialized) return;
            if (_effects.Count == 0) return;

            uint nowTick = TimeManager.Tick;

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = _effects[i];
                if (effect == null || effect.Definition == null)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                // Expire.
                if (effect.EndTick != 0u && nowTick >= effect.EndTick)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                // Tick.
                if (nowTick >= effect.NextTick)
                {
                    uint intervalTicks = effect.IntervalTicks;

                    // Cap to avoid long loops if timing settings are changed unexpectedly.
                    int maxTicksThisCallback = 8;
                    int ticks = 0;
                    while (nowTick >= effect.NextTick && ticks < maxTicksThisCallback)
                    {
                        ApplyTick(effect);
                        ticks++;

                        if (intervalTicks == 0u)
                        {
                            // One-shot.
                            effect.NextTick = uint.MaxValue;
                            break;
                        }

                        effect.NextTick = AddTicksClamped(effect.NextTick, intervalTicks);
                    }
                }
            }
        }

        /// <summary>
        /// Server-only: adds an effect and returns a handle id. Returns -1 if not added.
        /// </summary>
        /// <param name="definition">The effect definition to add</param>
        /// <param name="stacks">Number of stacks to apply</param>
        /// <param name="instigatorId">NetworkObjectId of the player/entity that caused this effect. Default -1 for environmental effects.</param>
        public int AddEffect(StatusEffectDefinition definition, int stacks = 1, int instigatorId = -1)
        {
            if (definition == null) {
                Debug.LogError($"[{nameof(StatusEffectTickRunner)}] Cannot add effect because definition is null.", gameObject);
                return -1;
            }
            if (!IsServerInitialized) {
                Debug.LogError($"[{nameof(StatusEffectTickRunner)}] Cannot add effect '{definition.name}' because server is not initialized.", gameObject);
                return -1;
            }

            stacks = Mathf.Max(1, stacks);
            Debug.Log($"[{nameof(StatusEffectTickRunner)}] Adding effect '{definition.name}' x{stacks} to '{gameObject.name}'.", gameObject);
            uint nowTick = TimeManager.Tick;
            uint intervalTicks = SecondsToTicksInterval(definition.TickIntervalSeconds);

            // If configured as a single stacked instance, merge into existing.
            if (definition.StackingMode == StatusEffectDefinition.StackMode.Single)
            {
                ActiveEffect existing = null;
                for (int i = 0; i < _effects.Count; i++)
                {
                    if (_effects[i] == null) continue;
                    if (_effects[i].Definition != definition) continue;
                    existing = _effects[i];
                    break;
                }

                if (existing != null)
                {
                    int maxStacks = Mathf.Max(0, definition.MaxStacks);
                    int oldStacks = Mathf.Max(1, existing.Stacks);

                    if (maxStacks > 0 && oldStacks >= maxStacks)
                    {
                        if (definition.OnMaxStacksReached == StatusEffectDefinition.MaxStacksReachedAction.RefreshDuration)
                            RefreshDuration(existing, definition, nowTick);

                        return existing.HandleId;
                    }

                    int targetStacks = oldStacks + stacks;
                    if (maxStacks > 0)
                        targetStacks = Mathf.Min(maxStacks, targetStacks);

                    int addedStacks = Mathf.Max(0, targetStacks - oldStacks);
                    existing.Stacks = Mathf.Max(1, targetStacks);

                    // If this is a one-shot, apply the incremental delta immediately.
                    if (intervalTicks == 0u && addedStacks > 0)
                    {
                        ApplyDelta(definition, definition.DeltaPerTick * addedStacks);
                        existing.NextTick = uint.MaxValue;
                    }

                    // If we hit max as a result of this add, optionally refresh.
                    if (maxStacks > 0 && existing.Stacks >= maxStacks &&
                        definition.OnMaxStacksReached == StatusEffectDefinition.MaxStacksReachedAction.RefreshDuration)
                    {
                        RefreshDuration(existing, definition, nowTick);
                    }

                    return existing.HandleId;
                }
            }

            int instanceMaxStacks = Mathf.Max(0, definition.MaxStacks);
            if (instanceMaxStacks > 0)
                stacks = Mathf.Min(instanceMaxStacks, stacks);

            var effect = new ActiveEffect
            {
                HandleId = _nextHandleId++,
                Definition = definition,
                Stacks = stacks,
                InstigatorId = instigatorId,
                StartTick = nowTick,
                EndTick = GetEndTick(definition, nowTick),
                IntervalTicks = intervalTicks,
                NextTick = intervalTicks == 0u ? nowTick : AddTicksClamped(nowTick, intervalTicks)
            };

            _effects.Add(effect);

            // If it's a one-shot, apply immediately.
            if (intervalTicks == 0u)
            {
                ApplyTick(effect);
                effect.NextTick = uint.MaxValue;
            }

            return effect.HandleId;
        }

        public bool RemoveEffect(int handleId)
        {
            if (!IsServerInitialized) return false;

            for (int i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].HandleId != handleId) continue;
                _effects.RemoveAt(i);
                return true;
            }

            return false;
        }

        public int RemoveEffects(StatusEffectDefinition definition)
        {
            if (!IsServerInitialized) return 0;
            if (definition == null) return 0;

            int removed = 0;
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].Definition != definition) continue;
                _effects.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        public void ClearEffects()
        {
            if (!IsServerInitialized) return;
            _effects.Clear();
        }

        private uint GetEndTick(StatusEffectDefinition definition, uint startTick)
        {
            if (definition == null) return 0u;
            if (definition.DurationSeconds <= 0f) return 0u;

            uint durationTicks = SecondsToTicksDuration(definition.DurationSeconds);
            return AddTicksClamped(startTick, durationTicks);
        }

        private uint SecondsToTicksInterval(float seconds)
        {
            if (seconds <= 0f) return 0u;

            // Round up so small intervals still tick at least once per FishNet tick.
            uint ticks = TimeManager.TimeToTicks(seconds, TickRounding.RoundUp);
            return (uint)Mathf.Max(1u, ticks);
        }

        private uint SecondsToTicksDuration(float seconds)
        {
            if (seconds <= 0f) return 0u;

            uint ticks = TimeManager.TimeToTicks(seconds, TickRounding.RoundUp);
            return (uint)Mathf.Max(1u, ticks);
        }

        private static uint AddTicksClamped(uint a, uint b)
        {
            if (uint.MaxValue - a < b)
                return uint.MaxValue;
            return a + b;
        }

        private void ApplyTick(ActiveEffect effect)
        {
            StatusEffectDefinition def = effect.Definition;
            if (def == null) return;

            float delta = def.DeltaPerTick * Mathf.Max(1, effect.Stacks);
            ApplyDelta(def, delta, effect.InstigatorId);
        }

        private void ApplyDelta(StatusEffectDefinition def, float delta, int instigatorId = -1)
        {
            if (def == null) return;
            if (Mathf.Abs(delta) <= 0.0001f) return;

            if (def.TargetResource == null)
            {
                if (!def.SilentIfTargetMissing)
                    Debug.LogWarning($"[{nameof(StatusEffectTickRunner)}] Effect '{def.name}' has no Target Resource assigned.", gameObject);
                return;
            }

            bool hasResource = PlayerResourceUtils.TryGetResource(gameObject, def.TargetResource, out var resource);
            if (!hasResource || resource == null)
            {
                if (!def.SilentIfTargetMissing)
                    Debug.LogWarning($"[{nameof(StatusEffectTickRunner)}] Missing target resource '{def.TargetResource.DisplayName}' for effect '{def.name}'.", gameObject);
                return;
            }

            // Health special-case: NetworkHealth has int HP and proper death flow.
            if (resource is NetworkHealth health)
            {
                ApplyToNetworkHealth(def, health, delta, instigatorId);
                return;
            }

            // Generic PlayerResource pathway.
            if (delta < 0f)
            {
                // Deplete.
                resource.TryConsume(-delta);
            }
            else
            {
                // Recover.
                resource.Add(delta);
            }
        }

        private void ApplyToNetworkHealth(StatusEffectDefinition def, NetworkHealth health, float delta, int instigatorId = -1)
        {
            if (health == null) return;
            if (!health.IsServerInitialized) return;
            if (!health.IsAlive) return;

            int amount = Mathf.RoundToInt(Mathf.Abs(delta));
            if (amount <= 0) return;

            if (delta > 0f)
            {
                health.Heal(amount);
                return;
            }

            if (def.UseDamageInfoForHealth)
            {
                var info = new DamageInfo
                {
                    Amount = amount,
                    Type = def.HealthDamageType,
                    Point = transform.position,
                    Normal = Vector3.up,
                    InstigatorId = instigatorId,
                    Source = new DamageSource
                    {
                        AttackerName = def.name,
                        AttackerAvatarUrl = string.Empty,
                        SourcePosition = transform.position,
                        WeaponIconKey = string.Empty
                    }
                };

                health.TryConsume(info);
            }
            else
            {
                health.TryConsume(amount);
            }
        }

        private void RefreshDuration(ActiveEffect effect, StatusEffectDefinition def, uint nowTick)
        {
            if (effect == null || def == null) return;
            effect.EndTick = GetEndTick(def, nowTick);
        }
    }
}
