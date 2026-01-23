using RoachRace.Networking.Inventory;
using RoachRace.Interaction;
using RoachRace.UI.Models;
using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Editor.Inventory
{
    [CustomEditor(typeof(NetworkPlayerInventory))]
    public sealed class NetworkPlayerInventoryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Extra guidance when the global asset is missing/misconfigured.
            bool hasGlobals = InventoryGlobals.TryGet(out var globals) && globals != null;
            if (!hasGlobals)
            {
                EditorGUILayout.HelpBox(
                    "No InventoryGlobals asset found in any Resources folder.\n" +
                    "Create one via: Create > RoachRace > Inventory > Inventory Globals\n" +
                    "Then place it under a Resources folder (e.g., Assets/Resources/RoachRace/Inventory/InventoryGlobals.asset).",
                    MessageType.Warning);
            }
            else
            {
                if (globals.itemDatabase == null || globals.inventoryModel == null)
                {
                    EditorGUILayout.HelpBox(
                        "InventoryGlobals is present, but missing references.\n" +
                        "Assign both ItemDatabase and InventoryModel on the InventoryGlobals asset.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.HelpBox(
                "ItemDatabase and InventoryModel are auto-wired from InventoryGlobals by default.\n" +
                "Enable Override Global References only when you need per-prefab overrides.",
                MessageType.Info);

            serializedObject.Update();

            // Draw all non-dependency fields normally.
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "overrideGlobalInventoryReferences",
                "itemDatabase",
                "inventoryModel",
                "itemRegistry");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);

            var overrideGlobalsProp = serializedObject.FindProperty("overrideGlobalInventoryReferences");
            var itemDatabaseProp = serializedObject.FindProperty("itemDatabase");
            var inventoryModelProp = serializedObject.FindProperty("inventoryModel");
            var itemRegistryProp = serializedObject.FindProperty("itemRegistry");

            EditorGUILayout.PropertyField(overrideGlobalsProp, new GUIContent("Override Global References"));

            if (hasGlobals)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Resolved InventoryGlobals", EditorStyles.miniBoldLabel);
                    EditorGUILayout.ObjectField(new GUIContent("InventoryGlobals"), globals, typeof(InventoryGlobals), false);
                    EditorGUILayout.ObjectField(new GUIContent("Globals ItemDatabase"), globals.itemDatabase, typeof(ItemDatabase), false);
                    EditorGUILayout.ObjectField(new GUIContent("Globals InventoryModel"), globals.inventoryModel, typeof(InventoryModel), false);
                }
            }

            using (new EditorGUI.DisabledScope(!overrideGlobalsProp.boolValue))
            {
                EditorGUILayout.PropertyField(itemDatabaseProp);
                EditorGUILayout.PropertyField(inventoryModelProp);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(itemRegistryProp);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
