using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(NetworkExplosionForce))]
    public class NetworkExplosionForceEditor : UnityEditor.Editor
    {
        private float _previewDashLength = 0.35f;
        private float _previewGapLength = 0.22f;
        private int _previewLabelRings = 3;

        private bool _previewShowRigidbodiesRays = true;
        private bool _previewShowBlockedRigidbodiesRays = false;
        private float _previewRigidbodyRayThickness = 2f;

        private SerializedProperty radius;
        private SerializedProperty damageLayers;
        private SerializedProperty triggerInteraction;
        private SerializedProperty requireLineOfSight;
        private SerializedProperty lineOfSightBlockers;
        private SerializedProperty lineOfSightStartOffset;

        private const int DiskDirections = 24;

        private void OnEnable()
        {
            radius = serializedObject.FindProperty("radius");
            damageLayers = serializedObject.FindProperty("damageLayers");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            requireLineOfSight = serializedObject.FindProperty("requireLineOfSight");
            lineOfSightBlockers = serializedObject.FindProperty("lineOfSightBlockers");
            lineOfSightStartOffset = serializedObject.FindProperty("lineOfSightStartOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
            _previewDashLength = Mathf.Max(0.01f, EditorGUILayout.FloatField("Dash Length", _previewDashLength));
            _previewGapLength = Mathf.Max(0.0f, EditorGUILayout.FloatField("Gap Length", _previewGapLength));
            _previewLabelRings = Mathf.Clamp(EditorGUILayout.IntField("Label Rings", _previewLabelRings), 1, 10);
            _previewShowRigidbodiesRays = EditorGUILayout.Toggle("Show Rigidbody Rays", _previewShowRigidbodiesRays);
            using (new EditorGUI.DisabledScope(!_previewShowRigidbodiesRays))
            {
                _previewShowBlockedRigidbodiesRays = EditorGUILayout.Toggle("Show Blocked Rays", _previewShowBlockedRigidbodiesRays);
                _previewRigidbodyRayThickness = Mathf.Clamp(EditorGUILayout.FloatField("Ray Thickness", _previewRigidbodyRayThickness), 1f, 6f);
            }

            serializedObject.ApplyModifiedProperties();

            var explosion = target as NetworkExplosionForce;
            if (explosion == null)
                return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);

            bool canRun = EditorApplication.isPlaying && (explosion.IsServerInitialized || explosion.IsClientInitialized);
            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("Apply Force Again"))
                    explosion.EditorApplyForceAgain();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to use debug actions.", MessageType.None);
        }

        private void OnSceneGUI()
        {
            if (!_previewShowRigidbodiesRays)
            {
                // Still draw radius + LoS spokes even if rays are disabled.
                var explosionNoRays = (NetworkExplosionForce)target;
                if (explosionNoRays == null)
                    return;

                float rr = Mathf.Max(0f, radius != null ? radius.floatValue : 0f);
                if (rr <= 0.0001f)
                    return;

                Vector3 c = explosionNoRays.transform.position;
                DrawStackPreviewRingsXZ(c, rr);
                DrawLoSSpokesXZ(c, rr);
                return;
            }

            var explosion = (NetworkExplosionForce)target;
            if (explosion == null)
                return;

            float r = Mathf.Max(0f, radius != null ? radius.floatValue : 0f);
            if (r <= 0.0001f)
                return;

            Vector3 center = explosion.transform.position;

            DrawStackPreviewRingsXZ(center, r);
            DrawLoSSpokesXZ(center, r);
            int layers = damageLayers != null ? damageLayers.intValue : ~0;
            QueryTriggerInteraction qti = QueryTriggerInteraction.Ignore;
            if (triggerInteraction != null)
                qti = (QueryTriggerInteraction)triggerInteraction.enumValueIndex;

            Collider[] hits = Physics.OverlapSphere(center, r, layers, qti);
            if (hits == null || hits.Length == 0)
                return;

            var perRb = new Dictionary<Rigidbody, (Collider collider, Vector3 point, float normalized)>(hits.Length);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                    continue;

                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null)
                    continue;

                Vector3 closestPoint = hit.ClosestPoint(center);
                float dist = Vector3.Distance(center, closestPoint);
                float normalized = Mathf.Clamp01(dist / Mathf.Max(r, 0.0001f));

                if (perRb.TryGetValue(rb, out var existing))
                {
                    if (normalized < existing.normalized)
                        perRb[rb] = (hit, closestPoint, normalized);
                }
                else
                {
                    perRb[rb] = (hit, closestPoint, normalized);
                }
            }

            if (perRb.Count == 0)
                return;

            bool losEnabled = requireLineOfSight != null && requireLineOfSight.boolValue;
            float startOffset = lineOfSightStartOffset != null ? Mathf.Max(0f, lineOfSightStartOffset.floatValue) : 0.05f;
            int blockersMask = lineOfSightBlockers != null ? lineOfSightBlockers.intValue : ~0;

            var clearColor = new Color(0.15f, 0.95f, 0.15f, 0.9f);
            var blockedColor = new Color(0.55f, 0.2f, 0.2f, 0.85f);

            int safety = 0;
            const int max = 128;
            foreach (var kvp in perRb)
            {
                if (safety++ > max)
                    break;

                Rigidbody rb = kvp.Key;
                var data = kvp.Value;
                if (rb == null)
                    continue;

                bool blocked = false;
                if (losEnabled)
                {
                    blocked = IsLineOfSightBlocked(center, data.point, blockersMask, startOffset, data.collider, rb, explosion.transform);
                    if (blocked && !_previewShowBlockedRigidbodiesRays)
                        continue;
                }

                using (new Handles.DrawingScope(blocked ? blockedColor : clearColor))
                {
                    Handles.DrawLine(center, data.point, _previewRigidbodyRayThickness);

                    float size = HandleUtility.GetHandleSize(data.point) * 0.03f;
                    Handles.SphereHandleCap(0, data.point, Quaternion.identity, size, EventType.Repaint);
                }
            }
        }

        private void DrawStackPreviewRingsXZ(Vector3 center, float radiusValue)
        {
            if (radiusValue <= 0f) return;

            int rings = Mathf.Clamp(_previewLabelRings, 1, 10);
            float step = 1f / rings;

            for (int i = 1; i <= rings; i++)
            {
                float t = i * step;
                float rr = radiusValue * t;
                if (rr <= 0.0001f) continue;

                Color c = new Color(1f, 1f, 1f, 0.65f);
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

                if (hitDistance > 0.0001f)
                {
                    using (new Handles.DrawingScope(clearColor))
                        DrawDashedLine(center, center + dir * hitDistance, _previewDashLength, _previewGapLength, 1f);
                }

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

        private static bool IsLineOfSightBlocked(
            Vector3 origin,
            Vector3 target,
            int blockersMask,
            float startOffset,
            Collider targetCollider,
            Rigidbody targetRigidbody,
            Transform explosionTransform)
        {
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f)
                return false;

            Vector3 direction = toTarget / distance;
            float offset = Mathf.Clamp(startOffset, 0f, distance * 0.5f);

            Vector3 rayOrigin = origin + direction * offset;
            float rayDistance = distance - offset;
            if (rayDistance <= 0.0001f)
                return false;

            if (!Physics.Raycast(rayOrigin, direction, out RaycastHit hit, rayDistance, blockersMask, QueryTriggerInteraction.Ignore))
                return false;

            if (hit.collider != null && explosionTransform != null && hit.collider.transform != null && hit.collider.transform.IsChildOf(explosionTransform))
                return false;

            if (targetCollider != null)
            {
                if (hit.collider == targetCollider)
                    return false;

                if (targetRigidbody != null)
                {
                    if (hit.rigidbody == targetRigidbody)
                        return false;
                    if (hit.collider != null && hit.collider.attachedRigidbody == targetRigidbody)
                        return false;
                }

                if (hit.collider != null && hit.collider.transform != null && targetCollider.transform != null)
                {
                    if (hit.collider.transform.IsChildOf(targetCollider.transform) || targetCollider.transform.IsChildOf(hit.collider.transform))
                        return false;
                }
            }

            return true;
        }
    }
}
