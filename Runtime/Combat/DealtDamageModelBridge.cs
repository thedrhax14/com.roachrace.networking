using RoachRace.Networking.Inventory;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Combat
{
    /// <summary>
    /// Bridges owner-local dealt-damage feedback into a <see cref="DealtDamageModel"/>.<br/>
    /// Typical usage: place this on the same GameObject as <see cref="NetworkDamageFeedback"/> so the owning attacker client can publish authoritative dealt damage into UI state without coupling the transport to any specific view.<br/>
    /// Configuration/context: if <see cref="dealtDamageModel"/> is not assigned explicitly, the bridge attempts to resolve it from <see cref="InventoryGlobals"/>.
    /// </summary>
    public sealed class DealtDamageModelBridge : MonoBehaviour, IDealtDamageObserver
    {
        [Header("Dependencies")]
        [SerializeField]
        [Tooltip("Owner-local feedback controller that receives attacker-side TargetRpc notifications. If empty, the component tries to resolve it on the same GameObject.")]
        private NetworkDamageFeedback damageFeedbackController;

        [SerializeField]
        [Tooltip("Owner-local UI model that publishes dealt-damage notifications. If empty, the component tries to resolve it from InventoryGlobals.")]
        private DealtDamageModel dealtDamageModel;

        private bool isSubscribed;

        /// <summary>
        /// Resolves dependencies and subscribes to owner-local dealt-damage feedback when the component becomes active.<br/>
        /// Typical usage: Unity invokes this during object activation on clients; the bridge only subscribes when it can resolve both the feedback controller and the UI model.<br/>
        /// Configuration/context: explicit references take precedence over InventoryGlobals-based fallback.
        /// </summary>
        private void OnEnable()
        {
            ResolveDamageFeedbackController();
            ResolveDealtDamageModel();

            if (damageFeedbackController == null || dealtDamageModel == null)
                return;

            damageFeedbackController.AddDealtDamageObserver(this);
            isSubscribed = true;
        }

        /// <summary>
        /// Unsubscribes from owner-local dealt-damage feedback when the component is disabled.<br/>
        /// Typical usage: Unity invokes this during teardown or deactivation to avoid stale observer references.<br/>
        /// Configuration/context: safe to call even when the subscription was never established.
        /// </summary>
        private void OnDisable()
        {
            if (!isSubscribed || damageFeedbackController == null)
                return;

            damageFeedbackController.RemoveDealtDamageObserver(this);
            isSubscribed = false;
        }

        /// <summary>
        /// Publishes owner-local dealt damage into the configured UI model.<br/>
        /// Typical usage: invoked by <see cref="NetworkDamageFeedback"/> after receiving authoritative attacker feedback from the server.<br/>
        /// Server/client constraints: owning-client only; this should not be used for gameplay-authoritative logic.
        /// </summary>
        /// <param name="feedbackController">The local feedback controller that received the authoritative dealt-damage notification.</param>
        /// <param name="damageInfo">Resolved authoritative dealt-damage information forwarded from the server.</param>
        public void OnDealtDamage(NetworkDamageFeedback feedbackController, DealtDamageInfo damageInfo)
        {
            if (dealtDamageModel == null)
                return;

            dealtDamageModel.Publish(new DealtDamageEntry(
                damageInfo.DamageAmount,
                damageInfo.WeaponIconKey,
                damageInfo.TargetConnectionId,
                damageInfo.TargetObjectId,
                damageInfo.TargetHealthAfterHit,
                damageInfo.TargetWorldPosition,
                damageInfo.IsFatal));
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

            Debug.LogError($"[{nameof(DealtDamageModelBridge)}] Missing required reference on '{gameObject.name}': {nameof(damageFeedbackController)}.", gameObject);
        }

        /// <summary>
        /// Resolves the dealt-damage UI model, preferring an explicit reference and falling back to <see cref="InventoryGlobals"/>.<br/>
        /// Typical usage: called during <see cref="OnEnable"/> before the bridge subscribes to feedback callbacks.<br/>
        /// Configuration/context: logs an error when no model can be found because owner-local dealt-damage UI cannot update without a target model.
        /// </summary>
        private void ResolveDealtDamageModel()
        {
            if (dealtDamageModel != null)
                return;

            if (InventoryGlobals.TryGet(out InventoryGlobals globals) && globals != null)
                dealtDamageModel = globals.dealtDamageModel;

            if (dealtDamageModel != null)
                return;

            Debug.LogError($"[{nameof(DealtDamageModelBridge)}] Missing required reference on '{gameObject.name}': {nameof(dealtDamageModel)}.", gameObject);
        }
    }
}