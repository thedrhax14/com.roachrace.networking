#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking
{
    [CustomEditor(typeof(RoachRaceNetMapGen))]
    public class RoachRaceNetMapGenEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mapGen = (RoachRaceNetMapGen)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MapGen (Editor)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Load Catalog + Instantiate MapGen"))
                {
                    mapGen.LoadCatalogFromInspector();
                }
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to load the Addressables catalog manually. Auto-loading is disabled while running in the Unity Editor.",
                    MessageType.Info);
            }
        }
    }
}
#endif
