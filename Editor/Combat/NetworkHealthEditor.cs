using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(NetworkHealth))]
    public class NetworkHealthEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var health = target as NetworkHealth;
            if (health == null)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

            bool canRun = EditorApplication.isPlaying && health.IsServerInitialized;
            using (new EditorGUI.DisabledScope(!canRun || !health.IsAlive))
            {
                if (GUILayout.Button("Trigger Death (Server)"))
                    health.EditorTriggerDeath();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to use debug actions.", MessageType.None);
            else if (!health.IsServerInitialized)
                EditorGUILayout.HelpBox("This button is only enabled on the server instance.", MessageType.Info);
        }
    }
}
