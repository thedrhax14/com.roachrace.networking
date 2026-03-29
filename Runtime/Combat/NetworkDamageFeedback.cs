using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Bridges authoritative health damage into owner-only client notifications.<br/>
    /// Typical usage: place this on the same NetworkObject as <see cref="NetworkHealthObserver"/> so the server can subscribe to damage callbacks and forward victim-only feedback with a <c>TargetRpc</c>.<br/>
    /// Configuration/context: this component does not update UI directly; owner-side systems subscribe via <see cref="AddIncomingDamageObserver"/> and decide how to present the received damage data.
    /// </summary>
    public sealed class NetworkDamageFeedback : NetworkBehaviour, INetworkHealthServerDamageObserver
    {
        [Header("Dependencies")]
        [SerializeField]
        [Tooltip("Authoritative health observer that produces server-side damage callbacks. If empty, the component tries to resolve it on the same GameObject.")]
        private NetworkHealthObserver healthObserver;

        private readonly HashSet<IIncomingDamageObserver> clientDamageObservers = new();
        private readonly HashSet<IDealtDamageObserver> clientDealtDamageObservers = new();

        /// <summary>
        /// Resolves required dependencies and subscribes to authoritative server-side damage callbacks.<br/>
        /// Typical usage: FishNet invokes this when the object starts on the server; external callers should not call it directly.<br/>
        /// Configuration/context: the component expects <see cref="healthObserver"/> on the same NetworkObject so owner-only feedback can be routed using this object's ownership.
        /// </summary>
        public override void OnStartServer()
        {
            base.OnStartServer();
            ResolveHealthObserver();
            healthObserver.AddServerDamageObserver(this);
        }

        /// <summary>
        /// Unsubscribes from authoritative damage callbacks during server teardown.<br/>
        /// Typical usage: FishNet invokes this automatically to avoid stale observer registrations.<br/>
        /// Server/client constraints: server-only lifecycle callback.
        /// </summary>
        public override void OnStopServer()
        {
            if (healthObserver != null)
                healthObserver.RemoveServerDamageObserver(this);

            base.OnStopServer();
        }

        /// <summary>
        /// Adds an owner-local observer that will be notified when the server reports incoming damage for this object.<br/>
        /// Typical usage: a local presentation bridge registers on the owning client and forwards damage into UI models or transient FX systems.<br/>
        /// Server/client constraints: intended for local client usage; registration is safe on host as well.
        /// </summary>
        /// <param name="observer">The local observer to register.</param>
        public void AddIncomingDamageObserver(IIncomingDamageObserver observer)
        {
            if (observer == null)
                return;

            clientDamageObservers.Add(observer);
        }

        /// <summary>
        /// Removes an owner-local observer from this feedback controller.<br/>
        /// Typical usage: UI/presentation bridges unregister during teardown to avoid stale local references.<br/>
        /// Server/client constraints: intended for local client usage; registration is safe on host as well.
        /// </summary>
        /// <param name="observer">The local observer to remove.</param>
        public void RemoveIncomingDamageObserver(IIncomingDamageObserver observer)
        {
            if (observer == null)
                return;

            clientDamageObservers.Remove(observer);
        }

        /// <summary>
        /// Adds an owner-local observer that will be notified when the server confirms damage dealt by this object's owner/source.<br/>
        /// Typical usage: a local presentation bridge registers on the attacking client and forwards dealt damage into UI models or transient hit-confirmation systems.<br/>
        /// Server/client constraints: intended for local client usage; registration is safe on host as well.
        /// </summary>
        /// <param name="observer">The local observer to register.</param>
        public void AddDealtDamageObserver(IDealtDamageObserver observer)
        {
            if (observer == null)
                return;

            clientDealtDamageObservers.Add(observer);
        }

        /// <summary>
        /// Removes an owner-local dealt-damage observer from this feedback controller.<br/>
        /// Typical usage: UI/presentation bridges unregister during teardown to avoid stale local references.<br/>
        /// Server/client constraints: intended for local client usage; registration is safe on host as well.
        /// </summary>
        /// <param name="observer">The local observer to remove.</param>
        public void RemoveDealtDamageObserver(IDealtDamageObserver observer)
        {
            if (observer == null)
                return;

            clientDealtDamageObservers.Remove(observer);
        }

        /// <summary>
        /// Receives authoritative damage on the server and forwards it to the owning client only.<br/>
        /// Typical usage: invoked by <see cref="NetworkHealthObserver"/> after health decreases; this method routes the resulting payload through a FishNet <c>TargetRpc</c>.<br/>
        /// Configuration/context: if the object has no current owner, the feedback is dropped because there is no victim client to notify.
        /// </summary>
        /// <param name="healthObserver">The authoritative health observer that reported the damage.</param>
        /// <param name="damageInfo">Resolved server-side damage attribution and health totals for this hit.</param>
        public void OnNetworkHealthServerDamaged(NetworkHealthObserver healthObserver, NetworkHealthDamageInfo damageInfo)
        {
            int targetConnectionId = Owner != null ? Owner.ClientId : -1;
            int targetObjectId = NetworkObject != null ? NetworkObject.ObjectId : -1;

            if (Owner == null)
            {
                SendDealtDamageToInstigator(damageInfo, targetConnectionId, targetObjectId);
                return;
            }
            DamageTakenTargetRpc(
                Owner,
                damageInfo.PreviousHealth,
                damageInfo.CurrentHealth,
                damageInfo.DamageAmount,
                damageInfo.WeaponIconKey,
                damageInfo.InstigatorConnectionId,
                damageInfo.InstigatorObjectId,
                damageInfo.HasSourceWorldPosition,
                damageInfo.SourceWorldPosition);

            SendDealtDamageToInstigator(damageInfo, targetConnectionId, targetObjectId);
        }

        /// <summary>
        /// Receives an owner-only damage notification from the server and fans it out to local observers.<br/>
        /// Typical usage: called internally by FishNet when the owner receives damage feedback for this controller/object.<br/>
        /// Server/client constraints: owning-client only.
        /// </summary>
        /// <param name="owner">The FishNet owner connection targeted by the server.</param>
        /// <param name="previousHealth">Health total before the damage was applied.</param>
        /// <param name="currentHealth">Health total after the damage was applied.</param>
        /// <param name="damageAmount">Positive magnitude of the applied health loss.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon/effect key used for attribution.</param>
        /// <param name="instigatorConnectionId">ClientId of the instigator connection, or -1 for environment/unknown.</param>
        /// <param name="instigatorObjectId">NetworkObjectId of the instigator object, or -1 for environment/unknown.</param>
        /// <param name="hasSourceWorldPosition">Whether a world-space source position was supplied for this hit.</param>
        /// <param name="sourceWorldPosition">World-space source position when <paramref name="hasSourceWorldPosition"/> is true.</param>
        [TargetRpc]
        private void DamageTakenTargetRpc(NetworkConnection owner, int previousHealth, int currentHealth, int damageAmount, string weaponIconKey, int instigatorConnectionId, int instigatorObjectId, bool hasSourceWorldPosition, Vector3 sourceWorldPosition)
        {
            var damageInfo = new NetworkHealthDamageInfo(previousHealth, currentHealth, damageAmount, weaponIconKey, instigatorConnectionId, instigatorObjectId, transform.position, hasSourceWorldPosition, sourceWorldPosition);
            NotifyClientDamageObservers(damageInfo);
        }

        /// <summary>
        /// Receives an owner-only dealt-damage notification from the server and fans it out to local attacker-side observers.<br/>
        /// Typical usage: called internally by FishNet when the attacker owner receives hit confirmation for damage they dealt.<br/>
        /// Server/client constraints: owning-client only.
        /// </summary>
        /// <param name="owner">The FishNet owner connection targeted by the server.</param>
        /// <param name="damageAmount">Positive magnitude of the applied damage.</param>
        /// <param name="weaponIconKey">Optional UI-facing weapon/effect key used for attribution.</param>
        /// <param name="targetConnectionId">ClientId of the damaged target owner, or -1 when unknown/non-player.</param>
        /// <param name="targetObjectId">NetworkObjectId of the damaged target, or -1 when unknown.</param>
        /// <param name="targetHealthAfterHit">Resolved target health after the hit was applied.</param>
        /// <param name="targetWorldPosition">World-space position of the damaged target when the hit was applied.</param>
        /// <param name="isFatal">Whether the hit reduced the target to zero or below.</param>
        [TargetRpc]
        private void DamageDealtTargetRpc(NetworkConnection owner, int damageAmount, string weaponIconKey, int targetConnectionId, int targetObjectId, int targetHealthAfterHit, Vector3 targetWorldPosition, bool isFatal)
        {
            var damageInfo = new DealtDamageInfo(damageAmount, weaponIconKey, targetConnectionId, targetObjectId, targetHealthAfterHit, targetWorldPosition, isFatal);
            NotifyClientDealtDamageObservers(damageInfo);
        }

        /// <summary>
        /// Resolves the required <see cref="NetworkHealthObserver"/> dependency on the same GameObject.<br/>
        /// Typical usage: called during server startup before observer registration begins.<br/>
        /// Configuration/context: this intentionally fails fast because routing victim feedback from a different NetworkObject would produce incorrect ownership semantics.
        /// </summary>
        private void ResolveHealthObserver()
        {
            if (healthObserver != null)
                return;

            if (TryGetComponent(out healthObserver))
                return;

            Debug.LogError($"[{nameof(NetworkDamageFeedback)}] Missing required reference on '{gameObject.name}': {nameof(healthObserver)}.", gameObject);
            throw new InvalidOperationException($"[{nameof(NetworkDamageFeedback)}] Missing required reference on '{gameObject.name}': {nameof(healthObserver)}.");
        }

        /// <summary>
        /// Attempts to route attacker-side hit confirmation to the instigator owner when the damage attribution identifies a player connection.<br/>
        /// Typical usage: called after the victim-side feedback is sent so the attacker owner can also receive damage-dealt confirmation.<br/>
        /// Configuration/context: self-damage does not produce separate attacker feedback because the owner is already receiving victim feedback.
        /// </summary>
        /// <param name="damageInfo">Resolved authoritative damage information for the hit.</param>
        /// <param name="targetConnectionId">ClientId of the damaged target owner, or -1 when unknown/non-player.</param>
        /// <param name="targetObjectId">NetworkObjectId of the damaged target, or -1 when unknown.</param>
        private void SendDealtDamageToInstigator(NetworkHealthDamageInfo damageInfo, int targetConnectionId, int targetObjectId)
        {
            if (damageInfo.InstigatorConnectionId < 0)
                return;

            if (targetConnectionId >= 0 && damageInfo.InstigatorConnectionId == targetConnectionId)
                return;

            if (!NetworkPlayerRegistry.TryGetPlayer(damageInfo.InstigatorConnectionId, out NetworkPlayer instigatorPlayer) || instigatorPlayer == null)
                return;

            NetworkConnection instigatorOwner = instigatorPlayer.Owner;
            if (instigatorOwner == null)
                return;

            DamageDealtTargetRpc(
                instigatorOwner,
                damageInfo.DamageAmount,
                damageInfo.WeaponIconKey,
                targetConnectionId,
                targetObjectId,
                damageInfo.CurrentHealth,
                damageInfo.TargetWorldPosition,
                damageInfo.IsFatal);
        }

        /// <summary>
        /// Notifies all registered owner-local observers about a confirmed damage event.<br/>
        /// Typical usage: called internally after <see cref="DamageTakenTargetRpc"/> reconstructs the authoritative payload on the owning client.<br/>
        /// Configuration/context: snapshot iteration allows observers to safely add or remove subscriptions during callback.
        /// </summary>
        /// <param name="damageInfo">Resolved authoritative damage information delivered by the server.</param>
        private void NotifyClientDamageObservers(NetworkHealthDamageInfo damageInfo)
        {
            if (clientDamageObservers.Count == 0)
                return;

            foreach (var observer in clientDamageObservers)
            {
                if (observer == null)
                    continue;

                observer.OnIncomingDamage(this, damageInfo);
            }
        }

        /// <summary>
        /// Notifies all registered owner-local dealt-damage observers about a confirmed outgoing damage event.<br/>
        /// Typical usage: called internally after <see cref="DamageDealtTargetRpc"/> reconstructs the authoritative payload on the attacking client.<br/>
        /// Configuration/context: snapshot iteration is unnecessary here because observer registration is local and stable, but null observers are still ignored defensively.
        /// </summary>
        /// <param name="damageInfo">Resolved authoritative dealt-damage information delivered by the server.</param>
        private void NotifyClientDealtDamageObservers(DealtDamageInfo damageInfo)
        {
            if (clientDealtDamageObservers.Count == 0)
                return;

            foreach (var observer in clientDealtDamageObservers)
            {
                if (observer == null)
                    continue;

                observer.OnDealtDamage(this, damageInfo);
            }
        }
    }
}