#if UNITY_EDITOR
using RoachRace.Networking.Input;
using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Editor.Input
{
    [InitializeOnLoad]
    internal static class NetworkItemUseInputListenerHierarchyWarningIcon
    {
        private static readonly GUIContent WarnIcon = EditorGUIUtility.IconContent("console.warnicon.sml");

        static NetworkItemUseInputListenerHierarchyWarningIcon()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
        }

        private static void OnHierarchyWindowItemOnGUI(int instanceId, Rect selectionRect)
        {
            if (Application.isPlaying) return;

            var obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is not GameObject go) return;

            if (!go.TryGetComponent<NetworkItemUseInputListener>(out var listener)) return;
            if (listener == null) return;

            // Determine misconfiguration reasons (edit-mode authoring issues).
            // Note: We only check authoring-time fields, not runtime ownership/network state.
            var so = new SerializedObject(listener);
            var configProp = so.FindProperty("config");
            var config = configProp != null ? configProp.objectReferenceValue as NetworkItemUseInputListenerConfig : null;

            string tooltip = null;
            if (config == null)
            {
                tooltip = "NetworkItemUseInputListener is missing Config";
            }
            else if (config.InputActionReference == null)
            {
                tooltip = "NetworkItemUseInputListenerConfig is missing InputActionReference";
            }
            else if (config.ItemId == 0)
            {
                tooltip = "NetworkItemUseInputListenerConfig itemId is 0 (reserved)";
            }

            if (string.IsNullOrEmpty(tooltip)) return;

            var rect = new Rect(selectionRect);
            rect.x = rect.xMax - 18f;
            rect.width = 18f;

            var content = new GUIContent(WarnIcon)
            {
                tooltip = tooltip
            };

            GUI.Label(rect, content);
        }
    }
}
#endif
