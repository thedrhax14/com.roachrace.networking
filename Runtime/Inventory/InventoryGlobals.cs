using RoachRace.Interaction;
using RoachRace.UI.Models;
using UnityEngine;

namespace RoachRace.Networking.Inventory
{
    /// <summary>
    /// Project-wide inventory references to avoid per-prefab manual wiring.
    ///
    /// Place exactly one instance of this asset in a Resources folder so it can be auto-loaded
    /// (e.g., Assets/Resources/RoachRace/Inventory/InventoryGlobals.asset).
    /// </summary>
    [CreateAssetMenu(menuName = "RoachRace/Inventory/Inventory Globals", fileName = "InventoryGlobals")]
    public sealed class InventoryGlobals : ScriptableObject
    {
        [Tooltip("Required. The single ItemDatabase used to resolve item ids -> definitions.")]
        public ItemDatabase itemDatabase;

        [Tooltip("Required. The single InventoryModel used for the local HUD.")]
        public InventoryModel inventoryModel;

        private static InventoryGlobals _cached;
        private static bool _attemptedLoad;

        public static bool TryGet(out InventoryGlobals globals)
        {
            if (_cached != null)
            {
                globals = _cached;
                return true;
            }

#if UNITY_EDITOR
            // In the editor, assets can be created/moved frequently. Avoid caching a "not found" result
            // forever in edit mode; allow re-loading to pick up changes without a domain reload.
            if (!Application.isPlaying)
            {
                var editorAll = UnityEngine.Resources.LoadAll<InventoryGlobals>(string.Empty);
                if (editorAll == null || editorAll.Length == 0)
                {
                    globals = null;
                    return false;
                }

                if (editorAll.Length > 1)
                {
                    Debug.LogError($"[{nameof(InventoryGlobals)}] Multiple InventoryGlobals assets found in Resources. Ensure there is exactly one.");
                    globals = null;
                    return false;
                }

                _cached = editorAll[0];
                globals = _cached;
                return true;
            }
#endif

            if (_attemptedLoad)
            {
                globals = null;
                return false;
            }

            _attemptedLoad = true;

            // Load all to avoid hardcoding a Resources path.
            var all = UnityEngine.Resources.LoadAll<InventoryGlobals>(string.Empty);
            if (all == null || all.Length == 0)
            {
                globals = null;
                return false;
            }

            if (all.Length > 1)
            {
                Debug.LogError($"[{nameof(InventoryGlobals)}] Multiple InventoryGlobals assets found in Resources. Ensure there is exactly one.");
                globals = null;
                return false;
            }

            _cached = all[0];
            globals = _cached;
            return true;
        }
    }
}
