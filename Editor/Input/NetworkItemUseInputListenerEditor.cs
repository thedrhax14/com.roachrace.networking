#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Input
{
    [CustomEditor(typeof(NetworkItemUseInputListener))]
    public sealed class NetworkItemUseInputListenerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var forwarder = (NetworkItemUseInputListener)target;

            // Validate and show actionable help.
            if (forwarder == null)
                return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            var configProp = serializedObject.FindProperty("config");
            if (configProp == null)
            {
                EditorGUILayout.HelpBox("Internal error: couldn't find 'config' property.", MessageType.Error);
                return;
            }

            if (configProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Config is not assigned. This component requires a NetworkItemUseInputListenerConfig asset.",
                    MessageType.Error);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Create + Assign Config", GUILayout.Height(26)))
                    CreateAndAssignConfigAsset(configProp);

                if (GUILayout.Button("Select Forwarder", GUILayout.Height(26)))
                {
                    Selection.activeObject = forwarder;
                    EditorGUIUtility.PingObject(forwarder);
                }

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                var config = configProp.objectReferenceValue as NetworkItemUseInputListenerConfig;
                if (config == null)
                {
                    EditorGUILayout.HelpBox("Config reference is not a NetworkItemUseInputListenerConfig.", MessageType.Error);
                }
                else
                {
                    bool hasAction = config.InputActionReference != null && config.InputActionReference.action != null;
                    bool hasItemId = config.ItemId != 0;

                    if (!hasAction)
                        EditorGUILayout.HelpBox("Config is missing InputActionReference (or its action).", MessageType.Warning);
                    if (!hasItemId)
                        EditorGUILayout.HelpBox("Config.ItemId is 0 (reserved). Assign an ItemDefinition or a non-zero id in the config.", MessageType.Warning);

                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Select Config", GUILayout.Height(22)))
                    {
                        Selection.activeObject = config;
                        EditorGUIUtility.PingObject(config);
                    }

                    if (GUILayout.Button("Duplicate Config", GUILayout.Height(22)))
                        DuplicateConfigAsset(config, configProp);

                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private static void CreateAndAssignConfigAsset(SerializedProperty configProp)
        {
            // Default to creating in Assets/ScriptableObjects if it exists, else Assets.
            string defaultFolder = Directory.Exists("Assets/ScriptableObjects") ? "Assets/ScriptableObjects" : "Assets";
            string defaultName = "NetworkItemUseInputListenerConfig.asset";

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Network Item Use Input Listener Config",
                defaultName,
                "asset",
                "Choose where to save the config asset.",
                defaultFolder);

            if (string.IsNullOrWhiteSpace(path))
                return;

            var config = ScriptableObject.CreateInstance<NetworkItemUseInputListenerConfig>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            configProp.objectReferenceValue = config;
            configProp.serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(configProp.serializedObject.targetObject);
            EditorGUIUtility.PingObject(config);
            Selection.activeObject = config;
        }

        private static void DuplicateConfigAsset(NetworkItemUseInputListenerConfig source, SerializedProperty configProp)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;

            string folder = Path.GetDirectoryName(sourcePath) ?? "Assets";
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string ext = Path.GetExtension(sourcePath);

            string targetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName + " Copy" + ext));
            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                Debug.LogError($"Failed to duplicate config asset '{sourcePath}' to '{targetPath}'.");
                return;
            }

            AssetDatabase.SaveAssets();

            var duplicated = AssetDatabase.LoadAssetAtPath<NetworkItemUseInputListenerConfig>(targetPath);
            if (duplicated == null)
                return;

            configProp.objectReferenceValue = duplicated;
            configProp.serializedObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(configProp.serializedObject.targetObject);
            EditorGUIUtility.PingObject(duplicated);
            Selection.activeObject = duplicated;
        }
    }
}
#endif
