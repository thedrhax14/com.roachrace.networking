using RoachRace.Networking.Inventory;
using UnityEngine;

namespace RoachRace.Networking.Weapons
{
    /// <summary>
    /// Local-only feedback for empty-mag fire attempts.
    /// Listens to <see cref="NetworkItemUseFeedbackController"/> on the owning client and plays a click.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponEmptyClickFeedback : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private NetworkItemUseFeedbackController feedback;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip emptyClickClip;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        private void Awake()
        {
            if (feedback == null)
                feedback = GetComponentInParent<NetworkItemUseFeedbackController>();

            if (audioSource == null)
                audioSource = GetComponentInParent<AudioSource>();
        }

        private void OnEnable()
        {
            if (feedback != null)
                feedback.OnItemUseFailed += OnItemUseFailed;
        }

        private void OnDisable()
        {
            if (feedback != null)
                feedback.OnItemUseFailed -= OnItemUseFailed;
        }

        private void OnItemUseFailed(ItemUseFailure failure)
        {
            if (failure.Reason != ItemUseFailReason.NoAmmoInMagazine)
                return;

            if (emptyClickClip == null)
                return;

            if (audioSource == null)
                return;

            audioSource.PlayOneShot(emptyClickClip, volume);
        }
    }
}
