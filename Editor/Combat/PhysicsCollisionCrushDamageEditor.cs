using UnityEditor;
using UnityEngine;
using RoachRace.Networking.Combat;

namespace RoachRace.Networking.Editor.Combat
{
    /// <summary>
    /// Custom inspector for <see cref="PhysicsCollisionCrushStatusEffect"/> that previews the mass-based stack split.<br>
    /// Use this in the Unity Editor to tune the impact threshold, effect entry, and multipliers.
    /// </summary>
    [CustomEditor(typeof(PhysicsCollisionCrushStatusEffect))]
    public class PhysicsCollisionCrushStatusEffectEditor : UnityEditor.Editor
    {
        private float previewCrushImpulse = 4f;
        private Rigidbody otherRigidbody;
        
        private SerializedProperty statusEffect;
        private SerializedProperty minCrushImpulsePerSecond;
        private SerializedProperty selfStackMultiplier;
        private SerializedProperty outgoingStackMultiplier;

        /// <summary>
        /// Caches serialized properties used by the custom inspector.
        /// </summary>
        private void OnEnable()
        {
            statusEffect = serializedObject.FindProperty("statusEffect");
            minCrushImpulsePerSecond = serializedObject.FindProperty("minCrushImpulsePerSecond");
            selfStackMultiplier = serializedObject.FindProperty("selfStackMultiplier");
            outgoingStackMultiplier = serializedObject.FindProperty("outgoingStackMultiplier");
        }

        /// <summary>
        /// Returns the rigidbody mass for the inspected object, or 1 if it is unexpectedly missing.
        /// </summary>
        /// <returns>The object's rigidbody mass used for preview calculations.</returns>
        private float GetSelfMass()
        {
            var component = (PhysicsCollisionCrushStatusEffect)target;
            return component.TryGetComponent<Rigidbody>(out var rb) && rb != null ? rb.mass : 1f;
        }

        /// <summary>
        /// Returns the preview mass for the other object, or a large value when no test rigidbody is assigned.
        /// </summary>
        /// <returns>The other object's rigidbody mass used for preview calculations.</returns>
        private float GetOtherMass()
        {
            return otherRigidbody != null ? otherRigidbody.mass : 100000f;
        }

        /// <summary>
        /// Draws the inspector UI and the mass-based status effect preview.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(statusEffect);
            EditorGUILayout.PropertyField(minCrushImpulsePerSecond);
            EditorGUILayout.PropertyField(selfStackMultiplier);
            EditorGUILayout.PropertyField(outgoingStackMultiplier);
            
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Crush Status Effect Preview", EditorStyles.boldLabel);
            
            DrawSeparator();
            
            DrawStatusEffectPreview();
        }

        /// <summary>
        /// Draws the live preview using the configured threshold, effect stacks, and mass ratios.
        /// </summary>
        private void DrawStatusEffectPreview()
        {
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
                EditorGUILayout.HelpBox($"No status effect - below threshold ({minCrushImpulsePerSecond.floatValue})", MessageType.Info);
            }
            else
            {
                SerializedProperty effectProperty = statusEffect.FindPropertyRelative("effect");
                SerializedProperty stacksProperty = statusEffect.FindPropertyRelative("stacks");
                if (effectProperty != null && effectProperty.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("Assign a status effect to preview stack distribution.", MessageType.Warning);
                    EditorGUI.indentLevel--;
                    return;
                }

                int configuredStacks = stacksProperty != null ? Mathf.Max(1, stacksProperty.intValue) : 1;
                
                var (selfStacks, otherStacks) = PhysicsCollisionDamageCalculator.CalculateStacksDistribution(
                    configuredStacks,
                    selfMass,
                    otherMass,
                    selfStackMultiplier.floatValue,
                    outgoingStackMultiplier.floatValue
                );
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Self Stacks:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{selfStacks}", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Other Stacks:", GUILayout.Width(120));
                EditorGUILayout.LabelField($"{otherStacks}", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws a simple horizontal separator between inspector sections.
        /// </summary>
        private void DrawSeparator()
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }
    }
}
