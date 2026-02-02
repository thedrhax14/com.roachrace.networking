using System;
using System.Collections.Generic;
using FishNet.Component.Transforming.Beta;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Smooths remote players by rendering a VisualRoot using past-time snapshot interpolation.
    /// This component must not affect simulation/authority transforms.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Smoothing/Remote Player Visual Smoother")]
    public class RemotePlayerVisualSmoother : NetworkBehaviour
    {
        private enum RenderMode : byte
        {
            None = 0,
            InterpolateLinear = 1,
            InterpolateHermite = 2,
            Extrapolate = 3,
            Freeze = 4,
            ClampOldest = 5,
        }

        [Serializable]
        private struct PlayerSnapshot
        {
            public double serverTime;
            public Vector3 position;
            public Vector3 velocity;
            public float yaw;
        }

        [Header("Scene References")]
        [Tooltip("Transform that receives authoritative network updates (usually the Rigidbody root).")]
        [SerializeField] private Transform authorityRoot;

        [Tooltip("Optional: Rigidbody on the authority root, used for velocity-assisted interpolation.")]
        [SerializeField] private Rigidbody authorityRigidbody;

        [Tooltip("Transform to render smoothly. Should be visuals only (no physics).")]
        [SerializeField] private Transform visualRoot;

        [Header("Buffer")]
        [SerializeField, Range(0.05f, 0.25f)] private float interpolationDelay = 0.12f;
        [SerializeField, Range(4, 20)] private int maxSnapshots = 10;
        [SerializeField, Range(0.02f, 0.15f)] private float maxExtrapolation = 0.08f;

        [Header("Interpolation")]
        [SerializeField] private bool useHermite = true;
        [SerializeField, Range(0.0f, 5f)] private float maxHermiteTangentDistance = 1.25f;
        [SerializeField, Range(-1f, 1f)] private float hermiteVelocityDotFallback = 0.0f;
        [SerializeField] private Vector3 cameraOffset = new(0f, 1.5f, 0);

        [Header("Error Handling")]
        [SerializeField, Range(0f, 0.5f)] private float ignoreErrorDistance = 0.05f;
        [SerializeField, Range(0.05f, 1f)] private float smoothErrorDistance = 0.3f;
        [SerializeField, Range(0.1f, 2f)] private float snapErrorDistance = 0.6f;
        [SerializeField, Range(0.2f, 5f)] private float teleportErrorDistance = 1.0f;
        [SerializeField, Range(0.01f, 0.25f)] private float correctionSmoothTime = 0.08f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos;
        [SerializeField] private bool debugHud;
        [SerializeField] private bool debugHudGraph;
        [SerializeField, Range(30, 600)] private int debugGraphSamples = 240;
        [SerializeField, Range(20f, 140f)] private float debugGraphHeight = 70f;
        [Tooltip("Vertical scale for the graph in ms per frame. If your renderTime is tick-quantized you will see spikes near 50ms.")]
        [SerializeField, Range(2.5f, 80f)] private float debugGraphMsScale = 50f;
        [SerializeField, Range(0.01f, 0.35f)] private float debugSnapshotSphereRadius = 0.05f;
        [SerializeField] private Color debugSnapshotColor = new(0f, 0.9f, 0.3f, 0.9f);
        [SerializeField] private Color debugSegmentColor = new(1f, 0.9f, 0.1f, 0.95f);
        [SerializeField] private Color debugCursorColor = new(0.2f, 0.7f, 1f, 0.95f);

        private readonly List<PlayerSnapshot> _snapshots = new(12);

        private Vector3 _correctionVelocity;
        private float _correctionYawVelocity;

        // Offset from authority to visuals, stored in "yaw-space" so it rotates with the interpolated yaw.
        private Vector3 _visualOffsetYawSpace;
        private float _visualYawOffset;

        private bool _subscribed;
        private bool _initialized;

        private Vector3 _lastAuthorityPos;
        private double _lastAuthorityTime;
        private bool _hasLastAuthority;

        private RenderMode _lastRenderMode;
        private int _lastBracketIndexA = -1;
        private int _lastBracketIndexB = -1;
        private double _lastNow;
        private double _lastRenderTime;
        private float _lastT;

        private float[] _renderTimeDeltaMs;
        private int _renderTimeDeltaIndex;
        private bool _hasPrevRenderTime;
        private double _prevRenderTime;
        private static Texture2D _debugWhiteTex;

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Only runs on clients, and only for remote objects.
            if (!IsClientInitialized)
                return;

            if (visualRoot == null)
            {
                Debug.LogError($"[{nameof(RemotePlayerVisualSmoother)}] VisualRoot is not assigned on '{gameObject.name}'.", gameObject);
                throw new NullReferenceException($"[{nameof(RemotePlayerVisualSmoother)}] visualRoot is null on '{gameObject.name}'.");
            }

            visualRoot.transform.SetParent(null, true);

            if (authorityRoot == null)
                authorityRoot = transform;

            if (authorityRigidbody == null)
            {
                // Prefer Rigidbody on authorityRoot, fall back to this GameObject.
                if (!authorityRoot.TryGetComponent(out authorityRigidbody))
                    TryGetComponent(out authorityRigidbody);
            }

            // Offsets allow placing visuals with an offset while still smoothing in world-space.
            // IMPORTANT: store the positional offset in yaw-space so it rotates with interpolated yaw.
            float authorityYaw = authorityRoot.eulerAngles.y;
            float visualYaw = visualRoot.eulerAngles.y;
            Vector3 worldOffset = visualRoot.position - authorityRoot.position;
            Quaternion invAuthorityYaw = Quaternion.Inverse(Quaternion.Euler(0f, authorityYaw, 0f));
            _visualOffsetYawSpace = invAuthorityYaw * worldOffset;
            _visualYawOffset = Mathf.DeltaAngle(authorityYaw, visualYaw);

            SubscribeToTicks();

            // Prime buffer with current state.
            _snapshots.Clear();
            CaptureSnapshot();
            CaptureSnapshot();

            _initialized = true;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            UnsubscribeFromTicks();
            _snapshots.Clear();
            _initialized = false;
        }

        private void OnDestroy()
        {
            UnsubscribeFromTicks();
        }

        private void Update()
        {
            if (!_initialized || !enabled)
                return;

            if (_snapshots.Count < 2)
                return;

            double now = GetApproximateNetworkTime();
            double renderTime = now - interpolationDelay;

            _lastNow = now;
            _lastRenderTime = renderTime;

            RecordRenderTimeGraph(renderTime);

            bool haveTarget = TryGetTarget(renderTime, out Vector3 targetPos, out float targetYaw);
            if (!haveTarget)
                return;

            // Apply positional offset in yaw-space so it rotates with yaw.
            targetPos += Quaternion.Euler(0f, targetYaw, 0f) * _visualOffsetYawSpace;
            targetYaw = Mathf.Repeat(targetYaw + _visualYawOffset, 360f);

            ApplySmoothed(targetPos, targetYaw);
        }

        private void RecordRenderTimeGraph(double renderTime)
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

        private void SubscribeToTicks()
        {
            if (_subscribed)
                return;

            if (TimeManager == null)
            {
                Debug.LogError($"[{nameof(RemotePlayerVisualSmoother)}] TimeManager is null on '{gameObject.name}'.", gameObject);
                throw new NullReferenceException($"[{nameof(RemotePlayerVisualSmoother)}] TimeManager is null on '{gameObject.name}'.");
            }

            TimeManager.OnPostTick += TimeManager_OnPostTick;
            _subscribed = true;
        }

        private void UnsubscribeFromTicks()
        {
            if (!_subscribed)
                return;

            if (TimeManager != null)
                TimeManager.OnPostTick -= TimeManager_OnPostTick;

            _subscribed = false;
        }

        private void TimeManager_OnPostTick()
        {
            // Only sample authoritative results for remotes.
            if (!IsClientInitialized)
                return;

            CaptureSnapshot();
        }

        private void CaptureSnapshot()
        {
            double t = GetApproximateNetworkTime();

            Vector3 pos = authorityRoot.position;
            float yaw = authorityRoot.eulerAngles.y;

            Vector3 vel = Vector3.zero;
            if (authorityRigidbody != null)
            {
                vel = authorityRigidbody.linearVelocity;
            }
            else if (_hasLastAuthority)
            {
                double dt = t - _lastAuthorityTime;
                if (dt > 0.0001)
                    vel = (pos - _lastAuthorityPos) / (float)dt;
            }

            _lastAuthorityPos = pos;
            _lastAuthorityTime = t;
            _hasLastAuthority = true;

            // Keep buffer time monotonic even if FishNet adjusts Tick backwards.
            if (_snapshots.Count > 0)
            {
                double prevT = _snapshots[_snapshots.Count - 1].serverTime;
                if (t <= prevT)
                    t = prevT + 0.0001;
            }

            _snapshots.Add(new PlayerSnapshot
            {
                serverTime = t,
                position = pos,
                velocity = vel,
                yaw = yaw
            });

            int overflow = _snapshots.Count - maxSnapshots;
            if (overflow > 0)
                _snapshots.RemoveRange(0, overflow);
        }

        private double GetApproximateNetworkTime()
        {
            // FishNet time is derived from the approximated server Tick.
            // Using PreciseTick includes the intra-tick percent for smoother renderTime.
            if (TimeManager == null)
                return Time.unscaledTimeAsDouble;

            PreciseTick pt = TimeManager.GetPreciseTick(TickType.Tick);
            return TimeManager.TicksToTime(pt);
        }

        private bool TryGetTarget(double renderTime, out Vector3 pos, out float yaw)
        {
            pos = default;
            yaw = default;

            _lastRenderMode = RenderMode.None;
            _lastBracketIndexA = -1;
            _lastBracketIndexB = -1;
            _lastT = 0f;

            int count = _snapshots.Count;
            if (count < 2)
                return false;

            // Too old: clamp to first snapshot.
            if (renderTime <= _snapshots[0].serverTime)
            {
                pos = _snapshots[0].position;
                yaw = _snapshots[0].yaw;
                _lastRenderMode = RenderMode.ClampOldest;
                return true;
            }

            // Within buffer: find [a,b] that brackets renderTime.
            for (int i = 0; i < count - 1; i++)
            {
                PlayerSnapshot a = _snapshots[i];
                PlayerSnapshot b = _snapshots[i + 1];

                if (renderTime > b.serverTime)
                    continue;

                double dt = b.serverTime - a.serverTime;
                if (dt <= 0.000001)
                {
                    pos = b.position;
                    yaw = b.yaw;
                    return true;
                }

                float t = (float)((renderTime - a.serverTime) / dt);
                t = Mathf.Clamp01(t);

                _lastBracketIndexA = i;
                _lastBracketIndexB = i + 1;
                _lastT = t;

                pos = InterpolatePosition(a, b, t, out RenderMode mode);
                yaw = Mathf.LerpAngle(a.yaw, b.yaw, t);
                _lastRenderMode = mode;
                return true;
            }

            // Newer than last snapshot: limited extrapolation.
            PlayerSnapshot last = _snapshots[count - 1];
            double extrapDt = renderTime - last.serverTime;

            if (extrapDt <= maxExtrapolation)
            {
                pos = last.position + last.velocity * (float)extrapDt;
                yaw = last.yaw;
                _lastRenderMode = RenderMode.Extrapolate;
                return true;
            }

            // Freeze before snapping.
            pos = last.position;
            yaw = last.yaw;
            _lastRenderMode = RenderMode.Freeze;
            return true;
        }

        private Vector3 InterpolatePosition(PlayerSnapshot a, PlayerSnapshot b, float t, out RenderMode mode)
        {
            mode = RenderMode.InterpolateLinear;

            if (!useHermite)
                return Vector3.Lerp(a.position, b.position, t);

            float dt = (float)(b.serverTime - a.serverTime);
            if (dt <= 0.000001f)
                return b.position;

            Vector3 v0 = a.velocity;
            Vector3 v1 = b.velocity;

            // Fallback to linear when direction changes sharply.
            float dot = 0f;
            if (v0.sqrMagnitude > 0.0001f && v1.sqrMagnitude > 0.0001f)
                dot = Vector3.Dot(v0.normalized, v1.normalized);

            if (dot < hermiteVelocityDotFallback)
                return Vector3.Lerp(a.position, b.position, t);

            // TODO: Add an overshoot/segment-bounds safety check (eg AABB or capsule around [a.position,b.position])
            // and/or curvature-based fallback. Dot-product fallback alone won't catch all bad Hermite cases.

            // Convert velocities to tangents (distance over the segment).
            Vector3 m0 = v0 * dt;
            Vector3 m1 = v1 * dt;

            m0 = Vector3.ClampMagnitude(m0, maxHermiteTangentDistance);
            m1 = Vector3.ClampMagnitude(m1, maxHermiteTangentDistance);

            mode = RenderMode.InterpolateHermite;
            return Hermite(a.position, m0, b.position, m1, t);
        }

        // Matches the hermite basis used in Assets/GameData/Scripts/SplineSection.cs.
        private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return (p0 * ((2.0f * t3) - (3.0f * t2) + 1.0f))
                   + (m0 * (t3 + (-2.0f * t2) + t))
                   + (p1 * ((-2.0f * t3) + (3.0f * t2)))
                   + (m1 * (t3 - t2));
        }

        private void ApplySmoothed(Vector3 targetPos, float targetYaw)
        {
            // Position.
            float posError = Vector3.Distance(visualRoot.position, targetPos);
            if (posError >= teleportErrorDistance)
            {
                visualRoot.position = targetPos;
                _correctionVelocity = Vector3.zero;
            }
            else if (posError >= snapErrorDistance)
            {
                visualRoot.position = targetPos;
                _correctionVelocity = Vector3.zero;
            }
            else if (posError >= smoothErrorDistance)
            {
                visualRoot.position = Vector3.SmoothDamp(visualRoot.position, targetPos, ref _correctionVelocity, correctionSmoothTime);
            }
            else if (posError > ignoreErrorDistance)
            {
                // Small errors: still converge, but quickly.
                float fast = Mathf.Max(0.01f, correctionSmoothTime * 0.5f);
                visualRoot.position = Vector3.SmoothDamp(visualRoot.position, targetPos, ref _correctionVelocity, fast);
            }
            else
            {
                visualRoot.position = targetPos;
            }
            if(IsOwner)
            {
                Camera.main.transform.position = visualRoot.position + cameraOffset;
            }

            // Yaw only.
            float currentYaw = visualRoot.eulerAngles.y;
            float yawError = Mathf.Abs(Mathf.DeltaAngle(currentYaw, targetYaw));

            float newYaw;
            if (yawError >= 90f || posError >= snapErrorDistance)
            {
                newYaw = targetYaw;
                _correctionYawVelocity = 0f;
            }
            else
            {
                newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _correctionYawVelocity, correctionSmoothTime);
            }

            visualRoot.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!debugHud)
                return;

            if (!_initialized || !enabled)
                return;

            if (!IsClientInitialized)
                return;

            const int pad = 8;
            const int w = 420;
            int h = debugHudGraph ? 230 : 140;
            Rect r = new(pad, pad, w, h);
            GUI.Box(r, $"{nameof(RemotePlayerVisualSmoother)}\n{gameObject.name}");

            GUILayout.BeginArea(new Rect(pad + 10, pad + 24, w - 20, h - 30));
            GUILayout.Label($"snapshots: {_snapshots.Count}/{maxSnapshots}");
            GUILayout.Label($"delay: {interpolationDelay:0.000}s  extrapMax: {maxExtrapolation:0.000}s");
            GUILayout.Label($"now: {_lastNow:0.000}  renderTime: {_lastRenderTime:0.000}");
            GUILayout.Label($"mode: {_lastRenderMode}  seg: {_lastBracketIndexA}->{_lastBracketIndexB}  t: {_lastT:0.000}");

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
                Color c = ms >= debugGraphMsScale ? new Color(1f, 0.25f, 0.25f, 0.85f) : new Color(0.2f, 0.7f, 1f, 0.7f);
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

        private void OnDrawGizmos()
        {
            if (!debugGizmos)
                return;

            if (!Application.isPlaying)
                return;

            // Snapshot points.
            Gizmos.color = debugSnapshotColor;
            for (int i = 0; i < _snapshots.Count; i++)
            {
                Vector3 pi = _snapshots[i].position + Quaternion.Euler(0f, _snapshots[i].yaw, 0f) * _visualOffsetYawSpace;
                Gizmos.DrawSphere(pi, debugSnapshotSphereRadius);
                if (i > 0)
                {
                    Vector3 pj = _snapshots[i - 1].position + Quaternion.Euler(0f, _snapshots[i - 1].yaw, 0f) * _visualOffsetYawSpace;
                    Gizmos.DrawLine(pj, pi);
                }
            }

            // Current interpolation segment.
            if (_lastBracketIndexA >= 0 && _lastBracketIndexB >= 0 &&
                _lastBracketIndexA < _snapshots.Count && _lastBracketIndexB < _snapshots.Count)
            {
                Vector3 a = _snapshots[_lastBracketIndexA].position + Quaternion.Euler(0f, _snapshots[_lastBracketIndexA].yaw, 0f) * _visualOffsetYawSpace;
                Vector3 b = _snapshots[_lastBracketIndexB].position + Quaternion.Euler(0f, _snapshots[_lastBracketIndexB].yaw, 0f) * _visualOffsetYawSpace;
                Gizmos.color = debugSegmentColor;
                Gizmos.DrawLine(a, b);
            }

            // Render cursor at current visual root.
            if (visualRoot != null)
            {
                Gizmos.color = debugCursorColor;
                Gizmos.DrawWireSphere(visualRoot.position, debugSnapshotSphereRadius * 1.2f);
                Gizmos.DrawRay(visualRoot.position, visualRoot.forward * 0.5f);
            }
        }
#endif
    }
}
