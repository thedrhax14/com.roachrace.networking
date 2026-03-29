using System;
using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Bridges owner-local health damage feedback into an <see cref="IncomingDamageModel"/>.<br/>
    /// Typical usage: place this on the same GameObject as <see cref="NetworkDamageFeedback"/> so the owning client can publish authoritative incoming damage into UI state without directly coupling the feedback transport to any specific view.<br/>
    /// Configuration/context: if <see cref="incomingDamageModel"/> is not assigned explicitly, the bridge attempts to resolve it from <see cref="InventoryGlobals"/>.
    /// </summary>
    public sealed class IncomingDamageModelBridge : MonoBehaviour, IIncomingDamageObserver
    {
        [Header("Dependencies")]
        [SerializeField]
        [Tooltip("Owner-local feedback controller that receives incoming damage TargetRpc notifications. If empty, the component tries to resolve it on the same GameObject.")]
        private NetworkDamageFeedback damageFeedbackController;

        [SerializeField]
        [Tooltip("Owner-local UI model that publishes incoming damage notifications. If empty, the component tries to resolve it from InventoryGlobals.")]
        private IncomingDamageModel incomingDamageModel;

        private bool isSubscribed;

        /// <summary>
        /// Resolves dependencies and subscribes to owner-local damage feedback when the component becomes active.<br/>
        /// Typical usage: Unity invokes this during object activation on clients; the bridge only subscribes when it can resolve both the feedback controller and the UI model.<br/>
        /// Configuration/context: explicit references take precedence over InventoryGlobals-based fallback.
        /// </summary>
        private void OnEnable()
        {
            ResolveDamageFeedbackController();
            ResolveIncomingDamageModel();

            if (damageFeedbackController == null || incomingDamageModel == null) {
                Debug.LogError($"[{nameof(IncomingDamageModelBridge)}] Cannot subscribe to damage feedback for '{gameObject.name}' because required references are missing. DamageFeedbackController: {(damageFeedbackController != null ? damageFeedbackController.name : "null")}, IncomingDamageModel: {(incomingDamageModel != null ? incomingDamageModel.name : "null")}.", gameObject);
                return;
            }

            damageFeedbackController.AddIncomingDamageObserver(this);
            isSubscribed = true;
        }

        /// <summary>
        /// Unsubscribes from owner-local damage feedback when the component is disabled.<br/>
        /// Typical usage: Unity invokes this during teardown or deactivation to avoid stale observer references.<br/>
        /// Configuration/context: safe to call even when the subscription was never established.
        /// </summary>
        private void OnDisable()
        {
            if (!isSubscribed || damageFeedbackController == null)
                return;

            damageFeedbackController.RemoveIncomingDamageObserver(this);
            isSubscribed = false;
        }

        /// <summary>
        /// Publishes owner-local incoming damage into the configured UI model.<br/>
        /// Typical usage: invoked by <see cref="NetworkDamageFeedback"/> after receiving authoritative damage feedback from the server.<br/>
        /// Server/client constraints: owning-client only; this should not be used for gameplay-authoritative logic.
        /// </summary>
        /// <param name="feedbackController">The local feedback controller that received the authoritative damage notification.</param>
        /// <param name="damageInfo">Resolved authoritative damage information forwarded from the server.</param>
        public void OnIncomingDamage(NetworkDamageFeedback feedbackController, NetworkHealthDamageInfo damageInfo)
        {
            if (incomingDamageModel == null) {
                Debug.LogError($"[{nameof(IncomingDamageModelBridge)}] Cannot publish incoming damage for '{gameObject.name}' because {nameof(incomingDamageModel)} reference is missing.", gameObject);
                return;
            }

            incomingDamageModel.Publish(new IncomingDamageEntry(
                damageInfo.PreviousHealth,
                damageInfo.CurrentHealth,
                damageInfo.DamageAmount,
                damageInfo.WeaponIconKey,
                damageInfo.InstigatorConnectionId,
                damageInfo.InstigatorObjectId));
        }

        /// <summary>
        /// Resolves the owner-local damage feedback controller dependency on the same GameObject.<br/>
        /// Typical usage: called during <see cref="OnEnable"/> before subscription begins.<br/>
        /// Configuration/context: missing controller is logged as an error because the bridge cannot function without the owner-local notification source.
        /// </summary>
        private void ResolveDamageFeedbackController()
        {
            if (damageFeedbackController != null)
                return;

            if (TryGetComponent(out damageFeedbackController))
                return;

            Debug.LogError($"[{nameof(IncomingDamageModelBridge)}] Missing required reference on '{gameObject.name}': {nameof(damageFeedbackController)}.", gameObject);
        }

        /// <summary>
        /// Resolves the incoming-damage UI model, preferring an explicit reference and falling back to <see cref="InventoryGlobals"/>.<br/>
        /// Typical usage: called during <see cref="OnEnable"/> before the bridge subscribes to feedback callbacks.<br/>
        /// Configuration/context: logs an error when no model can be found because owner-local damage UI cannot update without a target model.
        /// </summary>
        private void ResolveIncomingDamageModel()
        {
            if (incomingDamageModel != null)
                return;

            if (InventoryGlobals.TryGet(out InventoryGlobals globals) && globals != null)
                incomingDamageModel = globals.incomingDamageModel;

            if (incomingDamageModel != null)
                return;

            Debug.LogError($"[{nameof(IncomingDamageModelBridge)}] Missing required reference on '{gameObject.name}': {nameof(incomingDamageModel)}.", gameObject);
        }
    }
}