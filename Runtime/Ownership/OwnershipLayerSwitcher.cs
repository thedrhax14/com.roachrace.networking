using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking
{
    [AddComponentMenu("RoachRace/Networking/Ownership/Ownership Layer Switcher")]
    public sealed class OwnershipLayerSwitcher : NetworkBehaviour
    {
        [Header("Targets")]
        [Tooltip("Root transform to apply layer changes to. If not assigned, uses this GameObject's transform.")]
        [SerializeField] private Transform targetRoot;

        [Tooltip("If set, these GameObjects will be used instead of TargetRoot. Useful for changing layers on specific sub-objects only.")]
        [SerializeField] private List<GameObject> explicitTargets = new();

        [Tooltip("Apply the layer change to all children of each target.")]
        [SerializeField] private bool includeChildren = true;

        [Tooltip("Include inactive children when searching.")]
        [SerializeField] private bool includeInactive = true;

        [Header("Layers")]
        [Tooltip("Layer to set when this client owns the NetworkObject.")]
        [SerializeField] private int ownerLayer = 0;

        [Tooltip("Layer to set when this client does NOT own the NetworkObject.")]
        [SerializeField] private int nonOwnerLayer = 0;

        [Header("Lifecycle")]
        [Tooltip("If true, restores original layers when the object stops on the client.")]
        [SerializeField] private bool restoreOriginalLayersOnStopClient = true;

        private readonly Dictionary<int, int> _originalLayerByInstanceId = new();
        private readonly List<GameObject> _resolvedTargets = new();
        private bool _cachedOriginal;

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!IsClientInitialized)
                return;

            ValidateConfiguration();
            ResolveTargets();
            CacheOriginalLayersIfNeeded();
            ApplyForCurrentOwnership();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            if (!IsClientInitialized)
                return;

            ValidateConfiguration();
            ResolveTargets();
            CacheOriginalLayersIfNeeded();
            ApplyForCurrentOwnership();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (!restoreOriginalLayersOnStopClient)
                return;

            RestoreOriginalLayers();
        }

        /// <summary>
        /// Manually applies the configured layer for the current ownership state.
        /// Useful if you change configuration at runtime.
        /// </summary>
        public void ApplyNow()
        {
            if (!IsClientInitialized)
                return;

            ValidateConfiguration();
            ResolveTargets();
            CacheOriginalLayersIfNeeded();
            ApplyForCurrentOwnership();
        }

        private void ValidateConfiguration()
        {
            if (targetRoot == null)
                targetRoot = transform;

            if (targetRoot == null)
            {
                Debug.LogError($"[{nameof(OwnershipLayerSwitcher)}] TargetRoot is null on '{gameObject.name}'.", gameObject);
                throw new NullReferenceException($"[{nameof(OwnershipLayerSwitcher)}] targetRoot is null on '{gameObject.name}'.");
            }

            ValidateLayer(ownerLayer, nameof(ownerLayer));
            ValidateLayer(nonOwnerLayer, nameof(nonOwnerLayer));
        }

        private void ValidateLayer(int layer, string fieldName)
        {
            if (layer is < 0 or > 31)
            {
                Debug.LogError(
                    $"[{nameof(OwnershipLayerSwitcher)}] '{fieldName}' must be in range [0..31] but was {layer} on '{gameObject.name}'.",
                    gameObject);
                throw new ArgumentOutOfRangeException(fieldName, layer,
                    $"[{nameof(OwnershipLayerSwitcher)}] '{fieldName}' is out of range on '{gameObject.name}'.");
            }
        }

        private void ResolveTargets()
        {
            _resolvedTargets.Clear();

            if (explicitTargets != null && explicitTargets.Count > 0)
            {
                for (int i = 0; i < explicitTargets.Count; i++)
                {
                    GameObject go = explicitTargets[i];
                    if (go != null)
                        _resolvedTargets.Add(go);
                }

                if (_resolvedTargets.Count > 0)
                    return;
            }

            _resolvedTargets.Add(targetRoot.gameObject);
        }

        private void CacheOriginalLayersIfNeeded()
        {
            if (_cachedOriginal)
                return;

            _originalLayerByInstanceId.Clear();

            for (int i = 0; i < _resolvedTargets.Count; i++)
            {
                GameObject root = _resolvedTargets[i];
                if (root == null)
                    continue;

                CacheOriginalForGameObject(root);

                if (!includeChildren)
                    continue;

                Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive);
                for (int c = 0; c < children.Length; c++)
                {
                    Transform child = children[c];
                    if (child == null)
                        continue;

                    CacheOriginalForGameObject(child.gameObject);
                }
            }

            _cachedOriginal = true;
        }

        private void CacheOriginalForGameObject(GameObject go)
        {
            int id = go.GetInstanceID();
            if (_originalLayerByInstanceId.ContainsKey(id))
                return;

            _originalLayerByInstanceId.Add(id, go.layer);
        }

        private void ApplyForCurrentOwnership()
        {
            int layerToApply = IsOwner ? ownerLayer : nonOwnerLayer;

            for (int i = 0; i < _resolvedTargets.Count; i++)
            {
                GameObject root = _resolvedTargets[i];
                if (root == null)
                    continue;

                ApplyLayerToGameObject(root, layerToApply);

                if (!includeChildren)
                    continue;

                Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive);
                for (int c = 0; c < children.Length; c++)
                {
                    Transform child = children[c];
                    if (child == null)
                        continue;

                    ApplyLayerToGameObject(child.gameObject, layerToApply);
                }
            }
        }

        private static void ApplyLayerToGameObject(GameObject go, int layer)
        {
            if (go.layer == layer)
                return;

            go.layer = layer;
        }

        private void RestoreOriginalLayers()
        {
            if (!_cachedOriginal)
                return;

            for (int i = 0; i < _resolvedTargets.Count; i++)
            {
                GameObject root = _resolvedTargets[i];
                if (root == null)
                    continue;

                RestoreOriginalForGameObject(root);

                if (!includeChildren)
                    continue;

                Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive);
                for (int c = 0; c < children.Length; c++)
                {
                    Transform child = children[c];
                    if (child == null)
                        continue;

                    RestoreOriginalForGameObject(child.gameObject);
                }
            }
        }

        private void RestoreOriginalForGameObject(GameObject go)
        {
            int id = go.GetInstanceID();
            if (!_originalLayerByInstanceId.TryGetValue(id, out int originalLayer))
                return;

            go.layer = originalLayer;
        }
    }
}
