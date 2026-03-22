using FishNet;
using FishNet.Connection;
using FishNet.Object;
using RoachRace.Data;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Scene-level network broadcaster for death log entries.
    /// 
    /// Setup:
    /// - Add this to a scene NetworkObject which is observed by all clients (e.g. NetworkGameManager object).
    /// - Assign a DeathLogModel ScriptableObject.
    /// 
    /// Server:
    /// - Call <see cref="ServerPublishDeath"/>.
    /// 
    /// Clients:
    /// - Receives entries via ObserversRpc and publishes them into the model.
    /// </summary>
    public sealed class DeathLogBroadcaster : NetworkBehaviour
    {
        [SerializeField] private DeathLogModel deathLogModel;

        public static DeathLogBroadcaster Instance { get; private set; }

        private void Awake()
        {
            // Prefer the first enabled instance.
            if (Instance == null)
                Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [Server]
        public void ServerPublishDeath(NetworkHealth victimHealth, DamageInfo damageInfo)
        {
            if (!IsServerInitialized) return;

            DeathLogEntry entry = BuildEntry(victimHealth, damageInfo);
            RpcPublish(entry);
        }

        private DeathLogEntry BuildEntry(NetworkHealth victimHealth, DamageInfo damageInfo)
        {
            var entry = new DeathLogEntry
            {
                Attacker = BuildAttacker(damageInfo),
                Victim = BuildVictim(victimHealth),
                WeaponIconKey = damageInfo.Source.WeaponIconKey,
                ServerTick = InstanceFinder.TimeManager != null ? InstanceFinder.TimeManager.Tick : 0u
            };

            return entry;
        }

        private static DeathLogActor BuildAttacker(DamageInfo damageInfo)
        {
            var actor = new DeathLogActor
            {
                Name = damageInfo.Source.AttackerName,
                AvatarUrl = damageInfo.Source.AttackerAvatarUrl,
                TeamId = -1
            };

            // Best-effort: resolve team/name/avatar from the instigator ClientId (real user) by finding an owned
            // NetworkObject with a NetworkPlayer component.
            if (InstanceFinder.NetworkManager != null && damageInfo.InstigatorId >= 0)
            {
                if (InstanceFinder.NetworkManager.ServerManager != null &&
                    InstanceFinder.NetworkManager.ServerManager.Clients != null &&
                    InstanceFinder.NetworkManager.ServerManager.Clients.TryGetValue(damageInfo.InstigatorId, out NetworkConnection conn) &&
                    conn != null &&
                    conn.Objects != null)
                {
                    foreach (var ownedObject in conn.Objects)
                    {
                        if (ownedObject == null) continue;

                        var player = ownedObject.GetComponentInParent<RoachRace.Networking.NetworkPlayer>();
                        if (player == null) continue;

                        if (string.IsNullOrWhiteSpace(actor.Name)) actor.Name = player.PlayerName;
                        if (string.IsNullOrWhiteSpace(actor.AvatarUrl)) actor.AvatarUrl = player.ImageUrl;
                        actor.TeamId = (int)player.Team;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(actor.Name))
                actor.Name = "Environment";

            return actor;
        }

        private static DeathLogActor BuildVictim(NetworkHealth victimHealth)
        {
            var actor = new DeathLogActor
            {
                Name = victimHealth != null ? victimHealth.gameObject.name : "?",
                AvatarUrl = string.Empty,
                TeamId = -1
            };

            if (victimHealth != null)
            {
                var player = victimHealth.GetComponentInParent<RoachRace.Networking.NetworkPlayer>();
                if (player != null)
                {
                    actor.Name = player.PlayerName;
                    actor.AvatarUrl = player.ImageUrl;
                    actor.TeamId = (int)player.Team;
                }
            }

            return actor;
        }

        [ObserversRpc]
        private void RpcPublish(DeathLogEntry entry)
        {
            if (deathLogModel != null)
                deathLogModel.Publish(entry);
        }
    }
}
