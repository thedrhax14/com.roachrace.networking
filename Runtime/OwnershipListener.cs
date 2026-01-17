using FishNet.Connection;
using FishNet.Object;
using UnityEngine.Events;

namespace RoachRace.Networking
{
    public class OwnershipListener : NetworkBehaviour
    {
        public UnityEvent OnGainedOwnership;
        public UnityEvent OnLostOwnership;

        bool wasOwner = false;

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (IsOwner)
            {
                OnGainedOwnership.Invoke();
                wasOwner = true;
            }
            else if(wasOwner)
            {
                OnLostOwnership.Invoke();
                wasOwner = false;
            }
        }
    }
}