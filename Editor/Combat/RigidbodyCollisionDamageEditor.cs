using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    [CustomEditor(typeof(RigidbodyCollisionDamage))]
    public class RigidbodyCollisionDamageEditor : UnityEditor.Editor
    {
        private float previewImpulse = 10f;
        private Rigidbody otherRigidbody;
        
        private SerializedProperty minImpactImpulse;
        private SerializedProperty impulseToDamage;
        private SerializedProperty selfDamageMultiplier;
        private SerializedProperty outgoingDamageMultiplier;
        private SerializedProperty minDownwardVelocity;

        private void OnEnable()
        {
            minImpactImpulse = serializedObject.FindProperty("minImpactImpulse");
            impulseToDamage = serializedObject.FindProperty("impulseToDamage");
            selfDamageMultiplier = serializedObject.FindProperty("selfDamageMultiplier");
            outgoingDamageMultiplier = serializedObject.FindProperty("outgoingDamageMultiplier");
            minDownwardVelocity = serializedObject.FindProperty("minDownwardVelocity");
        }

        private float GetSelfMass()
        {
            var component = (RigidbodyCollisionDamage)target;
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
            EditorGUILayout.PropertyField(minImpactImpulse);
            EditorGUILayout.PropertyField(impulseToDamage);
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(selfDamageMultiplier);
            EditorGUILayout.PropertyField(outgoingDamageMultiplier);
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(minDownwardVelocity);
            
            serializedObject.ApplyModifiedProperties();

            // Damage Preview Section
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Damage Preview Calculator", EditorStyles.boldLabel);
            
            DrawDamageSeparator();
            
            // Impact Damage Preview
            DrawImpactDamagePreview();
            
            EditorGUILayout.Space(5);
            DrawDamageSeparator();
            
            // Common Impact Examples
            DrawImpactExamples();
        }

        private void DrawImpactDamagePreview()
        {
            EditorGUILayout.LabelField("Impact Damage Preview", EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            
            previewImpulse = EditorGUILayout.FloatField("Test Impulse", previewImpulse);
            
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
            
            if (previewImpulse < minImpactImpulse.floatValue)
            {
                EditorGUILayout.HelpBox($"No damage - impulse below threshold ({minImpactImpulse.floatValue})", MessageType.Info);
            }
            else
            {
                float baseDamage = previewImpulse * impulseToDamage.floatValue;
                
                var (selfDamage, otherDamage) = PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                    baseDamage,
                    selfMass,
                    otherMass,
                    selfDamageMultiplier.floatValue,
                    outgoingDamageMultiplier.floatValue
                );
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Self Damage:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{selfDamage} HP", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Other Damage:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{otherDamage} HP", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Total Damage:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{selfDamage + otherDamage} HP", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUI.indentLevel--;
        }

        private void DrawImpactExamples()
        {
            EditorGUILayout.LabelField("Common Impact Examples", EditorStyles.miniBoldLabel);
            
            EditorGUI.indentLevel++;
            
            DrawExampleRow("Light bump (impulse 3)", 3f);
            DrawExampleRow("Medium hit (impulse 10)", 10f);
            DrawExampleRow("Heavy impact (impulse 25)", 25f);
            DrawExampleRow("Severe crash (impulse 50)", 50f);
            DrawExampleRow("Lethal collision (impulse 100)", 100f);
            
            EditorGUI.indentLevel--;
        }

        private void DrawExampleRow(string label, float impulse)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField(label, GUILayout.Width(200));
            
            if (impulse < minImpactImpulse.floatValue)
            {
                EditorGUILayout.LabelField("No damage", EditorStyles.miniLabel);
            }
            else
            {
                float selfMass = GetSelfMass();
                float otherMass = GetOtherMass();
                
                float baseDamage = impulse * impulseToDamage.floatValue;
                
                var (selfDamage, otherDamage) = PhysicsCollisionDamageCalculator.CalculateDamageDistribution(
                    baseDamage,
                    selfMass,
                    otherMass,
                    selfDamageMultiplier.floatValue,
                    outgoingDamageMultiplier.floatValue
                );
                
                EditorGUILayout.LabelField($"Self: {selfDamage} | Other: {otherDamage}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDamageSeparator()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }
    }
}
