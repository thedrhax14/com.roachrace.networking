using UnityEngine;
using FishNet.Component.Observing;
using System.Reflection;

namespace RoachRace.Networking.Dev
{
    /// <summary>
    /// Helper component to visualize the FishNet HashGrid boundaries in the scene view.
    /// Add this to the same GameObject as your HashGrid (usually on NetworkManager).
    /// </summary>
    [RequireComponent(typeof(HashGrid))]
    public class HashGridVisualizer : MonoBehaviour
    {
        [Header("Visualization Settings")]
        [SerializeField] private Color gridColor = new Color(0, 1, 0, 0.3f);
        [SerializeField] private Color volumeColor = new Color(0.5f, 0.5f, 0.5f, 0.15f);
        [SerializeField] private float drawDistance = 100f; // How far from camera/center to draw
        [SerializeField] private bool centerOnCamera = true;
        [SerializeField] private Transform centerTarget;
        [Space]
        [Tooltip("If true, the main green grid plane will be locked to a specific world coordinate on the missing axis (e.g. Y=0 for XZ grid).")]
        [SerializeField] private bool lockGridLevel = true;
        [SerializeField] private float fixedGridLevel = 0f;

        private HashGrid _hashGrid;
        
        // Reflected values
        private FieldInfo _accuracyField;
        private FieldInfo _axesField;

        private void Awake()
        {
            _hashGrid = GetComponent<HashGrid>();
            CacheReflection();
        }

        private void OnValidate()
        {
            _hashGrid = GetComponent<HashGrid>();
            CacheReflection();
        }

        private void CacheReflection()
        {
            if (_hashGrid == null) return;
            
            var type = typeof(HashGrid);
            _accuracyField = type.GetField("_accuracy", BindingFlags.Instance | BindingFlags.NonPublic);
            _axesField = type.GetField("_gridAxes", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private void OnDrawGizmos()
        {
            if (_hashGrid == null) _hashGrid = GetComponent<HashGrid>();
            if (_hashGrid == null) return;

            if (_accuracyField == null || _axesField == null) CacheReflection();
            if (_accuracyField == null || _axesField == null) return;

            // Get values via reflection
            ushort accuracy = (ushort)_accuracyField.GetValue(_hashGrid);
            HashGrid.GridAxes axes = (HashGrid.GridAxes)_axesField.GetValue(_hashGrid);

            // Calculate grid cell size (logic from HashGrid.cs)
            int cellSize = Mathf.CeilToInt((float)accuracy / 2f);
            if (cellSize <= 0) return;

            // Determine center position
            Vector3 center = Vector3.zero;
            if (Application.isPlaying)
            {
                if (centerOnCamera && Camera.main != null)
                    center = Camera.main.transform.position;
                else if (centerTarget != null)
                    center = centerTarget.position;
            }
            else
            {
#if UNITY_EDITOR
                if (UnityEditor.SceneView.lastActiveSceneView != null)
                    center = UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
#endif
            }

            // Snap center to grid
            Vector3 snappedCenter = SnapToGrid(center, cellSize);

            // Apply level locking (keep main plane at fixed height/pos)
            if (lockGridLevel)
            {
                if (axes == HashGrid.GridAxes.XZ) snappedCenter.y = fixedGridLevel;
                else if (axes == HashGrid.GridAxes.XY) snappedCenter.z = fixedGridLevel;
                else if (axes == HashGrid.GridAxes.YZ) snappedCenter.x = fixedGridLevel;
            }
            
            DrawLayeredGrid(snappedCenter, cellSize, drawDistance, axes);
        }

        private Vector3 SnapToGrid(Vector3 pos, int size)
        {
            float x = Mathf.Floor(pos.x / size) * size;
            float y = Mathf.Floor(pos.y / size) * size;
            float z = Mathf.Floor(pos.z / size) * size;
            
            return new Vector3(x, y, z);
        }

        private void DrawLayeredGrid(Vector3 center, int cellSize, float range, HashGrid.GridAxes axes)
        {
            int lines = Mathf.CeilToInt(range / cellSize);
            float realRange = lines * cellSize;

            // 1. Draw Main Plane (Green) at center
            Gizmos.color = gridColor;
            DrawPlane(center, cellSize, realRange, axes, 0);

            // 2. Draw Volume Planes (Grey)
            Gizmos.color = volumeColor;
            for (int i = 1; i <= lines; i++)
            {
                float offset = i * cellSize;
                DrawPlane(center, cellSize, realRange, axes, offset);
                DrawPlane(center, cellSize, realRange, axes, -offset);
            }

            // 3. Draw Vertical Connectors (Grey)
            DrawConnectors(center, cellSize, realRange, axes);
        }

        private void DrawPlane(Vector3 center, int cellSize, float range, HashGrid.GridAxes axes, float offset)
        {
            // HashGrid uses these axes:
            // XY = 0 (2D Side Scroller) -> Missing Z
            // YZ = 1 -> Missing X
            // XZ = 2 (Top Down / 3D Ground) -> Missing Y

            if (axes == HashGrid.GridAxes.XZ)
            {
                float y = center.y + offset;
                for (float x = center.x - range; x <= center.x + range; x += cellSize)
                    Gizmos.DrawLine(new Vector3(x, y, center.z - range), new Vector3(x, y, center.z + range));
                
                for (float z = center.z - range; z <= center.z + range; z += cellSize)
                    Gizmos.DrawLine(new Vector3(center.x - range, y, z), new Vector3(center.x + range, y, z));
            }
            else if (axes == HashGrid.GridAxes.XY)
            {
                float z = center.z + offset;
                for (float x = center.x - range; x <= center.x + range; x += cellSize)
                    Gizmos.DrawLine(new Vector3(x, center.y - range, z), new Vector3(x, center.y + range, z));
                
                for (float y = center.y - range; y <= center.y + range; y += cellSize)
                    Gizmos.DrawLine(new Vector3(center.x - range, y, z), new Vector3(center.x + range, y, z));
            }
            else if (axes == HashGrid.GridAxes.YZ)
            {
                float x = center.x + offset;
                for (float y = center.y - range; y <= center.y + range; y += cellSize)
                    Gizmos.DrawLine(new Vector3(x, y, center.z - range), new Vector3(x, y, center.z + range));
                
                for (float z = center.z - range; z <= center.z + range; z += cellSize)
                    Gizmos.DrawLine(new Vector3(x, center.y - range, z), new Vector3(x, center.y + range, z));
            }
        }

        private void DrawConnectors(Vector3 center, int cellSize, float range, HashGrid.GridAxes axes)
        {
            // Draws lines in the missing axis direction at grid intersections
            if (axes == HashGrid.GridAxes.XZ)
            {
                float yMin = center.y - range;
                float yMax = center.y + range;
                
                for (float x = center.x - range; x <= center.x + range; x += cellSize)
                {
                    for (float z = center.z - range; z <= center.z + range; z += cellSize)
                    {
                        Gizmos.DrawLine(new Vector3(x, yMin, z), new Vector3(x, yMax, z));
                    }
                }
            }
            else if (axes == HashGrid.GridAxes.XY)
            {
                float zMin = center.z - range;
                float zMax = center.z + range;
                
                for (float x = center.x - range; x <= center.x + range; x += cellSize)
                {
                    for (float y = center.y - range; y <= center.y + range; y += cellSize)
                    {
                        Gizmos.DrawLine(new Vector3(x, y, zMin), new Vector3(x, y, zMax));
                    }
                }
            }
            else if (axes == HashGrid.GridAxes.YZ)
            {
                float xMin = center.x - range;
                float xMax = center.x + range;
                
                for (float y = center.y - range; y <= center.y + range; y += cellSize)
                {
                    for (float z = center.z - range; z <= center.z + range; z += cellSize)
                    {
                        Gizmos.DrawLine(new Vector3(xMin, y, z), new Vector3(xMax, y, z));
                    }
                }
            }
        }
    }
}
