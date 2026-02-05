using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Utility.Template;
using RoachRace.Controls;
using UnityEngine;
using UnityEngine.Events;

namespace RoachRace.Networking
{
    public abstract class ServerAuthMonsterController : TickNetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] protected Rigidbody _rb;
        NetworkGhostController _originalGhostController;
        public UnityEvent OnPlayerControlActivated = new ();

        protected virtual void Awake()
        {
            if (TryGetComponent(out _rb) == false)
            {
                Debug.LogError($"[{nameof(ServerAuthMonsterController)}] Rigidbody is not assigned and was not found on GameObject '{gameObject.name}'.", gameObject);
                throw new System.NullReferenceException($"[{nameof(ServerAuthMonsterController)}] Rigidbody is null on GameObject '{gameObject.name}'.");
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            SetTickCallbacks(TickCallback.Tick);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_rb != null) _rb.isKinematic = false;
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            Debug.Log($"[{nameof(ServerAuthMonsterController)}] OnOwnershipServer called. Previous OwnerId: {prevOwner?.ClientId}, New OwnerId: {OwnerId}", gameObject);
            if(prevOwner.ClientId != -1)
            {
                _originalGhostController.GiveOwnership(prevOwner);
                GiveOwnership(null);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized && _rb != null) _rb.isKinematic = true;
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if(OwnerId != -1) OnPlayerControlActivated.Invoke();

            if (!IsClientInitialized)
                return;

            if (IsOwner)
                LocalPlayerControllerContext.Set(gameObject);
            else
                LocalPlayerControllerContext.ClearIf(gameObject);
        }

        protected override void TimeManager_OnTick()
        {
            if (IsOwner && IsClientInitialized) OnOwnerClientTick();
            if (IsServerInitialized) OnServerTick((float)TimeManager.TickDelta);
        }

        protected Rigidbody Rigidbody => _rb;

        protected virtual void OnOwnerClientTick() { }
        protected virtual void OnServerTick(float delta) { }

        public void TakeControl(NetworkGhostController ghostController, NetworkConnection newOwner)
        {
            if (OwnerId != -1)
            {
                Debug.LogWarning($"[{nameof(ServerAuthMonsterController)}] Attempted to take control of a MonsterController that is already owned by ({OwnerId}). Release ownership first.", gameObject);
                return;
            }

            ghostController.GiveOwnership(null);
            _originalGhostController = ghostController;
            GiveOwnership(newOwner);
        }
    }
}
