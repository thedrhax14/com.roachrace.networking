using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
#pragma warning disable CS0618 // NetworkExplosionOnSpawn is intentionally supported for legacy prefabs.
    [CustomEditor(typeof(NetworkExplosionOnSpawn))]
    [System.Obsolete("Deprecated: use NetworkExplosionLifecycleOnSpawn + NetworkExplosionForceOnSpawn + NetworkExplosionStatusEffectsOnSpawn instead. This wrapper remains for backwards compatibility with existing prefabs.", false)]
    public class NetworkExplosionOnSpawnEditor : UnityEditor.Editor
    {
        // Editor-only preview settings (not serialized on the component)
        private float _previewDashLength = 0.35f;
        private float _previewGapLength = 0.22f;
        private int _previewLabelRings = 3;

        private SerializedProperty effects;
        private SerializedProperty removeExistingFirst;

        private SerializedProperty scaleStacksByDistance;
        private SerializedProperty stackFalloff;

        private SerializedProperty legacyEffect;
        private SerializedProperty legacyStacks;

        private SerializedProperty radius;

        private SerializedProperty applyPhysicsForce;
        private SerializedProperty explosionForce;
        private SerializedProperty upwardsModifier;
        private SerializedProperty forceMode;
        private SerializedProperty forceFalloff;

        private SerializedProperty damageLayers;
        private SerializedProperty triggerInteraction;
        private SerializedProperty ignoreInstigator;
        private SerializedProperty requireLineOfSight;
        private SerializedProperty lineOfSightBlockers;
        private SerializedProperty lineOfSightStartOffset;
        private SerializedProperty visualRoot;
        private SerializedProperty despawnAfterSeconds;

        private const int DiskDirections = 24;

        private void OnEnable()
        {
            radius = serializedObject.FindProperty("radius");

            applyPhysicsForce = serializedObject.FindProperty("applyPhysicsForce");
            explosionForce = serializedObject.FindProperty("explosionForce");
            upwardsModifier = serializedObject.FindProperty("upwardsModifier");
            forceMode = serializedObject.FindProperty("forceMode");
            forceFalloff = serializedObject.FindProperty("forceFalloff");

            effects = serializedObject.FindProperty("effects");
            removeExistingFirst = serializedObject.FindProperty("removeExistingFirst");

            scaleStacksByDistance = serializedObject.FindProperty("scaleStacksByDistance");
            stackFalloff = serializedObject.FindProperty("stackFalloff");

            legacyEffect = serializedObject.FindProperty("_legacyEffect");
            legacyStacks = serializedObject.FindProperty("_legacyStacks");

            damageLayers = serializedObject.FindProperty("damageLayers");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            ignoreInstigator = serializedObject.FindProperty("ignoreInstigator");
            requireLineOfSight = serializedObject.FindProperty("requireLineOfSight");
            lineOfSightBlockers = serializedObject.FindProperty("lineOfSightBlockers");
            lineOfSightStartOffset = serializedObject.FindProperty("lineOfSightStartOffset");
            visualRoot = serializedObject.FindProperty("visualRoot");
            despawnAfterSeconds = serializedObject.FindProperty("despawnAfterSeconds");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "NetworkExplosionOnSpawn is DEPRECATED.\n\n" +
                "Use these components instead:\n" +
                "- NetworkExplosionLifecycleOnSpawn (visuals + despawn)\n" +
                "- NetworkExplosionForceOnSpawn (physics force)\n" +
                "- NetworkExplosionStatusEffectsOnSpawn (status effects)\n\n" +
                "This component remains for backwards compatibility with existing prefabs.",
                MessageType.Warning);

            EditorGUILayout.LabelField("Effects", EditorStyles.boldLabel);
            if (effects != null)
                EditorGUILayout.PropertyField(effects, includeChildren: true);
            if (removeExistingFirst != null)
                EditorGUILayout.PropertyField(removeExistingFirst);

            if (scaleStacksByDistance != null)
                EditorGUILayout.PropertyField(scaleStacksByDistance);
            if (scaleStacksByDistance != null && scaleStacksByDistance.boolValue && stackFalloff != null)
            {
                EditorGUILayout.PropertyField(stackFalloff);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
                _previewDashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("Dash Length", _previewDashLength));
                _previewGapLength = Mathf.Max(0.0f, EditorGUILayout.FloatField("Gap Length", _previewGapLength));
                _previewLabelRings = Mathf.Clamp(EditorGUILayout.IntField("Label Rings", _previewLabelRings), 1, 10);
            }

            if (effects != null && effects.arraySize == 0 && legacyEffect != null && legacyEffect.objectReferenceValue != null)
            {
                int stacks = legacyStacks != null ? Mathf.Max(1, legacyStacks.intValue) : 1;
                EditorGUILayout.HelpBox(
                    $"This explosion is using a legacy single-effect setup (\"{legacyEffect.objectReferenceValue.name}\" x{stacks}).\n" +
                    "Click Migrate to copy it into the new Effects list.",
                    MessageType.Warning);

                if (GUILayout.Button("Migrate Legacy Effect -> Effects List"))
                {
                    int idx = effects.arraySize;
                    effects.arraySize = idx + 1;

                    SerializedProperty elem = effects.GetArrayElementAtIndex(idx);
                    SerializedProperty def = elem.FindPropertyRelative("definition");
                    SerializedProperty st = elem.FindPropertyRelative("stacks");
                    SerializedProperty edgeSt = elem.FindPropertyRelative("edgeStacks");
                    if (def != null)
                        def.objectReferenceValue = legacyEffect.objectReferenceValue;
                    if (st != null)
                        st.intValue = stacks;
                    if (edgeSt != null)
                        edgeSt.intValue = 0;

                    legacyEffect.objectReferenceValue = null;
                    if (legacyStacks != null)
                        legacyStacks.intValue = 1;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Targeting", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(radius);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Physics Force", EditorStyles.boldLabel);
            if (applyPhysicsForce != null)
                EditorGUILayout.PropertyField(applyPhysicsForce);

            if (applyPhysicsForce != null && applyPhysicsForce.boolValue)
            {
                if (explosionForce != null)
                    EditorGUILayout.PropertyField(explosionForce);
                if (upwardsModifier != null)
                    EditorGUILayout.PropertyField(upwardsModifier);
                if (forceMode != null)
                    EditorGUILayout.PropertyField(forceMode);
                if (forceFalloff != null)
                    EditorGUILayout.PropertyField(forceFalloff);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(damageLayers);
            EditorGUILayout.PropertyField(triggerInteraction);
            EditorGUILayout.PropertyField(ignoreInstigator);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(requireLineOfSight);
            if (requireLineOfSight.boolValue)
            {
                EditorGUILayout.PropertyField(lineOfSightBlockers);
                EditorGUILayout.PropertyField(lineOfSightStartOffset);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(visualRoot);
            EditorGUILayout.PropertyField(despawnAfterSeconds);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Scene View (when selected): shows a horizontal (XZ) effect radius disk.\n" +
                "If stack scaling is enabled, preview shows dashed rings with stack counts by distance.\n" +
                "Line-of-sight preview is always shown as dashed spokes (green=clear, gray=blocked).",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();

            DrawDebugControls();
        }

        private void DrawDebugControls()
        {
            var explosion = target as NetworkExplosionOnSpawn;
            if (explosion == null)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

            bool canExplodeNow = EditorApplication.isPlaying && explosion.IsServerInitialized;
            using (new EditorGUI.DisabledScope(!canExplodeNow))
            {
                if (GUILayout.Button("Explode Again (Server)"))
                    explosion.EditorExplodeAgain();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to use debug actions.", MessageType.None);
            else if (!explosion.IsServerInitialized)
                EditorGUILayout.HelpBox("This button is only enabled on the server instance.", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            var explosion = (NetworkExplosionOnSpawn)target;
            if (explosion == null) return;

            float r = Mathf.Max(0f, radius != null ? radius.floatValue : 0f);
            if (r <= 0f) return;

            var t = explosion.transform;

            bool scale = scaleStacksByDistance != null && scaleStacksByDistance.boolValue;
            // Always draw dashed rings only (no extra wire-disc radius/gradient clutter).
            DrawStackPreviewRingsXZ(t.position, r);

            DrawLoSSpokesXZ(t.position, r);

            if (scale)
                DrawHoverStacksPreview(t.position, r);

            string effectsLabel = BuildEffectsLabel();
            string label = $"Explosion: r={r:0.##}  {effectsLabel}" + (scale ? "  (scaled)" : string.Empty);
            Handles.Label(t.position + Vector3.up * (r + 0.25f), label);
        }

        private void DrawHoverStacksPreview(Vector3 center, float radiusValue)
        {
            Event e = Event.current;
            if (e == null)
                return;

            // Ray from mouse into world, intersect with the XZ plane at the explosion's Y.
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, center.y, 0f));
            if (!plane.Raycast(ray, out float enter))
                return;

            Vector3 point = ray.GetPoint(enter);
            Vector3 delta = point - center;
            delta.y = 0f;
            float dist = delta.magnitude;

            // If you're too far away, it's noise; bail.
            float maxShow = radiusValue * 2.5f;
            if (dist > maxShow)
                return;

            float normalized = radiusValue > 0.0001f ? Mathf.Clamp01(dist / radiusValue) : 1f;
            float strength = GetStrength01(normalized);

            bool inside = dist <= radiusValue + 0.0001f;
            bool losEnabled = requireLineOfSight != null && requireLineOfSight.boolValue;
            bool blocked = false;
            if (inside && losEnabled)
            {
                float startOffset = lineOfSightStartOffset != null ? Mathf.Max(0f, lineOfSightStartOffset.floatValue) : 0.05f;
                int blockersMask = lineOfSightBlockers != null ? lineOfSightBlockers.intValue : ~0;
                blocked = IsLineOfSightBlocked(center, point, blockersMask, startOffset);
            }

            // Marker + label.
            Color markerColor;
            if (!inside)
                markerColor = new Color(1f, 1f, 1f, 0.35f);
            else if (losEnabled && blocked)
                markerColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
            else
                markerColor = new Color(1f, 1f, 1f, 0.95f);

            using (new Handles.DrawingScope(markerColor))
            {
                float size = HandleUtility.GetHandleSize(point) * 0.06f;
                Handles.SphereHandleCap(0, point, Quaternion.identity, size, EventType.Repaint);
            }

            string stacks = BuildStacksLabelAtDistance(normalized);
            string header = inside
                ? $"dist={dist:0.##}" + (losEnabled ? (blocked ? "  (LoS BLOCKED)" : "  (LoS OK)") : string.Empty)
                : $"outside radius  dist={dist:0.##}";

            string full = string.IsNullOrWhiteSpace(stacks) ? header : header + "\n" + stacks;
            float y = HandleUtility.GetHandleSize(point) * 0.5f;
            Handles.Label(point + Vector3.up * y, full);

            // Repaint so the hover readout feels responsive.
            if (e.type == EventType.MouseMove)
                SceneView.RepaintAll();
        }

        private void DrawStackPreviewRingsXZ(Vector3 center, float radiusValue)
        {
            if (radiusValue <= 0f) return;

            int rings = Mathf.Clamp(_previewLabelRings, 1, 10);
            float step = 1f / rings;

            // Draw N rings from inside -> edge (inclusive of edge).
            for (int i = 1; i <= rings; i++)
            {
                float t = i * step;
                float rr = radiusValue * t;
                if (rr <= 0.0001f) continue;

                float strength = GetStrength01(t);
                Color c = Color.Lerp(new Color(0.35f, 0.35f, 0.35f, 0.75f), new Color(1f, 1f, 1f, 0.9f), strength);
                using (new Handles.DrawingScope(c))
                    DrawDashedWireDiscXZ(center, rr, _previewDashLength, _previewGapLength);
            }
        }

        private static void DrawDashedWireDiscXZ(Vector3 center, float radius, float dashLength, float gapLength)
        {
            radius = Mathf.Max(0f, radius);
            if (radius <= 0.0001f)
                return;

            dashLength = Mathf.Max(0.001f, dashLength);
            gapLength = Mathf.Max(0.0f, gapLength);

            float circumference = 2f * Mathf.PI * radius;
            float pattern = dashLength + gapLength;
            if (pattern <= 0.0001f || circumference <= 0.0001f)
            {
                Handles.DrawWireDisc(center, Vector3.up, radius);
                return;
            }

            // Convert world-length dash/gap into angle steps.
            float dashAngle = (dashLength / circumference) * (Mathf.PI * 2f);
            float stepAngle = (pattern / circumference) * (Mathf.PI * 2f);

            int maxSegments = 2048;
            int safety = 0;
            for (float a = 0f; a < Mathf.PI * 2f && safety++ < maxSegments; a += stepAngle)
            {
                float a0 = a;
                float a1 = Mathf.Min(a + dashAngle, Mathf.PI * 2f);

                Vector3 p0 = center + new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Handles.DrawLine(p0, p1);
            }
        }

        private void DrawLoSSpokesXZ(Vector3 center, float radiusValue)
        {
            if (radiusValue <= 0f) return;

            float startOffset = lineOfSightStartOffset != null ? Mathf.Max(0f, lineOfSightStartOffset.floatValue) : 0.05f;
            int blockersMask = lineOfSightBlockers != null ? lineOfSightBlockers.intValue : ~0;

            bool losEnabled = requireLineOfSight != null && requireLineOfSight.boolValue;
            Color clearColor = losEnabled
                ? new Color(0.2f, 1f, 0.2f, 0.7f)
                : new Color(0.6f, 0.6f, 0.6f, 0.35f);
            Color blockedColor = losEnabled
                ? new Color(0.2f, 0.2f, 0.2f, 0.75f)
                : new Color(0.35f, 0.35f, 0.35f, 0.35f);

            // For each direction, find the first blocker hit and draw clear + blocked segments.
            for (int i = 0; i < DiskDirections; i++)
            {
                float angle = (i / (float)DiskDirections) * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                float hitDistance;
                bool blocked = TryGetFirstBlockerDistance(center, dir, radiusValue, blockersMask, startOffset, out hitDistance);

                if (!blocked)
                {
                    using (new Handles.DrawingScope(clearColor))
                        DrawDashedLine(center, center + dir * radiusValue, _previewDashLength, _previewGapLength, 1f);
                    continue;
                }

                // Clear segment.
                if (hitDistance > 0.0001f)
                {
                    using (new Handles.DrawingScope(clearColor))
                        DrawDashedLine(center, center + dir * hitDistance, _previewDashLength, _previewGapLength, 1f);
                }

                // Blocked segment.
                float remaining = radiusValue - hitDistance;
                if (remaining > 0.0001f)
                {
                    using (new Handles.DrawingScope(blockedColor))
                        DrawDashedLine(center + dir * hitDistance, center + dir * radiusValue, _previewDashLength, _previewGapLength, 1f);
                }
            }
        }

        private static bool TryGetFirstBlockerDistance(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            int blockersMask,
            float startOffset,
            out float hitDistance)
        {
            hitDistance = 0f;

            float distance = Mathf.Max(0f, maxDistance);
            if (distance <= 0.0001f)
                return false;

            Vector3 dir = direction;
            dir.y = 0f;
            if (dir.sqrMagnitude <= 0.0001f)
                return false;
            dir.Normalize();

            float offset = Mathf.Clamp(startOffset, 0f, distance * 0.5f);
            Vector3 rayOrigin = origin + dir * offset;
            float rayDistance = distance - offset;
            if (rayDistance <= 0.0001f)
                return false;

            if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, rayDistance, blockersMask, QueryTriggerInteraction.Ignore))
            {
                // Convert back to distance from the true origin.
                hitDistance = offset + hit.distance;
                hitDistance = Mathf.Clamp(hitDistance, 0f, distance);
                return true;
            }

            return false;
        }

        private static void DrawDashedLine(Vector3 start, Vector3 end, float dashLength, float gapLength, float thickness)
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

        private string BuildEffectsLabel()
        {
            // Prefer the new list.
            if (effects != null && effects.arraySize > 0)
            {
                int count = 0;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < effects.arraySize; i++)
                {
                    SerializedProperty elem = effects.GetArrayElementAtIndex(i);
                    if (elem == null) continue;

                    SerializedProperty def = elem.FindPropertyRelative("definition");
                    SerializedProperty st = elem.FindPropertyRelative("stacks");
                    if (def == null || def.objectReferenceValue == null) continue;

                    int stacks = st != null ? Mathf.Max(1, st.intValue) : 1;
                    if (count > 0) sb.Append(", ");
                    sb.Append(def.objectReferenceValue.name);
                    sb.Append(" x");
                    sb.Append(stacks);
                    count++;

                    // Keep labels readable in scene view.
                    if (count >= 3)
                    {
                        if (effects.arraySize > i + 1)
                            sb.Append(", ...");
                        break;
                    }
                }

                return count == 0 ? "effects: (none)" : $"effects: {sb}";
            }

            // Legacy fallback.
            if (legacyEffect != null && legacyEffect.objectReferenceValue != null)
            {
                int stacks = legacyStacks != null ? Mathf.Max(1, legacyStacks.intValue) : 1;
                return $"effects: {legacyEffect.objectReferenceValue.name} x{stacks} (legacy)";
            }

            return "effects: (none)";
        }

        private string BuildStacksLabelAtDistance(float normalizedDistance)
        {
            float t = Mathf.Clamp01(normalizedDistance);
            float strength = GetStrength01(t);

            // Prefer the new list.
            if (effects != null && effects.arraySize > 0)
            {
                int count = 0;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();

                for (int i = 0; i < effects.arraySize; i++)
                {
                    SerializedProperty elem = effects.GetArrayElementAtIndex(i);
                    if (elem == null) continue;

                    SerializedProperty def = elem.FindPropertyRelative("definition");
                    SerializedProperty st = elem.FindPropertyRelative("stacks");
                    SerializedProperty edgeSt = elem.FindPropertyRelative("edgeStacks");
                    if (def == null || def.objectReferenceValue == null) continue;

                    int centerStacks = st != null ? Mathf.Max(0, st.intValue) : 0;
                    int edgeStacks = edgeSt != null ? Mathf.Max(0, edgeSt.intValue) : 0;

                    int applied = Mathf.RoundToInt(Mathf.Lerp(edgeStacks, centerStacks, strength));
                    if (applied <= 0) continue;

                    if (count > 0) sb.Append("  |  ");
                    sb.Append(def.objectReferenceValue.name);
                    sb.Append(" x");
                    sb.Append(applied);
                    count++;

                    if (count >= 2)
                    {
                        if (effects.arraySize > i + 1)
                            sb.Append("  |  ...");
                        break;
                    }
                }

                return count == 0 ? string.Empty : sb.ToString();
            }

            // Legacy fallback.
            if (legacyEffect != null && legacyEffect.objectReferenceValue != null)
            {
                int centerStacks = legacyStacks != null ? Mathf.Max(1, legacyStacks.intValue) : 1;
                int applied = Mathf.RoundToInt(Mathf.Lerp(0f, centerStacks, strength));
                if (applied <= 0) return string.Empty;
                return $"{legacyEffect.objectReferenceValue.name} x{applied}";
            }

            return string.Empty;
        }

        private float GetStrength01(float normalizedDistance)
        {
            float t = Mathf.Clamp01(normalizedDistance);

            if (stackFalloff == null)
                return 1f;

            AnimationCurve curve = stackFalloff.animationCurveValue;
            if (curve == null)
                return 1f;

            return Mathf.Clamp01(curve.Evaluate(t));
        }

        private static bool IsLineOfSightBlocked(Vector3 origin, Vector3 target, int blockersMask, float startOffset)
        {
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f) return false;

            Vector3 direction = toTarget / distance;
            float offset = Mathf.Clamp(startOffset, 0f, distance * 0.5f);

            origin += direction * offset;
            distance -= offset;
            if (distance <= 0.0001f) return false;

            return Physics.Raycast(origin, direction, distance, blockersMask, QueryTriggerInteraction.Ignore);
        }

    }

#pragma warning restore CS0618
}
