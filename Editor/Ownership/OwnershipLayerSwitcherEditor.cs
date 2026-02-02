using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Editor
{
    [CustomEditor(typeof(RoachRace.Networking.OwnershipLayerSwitcher))]
    public sealed class OwnershipLayerSwitcherEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetRoot;
        private SerializedProperty _explicitTargets;
        private SerializedProperty _includeChildren;
        private SerializedProperty _includeInactive;

        private SerializedProperty _ownerLayer;
        private SerializedProperty _nonOwnerLayer;

        private SerializedProperty _restoreOriginalLayersOnStopClient;

        private void OnEnable()
        {
            _targetRoot = serializedObject.FindProperty("targetRoot");
            _explicitTargets = serializedObject.FindProperty("explicitTargets");
            _includeChildren = serializedObject.FindProperty("includeChildren");
            _includeInactive = serializedObject.FindProperty("includeInactive");

            _ownerLayer = serializedObject.FindProperty("ownerLayer");
            _nonOwnerLayer = serializedObject.FindProperty("nonOwnerLayer");

            _restoreOriginalLayersOnStopClient = serializedObject.FindProperty("restoreOriginalLayersOnStopClient");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_targetRoot);
            EditorGUILayout.PropertyField(_explicitTargets);
            EditorGUILayout.PropertyField(_includeChildren);
            EditorGUILayout.PropertyField(_includeInactive);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                _ownerLayer.intValue = EditorGUILayout.LayerField(new GUIContent("Owner Layer"), _ownerLayer.intValue);
                _nonOwnerLayer.intValue = EditorGUILayout.LayerField(new GUIContent("Non-Owner Layer"), _nonOwnerLayer.intValue);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.PropertyField(_restoreOriginalLayersOnStopClient);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
