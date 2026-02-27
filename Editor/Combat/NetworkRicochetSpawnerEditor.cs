using RoachRace.Networking.Combat;
using UnityEditor;
using UnityEngine;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(NetworkRicochetSpawner))]
    public sealed class NetworkRicochetSpawnerEditor : UnityEditor.Editor
    {
        private SerializedProperty _rayOrigin;
        private SerializedProperty _localRayDirection;
        private SerializedProperty _segmentDistance;
        private SerializedProperty _ricochetCount;
        private SerializedProperty _hitMask;
        private SerializedProperty _triggerInteraction;
        private SerializedProperty _surfaceSpawnOffset;

        private SerializedProperty _spawnPrefab;

        private SerializedProperty _traceLinePrefab;
        private SerializedProperty _traceDestroyAfterSeconds;
        private SerializedProperty _traceStartPoint;

        private SerializedProperty _motor;

        private float _dashLength = 0.45f;
        private float _gapLength = 0.2f;

        private void OnEnable()
        {
            _rayOrigin = serializedObject.FindProperty("rayOrigin");
            _localRayDirection = serializedObject.FindProperty("localRayDirection");
            _segmentDistance = serializedObject.FindProperty("segmentDistance");
            _ricochetCount = serializedObject.FindProperty("ricochetCount");
            _hitMask = serializedObject.FindProperty("hitMask");
            _triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            _surfaceSpawnOffset = serializedObject.FindProperty("surfaceSpawnOffset");

            _spawnPrefab = serializedObject.FindProperty("spawnPrefab");

            _traceLinePrefab = serializedObject.FindProperty("traceLinePrefab");
            _traceDestroyAfterSeconds = serializedObject.FindProperty("traceDestroyAfterSeconds");
            _traceStartPoint = serializedObject.FindProperty("traceStartPoint");

            _motor = serializedObject.FindProperty("_motor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Raycast", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_rayOrigin);
            EditorGUILayout.PropertyField(_localRayDirection);
            EditorGUILayout.PropertyField(_segmentDistance);
            EditorGUILayout.PropertyField(_ricochetCount);
            EditorGUILayout.PropertyField(_hitMask);
            EditorGUILayout.PropertyField(_triggerInteraction);
            EditorGUILayout.PropertyField(_surfaceSpawnOffset);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_spawnPrefab);
            DrawSpawnValidation();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Trace Visualization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_traceLinePrefab);
            EditorGUILayout.PropertyField(_traceDestroyAfterSeconds);
            EditorGUILayout.PropertyField(_traceStartPoint);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_motor);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            _dashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("Dash Length", _dashLength));
            _gapLength = Mathf.Max(0f, EditorGUILayout.FloatField("Gap Length", _gapLength));

            EditorGUILayout.HelpBox(
                "Scene View (when selected) draws ricochet path and hit points.\n" +
                "Cyan = ray segment, Yellow = hit normal, Green sphere = ricochet hit point.\n" +
                "One trace line per invocation: first point comes from traceStartPoint, following points are ricochet hit points.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSpawnValidation()
        {
            Object prefabObject = _spawnPrefab.objectReferenceValue;
            if (prefabObject == null)
            {
                EditorGUILayout.HelpBox(
                    "Spawn Prefab is optional. Assign it if you want local hit-visual spawning.",
                    MessageType.Info);
                return;
            }

            GameObject prefabGameObject = prefabObject as GameObject;
            if (prefabGameObject == null)
            {
                EditorGUILayout.HelpBox(
                    "Spawn Prefab must be a GameObject prefab.",
                    MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox("Spawn setup looks valid.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            NetworkRicochetSpawner spawner = (NetworkRicochetSpawner)target;
            if (spawner == null)
                return;

            Transform originTransform = spawner.RayOrigin;
            if (originTransform == null)
                return;

            Vector3 currentOrigin = originTransform.position;
            Vector3 currentDirection = originTransform.TransformDirection(spawner.LocalRayDirection);
            if (currentDirection.sqrMagnitude < 0.0001f)
                currentDirection = originTransform.forward;

            currentDirection.Normalize();

            Vector3 firstTraceStart = _traceStartPoint.objectReferenceValue is Transform customTraceStart
                ? customTraceStart.position
                : currentOrigin;

            float distance = Mathf.Max(0.01f, spawner.SegmentDistance);
            int segmentCount = Mathf.Max(0, spawner.RicochetCount) + 1;
            int mask = spawner.HitMask;
            QueryTriggerInteraction query = spawner.TriggerInteraction;

            for (int i = 0; i < segmentCount; i++)
            {
                if (Physics.Raycast(currentOrigin, currentDirection, out RaycastHit hit, distance, mask, query))
                {
                    Vector3 segmentStart = i == 0 ? firstTraceStart : currentOrigin;
                    DrawDashedLine(segmentStart, hit.point, Color.cyan);

                    using (new Handles.DrawingScope(new Color(0.2f, 1f, 0.2f, 0.95f)))
                    {
                        float pointSize = HandleUtility.GetHandleSize(hit.point) * 0.06f;
                        Handles.SphereHandleCap(0, hit.point, Quaternion.identity, pointSize, EventType.Repaint);
                    }

                    using (new Handles.DrawingScope(new Color(1f, 0.9f, 0.2f, 0.95f)))
                    {
                        float normalLength = Mathf.Min(1.5f, distance * 0.25f);
                        Handles.DrawLine(hit.point, hit.point + hit.normal * normalLength);
                    }

                    Handles.Label(hit.point + hit.normal * 0.15f, $"R{i + 1}");

                    currentDirection = Vector3.Reflect(currentDirection, hit.normal).normalized;
                    float offset = Mathf.Max(0f, _surfaceSpawnOffset.floatValue);
                    currentOrigin = hit.point + currentDirection * offset;
                }
                else
                {
                    Vector3 segmentStart = i == 0 ? firstTraceStart : currentOrigin;
                    DrawDashedLine(segmentStart, currentOrigin + currentDirection * distance, new Color(0.8f, 0.8f, 0.8f, 0.8f));
                    break;
                }
            }
        }

        private void DrawDashedLine(Vector3 start, Vector3 end, Color color)
        {
            float totalLength = Vector3.Distance(start, end);
            if (totalLength < 0.0001f)
                return;

            Vector3 direction = (end - start) / totalLength;
            float patternLength = _dashLength + _gapLength;
            float traveled = 0f;

            using (new Handles.DrawingScope(color))
            {
                int safety = 0;
                while (traveled < totalLength && safety++ < 2048)
                {
                    float dashEnd = Mathf.Min(traveled + _dashLength, totalLength);
                    Vector3 from = start + direction * traveled;
                    Vector3 to = start + direction * dashEnd;
                    Handles.DrawLine(from, to);
                    traveled += patternLength;
                }
            }
        }
    }
}