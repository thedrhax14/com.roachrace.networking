using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(ProjectileController))]
    public class ProjectileControllerEditor : UnityEditor.Editor
    {
        private static float thickness = 1f;
        private SerializedProperty speed;
        private SerializedProperty ballisticOnce;
        private SerializedProperty ballisticUseGravity;
        private SerializedProperty maintainForwardVelocity;
        private SerializedProperty perlinNoiseAmount;
        private SerializedProperty perlinNoiseFrequency;

        // Preview settings (editor-only; not serialized into the component)
        private bool _showPreview = true;
        private bool _animatePreview = true;
        private bool _showNoisyPath = true;
        private bool _showStraightReference = true;
        private float _dashLength = 0.75f;
        private float _gapLength = 0.35f;
        private float _previewSeconds = 2.0f;
        private float _loopSeconds = 1.0f;
        private int _tickEverySeconds = 1;
        private float _simulationStepSeconds = 0.05f;

        private double _animStartTime;

        private readonly System.Collections.Generic.List<Vector3> _pathPoints = new();

        private void OnEnable()
        {
            speed = serializedObject.FindProperty("speed");
            ballisticOnce = serializedObject.FindProperty("ballisticOnce");
            ballisticUseGravity = serializedObject.FindProperty("ballisticUseGravity");
            maintainForwardVelocity = serializedObject.FindProperty("maintainForwardVelocity");
            perlinNoiseAmount = serializedObject.FindProperty("perlinNoiseAmount");
            perlinNoiseFrequency = serializedObject.FindProperty("perlinNoiseFrequency");

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

            // Only repaint when this editor is active/selected.
            if (Selection.activeObject == target)
                SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(speed);

            bool isBallistic = ballisticOnce != null && ballisticOnce.boolValue;
            if (ballisticOnce != null)
            {
                EditorGUILayout.PropertyField(ballisticOnce);
                isBallistic = ballisticOnce.boolValue;
                if (isBallistic && ballisticUseGravity != null)
                    EditorGUILayout.PropertyField(ballisticUseGravity);
            }

            using (new EditorGUI.DisabledScope(isBallistic))
            {
                EditorGUILayout.PropertyField(maintainForwardVelocity);
            }

            if (perlinNoiseAmount != null)
                EditorGUILayout.PropertyField(perlinNoiseAmount);
            if (perlinNoiseFrequency != null)
                EditorGUILayout.PropertyField(perlinNoiseFrequency);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            _showPreview = EditorGUILayout.Toggle("Show Preview", _showPreview);

            using (new EditorGUI.DisabledScope(!_showPreview))
            {
                _animatePreview = EditorGUILayout.Toggle("Animate", _animatePreview);
                _showNoisyPath = EditorGUILayout.Toggle("Show Noisy Path", _showNoisyPath);
                _showStraightReference = EditorGUILayout.Toggle("Show Straight Ref", _showStraightReference);

                _dashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("Dash Length", _dashLength));
                _gapLength = Mathf.Max(0.0f, EditorGUILayout.FloatField("Gap Length", _gapLength));

                _previewSeconds = Mathf.Max(0.1f, EditorGUILayout.FloatField("Preview Length (s)", _previewSeconds));
                _loopSeconds = Mathf.Max(0.1f, EditorGUILayout.FloatField("Loop Duration (s)", _loopSeconds));
                _tickEverySeconds = Mathf.Clamp(EditorGUILayout.IntField("Tick Every (s)", _tickEverySeconds), 1, 10);
                _simulationStepSeconds = Mathf.Clamp(EditorGUILayout.FloatField("Sim Step (s)", _simulationStepSeconds), 0.01f, 0.2f);
            }

            EditorGUILayout.HelpBox(
                "Scene view previews travel based on speed and (optionally) Perlin noise.\n" +
                "This is an editor-only visualization (no drag/collisions, no rigidbody simulation).",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (!_showPreview) return;

            var projectile = (ProjectileController)target;
            if (projectile == null) return;

            float spd = speed != null ? Mathf.Max(0f, speed.floatValue) : 0f;
            if (spd <= 0f) return;

            float noiseAmount = perlinNoiseAmount != null ? Mathf.Max(0f, perlinNoiseAmount.floatValue) : 0f;
            float noiseFrequency = perlinNoiseFrequency != null ? Mathf.Max(0f, perlinNoiseFrequency.floatValue) : 0f;

            bool isBallistic = ballisticOnce != null && ballisticOnce.boolValue;
            bool maintain = !isBallistic && maintainForwardVelocity != null && maintainForwardVelocity.boolValue;

            Transform t = projectile.transform;
            Vector3 origin = t.position;
            Vector3 forward = t.forward;
            Vector3 right = t.right;
            Vector3 up = t.up;

            float length = spd * Mathf.Max(0.1f, _previewSeconds);

            int id = GetPreviewId(projectile);
            float seedX = id * 0.173f + 13.37f;
            float seedY = id * 0.271f + 42.42f;

            double elapsed = EditorApplication.timeSinceStartup - _animStartTime;
            float phase = (float)((elapsed % _loopSeconds) / _loopSeconds);
            float previewStartTime = (float)(elapsed); // time offset for stable animation

            // Straight reference
            if (_showStraightReference)
            {
                Vector3 end = origin + forward * length;
                using (new Handles.DrawingScope(new Color(0.2f, 0.9f, 1f, 0.8f)))
                {
                    DrawDashedLine(origin, end, _dashLength, _gapLength);
                }
            }

            bool canShowNoisy = _showNoisyPath && noiseAmount > 0.0001f;
            if (canShowNoisy)
            {
                BuildNoisyPath(_pathPoints, origin, forward, right, up, spd, _previewSeconds, previewStartTime, _simulationStepSeconds, seedX, seedY, noiseAmount, noiseFrequency, maintain);

                using (new Handles.DrawingScope(new Color(1f, 0.4f, 1f, 0.9f)))
                {
                    for (int i = 1; i < _pathPoints.Count; i++)
                        DrawDashedLine(_pathPoints[i - 1], _pathPoints[i], _dashLength, _gapLength);
                }
            }

            // Tick marks each N seconds (on whichever path is active)
            DrawTimeTicks(origin, forward, right, up, spd, previewStartTime, seedX, seedY, noiseAmount, noiseFrequency, maintain, canShowNoisy);

            // Animated marker
            if (_animatePreview)
            {
                Vector3 p = canShowNoisy
                    ? SampleNoisyPathPosition(origin, forward, right, up, spd, _previewSeconds, previewStartTime, seedX, seedY, noiseAmount, noiseFrequency, maintain, phase)
                    : origin + forward * (length * phase);

                using (new Handles.DrawingScope(new Color(1f, 0.2f, 0.2f, 0.95f)))
                {
                    float size = HandleUtility.GetHandleSize(p) * 0.08f;
                    Handles.SphereHandleCap(0, p, Quaternion.identity, size, EventType.Repaint);
                }
            }

            // Label
            string label = noiseAmount > 0.0001f
                ? $"Speed: {spd:0.##} m/s  Noise: {noiseAmount:0.##}  Freq: {noiseFrequency:0.##}" + (isBallistic ? "  (Ballistic)" : string.Empty)
                : $"Speed: {spd:0.##} m/s" + (isBallistic ? "  (Ballistic)" : string.Empty);
            Handles.Label(origin + Vector3.up * (HandleUtility.GetHandleSize(origin) * 0.2f), label);
        }

        private int GetPreviewId(ProjectileController projectile)
        {
            // Prefer a stable network id in play mode, otherwise fall back to editor instance id.
            if (Application.isPlaying && projectile.NetworkObject != null && projectile.NetworkObject.IsSpawned)
                return projectile.NetworkObject.ObjectId;

            return projectile.GetInstanceID();
        }

        private static void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, float gapLength)
        {
            float totalLength = Vector3.Distance(start, end);
            if (totalLength <= 0.0001f)
                return;

            dashLength = Mathf.Max(0.001f, dashLength);
            gapLength = Mathf.Max(0.0f, gapLength);

            Vector3 dir = (end - start) / totalLength;
            float patternLength = dashLength + gapLength;
            if (patternLength <= 0.0001f)
            {
                Handles.DrawLine(start, end, thickness);
                return;
            }

            float pos = 0f;
            int safety = 0;
            while (pos < totalLength && safety++ < 4096)
            {
                float dashEnd = Mathf.Min(pos + dashLength, totalLength);
                Vector3 a = start + dir * pos;
                Vector3 b = start + dir * dashEnd;
                Handles.DrawLine(a, b, thickness);
                pos += patternLength;
            }
        }

        private static Vector3 GetNoisyDirection(
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float time,
            float seedX,
            float seedY,
            float amount,
            float frequency)
        {
            if (amount <= 0.0001f)
                return forward;

            float t = time * Mathf.Max(0f, frequency);
            float nx = (Mathf.PerlinNoise(seedX, t) * 2f) - 1f;
            float ny = (Mathf.PerlinNoise(seedY, t) * 2f) - 1f;

            Vector3 noise = (right * nx + up * ny) * amount;
            Vector3 dir = forward + noise;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : forward;
        }

        private static void BuildNoisyPath(
            System.Collections.Generic.List<Vector3> points,
            Vector3 origin,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float speed,
            float seconds,
            float startTime,
            float dt,
            float seedX,
            float seedY,
            float amount,
            float frequency,
            bool maintainForwardVelocity)
        {
            points.Clear();
            points.Add(origin);

            if (seconds <= 0f || speed <= 0f)
                return;

            if (!maintainForwardVelocity)
            {
                Vector3 dir = GetNoisyDirection(forward, right, up, startTime, seedX, seedY, amount, frequency);
                points.Add(origin + dir * (speed * seconds));
                return;
            }

            float time = startTime;
            Vector3 pos = origin;
            int steps = Mathf.Clamp(Mathf.CeilToInt(seconds / Mathf.Max(0.001f, dt)), 2, 1024);
            float step = seconds / steps;

            for (int i = 0; i < steps; i++)
            {
                Vector3 dir = GetNoisyDirection(forward, right, up, time, seedX, seedY, amount, frequency);
                pos += dir * (speed * step);
                points.Add(pos);
                time += step;
            }
        }

        private void DrawTimeTicks(
            Vector3 origin,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float speed,
            float startTime,
            float seedX,
            float seedY,
            float amount,
            float frequency,
            bool maintainForwardVelocity,
            bool noisy)
        {
            int ticks = Mathf.FloorToInt(_previewSeconds / Mathf.Max(0.001f, _tickEverySeconds));
            if (ticks <= 0) return;

            using (new Handles.DrawingScope(new Color(1f, 1f, 1f, 0.8f)))
            {
                for (int i = 1; i <= ticks; i++)
                {
                    float seconds = i * _tickEverySeconds;
                    Vector3 p = noisy
                        ? SampleNoisyPathPositionAtTime(origin, forward, right, up, speed, startTime, seedX, seedY, amount, frequency, maintainForwardVelocity, seconds)
                        : origin + forward * (speed * seconds);

                    float size = HandleUtility.GetHandleSize(p) * 0.06f;
                    Vector3 tickRight = Vector3.Cross(Vector3.up, forward);
                    if (tickRight.sqrMagnitude < 0.0001f)
                        tickRight = Vector3.Cross(Vector3.forward, forward);
                    tickRight.Normalize();

                    DrawDashedLine(p - tickRight * size, p + tickRight * size, _dashLength, _gapLength);
                    Handles.Label(p + Vector3.up * size, $"{seconds:0.#}s");
                }
            }
        }

        private static Vector3 SampleNoisyPathPosition(
            Vector3 origin,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float speed,
            float previewSeconds,
            float startTime,
            float seedX,
            float seedY,
            float amount,
            float frequency,
            bool maintainForwardVelocity,
            float phase01)
        {
            float seconds = Mathf.Clamp01(phase01) * Mathf.Max(0.001f, previewSeconds);
            return SampleNoisyPathPositionAtTime(origin, forward, right, up, speed, startTime, seedX, seedY, amount, frequency, maintainForwardVelocity, seconds);
        }

        private static Vector3 SampleNoisyPathPositionAtTime(
            Vector3 origin,
            Vector3 forward,
            Vector3 right,
            Vector3 up,
            float speed,
            float startTime,
            float seedX,
            float seedY,
            float amount,
            float frequency,
            bool maintainForwardVelocity,
            float seconds)
        {
            if (seconds <= 0f || speed <= 0f)
                return origin;

            if (!maintainForwardVelocity)
            {
                Vector3 dir = GetNoisyDirection(forward, right, up, startTime, seedX, seedY, amount, frequency);
                return origin + dir * (speed * seconds);
            }

            // Integrate with a fixed step (fast and stable enough for editor preview).
            float dt = 0.05f;
            int steps = Mathf.Clamp(Mathf.CeilToInt(seconds / dt), 1, 512);
            float step = seconds / steps;

            Vector3 pos = origin;
            float time = startTime;
            for (int i = 0; i < steps; i++)
            {
                Vector3 dir = GetNoisyDirection(forward, right, up, time, seedX, seedY, amount, frequency);
                pos += dir * (speed * step);
                time += step;
            }

            return pos;
        }
    }
}
