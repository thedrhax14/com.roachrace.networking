using FishNet.Connection;
using FishNet.Object;
using UnityEngine.Events;

namespace RoachRace.Networking
{
    public class OwnershipListener : NetworkBehaviour
    {
        public UnityEvent OnGainedOwnership = new ();
        public UnityEvent OnOwnedByLiveConnection = new ();

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (Owner != null && Owner.ClientId != -1)
                OnOwnedByLiveConnection.Invoke();
            if(IsOwner) OnGainedOwnership.Invoke();
        }
    }
}