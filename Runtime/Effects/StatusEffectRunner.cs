using System;
using System.Collections.Generic;
using FishNet.Object;
using RoachRace.Controls;
using RoachRace.Data;
using RoachRace.Networking.Combat;
using UnityEngine;

namespace RoachRace.Networking.Effects
{
    /// <summary>
    /// Server-authoritative runner for passive effects (buffs/debuffs).
    /// 
    /// - Effects are NOT replicated; only their impact on synced resources (eg NetworkHealth) is.
    /// - Designed to be attached to any networked entity (player, rocket, monster).
    /// - For deterministic FishNet tick cadence, use <see cref="StatusEffectTickRunner"/> instead.
    /// - TODO: Read Docs/Design/STATUS_EFFECTS_DESIGN.md before refactoring DamageInfo vs resource consumption.
    /// 
    /// How to use:
    /// - Add this component to the same GameObject (or a parent) that owns the target resources.
    ///   The runner resolves resources via <see cref="PlayerResourceUtils"/>.
    /// - Create one or more <see cref="StatusEffectDefinition"/> assets (RoachRace > Networking > Effects).
    ///   Pick a target resource (definition preferred), set TickIntervalSeconds/DurationSeconds, and DeltaPerTick.
    /// - Auto-apply: populate the <c>autoApply</c> list to apply effects automatically in <see cref="OnStartServer"/>.
    /// - Runtime apply: call <see cref="AddEffect"/> on the server and store the returned handle id to later
    ///   <see cref="RemoveEffect"/> (or <see cref="RemoveEffects"/> by definition).
    /// 
    /// Example: rocket lifetime via health drain
    /// - Add a <see cref="NetworkHealth"/> to the rocket prefab.
    /// - Create a StatusEffectDefinition targeting Health with DeltaPerTick negative (damage) and a tick interval.
    /// - Put that definition in <c>autoApply</c> so rockets start draining HP immediately on spawn.
    /// </summary>
    public class StatusEffectRunner : NetworkBehaviour
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
            public float StartTime;
            public float EndTime;
            public float NextTickTime;
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

        private void Update()
        {
            if (!IsServerInitialized) return;
            if (_effects.Count == 0) return;

            float now = Time.time;

            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = _effects[i];
                if (effect == null || effect.Definition == null)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                // Expire.
                if (effect.EndTime > 0f && now >= effect.EndTime)
                {
                    _effects.RemoveAt(i);
                    continue;
                }

                // Tick.
                if (now >= effect.NextTickTime)
                {
                    float interval = Mathf.Max(0f, effect.Definition.TickIntervalSeconds);

                    // Catch up if we missed ticks (e.g. hitch). Cap to avoid long loops.
                    int maxTicksThisFrame = 8;
                    int ticks = 0;
                    while (now >= effect.NextTickTime && ticks < maxTicksThisFrame)
                    {
                        ApplyTick(effect);
                        ticks++;

                        if (interval <= 0f)
                        {
                            // One-shot effect.
                            effect.NextTickTime = float.PositiveInfinity;
                            break;
                        }

                        effect.NextTickTime += interval;
                    }
                }
            }
        }

        /// <summary>
        /// Server-only: adds an effect and returns a handle id. Returns -1 if not added.
        /// </summary>
        public int AddEffect(StatusEffectDefinition definition, int stacks = 1)
        {
            if (!IsServerInitialized) return -1;
            if (definition == null) return -1;

            stacks = Mathf.Max(1, stacks);

            float now = Time.time;
            float interval = Mathf.Max(0f, definition.TickIntervalSeconds);

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
                            RefreshDuration(existing, definition, now);

                        return existing.HandleId;
                    }

                    int targetStacks = oldStacks + stacks;
                    if (maxStacks > 0)
                        targetStacks = Mathf.Min(maxStacks, targetStacks);

                    int addedStacks = Mathf.Max(0, targetStacks - oldStacks);
                    existing.Stacks = Mathf.Max(1, targetStacks);

                    // If this is a one-shot, apply the incremental delta immediately.
                    if (interval <= 0f && addedStacks > 0)
                    {
                        ApplyDelta(definition, definition.DeltaPerTick * addedStacks);
                        existing.NextTickTime = float.PositiveInfinity;
                    }

                    // If we hit max as a result of this add, optionally refresh.
                    if (maxStacks > 0 && existing.Stacks >= maxStacks &&
                        definition.OnMaxStacksReached == StatusEffectDefinition.MaxStacksReachedAction.RefreshDuration)
                    {
                        RefreshDuration(existing, definition, now);
                    }

                    return existing.HandleId;
                }
            }

            var effect = new ActiveEffect
            {
                HandleId = _nextHandleId++,
                Definition = definition,
                Stacks = stacks,
                StartTime = now,
                EndTime = definition.DurationSeconds > 0f ? now + definition.DurationSeconds : 0f,
                NextTickTime = interval <= 0f ? now : now + interval
            };

            _effects.Add(effect);

            // If it's a one-shot, apply immediately.
            if (interval <= 0f)
            {
                ApplyTick(effect);
                // Prevent double-application on the next Update().
                effect.NextTickTime = float.PositiveInfinity;
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

        private void ApplyTick(ActiveEffect effect)
        {
            StatusEffectDefinition def = effect.Definition;
            if (def == null) return;

            float delta = def.DeltaPerTick * Mathf.Max(1, effect.Stacks);
            ApplyDelta(def, delta);
        }

        private void ApplyDelta(StatusEffectDefinition def, float delta)
        {
            if (def == null) return;
            if (Mathf.Abs(delta) <= 0.0001f) return;

            if (def.TargetResource == null)
            {
                if (!def.SilentIfTargetMissing)
                    Debug.LogWarning($"[{nameof(StatusEffectRunner)}] Effect '{def.name}' has no Target Resource assigned.", gameObject);
                return;
            }

            bool hasResource = PlayerResourceUtils.TryGetResource(gameObject, def.TargetResource, out var resource);
            if (!hasResource || resource == null)
            {
                if (!def.SilentIfTargetMissing)
                    Debug.LogWarning($"[{nameof(StatusEffectRunner)}] Missing target resource '{def.TargetResource.DisplayName}' for effect '{def.name}'.", gameObject);
                return;
            }

            // Health special-case: NetworkHealth has int HP and proper death flow.
            if (resource is NetworkHealth health)
            {
                ApplyToNetworkHealth(def, health, delta);
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

        private static void RefreshDuration(ActiveEffect effect, StatusEffectDefinition def, float now)
        {
            if (effect == null || def == null) return;
            effect.EndTime = def.DurationSeconds > 0f ? now + def.DurationSeconds : 0f;
        }

        private void ApplyToNetworkHealth(StatusEffectDefinition def, NetworkHealth health, float delta)
        {
            if (health == null) return;
            if (!health.IsServerInitialized) return;
            if (!health.IsAlive) return;

            int amount = Mathf.RoundToInt(Mathf.Abs(delta));
            if (amount <= 0) return;

            if (delta > 0f)
            {
                // Heal.
                health.Heal(amount);
                return;
            }

            // Damage.
            if (def.UseDamageInfoForHealth)
            {
                var info = new DamageInfo
                {
                    Amount = amount,
                    Type = def.HealthDamageType,
                    Point = transform.position,
                    Normal = Vector3.up,
                    InstigatorId = -1,
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
                // Fallback: treat as consume if someone extended NetworkHealth to support it.
                health.TryConsume(amount);
            }
        }
    }
}
