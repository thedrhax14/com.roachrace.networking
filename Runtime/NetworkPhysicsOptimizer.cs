using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Optimizes server-side physics for networked objects by toggling isKinematic based on observer presence.
    /// <para>
    /// <b>Usage:</b><br/>
    /// 1. Add this component to a networked prefab with a Rigidbody.<br/>
    /// 2. Add a <see cref="FishNet.Component.Observing.GridCondition"/> (or similar distance condition) to the prefab.<br/>
    /// 3. Ensure your NetworkManager has a <see cref="FishNet.Component.Observing.HashGrid"/> component.<br/>
    /// </para>
    /// <para>
    /// <b>Behavior:</b><br/>
    /// - <b>Client:</b> Always kinematic (visual only).<br/>
    /// - <b>Server:</b> Becomes kinematic (sleeps) when no clients are observing. Wakes up (staggered) when observers arrive.<br/>
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkPhysicsOptimizer : NetworkBehaviour
    {
        [Tooltip("Max number of rigidbodies to wake up per physics frame. Helps prevent lag spikes when a player enters a crowded area.")]
        [SerializeField] private int maxWakeUpsPerFrame = 5;
        public bool IsSleeping;
        public int observers;

        private Rigidbody _rb;
        
        // STATIC MANAGER: Handles the queue so we don't spike frames
        private static readonly Queue<NetworkPhysicsOptimizer> _wakeUpQueue = new();
        private static Coroutine _processorRoutine;
        // Keep track of the manager (NetworkManager or any persistent MonoBehaviour) running the coroutine
        private static MonoBehaviour _coroutineRunner;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            // 1. Client Optimization:
            // Clients function purely as visual interpolation targets.
            // We disable local physics solving to save massive CPU.
            if (IsServerInitialized) return; // Skip if also server (host)
            _rb.isKinematic = true;
            _rb.detectCollisions = false; // Disable local triggers if not needed
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NetworkObject.OnObserversActive += OnObserversActive;
            
            // Default to kinematic/sleeping until a player is confirmed near
            // We perform an initial check in case observers are already present (e.g. late join or spawn on top)
            if (NetworkObject.Observers.Count > 0)
            {
                EnqueueWakeUp(this);
            }
            else
            {
                _rb.isKinematic = true;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            NetworkObject.OnObserversActive -= OnObserversActive;
        }

        private void OnObserversActive(NetworkObject nob)
        {
            // This event fires when observer count goes 0 -> 1 or X -> 0 (if logic in NetworkObject handles it that way, 
            // but FishNet OnObserversActive usually fires when observers > 0 coming from 0, or vice versa if handled manually).
            // FishNet's OnObserversActive specifically: "Called when this NetworkObject losses all observers or gains observers while previously having none."
            
            bool hasObservers = nob.Observers.Count > 0;
            observers = nob.Observers.Count;
            
            if (hasObservers)
            {
                // Queue for wake-up (prevent lag spike)
                EnqueueWakeUp(this);
            }
            else
            {
                // Sleep immediately (instant benefit, no cost)
                _rb.isKinematic = true;
                _rb.Sleep();
                IsSleeping = true;
#if UNITY_EDITOR
                Debug.Log($"{nameof(NetworkPhysicsOptimizer)}: Object {nob.name} is now sleeping (no observers).", gameObject);
#endif
            }
        }

        public void WakeUpNow()
        {
            _rb.isKinematic = false;
            _rb.WakeUp();
            IsSleeping = false;
#if UNITY_EDITOR
            Debug.Log($"{nameof(NetworkPhysicsOptimizer)}: Object {NetworkObject.name} woke up (observers present).", gameObject);
    #endif
        }

        // --- STATIC MANAGER LOGIC ---
        
        private static void EnqueueWakeUp(NetworkPhysicsOptimizer item)
        {
            _wakeUpQueue.Enqueue(item);
            
            // If we don't have a runner, or the previous runner was destroyed (e.g. scene change/shutdown), try to re-assign
            if (_coroutineRunner == null && item.NetworkManager != null)
            {
                _coroutineRunner = item.NetworkManager;
            }

            if (_processorRoutine == null && _coroutineRunner != null)
            {
                _processorRoutine = _coroutineRunner.StartCoroutine(ProcessQueue(item.maxWakeUpsPerFrame));
            }
        }

        private static IEnumerator ProcessQueue(int limitPerFrame)
        {
            var wait = new WaitForFixedUpdate();
            
            while (_wakeUpQueue.Count > 0)
            {
                // Wake up max N objects per physics frame to hide the re-indexing cost
                int processed = 0;
                
                while (_wakeUpQueue.Count > 0 && processed < limitPerFrame)
                {
                    var item = _wakeUpQueue.Dequeue();
                    
                    // Check if item is still valid (might have been destroyed while in queue)
                    if (item != null && item.NetworkObject != null && item.NetworkObject.IsSpawned)
                    {
                        item.WakeUpNow();
                    }
                    processed++;
                }
                yield return wait;
            }
            _processorRoutine = null;
        }
    }
}
