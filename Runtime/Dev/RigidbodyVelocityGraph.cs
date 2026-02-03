using UnityEngine;

namespace RoachRace.Networking
{
    /// <summary>
    /// Runtime debug graph which plots Rigidbody velocity components (X/Y/Z) over time.
    /// Intended for Editor/Development builds.
    /// </summary>
    [AddComponentMenu("RoachRace/Networking/Dev/Rigidbody Velocity Graph")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidbodyVelocityGraph : MonoBehaviour
    {
        Rigidbody target;

        [Header("Sampling")]
        [SerializeField, Min(8)] private int sampleCount = 100;

        [Header("Graph")]
        [SerializeField] private bool showGraph = true;
        [SerializeField] private Rect graphRect = new(10f, 10f, 520f, 180f);
        [SerializeField, Min(0f)] private float lineWidth = 2f;

        [Tooltip("Minimum value shown at the bottom of the graph.")]
        [SerializeField] private float minValue = -10f;

        [Tooltip("Maximum value shown at the top of the graph.")]
        [SerializeField] private float maxValue = 10f;

        [Header("Colors")]
        [SerializeField] private Color xColor = new(1f, 0.35f, 0.35f, 0.95f);
        [SerializeField] private Color yColor = new(0.45f, 1f, 0.45f, 0.95f);
        [SerializeField] private Color zColor = new(0.35f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color zeroLineColor = new(1f, 1f, 1f, 0.25f);

        private float[] _x;
        private float[] _y;
        private float[] _z;
        private int _head;
        private int _count;
        private float _nextSampleTime;

        private static Texture2D _whiteTex;

        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex != null)
                    return _whiteTex;

                _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _whiteTex.SetPixel(0, 0, Color.white);
                _whiteTex.Apply(false, true);
                return _whiteTex;
            }
        }

        private void Awake()
        {
            TryGetComponent(out target);
        }

        private void OnEnable()
        {
            EnsureBuffers();
            _nextSampleTime = 0f;
        }

        private void EnsureBuffers()
        {
            int size = Mathf.Clamp(sampleCount, 8, 4096);
            if (_x != null && _x.Length == size && _y != null && _y.Length == size && _z != null && _z.Length == size)
                return;

            _x = new float[size];
            _y = new float[size];
            _z = new float[size];
            _head = 0;
            _count = 0;
        }

        private void AddSample(Vector3 v)
        {
            _x[_head] = v.x;
            _y[_head] = v.y;
            _z[_head] = v.z;

            _head = (_head + 1) % _x.Length;
            _count = Mathf.Min(_count + 1, _x.Length);
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!showGraph)
                return;

            EnsureBuffers();
            if (target == null)
                return;

            float now = Time.unscaledTime;
            AddSample(target.linearVelocity);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var oldColor = GUI.color;
            var oldMatrix = GUI.matrix;

            GUI.color = color;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float length = (b - a).magnitude;

            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - (width * 0.5f), length, width), WhiteTex);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private float ValueToY(float value, float top, float bottom)
        {
            float min = minValue;
            float max = maxValue;
            if (Mathf.Abs(max - min) < 0.000001f)
                max = min + 0.000001f;

            float t = Mathf.InverseLerp(min, max, value);
            t = Mathf.Clamp01(t);
            return Mathf.Lerp(bottom, top, t);
        }

        private void OnGUI()
        {
            if (!showGraph)
                return;

            EnsureBuffers();
            if (_count <= 1)
                return;

            Rect rect = graphRect;
            GUI.Box(rect, "Rigidbody Velocity (X/Y/Z)");

            const float pad = 10f;
            float left = rect.x + pad;
            float right = rect.xMax - pad;
            float top = rect.y + 24f;
            float bottom = rect.yMax - pad;

            float width = Mathf.Max(1f, right - left);
            float height = Mathf.Max(1f, bottom - top);

            // Zero line (only when 0 is within range).
            if (minValue < 0f && maxValue > 0f)
            {
                float y0 = ValueToY(0f, top, bottom);
                DrawLine(new Vector2(left, y0), new Vector2(right, y0), zeroLineColor, 1f);
            }

            int startIndex = (_count == _x.Length) ? _head : 0;

            float Sample(float[] arr, int i)
            {
                int idx = startIndex + i;
                if (idx >= arr.Length)
                    idx -= arr.Length;
                return arr[idx];
            }

            float prevX = left;

            float prevVx = Sample(_x, 0);
            float prevVy = Sample(_y, 0);
            float prevVz = Sample(_z, 0);

            float prevYx = ValueToY(prevVx, top, bottom);
            float prevYy = ValueToY(prevVy, top, bottom);
            float prevYz = ValueToY(prevVz, top, bottom);

            for (int i = 1; i < _count; i++)
            {
                float t = i / (float)(_count - 1);
                float x = left + (t * width);

                float vx = Sample(_x, i);
                float vy = Sample(_y, i);
                float vz = Sample(_z, i);

                float yx = ValueToY(vx, top, bottom);
                float yy = ValueToY(vy, top, bottom);
                float yz = ValueToY(vz, top, bottom);

                DrawLine(new Vector2(prevX, prevYx), new Vector2(x, yx), xColor, lineWidth);
                DrawLine(new Vector2(prevX, prevYy), new Vector2(x, yy), yColor, lineWidth);
                DrawLine(new Vector2(prevX, prevYz), new Vector2(x, yz), zColor, lineWidth);

                prevX = x;
                prevYx = yx;
                prevYy = yy;
                prevYz = yz;
            }

            // Latest values / settings.
            float latestVx = Sample(_x, _count - 1);
            float latestVy = Sample(_y, _count - 1);
            float latestVz = Sample(_z, _count - 1);

            string hzText = $"dt={Time.unscaledDeltaTime * 1000f:0.#}ms";
            GUI.Label(
                new Rect(rect.x + pad, rect.yMax - 22f, rect.width - (pad * 2f), 20f),
                $"X={latestVx:0.###}  Y={latestVy:0.###}  Z={latestVz:0.###}   range=[{minValue:0.###}, {maxValue:0.###}]  samples={_count}  {hzText}"
            );
        }
#endif
    }
}
