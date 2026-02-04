using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoachRace.Networking
{
    [AddComponentMenu("RoachRace/Networking/Smoothing/Remote Player Visual Smoother Debug")]
    [DisallowMultipleComponent]
    public class RemotePlayerVisualSmootherDebug : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugGizmos;
        [SerializeField] private bool debugHud;
        [SerializeField] private bool debugHudGraph;

        [Header("HUD Graph")]
        [SerializeField, Range(30, 600)] private int debugGraphSamples = 240;
        [SerializeField, Range(20f, 140f)] private float debugGraphHeight = 70f;
        [Tooltip("Vertical scale for the graph in ms per frame. If your renderTime is tick-quantized you will see spikes near 50ms.")]
        [SerializeField, Range(2.5f, 80f)] private float debugGraphMsScale = 50f;

        [Header("Gizmos")]
        [SerializeField, Range(0.01f, 0.35f)] private float debugSnapshotSphereRadius = 0.05f;
        [SerializeField] private Color debugSnapshotColor = new(0f, 0.9f, 0.3f, 0.9f);
        [SerializeField] private Color debugSegmentColor = new(1f, 0.9f, 0.1f, 0.95f);
        [SerializeField] private Color debugCursorColor = new(0.2f, 0.7f, 1f, 0.95f);

        private IReadOnlyList<RemotePlayerVisualSmoother.PlayerSnapshot> _snapshots;

        private Transform _visualRoot;
        private Vector3 _visualOffsetYawSpace;
        private string _smootherName;

        private bool _isClientInitialized;
        private int _snapshotCount;
        private int _maxSnapshots;
        private float _interpolationDelay;
        private float _maxExtrapolation;
        private double _now;
        private double _renderTime;
        private RemotePlayerVisualSmoother.RenderMode _renderMode;
        private int _bracketIndexA = -1;
        private int _bracketIndexB = -1;
        private float _t;
        private Vector3 _lastTargetVelocity;

        private bool _adaptiveErrorThresholds;
        private float _adaptiveSpeedCap;
        private float _snapErrorDistance;
        private float _teleportErrorDistance;
        private float _snapTimeWindow;
        private float _teleportTimeWindow;

        private float[] _renderTimeDeltaMs;
        private int _renderTimeDeltaIndex;
        private bool _hasPrevRenderTime;
        private double _prevRenderTime;

        private static Texture2D _debugWhiteTex;

        internal void Initialize(string smootherName, Transform visualRoot, Vector3 visualOffsetYawSpace, float visualYawOffset)
        {
            _smootherName = smootherName;
            _visualRoot = visualRoot;
            _visualOffsetYawSpace = visualOffsetYawSpace;
        }

        internal void SetSnapshotSource(IReadOnlyList<RemotePlayerVisualSmoother.PlayerSnapshot> snapshots)
        {
            _snapshots = snapshots;
            _snapshotCount = snapshots?.Count ?? 0;
        }

        internal void RecordRenderTimeGraph(double renderTime)
        {
            if (!debugHud || !debugHudGraph)
                return;

            int sampleCount = Mathf.Clamp(debugGraphSamples, 30, 600);
            if (_renderTimeDeltaMs == null || _renderTimeDeltaMs.Length != sampleCount)
            {
                _renderTimeDeltaMs = new float[sampleCount];
                _renderTimeDeltaIndex = 0;
                _hasPrevRenderTime = false;
                _prevRenderTime = 0d;
            }

            float deltaMs = 0f;
            if (_hasPrevRenderTime)
                deltaMs = (float)((renderTime - _prevRenderTime) * 1000.0);

            _prevRenderTime = renderTime;
            _hasPrevRenderTime = true;

            _renderTimeDeltaMs[_renderTimeDeltaIndex] = deltaMs;
            _renderTimeDeltaIndex = (_renderTimeDeltaIndex + 1) % _renderTimeDeltaMs.Length;
        }

        internal void SetFrameState(
            bool isClientInitialized,
            int maxSnapshots,
            float interpolationDelay,
            float maxExtrapolation,
            double now,
            double renderTime,
            RemotePlayerVisualSmoother.RenderMode renderMode,
            int bracketIndexA,
            int bracketIndexB,
            float t,
            Vector3 lastTargetVelocity,
            bool adaptiveErrorThresholds,
            float adaptiveSpeedCap,
            float snapErrorDistance,
            float teleportErrorDistance,
            float snapTimeWindow,
            float teleportTimeWindow)
        {
            _isClientInitialized = isClientInitialized;
            _maxSnapshots = maxSnapshots;
            _interpolationDelay = interpolationDelay;
            _maxExtrapolation = maxExtrapolation;
            _now = now;
            _renderTime = renderTime;
            _renderMode = renderMode;
            _bracketIndexA = bracketIndexA;
            _bracketIndexB = bracketIndexB;
            _t = t;
            _lastTargetVelocity = lastTargetVelocity;

            _adaptiveErrorThresholds = adaptiveErrorThresholds;
            _adaptiveSpeedCap = adaptiveSpeedCap;
            _snapErrorDistance = snapErrorDistance;
            _teleportErrorDistance = teleportErrorDistance;
            _snapTimeWindow = snapTimeWindow;
            _teleportTimeWindow = teleportTimeWindow;
        }

        private static Texture2D GetWhiteTex()
        {
            if (_debugWhiteTex != null)
                return _debugWhiteTex;

            _debugWhiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            _debugWhiteTex.SetPixel(0, 0, Color.white);
            _debugWhiteTex.Apply(false, true);
            return _debugWhiteTex;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!debugHud)
                return;

            if (!enabled)
                return;

            if (!Application.isPlaying)
                return;

            if (!_isClientInitialized)
                return;

            const int pad = 8;
            const int w = 420;
            int h = debugHudGraph ? 230 : 140;
            Rect r = new(pad, pad, w, h);

            string title = string.IsNullOrEmpty(_smootherName) ? nameof(RemotePlayerVisualSmoother) : _smootherName;
            GUI.Box(r, $"{nameof(RemotePlayerVisualSmoother)}\n{title}");

            GUILayout.BeginArea(new Rect(pad + 10, pad + 24, w - 20, h - 30));
            GUILayout.Label($"snapshots: {_snapshotCount}/{_maxSnapshots}");
            GUILayout.Label($"delay: {_interpolationDelay:0.000}s  extrapMax: {_maxExtrapolation:0.000}s");
            GUILayout.Label($"now: {_now:0.000}  renderTime: {_renderTime:0.000}");
            GUILayout.Label($"mode: {_renderMode}  seg: {_bracketIndexA}->{_bracketIndexB}  t: {_t:0.000}");

            if (_adaptiveErrorThresholds)
            {
                float v = _lastTargetVelocity.magnitude;
                float vc = _adaptiveSpeedCap > 0f ? Mathf.Min(v, _adaptiveSpeedCap) : v;
                float es = Mathf.Max(_snapErrorDistance, vc * _snapTimeWindow);
                float et = Mathf.Max(_teleportErrorDistance, vc * _teleportTimeWindow);
                GUILayout.Label($"v={v:0.###} (cap {vc:0.###})  snapEff={es:0.###}  teleEff={et:0.###}");
            }

            if (debugHudGraph)
            {
                GUILayout.Space(6);
                GUILayout.Label("renderTime Δ per frame (ms) — spikes indicate tick-quantized time");

                Rect graphRect = GUILayoutUtility.GetRect(w - 20, debugGraphHeight);
                DrawRenderTimeDeltaGraph(graphRect);
            }

            GUILayout.EndArea();
        }

        private void DrawRenderTimeDeltaGraph(Rect rect)
        {
            if (_renderTimeDeltaMs == null || _renderTimeDeltaMs.Length < 2)
                return;

            Texture2D tex = GetWhiteTex();

            // Background.
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(rect, tex);
            GUI.color = prevColor;

            float height = Mathf.Max(10f, rect.height);
            float width = Mathf.Max(10f, rect.width);
            int n = _renderTimeDeltaMs.Length;

            float maxMs = Mathf.Max(1f, debugGraphMsScale);

            // Draw a baseline at 0 and at 50ms for reference.
            float yBottom = rect.y + height;
            DrawHLine(rect.x, rect.x + width, yBottom - 1f, new Color(1f, 1f, 1f, 0.15f));

            float ref50 = Mathf.Clamp01(50f / maxMs);
            float y50 = rect.y + height * (1f - ref50);
            DrawHLine(rect.x, rect.x + width, y50, new Color(1f, 0.9f, 0.1f, 0.18f));

            // Bars.
            float barW = width / n;
            for (int i = 0; i < n; i++)
            {
                int idx = (_renderTimeDeltaIndex + i) % n;
                float ms = _renderTimeDeltaMs[idx];
                float norm = Mathf.Clamp01(ms / maxMs);
                float barH = norm * height;

                float x = rect.x + i * barW;
                float y = rect.y + (height - barH);

                // Color spikes more vividly.
                Color c = ms >= debugGraphMsScale
                    ? new Color(1f, 0.25f, 0.25f, 0.85f)
                    : new Color(0.2f, 0.7f, 1f, 0.7f);

                GUI.color = c;
                GUI.DrawTexture(new Rect(x, y, Mathf.Max(1f, barW), barH), tex);
            }

            GUI.color = prevColor;
        }

        private static void DrawHLine(float x0, float x1, float y, Color c)
        {
            Texture2D tex = GetWhiteTex();
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x0, y, Mathf.Max(1f, x1 - x0), 1f), tex);
            GUI.color = prev;
        }
#endif

        private void OnDrawGizmos()
        {
            if (!debugGizmos)
                return;

            if (!Application.isPlaying)
                return;

            if (_snapshots == null)
                return;

            // Snapshot points.
            Gizmos.color = debugSnapshotColor;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                var si = _snapshots[i];
                Vector3 pi = si.position + Quaternion.Euler(0f, si.yaw, 0f) * _visualOffsetYawSpace;
                Gizmos.DrawSphere(pi, debugSnapshotSphereRadius);

                if (i > 0)
                {
                    var sj = _snapshots[i - 1];
                    Vector3 pj = sj.position + Quaternion.Euler(0f, sj.yaw, 0f) * _visualOffsetYawSpace;
                    Gizmos.DrawLine(pj, pi);
                }
            }

            // Current interpolation segment.
            if (_bracketIndexA >= 0 && _bracketIndexB >= 0 &&
                _bracketIndexA < _snapshots.Count && _bracketIndexB < _snapshots.Count)
            {
                var sa = _snapshots[_bracketIndexA];
                var sb = _snapshots[_bracketIndexB];
                Vector3 a = sa.position + Quaternion.Euler(0f, sa.yaw, 0f) * _visualOffsetYawSpace;
                Vector3 b = sb.position + Quaternion.Euler(0f, sb.yaw, 0f) * _visualOffsetYawSpace;

                Gizmos.color = debugSegmentColor;
                Gizmos.DrawLine(a, b);
            }

            // Render cursor at current visual root.
            if (_visualRoot != null)
            {
                Gizmos.color = debugCursorColor;
                Gizmos.DrawWireSphere(_visualRoot.position, debugSnapshotSphereRadius * 1.2f);
                Gizmos.DrawRay(_visualRoot.position, _visualRoot.forward * 0.5f);
            }
        }
    }
}
