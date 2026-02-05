using FishNet.Connection;
using FishNet.Object;
using RoachRace.Controls;
using UnityEngine;

namespace RoachRace.Networking
{
    public class NetworkGhostController : NetworkBehaviour, IPlayerResourcesProvider
    {
        [Header("Dependencies")]
        public GhostCameraController cameraController;

        [Header("Movement Settings")]
        [SerializeField] private float flySpeed = 10f;
        [SerializeField] private float fastFlySpeed = 20f;

        [SerializeField] private Transform cameraTransform;

        private bool _isRunning;
        public Vector2 _lookInput = Vector2.zero;

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            if (!IsClientInitialized)
                return;

            if (IsOwner)
            {
                LocalPlayerControllerContext.Set(gameObject);
                cameraController.enabled = true;
            } 
            else
            {
                LocalPlayerControllerContext.ClearIf(gameObject);
                cameraController.enabled = false;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;

            LocalPlayerControllerContext.Set(gameObject);
        }

        private void Update()
        {
            if(!IsOwner) return;

            var host = RoachRaceInputActionsHost.Instance;
            if (host == null)
            {
                Debug.LogWarning($"[{nameof(NetworkGhostController)}] No {nameof(RoachRaceInputActionsHost)} in scene; cannot read Move/Look input.", gameObject);
                return;
            }

            Vector2 _moveInput = host.Player.Move.ReadValue<Vector2>();
            _moveInput = Vector2.ClampMagnitude(_moveInput, 1f);
            _lookInput = host.SmoothedLook;
            float currentSpeed = _isRunning ? fastFlySpeed : flySpeed;
            
            Vector3 moveDir = Vector3.zero;

            if (cameraController != null)
            {
                Transform camTransform = cameraController.transform;
                // Keep speed consistent regardless of camera pitch by using planar directions.
                Vector3 forward = camTransform.forward;
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

                Vector3 right = camTransform.right;
                right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
                
                // Free flight: move in camera direction
                moveDir += forward * _moveInput.y;
                moveDir += right * _moveInput.x;
            }

            // Diagonal normalization so WASD doesn't move faster.
            if (moveDir.sqrMagnitude > 1f)
                moveDir.Normalize();

            Debug.DrawRay(transform.position, moveDir, Color.cyan);
            // Apply movement directly to transform (Ghost/Spectator usually ignores physics)
            transform.position += currentSpeed * Time.deltaTime * moveDir;
            cameraController.Rotate(_lookInput);
        }

        #region BasePlayerController Implementation


        public void Interact()
        {
            if (!IsOwner) return;

            Vector3 origin = Camera.main.transform.position;
            Vector3 direction = Camera.main.transform.forward;

            InteractServerRPC(origin, direction);
        }

        [ServerRpc]
        void InteractServerRPC(Vector3 origin, Vector3 direction, NetworkConnection sender = null)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, 5f))
            {
                GameObject hitObject = hit.collider.gameObject;
                if(hitObject.CompareTag("Monster"))
                {
                    if (hitObject.TryGetComponent(out ServerAuthMonsterController serverAuthMonsterController))
                    {
                        serverAuthMonsterController.TakeControl(this, sender);
                        return;
                    }

                    Debug.LogError($"[{nameof(NetworkGhostController)}] InteractServerRPC: No supported monster controller found on the interacted monster.", gameObject);
                }
            }
        }

        public void SetIsNPC(bool npc)
        {
            cameraController.gameObject.SetActive(!npc);
        }

        public void Move(Vector2 moveInput) { }
        public void Run(bool isRunning) => _isRunning = isRunning;
        public void Rotate(Vector2 lookInput) { }

        public void OnChangeItem() { }

        public Transform GetCameraTransform()
        {
            if (cameraTransform != null) return cameraTransform;
            if (cameraController != null) return cameraController.transform;
            return null;
        }

        public PlayerResource[] GetPlayerResources()
        {
            return GetComponentsInChildren<PlayerResource>(true);
        }

        #endregion
    }
}