using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(PhysicsCollisionCrushDamage))]
    public class PhysicsCollisionCrushDamageEditor : UnityEditor.Editor
    {
        private float previewCrushImpulse = 4f;
        private Rigidbody otherRigidbody;
        
        private SerializedProperty minCrushImpulsePerSecond;
        private SerializedProperty crushDamagePerImpulse;
        private SerializedProperty selfDamageMultiplier;
        private SerializedProperty outgoingDamageMultiplier;

        private void OnEnable()
        {
            minCrushImpulsePerSecond = serializedObject.FindProperty("minCrushImpulsePerSecond");
            crushDamagePerImpulse = serializedObject.FindProperty("crushDamagePerImpulse");
            selfDamageMultiplier = serializedObject.FindProperty("selfDamageMultiplier");
            outgoingDamageMultiplier = serializedObject.FindProperty("outgoingDamageMultiplier");
        }

        private float GetSelfMass()
        {
            var component = (PhysicsCollisionCrushDamage)target;
            var rb = component.GetComponent<Rigidbody>();
            return rb != null ? rb.mass : 1f;
        }

        private float GetOtherMass()
        {
            return otherRigidbody != null ? otherRigidbody.mass : 100000f;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw default properties
            EditorGUILayout.PropertyField(minCrushImpulsePerSecond);
            EditorGUILayout.PropertyField(crushDamagePerImpulse);
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(selfDamageMultiplier);
            EditorGUILayout.PropertyField(outgoingDamageMultiplier);
            
            serializedObject.ApplyModifiedProperties();

            // Damage Preview Section
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Crush Damage Preview", EditorStyles.boldLabel);
            
            DrawDamageSeparator();
            
            DrawCrushDamagePreview();
            
            EditorGUILayout.Space(5);
            DrawDamageSeparator();
            
            DrawCrushExamples();
        }

        private void DrawCrushDamagePreview()
        {
            EditorGUILayout.LabelField("Damage Calculator (per second)", EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            
            previewCrushImpulse = EditorGUILayout.FloatField("Impulse/Second", previewCrushImpulse);
            
            float selfMass = GetSelfMass();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.FloatField("Self Mass (from Rigidbody)", selfMass);
            EditorGUI.EndDisabledGroup();
            
            otherRigidbody = (Rigidbody)EditorGUILayout.ObjectField("Other Rigidbody", otherRigidbody, typeof(Rigidbody), true);
            
            float otherMass = GetOtherMass();
            EditorGUI.BeginDisabledGroup(true);
            string otherMassLabel = otherRigidbody != null ? "Other Mass (from Rigidbody)" : "Other Mass (infinite/static)";
            EditorGUILayout.FloatField(otherMassLabel, otherMass);
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.Space(3);
            
            if (previewCrushImpulse < minCrushImpulsePerSecond.floatValue)
            {
                EditorGUILayout.HelpBox($"No crush damage - below threshold ({minCrushImpulsePerSecond.floatValue})", MessageType.Info);
            }
            else
            {
                float damagePerSecond = previewCrushImpulse * crushDamagePerImpulse.floatValue;
                
                var (selfDPS, otherDPS) = PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                    damagePerSecond,
                    selfMass,
                    otherMass,
                    selfDamageMultiplier.floatValue,
                    outgoingDamageMultiplier.floatValue
                );
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Self DPS:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{selfDPS} HP/s", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Other DPS:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{otherDPS} HP/s", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Time to Kill (100 HP):", GUILayout.Width(120));
                if (selfDPS > 0)
                {
                    float timeToKill = 100f / selfDPS;
                    EditorGUILayout.LabelField($"{timeToKill:F1}s", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("N/A", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUI.indentLevel--;
        }

        private void DrawCrushExamples()
        {
            EditorGUILayout.LabelField("Common Crush Scenarios", EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            
            DrawExampleRow("Light pressure (2 imp/s)", 2f);
            DrawExampleRow("Moderate crush (5 imp/s)", 5f);
            DrawExampleRow("Heavy press (10 imp/s)", 10f);
            DrawExampleRow("Hydraulic crusher (20 imp/s)", 20f);
            DrawExampleRow("Instant death (50 imp/s)", 50f);
            
            EditorGUI.indentLevel--;
        }

        private void DrawExampleRow(string label, float impulsePerSecond)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            
            if (impulsePerSecond < minCrushImpulsePerSecond.floatValue)
            {
                EditorGUILayout.LabelField("No damage", EditorStyles.miniLabel);
            }
            else
            {
                float selfMass = GetSelfMass();
                float otherMass = GetOtherMass();
                
                float damagePerSecond = impulsePerSecond * crushDamagePerImpulse.floatValue;
                
                var (selfDPS, otherDPS) = PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                    damagePerSecond,
                    selfMass,
                    otherMass,
                    selfDamageMultiplier.floatValue,
                    outgoingDamageMultiplier.floatValue
                );
                
                EditorGUILayout.LabelField($"Self: {selfDPS} HP/s | Other: {otherDPS} HP/s", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDamageSeparator()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }
    }
}
