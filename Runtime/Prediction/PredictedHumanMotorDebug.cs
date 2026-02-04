using FishNet.Object.Prediction;
using UnityEngine;

namespace RoachRace.Networking
{
    [DisallowMultipleComponent]
    public class PredictedHumanMotorDebug : MonoBehaviour
    {
#if UNITY_EDITOR
        [Header("Replicate State Graph")]
        [SerializeField, Range(4, 400)] private int replicateStateSamples = 40;
        [SerializeField] private Rect replicateStateRect = new(10f, 10f, 420f, 90f);

        [Header("Collision Stay Logging")]
        [SerializeField] private bool logCollisionStay = false;
        [SerializeField, Min(1)] private int logCollisionStayEveryTicks = 20;

        private ReplicateState[] _replicateStateHistory;
        private bool[] _groundedHistory;
        private bool[] _jumpHistory;
        private bool[] _appliedJumpHistory;
        private uint[] _tickHistory;
        private int _replicateStateHead;
        private int _replicateStateCount;

        private int _replicateCallsTotal;
        private int _replicateCallsTickedNotReplayed;
        private int _replicateCallsReplayed;

        private float _collisionStayTime;
        private float _collisionStayFixedTime;
        private int _collisionStayTicks;

        private static Texture2D _debugWhiteTex;

        public void RecordReplicateState(ReplicateState state, bool grounded, bool jump, bool appliedJump, uint tick)
        {
            EnsureReplicateStateBuffer();
            _replicateStateHistory[_replicateStateHead] = state;
            _groundedHistory[_replicateStateHead] = grounded;
            _jumpHistory[_replicateStateHead] = jump;
            _appliedJumpHistory[_replicateStateHead] = appliedJump;
            _tickHistory[_replicateStateHead] = tick;
            _replicateStateHead = (_replicateStateHead + 1) % _replicateStateHistory.Length;
            _replicateStateCount = Mathf.Min(_replicateStateCount + 1, _replicateStateHistory.Length);

            _replicateCallsTotal++;
            bool ticked = (state & ReplicateState.Ticked) != 0;
            bool replayed = (state & ReplicateState.Replayed) != 0;
            if (replayed)
                _replicateCallsReplayed++;
            if (ticked && !replayed)
                _replicateCallsTickedNotReplayed++;
        }

        public void RecordCollisionStayTick(string motorName)
        {
            if (!logCollisionStay)
                return;

            _collisionStayTime += Time.deltaTime;
            _collisionStayFixedTime += Time.fixedDeltaTime;
            _collisionStayTicks += 1;

            int n = Mathf.Max(1, logCollisionStayEveryTicks);
            if (_collisionStayTicks % n == 0)
            {
                Debug.Log(
                    $"[{nameof(PredictedHumanMotor)}] OnCollisionStay called {_collisionStayTicks} ticks, {_collisionStayTime:F2} seconds, {_collisionStayFixedTime:F2} fixed seconds on '{motorName}'."
                );
            }
        }

        private static Texture2D GetDebugWhiteTex()
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

        private void EnsureReplicateStateBuffer()
        {
            int size = Mathf.Max(4, replicateStateSamples);
            if (_replicateStateHistory != null && _replicateStateHistory.Length == size)
                return;

            _replicateStateHistory = new ReplicateState[size];
            _groundedHistory = new bool[size];
            _jumpHistory = new bool[size];
            _appliedJumpHistory = new bool[size];
            _tickHistory = new uint[size];
            _replicateStateHead = 0;
            _replicateStateCount = 0;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
                return;

            EnsureReplicateStateBuffer();
            if (_replicateStateCount <= 0)
                return;

            Texture2D tex = GetDebugWhiteTex();
            Rect rect = replicateStateRect;

            int newestIndex = (_replicateStateHead - 1 + _replicateStateHistory.Length) % _replicateStateHistory.Length;
            uint latestTick = _tickHistory[newestIndex];
            int sameTickRun = 0;
            for (int i = 0; i < _replicateStateCount; i++)
            {
                int idx = newestIndex - i;
                if (idx < 0)
                    idx += _replicateStateHistory.Length;

                if (_tickHistory[idx] != latestTick)
                    break;

                sameTickRun++;
            }

            GUI.Box(rect,
                $"{nameof(PredictedHumanMotor)} ReplicateState: calls={_replicateCallsTotal}, tickedOnly={_replicateCallsTickedNotReplayed}, replayed={_replicateCallsReplayed}, latestTick={latestTick}, sameTickRun={sameTickRun}");

            const float pad = 8f;
            const float labelW = 18f;
            float left = rect.x + pad;
            float right = rect.xMax - pad;
            float top = rect.y + 24f;
            float bottom = rect.yMax - pad;

            float w = Mathf.Max(1f, right - left);
            float h = Mathf.Max(1f, bottom - top);

            // Rows: Created, Ticked, Replayed, Future(derived), Grounded, Jump, AppliedJump.
            const int rows = 7;
            float rowH = Mathf.Max(10f, h / rows);

            float gridLeft = left + labelW;
            float gridW = Mathf.Max(1f, w - labelW);
            int n = _replicateStateHistory.Length;
            float cellW = gridW / n;

            Color bg = new(0f, 0f, 0f, 0.35f);
            Color off = new(1f, 1f, 1f, 0.08f);
            Color cCreated = new(0.45f, 1f, 0.45f, 0.85f);
            Color cTicked = new(1f, 0.85f, 0.2f, 0.85f);
            Color cReplayed = new(1f, 0.35f, 0.35f, 0.85f);
            Color cFuture = new(0.35f, 0.85f, 1f, 0.85f);
            Color cGrounded = new(0.9f, 0.45f, 1f, 0.85f);
            Color cJump = new(1f, 1f, 1f, 0.85f);
            Color cAppliedJump = new(1f, 0.7f, 0.1f, 0.95f);

            Color prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(new Rect(gridLeft, top, gridW, rowH * rows), tex);
            GUI.color = prev;

            void DrawRowLabel(int row, string label)
            {
                GUI.Label(new Rect(left, top + row * rowH, labelW, rowH), label);
            }

            DrawRowLabel(0, "C");
            DrawRowLabel(1, "T");
            DrawRowLabel(2, "R");
            DrawRowLabel(3, "F");
            DrawRowLabel(4, "G");
            DrawRowLabel(5, "J");
            DrawRowLabel(6, "A");

            // Oldest -> newest.
            int start = (_replicateStateCount == n)
                ? _replicateStateHead
                : 0;

            for (int i = 0; i < n; i++)
            {
                int idx = start + i;
                if (idx >= n)
                    idx -= n;

                ReplicateState st = _replicateStateHistory[idx];
                bool grounded = _groundedHistory[idx];
                bool jump = _jumpHistory[idx];
                bool applied = _appliedJumpHistory[idx];

                bool created = (st & ReplicateState.Created) != 0;
                bool ticked = (st & ReplicateState.Ticked) != 0;
                bool replayed = (st & ReplicateState.Replayed) != 0;
                // "Future" is most commonly represented as being replayed with no created input.
                bool future = replayed && !created;

                float x = gridLeft + (i * cellW);
                float cw = Mathf.Max(1f, cellW - 1f);

                Rect r0 = new(x, top + 0 * rowH, cw, rowH - 2f);
                Rect r1 = new(x, top + 1 * rowH, cw, rowH - 2f);
                Rect r2 = new(x, top + 2 * rowH, cw, rowH - 2f);
                Rect r3 = new(x, top + 3 * rowH, cw, rowH - 2f);
                Rect r4 = new(x, top + 4 * rowH, cw, rowH - 2f);
                Rect r5 = new(x, top + 5 * rowH, cw, rowH - 2f);
                Rect r6 = new(x, top + 6 * rowH, cw, rowH - 2f);

                GUI.color = created ? cCreated : off;
                GUI.DrawTexture(r0, tex);

                GUI.color = ticked ? cTicked : off;
                GUI.DrawTexture(r1, tex);

                GUI.color = replayed ? cReplayed : off;
                GUI.DrawTexture(r2, tex);

                GUI.color = future ? cFuture : off;
                GUI.DrawTexture(r3, tex);

                GUI.color = grounded ? cGrounded : off;
                GUI.DrawTexture(r4, tex);

                GUI.color = jump ? cJump : off;
                GUI.DrawTexture(r5, tex);

                GUI.color = applied ? cAppliedJump : off;
                GUI.DrawTexture(r6, tex);
            }

            GUI.color = prev;
        }
#endif
    }
}
