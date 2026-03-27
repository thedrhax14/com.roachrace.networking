using UnityEngine;

namespace RoachRace.Networking.Effects
{
    [System.Serializable]
    public struct StatusEffectEntry
    {
        public StatusEffectDefinition effect;
        [Min(1)] public int stacks;
    }
}