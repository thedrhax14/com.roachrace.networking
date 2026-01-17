using UnityEngine;
using RoachRace.Data;

namespace RoachRace.Networking
{
    /// <summary>
    /// Trigger zone that marks survivors as having reached the end
    /// Attach to the finish line/end zone collider marked as trigger
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EndZone : MonoBehaviour
    {
        private void Awake()
        {
            // Ensure this is set as a trigger
            Collider col = GetComponent<Collider>();
            if (!col.isTrigger)
            {
                Debug.LogWarning("[EndZone] Collider is not marked as trigger - setting isTrigger to true");
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check if the object that entered has NetworkPlayerStats
            NetworkPlayerStats playerStats = other.GetComponent<NetworkPlayerStats>();
            if (playerStats != null && playerStats.Team == Team.Survivor)
            {
                playerStats.OnReachedEnd();
                Debug.Log($"[EndZone] Survivor reached end: {other.gameObject.name}", gameObject);
            }
            else if(playerStats != null)
            {
                Debug.Log($"[EndZone] {playerStats.Team} entered end zone: {other.gameObject.name}", gameObject);
            }
            else
            {
                Debug.Log($"[EndZone] Non-player object entered end zone: {other.gameObject.name}", gameObject);
            }
        }
    }
}
