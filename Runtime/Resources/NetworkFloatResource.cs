using FishNet.Object;
using FishNet.Object.Synchronizing;
using RoachRace.Controls;
using UnityEngine;
using UnityEngine.Serialization;

namespace RoachRace.Networking.Resources
{
    /// <summary>
    /// Common implementation for server-authoritative float-based resources (eg stamina, energy).
    /// 
    /// Notes:
    /// - The specific meaning/identity/UI of the resource is provided by the PlayerResourceDefinition
    ///   assigned on the component (via PlayerResource base).
    /// </summary>
    public abstract class NetworkFloatResource : PlayerResource
    {
        [SerializeField, Min(1f)]
        [FormerlySerializedAs("maxEnergy")]
        [FormerlySerializedAs("maxStamina")]
        private float maxValue = 100f;

        private readonly SyncVar<float> _current = new(100f);

        public override float Current => _current.Value;
        public override float Max => maxValue;

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (maxValue <= 0f) maxValue = 1f;
            _current.Value = maxValue;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _current.OnChange += OnValueChanged;
            NotifyChanged();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _current.OnChange -= OnValueChanged;
        }

        private void OnValueChanged(float prev, float next, bool asServer)
        {
            NotifyChanged();
        }

        [Server]
        public override bool TryConsume(float amount)
        {
            if (amount <= 0f) return false;
            if (!IsServerInitialized) return false;

            float current = _current.Value;
            if (current < amount) return false;

            _current.Value = current - amount;
            return true;
        }

        [Server]
        public override void Add(float amount)
        {
            if (amount <= 0f) return;
            if (!IsServerInitialized) return;

            if (maxValue <= 0f) maxValue = 1f;
            _current.Value = Mathf.Min(maxValue, _current.Value + amount);
        }
    }
}
