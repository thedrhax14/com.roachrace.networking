using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;
using System.Collections.Generic;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(ProjectileController))]
    public class ProjectileControllerEditor : UnityEditor.Editor
    {
        private static float thickness = 1f;
        private SerializedProperty movementProfile;
        private SerializedProperty speedMultiplier;
        private SerializedProperty moveDirection;

        // Preview settings
        private bool _showPreview = true;
        private bool _animatePreview = true;
        private bool _showStraightReference = true;
        private float _dashLength = 0.75f;
        private float _gapLength = 0.35f;
        private float _previewSeconds = 2.0f;
        private float _loopSeconds = 1.0f;
        private int _tickEverySeconds = 1;
        private float _simulationStepSeconds = 0.05f;

        private double _animStartTime;
        private readonly List<Vector3> _pathPoints = new();

        private void OnEnable()
        {
            movementProfile = serializedObject.FindProperty("movementProfile");
            speedMultiplier = serializedObject.FindProperty("speedMultiplier");
            moveDirection = serializedObject.FindProperty("moveDirection");

            _animStartTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_animatePreview) return;
            if (Selection.activeObject == target)
                SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(movementProfile);
            EditorGUILayout.PropertyField(speedMultiplier);
            EditorGUILayout.PropertyField(moveDirection);

            if (movementProfile.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a Movement Profile to configure flight behavior.", MessageType.Warning);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            _showPreview = EditorGUILayout.Toggle("Show Preview", _showPreview);

            using (new EditorGUI.DisabledScope(!_showPreview))
            {
                _animatePreview = EditorGUILayout.Toggle("Animate", _animatePreview);
                _showStraightReference = EditorGUILayout.Toggle("Show Straight Ref", _showStraightReference);

                _dashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("Dash Length", _dashLength));
                _gapLength = Mathf.Max(0.0f, EditorGUILayout.FloatField("Gap Length", _gapLength));

                _previewSeconds = Mathf.Max(0.1f, EditorGUILayout.FloatField("Preview Length (s)", _previewSeconds));
                _loopSeconds = Mathf.Max(0.1f, EditorGUILayout.FloatField("Loop Duration (s)", _loopSeconds));
                _tickEverySeconds = Mathf.Clamp(EditorGUILayout.IntField("Tick Every (s)", _tickEverySeconds), 1, 10);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (!_showPreview) return;

            var projectile = (ProjectileController)target;
            if (projectile == null || projectile.MovementProfile == null) return;

            float speedMult = speedMultiplier != null ? speedMultiplier.floatValue : 1.0f;
            
            Transform t = projectile.transform;
            Vector3 origin = t.position;
            
            // Determine forward basis
            Vector3 localDir = (projectile.MoveDirection.sqrMagnitude > 0.0001f) ? projectile.MoveDirection.normalized : Vector3.forward;
            Vector3 forward = t.TransformDirection(localDir);
            Vector3 right = t.right;
            Vector3 up = t.up;
            
            // Ensure basis is orthogonal
            Vector3.OrthoNormalize(ref forward, ref right, ref up);

            // Generate seeded randoms for consistent preview
            int id = GetPreviewId(projectile);
            float seedX = id * 0.173f + 13.37f;
            float seedY = id * 0.271f + 42.42f;

            // 1. Simulate Path
            projectile.MovementProfile.SimulatePath(
                _pathPoints, 
                origin, 
                forward, 
                up, 
                right, 
                speedMult, 
                _previewSeconds, 
                _simulationStepSeconds, 
                seedX, 
                seedY
            );

            // 2. Draw Curve
            if (_pathPoints.Count > 1)
            {
                using (new Handles.DrawingScope(new Color(1f, 0.4f, 1f, 0.9f)))
                {
                    for (int i = 1; i < _pathPoints.Count; i++)
                        DrawDashedLine(_pathPoints[i - 1], _pathPoints[i], _dashLength, _gapLength);
                }
            }

            // 3. Draw Straight Reference
            if (_showStraightReference)
            {
                float totalDist = projectile.MovementProfile.defaultSpeed * speedMult * _previewSeconds;
                Vector3 end = origin + forward * totalDist;
                using (new Handles.DrawingScope(new Color(0.2f, 0.9f, 1f, 0.4f)))
                {
                    Handles.DrawDottedLine(origin, end, 4f);
                }
            }

            // 4. Animation Ball
            if (_animatePreview && _pathPoints.Count > 1)
            {
                double elapsed = EditorApplication.timeSinceStartup - _animStartTime;
                float phase = (float)((elapsed % _loopSeconds) / _loopSeconds);
                
                // Interpolate along the generated path points
                int totalSegments = _pathPoints.Count - 1;
                float floatIndex = phase * totalSegments;
                int idx = Mathf.FloorToInt(floatIndex);
                float subPhase = floatIndex - idx;

                if (idx < _pathPoints.Count - 1)
                {
                    Vector3 p = Vector3.Lerp(_pathPoints[idx], _pathPoints[idx+1], subPhase);
                    
                    using (new Handles.DrawingScope(new Color(1f, 0.2f, 0.2f, 0.95f)))
                    {
                        float size = HandleUtility.GetHandleSize(p) * 0.08f;
                        Handles.SphereHandleCap(0, p, Quaternion.identity, size, EventType.Repaint);
                    }
                }
            }

            // 5. Labels
            Handles.Label(origin + Vector3.up * 0.5f, $"Profile: {projectile.MovementProfile.name}");
        }

        private int GetPreviewId(ProjectileController projectile)
        {
            if (Application.isPlaying && projectile.NetworkObject != null && projectile.NetworkObject.IsSpawned)
                return projectile.NetworkObject.ObjectId;
            return projectile.GetInstanceID();
        }

        private static void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, float gapLength)
        {
            float totalLength = Vector3.Distance(start, end);
            if (totalLength <= 0.0001f) return;

            Vector3 dir = (end - start) / totalLength;
            float patternLength = dashLength + gapLength;

            float pos = 0f;
            int safety = 0;
            while (pos < totalLength && safety++ < 1000)
            {
                float dashEnd = Mathf.Min(pos + dashLength, totalLength);
                Vector3 a = start + dir * pos;
                Vector3 b = start + dir * dashEnd;
                Handles.DrawLine(a, b, thickness);
                pos += patternLength;
            }
        }
    }
}
