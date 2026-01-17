using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Editor
{
    [CustomEditor(typeof(NetworkConnectionObserver))]
    public class NetworkConnectionObserverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Server Controls", EditorStyles.boldLabel);

            NetworkConnectionObserver observer = (NetworkConnectionObserver)target;

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
            {
                observer.StartServer();
            }

            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
            {
                observer.StopServer();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Client Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Connect to Server", GUILayout.Height(30)))
            {
                observer.ConnectToServer();
            }

            if (GUILayout.Button("Disconnect", GUILayout.Height(30)))
            {
                observer.DisconnectFromServer();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
